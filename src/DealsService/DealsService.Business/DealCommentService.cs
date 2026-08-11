using DealsService.Business.DTOs;
using DealsService.DataAccess;
using DealsService.Models;

namespace DealsService.Business;

public class DealCommentService(
    IDealRepository dealRepo, IDealCommentRepository commentRepo, DealSnapshotPublisher snapshots)
{
    public async Task<List<CommentDto>?> GetByDealAsync(string dealId, CancellationToken ct = default)
    {
        if (!await dealRepo.ExistsAsync(dealId, ct)) return null;
        var comments = await commentRepo.GetByDealAsync(dealId, ct);
        return comments.Select(MapToDto).ToList();
    }

    public async Task<ServiceResult<CommentDto>> CreateAsync(string dealId, CreateCommentDto input,
        string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Body))
            return ServiceResult<CommentDto>.Fail(ErrorCodes.Validation, "Invalid comment.",
                [new FieldError("body", "body is required.")]);

        if (!await dealRepo.ExistsAsync(dealId, ct))
            return ServiceResult<CommentDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        var comment = new DealComment
        {
            Id = "",
            DealId = dealId,
            ParentId = input.ParentId,
            Body = input.Body.Trim(),
            AuthorId = actorId,
            IsAiGenerated = false,
            CreatedAt = DateTime.UtcNow.ToString("O"),
        };

        var created = await commentRepo.CreateAsync(comment, ct);

        // Comment bodies are part of the deal's searchable text — this is the only reason
        // this service publishes anything at all (deal.comment_added is still unpublished).
        await snapshots.BumpReloadAndPublishAsync(dealId, ct);

        return ServiceResult<CommentDto>.Ok(MapToDto(created));
    }

    private static CommentDto MapToDto(DealComment c) => new(
        c.Id, c.DealId, c.ParentId, c.Body, c.AuthorId, c.IsAiGenerated, c.CreatedAt);
}
