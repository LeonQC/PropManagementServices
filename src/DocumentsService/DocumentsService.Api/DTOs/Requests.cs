namespace DocumentsService.Api.DTOs;

public record CreateUploadUrlRequest(string FileName, string ContentType, long SizeBytes);

public record ConfirmUploadRequest(string DocumentId);
