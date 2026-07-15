using DealsService.Business.DTOs;
using DealsService.Business.Events;
using DealsService.DataAccess;
using DealsService.Models;
using PropTrack.Messaging;

namespace DealsService.Business;

public class DealDocumentService(
    IDealRepository dealRepo,
    IDealDocumentRepository documentRepo,
    IEventPublisher eventPublisher)
{
    public async Task<List<DocumentDto>?> GetByDealAsync(string dealId, CancellationToken ct = default)
    {
        if (!await dealRepo.ExistsAsync(dealId, ct)) return null;
        var documents = await documentRepo.GetByDealAsync(dealId, ct);
        return documents.Select(MapToDto).ToList();
    }

    public async Task<ServiceResult<DocumentDto>> CreateAsync(string dealId, CreateDocumentDto input,
        string actorId, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.FileName))
            errors.Add(new FieldError("fileName", "fileName is required."));
        if (string.IsNullOrWhiteSpace(input.FileType))
            errors.Add(new FieldError("fileType", "fileType is required."));
        if (errors.Count > 0)
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.Validation, "Invalid document.", errors);

        if (!await dealRepo.ExistsAsync(dealId, ct))
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        var document = new DealDocument
        {
            Id = "",
            DealId = dealId,
            FileName = input.FileName.Trim(),
            FileType = input.FileType.Trim(),
            StorageUrl = input.StorageUrl,
            UploadedById = actorId,
            UploadedAt = DateTime.UtcNow.ToString("O"),
        };

        var created = await documentRepo.CreateAsync(document, ct);

        // Deal domain event: a document was attached to this deal. documents-service
        // reacts by extracting PDF text and publishing document.processed.
        await eventPublisher.PublishAsync(Topics.DealDocumentUploaded, created.DealId,
            new DealDocumentUploaded(created.DealId, created.Id, created.FileName, created.FileType,
                created.StorageUrl, created.UploadedById, created.UploadedAt), ct);

        return ServiceResult<DocumentDto>.Ok(MapToDto(created));
    }

    private static DocumentDto MapToDto(DealDocument d) => new(
        d.Id, d.DealId, d.FileName, d.FileType, d.StorageUrl, d.AiSummary, d.UploadedById, d.UploadedAt);
}
