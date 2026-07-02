using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Business.Security;

/// <summary>
/// Issues access tokens (signed RS256 JWTs) and opaque refresh tokens. Refresh
/// tokens are random 256-bit values; only their SHA-256 hash is persisted.
/// </summary>
public class TokenService(JwtKeyService keys, IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    /// <summary>Build a signed access token with sub/email/role + iat/exp claims.</summary>
    public (string Token, int ExpiresInSeconds) CreateAccessToken(Guid userId, string email, string role)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_o.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _o.Issuer,
            Audience = _o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = keys.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId.ToString(),
                ["email"] = email,
                ["role"] = role,
            },
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, _o.AccessTokenMinutes * 60);
    }

    /// <summary>Generate a new opaque refresh token: returns the raw value to hand to
    /// the client, its SHA-256 hash to persist, and the absolute expiry (ISO-8601 UTC).</summary>
    public (string Raw, string Hash, string ExpiresAt) CreateRefreshToken()
    {
        // URL-safe (base64url, no padding) so the token is safe in cookies, JSON, and headers.
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.AddDays(_o.RefreshTokenDays).ToString("O");
        return (raw, Hash(raw), expiresAt);
    }

    /// <summary>SHA-256 (hex) of a raw refresh token — used to look tokens up server-side.</summary>
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
