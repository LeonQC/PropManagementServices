namespace DocumentsService.Business.Events;

// Serialized camelCase by the shared publisher/consumer (JsonSerializerDefaults.Web).

/// <summary>
/// Inbound, published by deals-service. Must stay field-compatible with
/// DealsService.Business.Events.DealDocumentUploaded. StorageUrl carries the UI's
/// "/documents/v1/{documentId}" pointer for uploads that went through this
/// service; manual metadata-only records may carry anything else (or null).
/// </summary>
public record DealDocumentUploaded(
    string DealId,
    string DocumentId,
    string FileName,
    string FileType,
    string? StorageUrl,
    string UploadedById,
    string UploadedAt);

/// <summary>Outbound: PDF text extraction finished (architecture §2.4 payload).</summary>
public record DocumentProcessed(
    string DealId,
    string DocumentId,
    string? DocumentType,
    int ExtractedTextLength,
    string StorageUrl);
