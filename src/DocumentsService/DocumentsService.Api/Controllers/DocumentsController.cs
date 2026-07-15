using DocumentsService.Api.DTOs;
using DocumentsService.Api.Infrastructure;
using DocumentsService.Business;
using DocumentsService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("documents/v1")]
public class DocumentsController(DocumentService service) : ApiControllerBase
{
    /// <summary>Step 1 of the upload flow: presigned PUT URL + pending record.</summary>
    [HttpPost("upload-url")]
    public async Task<IActionResult> CreateUploadUrl([FromBody] CreateUploadUrlRequest request, CancellationToken ct)
    {
        var result = await service.CreateUploadUrlAsync(
            new CreateUploadUrlDto(request.FileName, request.ContentType, request.SizeBytes), ActorId, ct);
        return FromResult(Map(result, MapToUploadUrlResponse), StatusCodes.Status201Created);
    }

    /// <summary>Step 3: the browser PUT the bytes to storage; activate the record.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmUploadRequest request, CancellationToken ct)
    {
        var result = await service.ConfirmAsync(request.DocumentId, ct);
        return FromResult(Map(result, MapToDocumentResponse));
    }

    /// <summary>Presigned GET URL (15-minute expiry) for an active document.</summary>
    [HttpGet("{id}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(string id, CancellationToken ct)
    {
        var result = await service.GetDownloadUrlAsync(id, ct);
        return FromResult(Map(result, MapToDownloadUrlResponse));
    }

    /// <summary>Extracted PDF text (populated by the background worker).</summary>
    [HttpGet("{id}/text")]
    public async Task<IActionResult> GetText(string id, CancellationToken ct)
    {
        var result = await service.GetTextAsync(id, ct);
        return FromResult(Map(result, MapToTextResponse));
    }

    /// <summary>Soft delete: the record is tombstoned and the blob marked for deletion.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return FromResult(Map(result, MapToDocumentResponse));
    }

    private static ServiceResult<TResponse> Map<TDto, TResponse>(
        ServiceResult<TDto> result, Func<TDto, TResponse> map) =>
        result.Succeeded
            ? ServiceResult<TResponse>.Ok(map(result.Value!))
            : ServiceResult<TResponse>.Fail(result.Code!, result.Message!, result.Errors);

    private static UploadUrlResponse MapToUploadUrlResponse(UploadUrlDto d) =>
        new(d.DocumentId, d.StorageKey, d.UploadUrl, d.ExpiresAt);

    private static DocumentResponse MapToDocumentResponse(DocumentDto d) => new(
        d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Status,
        d.DealId, d.DocumentType, d.UploadedById, d.CreatedAt, d.ConfirmedAt);

    private static DownloadUrlResponse MapToDownloadUrlResponse(DownloadUrlDto d) =>
        new(d.DocumentId, d.FileName, d.DownloadUrl, d.ExpiresAt);

    private static DocumentTextResponse MapToTextResponse(DocumentTextDto d) =>
        new(d.DocumentId, d.Status, d.Text, d.Error, d.ExtractedAt, d.PageCount);
}
