using AuthService.Models;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Business.Security;

/// <summary>
/// Replaces Identity's default PBKDF2 hasher with bcrypt, per architecture §6.1.
/// Registered as IPasswordHasher&lt;ApplicationUser&gt; so UserManager uses it for
/// both hashing (register) and verification (login).
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher<ApplicationUser>
{
    public string HashPassword(ApplicationUser user, string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    public PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user, string hashedPassword, string providedPassword)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Stored hash isn't a bcrypt hash (e.g. legacy/corrupt) — treat as a failure.
            return PasswordVerificationResult.Failed;
        }
    }
}
