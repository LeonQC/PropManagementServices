namespace DealsService.Business.DTOs;

public record CreateDealDto(
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    string? Name,
    string? Priority,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate);

/// <summary>Partial update — only non-null fields are applied. Stage and DeadReason
/// deliberately absent: transitions go through Advance/Kill only.</summary>
public record UpdateDealDto(
    string? Name,
    string? Priority,
    string? OwnerId,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate);

public record DealDto(
    string Id,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    string Stage,
    string Priority,
    string OwnerId,
    string? DeadReason,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate,
    double? AiScore,
    string? AiScoreRationale,
    string? RiskFlags,
    string StageEnteredAt,
    string CreatedAt,
    string? UpdatedAt,
    int TaskCount,
    int DoneTaskCount,
    bool HasOverdueTasks);

public record StageHistoryDto(
    string Id,
    string? FromStage,
    string ToStage,
    string ChangedById,
    string ChangedAt,
    int? DaysInStage,
    string? Reason);

public record StageSummaryDto(string Stage, int Count, double TotalValue);

public record PipelineSummaryDto(
    int TotalActiveDeals,
    double TotalPipelineValue,
    IReadOnlyList<StageSummaryDto> Stages);
