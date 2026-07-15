using DocumentsService.Business.Extraction;
using DocumentsService.DataAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace DocumentsService.Business.Events;

/// <summary>
/// Reacts to deals-service attaching a document to a deal (architecture §2.4):
/// stamps the deal context onto our file record and enqueues PDF text extraction.
/// Documents that never went through this service (metadata-only records whose
/// storageUrl isn't our "/documents/v1/{id}" pointer) are skipped gracefully.
/// </summary>
public sealed class DealDocumentUploadedConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealDocumentUploadedConsumer> logger)
    : KafkaConsumerService<DealDocumentUploaded>(settings, Topics.DealDocumentUploaded, scopeFactory, logger)
{
    private readonly ILogger _logger = logger;

    protected override async Task HandleAsync(
        DealDocumentUploaded message, IServiceProvider services, CancellationToken ct)
    {
        var documentId = ParseDocumentId(message.StorageUrl);
        if (documentId is null)
        {
            _logger.LogInformation(
                "Skipping deal document {DealDocumentId} for deal {DealId}: storageUrl is not a documents-service pointer.",
                message.DocumentId, message.DealId);
            return;
        }

        var documents = services.GetRequiredService<IDocumentRepository>();
        var record = await documents.GetByIdAsync(documentId, ct);
        if (record is null)
        {
            _logger.LogWarning("deal.document_uploaded points at unknown document {DocumentId}.", documentId);
            return;
        }

        record.DealId = message.DealId;
        record.DocumentType = message.FileType;
        await documents.UpdateAsync(record, ct);

        if (DocumentService.IsPdf(record))
        {
            var queue = services.GetRequiredService<IExtractionQueue>();
            await queue.EnqueueAsync(record.Id, ct);
            _logger.LogInformation("Queued text extraction for document {DocumentId} (deal {DealId}).",
                record.Id, message.DealId);
        }
    }

    private static string? ParseDocumentId(string? storageUrl)
    {
        if (storageUrl is null || !storageUrl.StartsWith(DocumentService.StorageUrlPrefix, StringComparison.Ordinal))
            return null;
        var id = storageUrl[DocumentService.StorageUrlPrefix.Length..].TrimEnd('/');
        return Guid.TryParse(id, out _) ? id : null;
    }
}
