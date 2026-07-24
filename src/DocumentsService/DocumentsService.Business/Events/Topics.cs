namespace DocumentsService.Business.Events;

/// <summary>
/// Kafka topics touched by the documents domain (architecture v1.1 §2.4).
/// deal.document_uploaded is CONSUMED (deals-service publishes it when a document
/// is attached to a deal) to stamp deal context onto the file record. This
/// service publishes nothing — document.processed belongs to the
/// ingestion-service, which owns all text processing.
/// </summary>
public static class Topics
{
    public const string DealDocumentUploaded = "deal.document_uploaded";
}
