namespace DocumentsService.Business.DTOs;

public record CreateUploadUrlDto(string FileName, string ContentType, long SizeBytes);

public record UploadUrlDto(
    string DocumentId,
    string StorageKey,
    string UploadUrl,
    string ExpiresAt);

public record DocumentDto(
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

public record DownloadUrlDto(
    string DocumentId,
    string FileName,
    string DownloadUrl,
    string ExpiresAt);

public record DocumentTextDto(
    string DocumentId,
    string Status,
    string? Text,
    string? Error,
    string? ExtractedAt,
    int? PageCount);
