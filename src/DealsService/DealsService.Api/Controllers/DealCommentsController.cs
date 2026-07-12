using DealsService.Api.DTOs;
using DealsService.Api.Infrastructure;
using DealsService.Business;
using DealsService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DealsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("deals/v1/deals/{dealId}/comments")]
public class DealCommentsController(DealCommentService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(string dealId, CancellationToken ct)
    {
        var comments = await service.GetByDealAsync(dealId, ct);
        if (comments is null) return NotFoundError("Deal not found.");
        return Success(comments.Select(MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(string dealId, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(dealId,
            new CreateCommentDto(request.Body, request.ParentId), ActorId, ct);
        return FromResult(Map(result), StatusCodes.Status201Created);
    }

    private static ServiceResult<CommentResponse> Map(ServiceResult<CommentDto> result) =>
        result.Succeeded
            ? ServiceResult<CommentResponse>.Ok(MapToResponse(result.Value!))
            : ServiceResult<CommentResponse>.Fail(result.Code!, result.Message!, result.Errors);

    private static CommentResponse MapToResponse(CommentDto c) => new(
        c.Id, c.DealId, c.ParentId, c.Body, c.AuthorId, c.IsAiGenerated, c.CreatedAt);
}
