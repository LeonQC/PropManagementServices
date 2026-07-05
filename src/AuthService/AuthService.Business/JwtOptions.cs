namespace AuthService.Business;

/// <summary>JWT issuance/validation settings, bound from the "Jwt" config section.</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "proptrack-auth";
    public string Audience { get; set; } = "proptrack";

    /// <summary>Path to the RSA private key PEM, injected as a secret at container
    /// startup (architecture §6.2). When empty/missing, an ephemeral dev key is
    /// generated — never use that in production.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Key id published in the JWKS and stamped into token headers.</summary>
    public string KeyId { get; set; } = "proptrack-auth-key";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
