using DealsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DealsService.DataAccess;

public class DealRepository(DealsDbContext db) : IDealRepository
{
    public async Task<DealWithTaskStats?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var row = await db.Deals
            .Where(d => d.Id == id)
            .Select(d => new DealWithTaskStats(
                d,
                d.Tasks.Count,
                d.Tasks.Count(t => t.Status == "Done"),
                d.Tasks.Any(t => t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0)))
            .FirstOrDefaultAsync(ct);
        return row;
    }

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        => db.Deals.AnyAsync(d => d.Id == id, ct);

    public Task<bool> HasActiveDealForPropertyAsync(string propertyId, CancellationToken ct = default)
        => db.Deals.AnyAsync(d => d.PropertyId == propertyId && d.Stage != "Acquired" && d.Stage != "Dead", ct);

    public async Task<(List<DealWithTaskStats> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize,
        string? stage = null,
        string? ownerId = null,
        string? priority = null,
        CancellationToken ct = default)
    {
        var query = db.Deals.AsQueryable();

        if (!string.IsNullOrEmpty(stage))
            query = query.Where(d => d.Stage == stage);

        if (!string.IsNullOrEmpty(ownerId))
            query = query.Where(d => d.OwnerId == ownerId);

        if (!string.IsNullOrEmpty(priority))
            query = query.Where(d => d.Priority == priority);

        var ordered = query.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id);

        var totalCount = await ordered.CountAsync(ct);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DealWithTaskStats(
                d,
                d.Tasks.Count,
                d.Tasks.Count(t => t.Status == "Done"),
                d.Tasks.Any(t => t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0)))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<Deal> CreateAsync(Deal deal, DealStageHistory initialHistory, List<DealTask> templateTasks,
        CancellationToken ct = default)
    {
        deal.Id = Guid.NewGuid().ToString();
        initialHistory.Id = Guid.NewGuid().ToString();
        initialHistory.DealId = deal.Id;
        foreach (var task in templateTasks)
        {
            task.Id = Guid.NewGuid().ToString();
            task.DealId = deal.Id;
        }

        db.Deals.Add(deal);
        db.DealStageHistory.Add(initialHistory);
        db.DealTasks.AddRange(templateTasks);
        await db.SaveChangesAsync(ct);
        return deal;
    }

    public Task UpdateAsync(Deal deal, CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public async Task TransitionAsync(Deal deal, DealStageHistory historyRow, List<DealTask> newTasks,
        CancellationToken ct = default)
    {
        historyRow.Id = Guid.NewGuid().ToString();
        historyRow.DealId = deal.Id;
        foreach (var task in newTasks)
        {
            task.Id = Guid.NewGuid().ToString();
            task.DealId = deal.Id;
        }

        db.DealStageHistory.Add(historyRow);
        db.DealTasks.AddRange(newTasks);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<DealStageHistory>> GetHistoryAsync(string dealId, CancellationToken ct = default)
        => db.DealStageHistory
            .Where(h => h.DealId == dealId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(ct);

    public Task<List<StageAggregate>> GetPipelineSummaryAsync(CancellationToken ct = default)
        => db.Deals
            .GroupBy(d => d.Stage)
            .Select(g => new StageAggregate(g.Key, g.Count(), g.Sum(d => d.OfferPrice) ?? 0))
            .ToListAsync(ct);
}
