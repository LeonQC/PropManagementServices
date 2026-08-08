using DealsService.Business.Domain;
using DealsService.Business.DTOs;
using DealsService.Business.Events;
using DealsService.DataAccess;
using DealsService.Models;
using PropTrack.Messaging;

namespace DealsService.Business;

public class DealService(IDealRepository repo, IEventPublisher eventPublisher, DealSnapshotPublisher snapshots)
{
    public async Task<(List<DealDto> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize, DealFilterDto filters, CancellationToken ct = default)
    {
        var (items, totalCount) = await repo.GetAllAsync(page, pageSize, new DealQuery(
            filters.Stage, filters.OwnerId, filters.Priority, filters.PropertyType, filters.MetroArea,
            filters.CloseDateBefore, filters.CloseDateAfter, filters.OfferPriceMin, filters.OfferPriceMax,
            filters.CapRateMin, filters.CapRateMax, filters.HasOverdueTasks, filters.StaleDays,
            filters.Q), ct);
        return (items.Select(MapToDto).ToList(), totalCount);
    }

    public async Task<DealDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var row = await repo.GetByIdAsync(id, ct);
        return row is null ? null : MapToDto(row);
    }

    public async Task<ServiceResult<DealDto>> CreateAsync(CreateDealDto input, string actorId,
        CancellationToken ct = default)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.PropertyId))
            errors.Add(new FieldError("propertyId", "propertyId is required."));
        if (string.IsNullOrWhiteSpace(input.PropertyName))
            errors.Add(new FieldError("propertyName", "propertyName is required."));
        var priority = input.Priority ?? DealPriorities.Medium;
        if (!DealPriorities.All.Contains(priority))
            errors.Add(new FieldError("priority", $"priority must be one of: {string.Join(", ", DealPriorities.All)}."));
        if (errors.Count > 0)
            return ServiceResult<DealDto>.Fail(ErrorCodes.Validation, "Invalid deal.", errors);

        // One live acquisition per property at a time. The UI hides the entry
        // points, but this is the authoritative check (Swagger/stale clients).
        if (await repo.HasActiveDealForPropertyAsync(input.PropertyId, ct))
            return ServiceResult<DealDto>.Fail(ErrorCodes.Conflict,
                "This property already has an active deal.");

        var now = Now();
        var deal = new Deal
        {
            Id = "",
            Name = string.IsNullOrWhiteSpace(input.Name) ? $"{input.PropertyName} Acquisition" : input.Name!,
            PropertyId = input.PropertyId,
            PropertyName = input.PropertyName,
            PropertyType = input.PropertyType,
            MetroArea = input.MetroArea,
            OccupancyRate = input.OccupancyRate,
            MarketCapRateBenchmark = input.MarketCapRateBenchmark,
            Stage = DealStages.InitialInterest,
            Priority = priority,
            OwnerId = actorId,
            OfferPrice = input.OfferPrice,
            ProjectedCapRate = input.ProjectedCapRate,
            TargetIrr = input.TargetIrr,
            EquityMultiple = input.EquityMultiple,
            ProjectedCloseDate = input.ProjectedCloseDate,
            StageEnteredAt = now,
            CreatedAt = now,
        };

        var initialHistory = new DealStageHistory
        {
            Id = "",
            DealId = "",
            FromStage = null,
            ToStage = DealStages.InitialInterest,
            ChangedById = actorId,
            ChangedAt = now,
        };

        var templateTasks = StageTaskTemplates.Materialize(DealStages.InitialInterest, now);

        Deal created;
        try
        {
            created = await repo.CreateAsync(deal, initialHistory, templateTasks, ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Two creates raced past the check above; the partial unique index
            // on deals(property_id) rejected the second insert.
            return ServiceResult<DealDto>.Fail(ErrorCodes.Conflict,
                "This property already has an active deal.");
        }

        await eventPublisher.PublishAsync(Topics.DealCreated, created.PropertyId,
            new DealCreated(created.PropertyId, created.Id), ct);

        // Re-read rather than project from `created`: the template tasks were inserted
        // alongside it, and the snapshot carries their rollups.
        await snapshots.ReloadAndPublishAsync(created.Id, ct);

        // A brand-new deal has no dwell history and no overdue tasks, so its flag set
        // is whatever the snapshotted metrics alone imply.
        return ServiceResult<DealDto>.Ok(MapToDto(new DealWithTaskStats(
            created, templateTasks.Count, 0, false,
            DealHealth.Evaluate(created, false, [], DateTime.UtcNow))));
    }

    public async Task<ServiceResult<DealDto>> UpdateAsync(string id, UpdateDealDto input,
        CancellationToken ct = default)
    {
        var row = await repo.GetByIdAsync(id, ct);
        if (row is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        if (input.Priority is not null && !DealPriorities.All.Contains(input.Priority))
            return ServiceResult<DealDto>.Fail(ErrorCodes.Validation, "Invalid deal.",
                [new FieldError("priority", $"priority must be one of: {string.Join(", ", DealPriorities.All)}.")]);

        // Merge only the fields the caller sent, and record which ones actually moved so
        // deal.updated can tell a real edit from an idempotent PUT.
        var deal = row.Deal;
        var changed = new List<string>();
        if (input.Name is not null && input.Name != deal.Name)
        { deal.Name = input.Name; changed.Add("name"); }
        if (input.Priority is not null && input.Priority != deal.Priority)
        { deal.Priority = input.Priority; changed.Add("priority"); }
        if (input.OfferPrice is not null && input.OfferPrice != deal.OfferPrice)
        { deal.OfferPrice = input.OfferPrice; changed.Add("offerPrice"); }
        if (input.ProjectedCapRate is not null && input.ProjectedCapRate != deal.ProjectedCapRate)
        { deal.ProjectedCapRate = input.ProjectedCapRate; changed.Add("projectedCapRate"); }
        if (input.TargetIrr is not null && input.TargetIrr != deal.TargetIrr)
        { deal.TargetIrr = input.TargetIrr; changed.Add("targetIrr"); }
        if (input.EquityMultiple is not null && input.EquityMultiple != deal.EquityMultiple)
        { deal.EquityMultiple = input.EquityMultiple; changed.Add("equityMultiple"); }
        if (input.ProjectedCloseDate is not null && input.ProjectedCloseDate != deal.ProjectedCloseDate)
        { deal.ProjectedCloseDate = input.ProjectedCloseDate; changed.Add("projectedCloseDate"); }

        if (changed.Count == 0)
            return ServiceResult<DealDto>.Ok(MapToDto(row));

        deal.UpdatedAt = Now();
        await repo.UpdateAsync(deal, ct);

        await eventPublisher.PublishAsync(Topics.DealUpdated, deal.Id,
            new DealUpdated(deal.Id, deal.PropertyId, changed, deal.UpdatedAt!), ct);

        // Unlike deal.updated above, the snapshot isn't gated on which fields moved — but it
        // still sits below the no-op early return, because an idempotent PUT changes nothing
        // and doesn't bump the version.
        await snapshots.ReloadAndPublishAsync(id, ct);

        // Re-read: an edited offer price or cap rate can flip a health flag, and the
        // caller renders the response directly.
        var refreshed = await repo.GetByIdAsync(id, ct);
        return ServiceResult<DealDto>.Ok(MapToDto(refreshed ?? row));
    }

    public async Task<ServiceResult<DealDto>> AdvanceAsync(string id, string? expectedCurrentStage,
        string actorId, CancellationToken ct = default)
    {
        var row = await repo.GetByIdAsync(id, ct);
        if (row is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        var deal = row.Deal;
        if (expectedCurrentStage is not null && deal.Stage != expectedCurrentStage)
            return ServiceResult<DealDto>.Fail(ErrorCodes.Conflict,
                $"Deal is in stage {deal.Stage}, not {expectedCurrentStage}. Refresh and retry.");

        var next = DealStages.Next(deal.Stage);
        if (next is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.InvalidTransition,
                $"Cannot advance a deal in stage {deal.Stage}.");

        var now = Now();
        var fromStage = deal.Stage;
        var daysInStage = DaysBetween(deal.StageEnteredAt, now);

        deal.Stage = next;
        deal.StageEnteredAt = now;
        deal.UpdatedAt = now;

        var historyRow = new DealStageHistory
        {
            Id = "",
            DealId = deal.Id,
            FromStage = fromStage,
            ToStage = next,
            ChangedById = actorId,
            ChangedAt = now,
            DaysInStage = daysInStage,
        };

        var newTasks = StageTaskTemplates.Materialize(next, now);
        await repo.TransitionAsync(deal, historyRow, newTasks, ct);

        await eventPublisher.PublishAsync(Topics.DealStageChanged, deal.Id,
            new DealStageChanged(deal.Id, deal.PropertyId, fromStage, next, actorId, now, null, daysInStage), ct);

        if (next == DealStages.Acquired)
            await eventPublisher.PublishAsync(Topics.DealOutcomeRecorded, deal.PropertyId,
                new DealOutcomeRecorded(deal.PropertyId, deal.Id, "won"), ct);

        await snapshots.ReloadAndPublishAsync(id, ct);

        // Re-read so task rollups include the freshly templated tasks.
        var refreshed = await repo.GetByIdAsync(id, ct);
        return ServiceResult<DealDto>.Ok(MapToDto(refreshed ?? row));
    }

    public async Task<ServiceResult<DealDto>> KillAsync(string id, string reason,
        string? expectedCurrentStage, string actorId, bool isElevated, CancellationToken ct = default)
    {
        if (!DeadReasons.All.Contains(reason))
            return ServiceResult<DealDto>.Fail(ErrorCodes.Validation, "Invalid kill reason.",
                [new FieldError("reason", $"reason must be one of: {string.Join(", ", DeadReasons.All)}.")]);

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        if (!CanActOnDeal(row.Deal, actorId, isElevated))
            return ForbiddenResult();

        var deal = row.Deal;
        if (expectedCurrentStage is not null && deal.Stage != expectedCurrentStage)
            return ServiceResult<DealDto>.Fail(ErrorCodes.Conflict,
                $"Deal is in stage {deal.Stage}, not {expectedCurrentStage}. Refresh and retry.");

        if (DealStages.IsTerminal(deal.Stage))
            return ServiceResult<DealDto>.Fail(ErrorCodes.InvalidTransition,
                $"Cannot kill a deal in terminal stage {deal.Stage}.");

        var now = Now();
        var fromStage = deal.Stage;
        var daysInStage = DaysBetween(deal.StageEnteredAt, now);

        deal.Stage = DealStages.Dead;
        deal.DeadReason = reason;
        deal.StageEnteredAt = now;
        deal.UpdatedAt = now;

        var historyRow = new DealStageHistory
        {
            Id = "",
            DealId = deal.Id,
            FromStage = fromStage,
            ToStage = DealStages.Dead,
            ChangedById = actorId,
            ChangedAt = now,
            DaysInStage = daysInStage,
            Reason = reason,
        };

        await repo.TransitionAsync(deal, historyRow, [], ct);

        await eventPublisher.PublishAsync(Topics.DealStageChanged, deal.Id,
            new DealStageChanged(deal.Id, deal.PropertyId, fromStage, DealStages.Dead, actorId, now, reason, daysInStage), ct);
        await eventPublisher.PublishAsync(Topics.DealOutcomeRecorded, deal.PropertyId,
            new DealOutcomeRecorded(deal.PropertyId, deal.Id, "lost"), ct);

        await snapshots.ReloadAndPublishAsync(id, ct);

        return ServiceResult<DealDto>.Ok(MapToDto(row));
    }

    /// <summary>
    /// Reassigns the deal owner. Elevated callers only (the controller gates via
    /// [Authorize(Roles = AuthRoles.DealAdmin)]; re-checked here). Writes a same-stage
    /// history row with the OWNER_TRANSFER reason sentinel. newOwnerId is validated
    /// for shape only — the auth-service owns the user directory and services do not
    /// call each other, so existence is not verified.
    /// </summary>
    public async Task<ServiceResult<DealDto>> TransferOwnerAsync(string id, string newOwnerId,
        string actorId, bool isElevated, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newOwnerId) || !Guid.TryParse(newOwnerId, out _))
            return ServiceResult<DealDto>.Fail(ErrorCodes.Validation, "Invalid transfer.",
                [new FieldError("newOwnerId", "newOwnerId must be a user id (GUID).")]);

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        if (!isElevated)
            return ServiceResult<DealDto>.Fail(ErrorCodes.Forbidden,
                "Only an Admin or Managing Director can transfer deal ownership.");

        var deal = row.Deal;
        if (deal.OwnerId == newOwnerId)
            return ServiceResult<DealDto>.Ok(MapToDto(row));

        var now = Now();
        var previousOwnerId = deal.OwnerId;
        deal.OwnerId = newOwnerId;
        deal.UpdatedAt = now;

        var historyRow = new DealStageHistory
        {
            Id = "",
            DealId = deal.Id,
            FromStage = deal.Stage,
            ToStage = deal.Stage,
            ChangedById = actorId,
            ChangedAt = now,
            Reason = OwnershipTransfer.Reason(previousOwnerId, newOwnerId),
        };

        await repo.TransitionAsync(deal, historyRow, [], ct);

        // The only mutation with no business event of its own — but ownerId is an indexed,
        // filterable field, so without this the index silently keeps the old owner.
        await snapshots.ReloadAndPublishAsync(id, ct);

        return ServiceResult<DealDto>.Ok(MapToDto(row));
    }

    /// <summary>
    /// Republishes every deal's snapshot, for backfilling a new index or repairing drift.
    /// Pages so a large pipeline doesn't load in one go, and returns how many it emitted so
    /// the caller can compare against the row count.
    /// </summary>
    public async Task<int> RepublishAllAsync(CancellationToken ct = default)
    {
        const int pageSize = 200;
        var page = 1;
        var published = 0;

        while (true)
        {
            var (items, totalCount) = await repo.GetAllForReindexAsync(page, pageSize, ct);
            if (items.Count == 0) break;

            foreach (var row in items)
            {
                await snapshots.PublishAsync(row, ct);
                published++;
            }

            if (published >= totalCount) break;
            page++;
        }

        return published;
    }

    public async Task<List<StageHistoryDto>?> GetHistoryAsync(string dealId, CancellationToken ct = default)
    {
        if (!await repo.ExistsAsync(dealId, ct)) return null;
        var rows = await repo.GetHistoryAsync(dealId, ct);
        return rows.Select(h => new StageHistoryDto(
            h.Id, h.FromStage, h.ToStage, h.ChangedById, h.ChangedAt, h.DaysInStage, h.Reason)).ToList();
    }

    public async Task<PipelineSummaryDto> GetPipelineSummaryAsync(CancellationToken ct = default)
    {
        var aggregates = await repo.GetPipelineSummaryAsync(ct);
        var byStage = aggregates.ToDictionary(a => a.Stage);

        // Emit every stage in board order so the UI never has to fill gaps.
        var stages = DealStages.All
            .Select(s => byStage.TryGetValue(s, out var a)
                ? new StageSummaryDto(s, a.Count, a.TotalValue)
                : new StageSummaryDto(s, 0, 0))
            .ToList();

        // "Active" = still moving through the pipeline: terminal stages excluded.
        var active = stages.Where(s => !DealStages.IsTerminal(s.Stage)).ToList();
        return new PipelineSummaryDto(
            active.Sum(s => s.Count),
            active.Sum(s => s.TotalValue),
            stages);
    }

    /// <summary>Owner check (authorization matrix): the deal owner, or an elevated
    /// caller (Admin / Managing Director), may perform destructive actions.</summary>
    private static bool CanActOnDeal(Deal deal, string actorId, bool isElevated) =>
        isElevated || deal.OwnerId == actorId;

    private static ServiceResult<DealDto> ForbiddenResult() =>
        ServiceResult<DealDto>.Fail(ErrorCodes.Forbidden,
            "Only the deal owner (or an Admin/Managing Director) can perform this action.");

    private static string Now() => DateTime.UtcNow.ToString("O");

    private static int? DaysBetween(string fromIso, string toIso)
    {
        if (DateTime.TryParse(fromIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var from) &&
            DateTime.TryParse(toIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var to))
            return Math.Max(0, (int)(to - from).TotalDays);
        return null;
    }

    private static DealDto MapToDto(DealWithTaskStats row)
    {
        var d = row.Deal;
        return new DealDto(
            d.Id, d.Name, d.PropertyId, d.PropertyName, d.PropertyType, d.MetroArea,
            d.OccupancyRate, d.MarketCapRateBenchmark,
            d.Stage, d.Priority, d.OwnerId, d.DeadReason,
            d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
            d.AiScore, d.AiScoreRationale, d.RiskFlags,
            d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
            row.TaskCount, row.DoneTaskCount, row.HasOverdueTasks,
            row.HealthFlags.Select(f => new HealthFlagDto(f.Type, f.Severity, f.Message)).ToList());
    }
}
