namespace DealsService.Api.DTOs;

public record CreateDealRequest(
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

public record UpdateDealRequest(
    string? Name,
    string? Priority,
    string? OwnerId,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate);

/// <summary>Optional optimistic-concurrency guard: 409 when the deal has moved on.</summary>
public record AdvanceDealRequest(string? ExpectedCurrentStage);

public record KillDealRequest(string Reason, string? ExpectedCurrentStage);

public record CreateTaskRequest(string Title, string? AssigneeId, string? DueDate);

public record UpdateTaskRequest(string? Title, string? Status, string? AssigneeId, string? DueDate);

public record CreateCommentRequest(string Body, string? ParentId);

public record CreateDocumentRequest(string FileName, string FileType, string? StorageUrl);
