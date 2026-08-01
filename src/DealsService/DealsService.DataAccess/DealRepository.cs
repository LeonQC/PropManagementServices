using System.Text.RegularExpressions;
using DealsService.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace DealsService.DataAccess;

public class DealRepository(DealsDbContext db) : IDealRepository
{
    /// <summary>Deal plus task rollups straight out of SQL, before the health flags are
    /// layered on in memory. Kept private: callers only ever see DealWithTaskStats.</summary>
    private record DealRow(Deal Deal, int TaskCount, int DoneTaskCount, bool HasOverdueTasks);

    public async Task<DealWithTaskStats?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var row = await db.Deals
            .Where(d => d.Id == id)
            .Select(d => new DealRow(
                d,
                d.Tasks.Count,
                d.Tasks.Count(t => t.Status == "Done"),
                d.Tasks.Any(t => t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0)))
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var dwellAverages = await GetStageDwellAveragesAsync(ct);
        return WithHealth(row, dwellAverages, DateTime.UtcNow);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        => db.Deals.AnyAsync(d => d.Id == id, ct);

    public Task<bool> HasActiveDealForPropertyAsync(string propertyId, CancellationToken ct = default)
        => db.Deals.AnyAsync(d => d.PropertyId == propertyId && d.Stage != "Acquired" && d.Stage != "Dead", ct);

    public async Task<(List<DealWithTaskStats> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize, DealQuery filters, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        var query = db.Deals.AsQueryable();

        if (!string.IsNullOrEmpty(filters.Stage))
            query = query.Where(d => d.Stage == filters.Stage);

        if (!string.IsNullOrEmpty(filters.OwnerId))
            query = query.Where(d => d.OwnerId == filters.OwnerId);

        if (!string.IsNullOrEmpty(filters.Priority))
            query = query.Where(d => d.Priority == filters.Priority);

        if (!string.IsNullOrEmpty(filters.PropertyType))
            query = query.Where(d => d.PropertyType == filters.PropertyType);

        if (!string.IsNullOrEmpty(filters.MetroArea))
            query = query.Where(d => d.MetroArea == filters.MetroArea);

        // Dates are stored as strings — "yyyy-MM-dd" for close dates, round-trip ISO for
        // the timestamps. Both formats are fixed-width and UTC, so ordinal string
        // comparison is the same ordering as the dates themselves (the trick the
        // overdue-task predicate above already relies on).
        if (!string.IsNullOrEmpty(filters.CloseDateBefore))
            query = query.Where(d => d.ProjectedCloseDate != null
                && d.ProjectedCloseDate.CompareTo(filters.CloseDateBefore) < 0);

        if (!string.IsNullOrEmpty(filters.CloseDateAfter))
            query = query.Where(d => d.ProjectedCloseDate != null
                && d.ProjectedCloseDate.CompareTo(filters.CloseDateAfter) > 0);

        if (filters.OfferPriceMin.HasValue)
            query = query.Where(d => d.OfferPrice >= filters.OfferPriceMin.Value);

        if (filters.OfferPriceMax.HasValue)
            query = query.Where(d => d.OfferPrice <= filters.OfferPriceMax.Value);

        if (filters.CapRateMin.HasValue)
            query = query.Where(d => d.ProjectedCapRate >= filters.CapRateMin.Value);

        if (filters.CapRateMax.HasValue)
            query = query.Where(d => d.ProjectedCapRate <= filters.CapRateMax.Value);

        // Overdue is a property of the deal's tasks, so it filters in SQL rather than
        // after paging — otherwise the total count and the page would disagree.
        if (filters.HasOverdueTasks == true)
            query = query.Where(d => d.Tasks.Any(t =>
                t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0));
        else if (filters.HasOverdueTasks == false)
            query = query.Where(d => !d.Tasks.Any(t =>
                t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0));

        // staleDays=N → entered the current stage at least N days ago.
        if (filters.StaleDays is int staleDays && staleDays > 0)
        {
            var cutoff = now.AddDays(-staleDays).ToString("O");
            query = query.Where(d => d.StageEnteredAt.CompareTo(cutoff) < 0);
        }

        // Full-text search over the GIN-indexed generated tsvector (name^A,
        // property_name^B — see DealsDbContext), ANDed with the filters above.
        // BuildTsQuery sanitizes free text to lexemes, so the string handed to
        // to_tsquery as a parameter can never carry tsquery syntax.
        var tsQuery = BuildTsQuery(filters.Q);
        if (tsQuery != null)
            query = query.Where(d =>
                EF.Property<NpgsqlTsVector>(d, "SearchVector")
                    .Matches(EF.Functions.ToTsQuery("english", tsQuery)));

        // With a keyword present, rank by relevance; otherwise keep the board's
        // newest-first order. Both end in an Id tiebreaker so paging is stable.
        var ordered = tsQuery is null
            ? query.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            : query.OrderByDescending(d => EF.Property<NpgsqlTsVector>(d, "SearchVector")
                    .Rank(EF.Functions.ToTsQuery("english", tsQuery)))
                .ThenBy(d => d.Id);

        var totalCount = await ordered.CountAsync(ct);
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DealRow(
                d,
                d.Tasks.Count,
                d.Tasks.Count(t => t.Status == "Done"),
                d.Tasks.Any(t => t.Status != "Done" && t.DueDate != null && t.DueDate.CompareTo(today) < 0)))
            .ToListAsync(ct);

