namespace DealsService.Api.DTOs;

public record DealResponse(
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
