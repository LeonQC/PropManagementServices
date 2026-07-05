namespace AuthService.Models;

/// <summary>
/// A refresh token issued at login. The raw opaque token is never stored — only
/// its SHA-256 hash (<see cref="TokenHash"/>). Tokens are rotated on /refresh
/// (the old one is revoked and linked to its successor) and revoked on /logout.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash (hex) of the opaque token handed to the client.</summary>
    public required string TokenHash { get; set; }

    public required string CreatedAt { get; set; }
    public required string ExpiresAt { get; set; }

    public string? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one on rotation, if any.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public ApplicationUser? User { get; set; }
}