        // One aggregate for the whole page, not one per deal.
        var dwellAverages = await GetStageDwellAveragesAsync(ct);
        var items = rows.Select(r => WithHealth(r, dwellAverages, now)).ToList();

        return (items, totalCount);
    }

    /// <summary>
    /// Mean days spent in each stage, split by the property type of the deal that passed
    /// through it — the baseline the stale-stage flag compares against. Reads the
    /// append-only history: FromStage is the stage that was left, DaysInStage how long it
    /// took. Both are null on the creation row and on OWNER_TRANSFER rows, which is
    /// exactly why those are filtered out.
    /// </summary>
    private Task<List<StageDwellAverage>> GetStageDwellAveragesAsync(CancellationToken ct = default)
        => (from h in db.DealStageHistory
            join d in db.Deals on h.DealId equals d.Id
            where h.FromStage != null && h.DaysInStage != null
            group h by new { Stage = h.FromStage!, d.PropertyType } into g
            select new StageDwellAverage(
                g.Key.Stage,
                g.Key.PropertyType,
                g.Average(x => (double?)x.DaysInStage) ?? 0,
                g.Count()))
           .ToListAsync(ct);

    private static DealWithTaskStats WithHealth(
        DealRow row, IReadOnlyList<StageDwellAverage> dwellAverages, DateTime nowUtc) =>
        new(row.Deal, row.TaskCount, row.DoneTaskCount, row.HasOverdueTasks,
            DealHealth.Evaluate(row.Deal, row.HasOverdueTasks, dwellAverages, nowUtc));

    /// <summary>
    /// Turns a raw keyword string into a Postgres tsquery: sanitized to alphanumeric
    /// lexemes, AND'd together, with the final token made a prefix (term:*) for typeahead.
    /// Returns null when there's nothing searchable (so callers skip the filter).
    /// Same implementation as listings-service's PropertyRepository — punctuation like
    /// "a-b c'd" becomes plain lexemes rather than reaching to_tsquery as syntax.
    /// </summary>
    private static string? BuildTsQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return null;

        var terms = Regex.Split(q.Trim().ToLowerInvariant(), "[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToList();
        if (terms.Count == 0) return null;

        terms[^1] += ":*";                 // last token as a prefix, for as-you-type matching
        return string.Join(" & ", terms);  // AND semantics across tokens
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
