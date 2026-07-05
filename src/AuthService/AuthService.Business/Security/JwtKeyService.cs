using System.Security.Cryptography;
using AuthService.Business.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Business.Security;

/// <summary>
/// Owns the RSA signing key. The private key is loaded from the PEM file injected
/// at startup (architecture §6.2); if none is present an ephemeral dev key is
/// generated. Exposes RS256 signing credentials for token issuance, the public key
/// for validation, and the JWKS document served at /.well-known/jwks.
/// Registered as a singleton so the key is loaded once.
/// </summary>
public class JwtKeyService
{
    private readonly RsaSecurityKey _key;

    public string KeyId { get; }
    public SigningCredentials SigningCredentials { get; }
    public SecurityKey ValidationKey => _key;

    public JwtKeyService(IOptions<JwtOptions> options, ILogger<JwtKeyService> logger)
    {
        var o = options.Value;
        KeyId = o.KeyId;

        var rsa = RSA.Create();
        if (!string.IsNullOrWhiteSpace(o.PrivateKeyPath) && File.Exists(o.PrivateKeyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(o.PrivateKeyPath));
            logger.LogInformation("Loaded RSA JWT signing key from {Path}.", o.PrivateKeyPath);
        }
        else
        {
            rsa.Dispose();
            rsa = RSA.Create(2048);
            logger.LogWarning(
                "No JWT private key at '{Path}' — generated an EPHEMERAL 2048-bit dev key. " +
                "Tokens will not survive a restart and this must not be used in production.",
                o.PrivateKeyPath);
        }

        _key = new RsaSecurityKey(rsa) { KeyId = KeyId };
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>Public key as a JWKS document for other services to verify tokens.</summary>
    public JwksDto GetJwks()
    {
        var p = _key.Rsa!.ExportParameters(includePrivateParameters: false);
        var jwk = new JwkDto(
            Kty: "RSA",
            Use: "sig",
            Kid: KeyId,
            Alg: SecurityAlgorithms.RsaSha256,
            N: Base64UrlEncoder.Encode(p.Modulus),
            E: Base64UrlEncoder.Encode(p.Exponent));
        return new JwksDto([jwk]);
    }
}
