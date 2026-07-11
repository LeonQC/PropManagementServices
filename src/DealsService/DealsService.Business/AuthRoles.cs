namespace DealsService.Business;

/// <summary>
/// PropTrack role names as issued in the auth-service JWT "role" claim. Values
/// must match AuthService.Models.Roles exactly (note "Managing Director" contains
/// a space). Re-declared here because services never reference each other.
/// </summary>
public static class AuthRoles
{
    public const string Analyst = "Analyst";
    public const string Associate = "Associate";
    public const string VP = "VP";
    public const string Principal = "Principal";
    public const string ManagingDirector = "Managing Director";
    public const string Admin = "Admin";

    /// <summary>Roles allowed to kill a deal (authorization matrix §5.3: Associate and up).</summary>
    public const string KillDeal =
        $"{Associate},{VP},{Principal},{ManagingDirector},{Admin}";
}
