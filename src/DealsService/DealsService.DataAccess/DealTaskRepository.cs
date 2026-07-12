using DealsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DealsService.DataAccess;

public class DealTaskRepository(DealsDbContext db) : IDealTaskRepository
{
    public Task<List<DealTask>> GetByDealAsync(string dealId, CancellationToken ct = default)
        => db.DealTasks
            .Where(t => t.DealId == dealId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync(ct);

    public Task<DealTask?> GetByIdAsync(string dealId, string taskId, CancellationToken ct = default)
        => db.DealTasks.FirstOrDefaultAsync(t => t.DealId == dealId && t.Id == taskId, ct);

    public async Task<DealTask> CreateAsync(DealTask task, CancellationToken ct = default)
    {
        task.Id = Guid.NewGuid().ToString();
        db.DealTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public Task UpdateAsync(DealTask task, CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
