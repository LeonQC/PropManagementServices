using Microsoft.AspNetCore.Identity;

namespace AuthService.Models;

/// <summary>
/// The user aggregate. Extends the ASP.NET Identity user (Guid key) with the
/// profile fields the /me endpoint exposes. Identity owns email, password hash,
/// security stamp, etc.; everything here is PropTrack-specific.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>ISO-8601 UTC creation timestamp.</summary>
    public string? CreatedAt { get; set; }
}
