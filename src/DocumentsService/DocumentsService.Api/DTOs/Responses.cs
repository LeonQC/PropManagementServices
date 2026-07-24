namespace DocumentsService.Api.DTOs;

public record UploadUrlResponse(
    string DocumentId,
    string StorageKey,
    string UploadUrl,
    string ExpiresAt);

public record DocumentResponse(
    string Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    string? DealId,
    string? DocumentType,
    string UploadedById,
    string CreatedAt,
    string? ConfirmedAt);

public record DownloadUrlResponse(
    string DocumentId,
    string FileName,
    string DownloadUrl,
    string ExpiresAt);
