namespace DocumentsService.Business.Events;

/// <summary>
/// Kafka topics touched by the documents domain (architecture §2.4).
/// deal.document_uploaded is CONSUMED (deals-service publishes it when a document
/// is attached to a deal); document.processed is PUBLISHED once extraction lands.
/// </summary>
public static class Topics
{
    public const string DealDocumentUploaded = "deal.document_uploaded";
    public const string DocumentProcessed = "document.processed";
}
