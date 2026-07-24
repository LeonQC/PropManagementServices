using DocumentsService.Business.Domain;
using DocumentsService.Business.DTOs;
using DocumentsService.Business.Storage;
using DocumentsService.DataAccess;
using DocumentsService.Models;

namespace DocumentsService.Business;

public class DocumentService(
    IDocumentRepository documents,
    IBlobStorage storage)
{
    // Generous dev-time cap; real limits belong to the storage tier's policy.
    private const long MaxSizeBytes = 100 * 1024 * 1024;

    /// <summary>The record id → deal link pointer written into deals' storageUrl by the UI.</summary>
    public const string StorageUrlPrefix = "/documents/v1/";

    public async Task<ServiceResult<UploadUrlDto>> CreateUploadUrlAsync(
        CreateUploadUrlDto input, string actorId, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.FileName))
            errors.Add(new FieldError("fileName", "fileName is required."));
        if (string.IsNullOrWhiteSpace(input.ContentType))
            errors.Add(new FieldError("contentType", "contentType is required."));
        if (input.SizeBytes <= 0)
            errors.Add(new FieldError("sizeBytes", "sizeBytes must be positive."));
        else if (input.SizeBytes > MaxSizeBytes)
            errors.Add(new FieldError("sizeBytes", $"File exceeds the {MaxSizeBytes / (1024 * 1024)} MB limit."));
        if (errors.Count > 0)
            return ServiceResult<UploadUrlDto>.Fail(ErrorCodes.Validation, "Invalid upload request.", errors);

        var record = new DocumentRecord
        {
            Id = "",
            FileName = input.FileName.Trim(),
            ContentType = input.ContentType.Trim(),
            SizeBytes = input.SizeBytes,
            StorageKey = "", // set below — the key embeds the generated id
            Status = DocumentStatuses.Pending,
            UploadedById = actorId,
            CreatedAt = Now(),
        };

        // Two-step create: the repo generates the id, and the storage key embeds it
        // so keys are collision-proof and self-describing ({id}/{sanitized-name}).
        var created = await documents.CreateAsync(record, ct);
        created.StorageKey = $"{created.Id}/{SanitizeFileName(created.FileName)}";
        await documents.UpdateAsync(created, ct);

        var url = storage.GetUploadUrl(created.StorageKey, created.ContentType, out var expiresAt);
        return ServiceResult<UploadUrlDto>.Ok(
            new UploadUrlDto(created.Id, created.StorageKey, url, expiresAt.ToString("O")));
    }

    public async Task<ServiceResult<DocumentDto>> ConfirmAsync(string documentId, CancellationToken ct = default)
    {
        var record = await documents.GetByIdAsync(documentId, ct);
        if (record is null || record.Status == DocumentStatuses.Deleted)
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.NotFound, "Document not found.");
        if (record.Status == DocumentStatuses.Active)
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.Conflict, "Document is already confirmed.");

        if (!await storage.ExistsAsync(record.StorageKey, ct))
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.Validation, "File was not uploaded to storage.",
                [new FieldError("documentId", "No blob found for this document's storage key.")]);

        record.Status = DocumentStatuses.Active;
        record.ConfirmedAt = Now();
        await documents.UpdateAsync(record, ct);

        return ServiceResult<DocumentDto>.Ok(MapToDto(record));
    }

    public async Task<ServiceResult<DownloadUrlDto>> GetDownloadUrlAsync(string documentId, CancellationToken ct = default)
    {
        var record = await documents.GetByIdAsync(documentId, ct);
        if (record is null || record.Status == DocumentStatuses.Deleted)
            return ServiceResult<DownloadUrlDto>.Fail(ErrorCodes.NotFound, "Document not found.");
        if (record.Status != DocumentStatuses.Active)
            return ServiceResult<DownloadUrlDto>.Fail(ErrorCodes.Conflict, "Document upload has not been confirmed.");

        var url = storage.GetDownloadUrl(record.StorageKey, record.FileName, out var expiresAt);
        return ServiceResult<DownloadUrlDto>.Ok(
            new DownloadUrlDto(record.Id, record.FileName, url, expiresAt.ToString("O")));
    }

    public async Task<ServiceResult<DocumentDto>> DeleteAsync(string documentId, CancellationToken ct = default)
    {
        var record = await documents.GetByIdAsync(documentId, ct);
        if (record is null || record.Status == DocumentStatuses.Deleted)
            return ServiceResult<DocumentDto>.Fail(ErrorCodes.NotFound, "Document not found.");

        // Soft delete: the blob stays in storage, marked for deletion by DeletedAt.
        // A future cleanup job (or lifecycle rule in real S3) removes it.
        record.Status = DocumentStatuses.Deleted;
        record.DeletedAt = Now();
        await documents.UpdateAsync(record, ct);
        return ServiceResult<DocumentDto>.Ok(MapToDto(record));
    }

    private static string SanitizeFileName(string fileName)
    {
        var cleaned = new string(fileName.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "file" : cleaned;
    }

    private static string Now() => DateTime.UtcNow.ToString("O");

    private static DocumentDto MapToDto(DocumentRecord r) => new(
        r.Id, r.FileName, r.ContentType, r.SizeBytes, r.Status,
        r.DealId, r.DocumentType, r.UploadedById, r.CreatedAt, r.ConfirmedAt);
}
