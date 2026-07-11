namespace DealsService.Business.DTOs;

public record CreateDocumentDto(string FileName, string FileType, string? StorageUrl);

public record DocumentDto(
    string Id,
    string DealId,
    string FileName,
    string FileType,
    string? StorageUrl,
    string? AiSummary,
    string UploadedById,
    string UploadedAt);
