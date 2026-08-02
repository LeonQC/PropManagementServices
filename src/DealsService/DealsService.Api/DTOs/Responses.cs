namespace DealsService.Api.DTOs;

/// <summary>A deterministic health signal on a deal (design doc §6.6). Type is one of
/// stale_stage, overdue_tasks, expiring_loi, cap_rate_compression, low_occupancy;
/// severity is "warning" or "critical". Computed per request, never persisted.</summary>
public record HealthFlagResponse(string Type, string Severity, string Message);

public record DealResponse(
    string Id,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
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
    bool HasOverdueTasks,
    IReadOnlyList<HealthFlagResponse> HealthFlags);

public record StageHistoryResponse(
    string Id,
    string? FromStage,
    string ToStage,
    string ChangedById,
    string ChangedAt,
    int? DaysInStage,
    string? Reason);

public record TaskResponse(
    string Id,
    string DealId,
    string Title,
    string Stage,
    string Status,
    string? AssigneeId,
    string? DueDate,
    bool IsFromTemplate,
    string CreatedAt,
    string? CompletedAt);

public record CommentResponse(
    string Id,
    string DealId,
    string? ParentId,
    string Body,
    string AuthorId,
    bool IsAiGenerated,
    string CreatedAt);

public record DocumentResponse(
    string Id,
    string DealId,
    string FileName,
    string FileType,
    string? StorageUrl,
    string? AiSummary,
    string UploadedById,
    string UploadedAt);

public record StageSummaryResponse(string Stage, int Count, double TotalValue);

public record PipelineSummaryResponse(
    int TotalActiveDeals,
    double TotalPipelineValue,
    IReadOnlyList<StageSummaryResponse> Stages);

public record PaginatedResponse<T>(List<T> Items, int TotalCount, int Page, int PageSize);
