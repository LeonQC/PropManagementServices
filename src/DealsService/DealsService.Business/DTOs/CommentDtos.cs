namespace DealsService.Business.DTOs;

public record CreateCommentDto(string Body, string? ParentId);

public record CommentDto(
    string Id,
    string DealId,
    string? ParentId,
    string Body,
    string AuthorId,
    bool IsAiGenerated,
    string CreatedAt);
