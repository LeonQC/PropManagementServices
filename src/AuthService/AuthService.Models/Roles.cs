namespace AuthService.Models;

/// <summary>
/// The fixed set of PropTrack roles (architecture §2.1 / §5.3). These are seeded
/// as Identity roles on startup and referenced by [Authorize(Roles = ...)].
/// </summary>
public static class Roles
{
    public const string Analyst = "Analyst";
    public const string Associate = "Associate";
    public const string VP = "VP";
    public const string Principal = "Principal";
    public const string ManagingDirector = "Managing Director";
    public const string Admin = "Admin";

    public static readonly string[] All =
        [Analyst, Associate, VP, Principal, ManagingDirector, Admin];
}
