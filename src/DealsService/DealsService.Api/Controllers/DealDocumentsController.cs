using DealsService.Api.DTOs;
using DealsService.Api.Infrastructure;
using DealsService.Business;
using DealsService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DealsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("deals/v1/deals/{dealId}/documents")]
public class DealDocumentsController(DealDocumentService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(string dealId, CancellationToken ct)
    {
        var documents = await service.GetByDealAsync(dealId, ct);
        if (documents is null) return NotFoundError("Deal not found.");
        return Success(documents.Select(MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(string dealId, [FromBody] CreateDocumentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(dealId,
            new CreateDocumentDto(request.FileName, request.FileType, request.StorageUrl), ActorId, ct);
        return FromResult(Map(result), StatusCodes.Status201Created);
    }

    private static ServiceResult<DocumentResponse> Map(ServiceResult<DocumentDto> result) =>
        result.Succeeded
            ? ServiceResult<DocumentResponse>.Ok(MapToResponse(result.Value!))
            : ServiceResult<DocumentResponse>.Fail(result.Code!, result.Message!, result.Errors);

    private static DocumentResponse MapToResponse(DocumentDto d) => new(
        d.Id, d.DealId, d.FileName, d.FileType, d.StorageUrl, d.AiSummary, d.UploadedById, d.UploadedAt);
}
