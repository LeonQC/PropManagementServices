using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.DataAccess;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task UpdateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Revoke every active (non-revoked, unexpired) token for a user — used on logout.</summary>
    Task RevokeAllForUserAsync(Guid userId, string revokedAtUtc, CancellationToken ct = default);
}

public class RefreshTokenRepository(AuthDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Id = Guid.NewGuid();
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }

    public Task RevokeAllForUserAsync(Guid userId, string revokedAtUtc, CancellationToken ct = default) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAtUtc), ct);
}
