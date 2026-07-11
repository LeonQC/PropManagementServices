namespace DealsService.Business.DTOs;

public record CreateTaskDto(string Title, string? AssigneeId, string? DueDate);

/// <summary>Partial update — only non-null fields are applied.</summary>
public record UpdateTaskDto(string? Title, string? Status, string? AssigneeId, string? DueDate);

public record TaskDto(
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
