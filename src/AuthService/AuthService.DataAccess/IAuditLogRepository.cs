using AuthService.Models;

namespace AuthService.DataAccess;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);
}

public class AuditLogRepository(AuthDbContext db) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        entry.Id = Guid.NewGuid();
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
