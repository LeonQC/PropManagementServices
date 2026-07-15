namespace DocumentsService.Business.Security;

/// <summary>
/// Settings for validating auth-service-issued JWTs, bound from the "Jwt" config
/// section. This service only validates tokens — it never issues them.
/// </summary>
public class JwtValidationOptions
{
    public string Issuer { get; set; } = "proptrack-auth";
    public string Audience { get; set; } = "proptrack";

    /// <summary>The auth-service JWKS endpoint the RSA public key is fetched from.</summary>
    public string JwksUrl { get; set; } = "http://localhost:5300/auth/v1/.well-known/jwks.json";
}
