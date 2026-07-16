namespace DocumentsService.Models;

/// <summary>
/// File-centric record owned by the documents-service (architecture §2.4): one row
/// per uploaded blob. The deal-facing metadata record lives in deals-service's
/// deal_documents — this table only tracks the file's storage lifecycle.
/// DealId/DocumentType arrive later, via the consumed deal.document_uploaded event.
/// </summary>
public class DocumentRecord
{
    public required string Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StorageKey { get; set; }
    public required string Status { get; set; }
    public string? DealId { get; set; }
    public string? DocumentType { get; set; }
    public required string UploadedById { get; set; }
    public required string CreatedAt { get; set; }
    public string? ConfirmedAt { get; set; }
    public string? DeletedAt { get; set; }

    public DocumentText? Text { get; set; }
}
