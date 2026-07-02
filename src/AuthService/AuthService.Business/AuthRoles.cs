using AuthService.Models;

namespace AuthService.Business;

/// <summary>
/// Role-name constants re-exported for the Api layer's [Authorize(Roles = ...)]
/// attributes. The Api can't reference Models directly (strict N-tier), so it uses
/// these. Values mirror <see cref="Roles"/>.
/// </summary>
public static class AuthRoles
{
    public const string Analyst = Roles.Analyst;
    public const string Associate = Roles.Associate;
    public const string VP = Roles.VP;
    public const string Principal = Roles.Principal;
    public const string ManagingDirector = Roles.ManagingDirector;
    public const string Admin = Roles.Admin;
}
