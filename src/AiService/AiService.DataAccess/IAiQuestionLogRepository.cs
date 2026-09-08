using AiService.Models;

namespace AiService.DataAccess;

public interface IAiQuestionLogRepository
{
    Task AddAsync(AiQuestionLog entry, CancellationToken ct = default);
}

public class AiQuestionLogRepository(AiDbContext db) : IAiQuestionLogRepository
{
    public async Task AddAsync(AiQuestionLog entry, CancellationToken ct = default)
    {
        entry.Id = Guid.NewGuid().ToString();
        db.AiQuestionLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
