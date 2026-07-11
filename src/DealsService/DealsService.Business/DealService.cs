using DealsService.Business.Domain;
using DealsService.Business.DTOs;
using DealsService.Business.Events;
using DealsService.DataAccess;
using DealsService.Models;
using PropTrack.Messaging;

namespace DealsService.Business;

public class DealService(IDealRepository repo, IEventPublisher eventPublisher)
{
    public async Task<(List<DealDto> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize, string? stage, string? ownerId, string? priority,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await repo.GetAllAsync(page, pageSize, stage, ownerId, priority, ct);
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

        var now = Now();
        var deal = new Deal
        {
            Id = "",
            Name = string.IsNullOrWhiteSpace(input.Name) ? $"{input.PropertyName} Acquisition" : input.Name!,
            PropertyId = input.PropertyId,
            PropertyName = input.PropertyName,
            PropertyType = input.PropertyType,
            MetroArea = input.MetroArea,
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

        var created = await repo.CreateAsync(deal, initialHistory, templateTasks, ct);

        await eventPublisher.PublishAsync(Topics.DealCreated, created.PropertyId,
            new DealCreated(created.PropertyId, created.Id), ct);

        return ServiceResult<DealDto>.Ok(MapToDto(
            new DealWithTaskStats(created, templateTasks.Count, 0, false)));
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

        var deal = row.Deal;
        if (input.Name is not null) deal.Name = input.Name;
        if (input.Priority is not null) deal.Priority = input.Priority;
        if (input.OwnerId is not null) deal.OwnerId = input.OwnerId;
        if (input.OfferPrice is not null) deal.OfferPrice = input.OfferPrice;
        if (input.ProjectedCapRate is not null) deal.ProjectedCapRate = input.ProjectedCapRate;
        if (input.TargetIrr is not null) deal.TargetIrr = input.TargetIrr;
        if (input.EquityMultiple is not null) deal.EquityMultiple = input.EquityMultiple;
        if (input.ProjectedCloseDate is not null) deal.ProjectedCloseDate = input.ProjectedCloseDate;
        deal.UpdatedAt = Now();

        await repo.UpdateAsync(deal, ct);
        return ServiceResult<DealDto>.Ok(MapToDto(row));
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

        // Re-read so task rollups include the freshly templated tasks.
        var refreshed = await repo.GetByIdAsync(id, ct);
        return ServiceResult<DealDto>.Ok(MapToDto(refreshed ?? row));
    }

    public async Task<ServiceResult<DealDto>> KillAsync(string id, string reason,
        string? expectedCurrentStage, string actorId, CancellationToken ct = default)
    {
        if (!DeadReasons.All.Contains(reason))
            return ServiceResult<DealDto>.Fail(ErrorCodes.Validation, "Invalid kill reason.",
                [new FieldError("reason", $"reason must be one of: {string.Join(", ", DeadReasons.All)}.")]);

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null)
            return ServiceResult<DealDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

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

        return ServiceResult<DealDto>.Ok(MapToDto(row));
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
            d.Stage, d.Priority, d.OwnerId, d.DeadReason,
            d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
            d.AiScore, d.AiScoreRationale, d.RiskFlags,
            d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
            row.TaskCount, row.DoneTaskCount, row.HasOverdueTasks);
    }
}
