using AiService.Models;

namespace AiService.DataAccess;

public interface IAiRequestLogRepository
{
    Task AddAsync(AiRequestLog entry, CancellationToken ct = default);
}

public class AiRequestLogRepository(AiDbContext db) : IAiRequestLogRepository
{
    public async Task AddAsync(AiRequestLog entry, CancellationToken ct = default)
    {
        entry.Id = Guid.NewGuid().ToString();
        db.AiRequestLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
