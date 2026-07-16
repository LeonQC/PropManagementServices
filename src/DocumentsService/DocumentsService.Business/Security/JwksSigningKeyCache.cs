using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocumentsService.Business.Security;

/// <summary>
/// Fetches and caches the auth-service's JWKS so bearer tokens can be validated
/// locally. Auth serves a raw JWKS document (no OIDC discovery), so the standard
/// metadata-based key retrieval doesn't apply; instead this cache backs
/// TokenValidationParameters.IssuerSigningKeyResolver.
///
/// The TTL is deliberately short: the auth-service publishes a constant kid even
/// if its key changes (e.g. an ephemeral dev key after a restart), so a stale
/// cache can't be detected by kid alone. An unknown kid forces an early refresh.
/// </summary>
public sealed class JwksSigningKeyCache(
    IOptions<JwtValidationOptions> options,
    ILogger<JwksSigningKeyCache> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTime _fetchedAt = DateTime.MinValue;

    public IEnumerable<SecurityKey> GetKeys(string? kid)
    {
        var keys = CurrentOrRefreshed(force: false);

        // Token carries a kid we don't know — the signing key may have rotated
        // since the last fetch. Refresh early (rate-limited) and retry the match.
        if (kid is not null && !keys.Any(k => k.KeyId == kid))
            keys = CurrentOrRefreshed(force: true);

        return keys;
    }

    private IReadOnlyList<SecurityKey> CurrentOrRefreshed(bool force)
    {
        var maxAge = force ? MinRefreshInterval : Ttl;
        if (DateTime.UtcNow - _fetchedAt < maxAge && _keys.Count > 0) return _keys;

        lock (_lock)
        {
            if (DateTime.UtcNow - _fetchedAt < maxAge && _keys.Count > 0) return _keys;
            try
            {
                // Sync-over-async is confined to this rarely-hit path: the resolver
                // callback is synchronous by contract and the result is cached.
                var json = Http.GetStringAsync(options.Value.JwksUrl).GetAwaiter().GetResult();
                _keys = [.. new JsonWebKeySet(json).GetSigningKeys()];
                _fetchedAt = DateTime.UtcNow;
                logger.LogInformation("Loaded {Count} signing key(s) from {Url}", _keys.Count, options.Value.JwksUrl);
            }
            catch (Exception ex)
            {
                // Keep serving the last-known keys; validation fails cleanly if none.
                logger.LogError(ex, "Failed to fetch JWKS from {Url}", options.Value.JwksUrl);
            }
            return _keys;
        }
    }
}
