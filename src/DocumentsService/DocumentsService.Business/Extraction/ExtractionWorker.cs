using DocumentsService.Business.Domain;
using DocumentsService.Business.Events;
using DocumentsService.Business.Storage;
using DocumentsService.DataAccess;
using DocumentsService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;
using UglyToad.PdfPig;

namespace DocumentsService.Business.Extraction;

/// <summary>
/// Background PDF text extraction (architecture §2.4): drains the in-process
/// queue fed by <see cref="Events.DealDocumentUploadedConsumer"/>, pulls the blob
/// from storage, extracts page text with PdfPig into document_text, and publishes
/// document.processed for the future ai-service.
/// </summary>
public sealed class ExtractionWorker(
    IServiceScopeFactory scopeFactory,
    IExtractionQueue queue,
    IBlobStorage storage,
    IEventPublisher publisher,
    ILogger<ExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var documentId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Isolate failures so one bad document can't stop the worker.
                logger.LogError(ex, "Unhandled extraction failure for document {DocumentId}.", documentId);
            }
        }
    }

    private async Task ProcessAsync(string documentId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var texts = scope.ServiceProvider.GetRequiredService<IDocumentTextRepository>();

        var record = await documents.GetByIdAsync(documentId, ct);
        if (record is null || record.Status != DocumentStatuses.Active)
        {
            logger.LogWarning("Skipping extraction for document {DocumentId}: not found or not active.", documentId);
            return;
        }

        var result = new DocumentText { DocumentId = record.Id, Status = TextExtractionStatuses.Failed };
        try
        {
            var bytes = await storage.DownloadAsync(record.StorageKey, ct);
            using var pdf = PdfDocument.Open(bytes);
            var pages = pdf.GetPages().Select(p => p.Text).ToList();
            result.Text = string.Join("\n\n", pages);
            result.PageCount = pages.Count;
            result.Status = TextExtractionStatuses.Succeeded;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            logger.LogError(ex, "PDF text extraction failed for document {DocumentId}.", record.Id);
        }

        result.ExtractedAt = DateTime.UtcNow.ToString("O");
        await texts.UpsertAsync(result, ct);

        if (result.Status == TextExtractionStatuses.Succeeded)
        {
            logger.LogInformation("Extracted {Chars} chars over {Pages} page(s) from document {DocumentId}.",
                result.Text?.Length ?? 0, result.PageCount, record.Id);

            await publisher.PublishAsync(Topics.DocumentProcessed, record.DealId ?? record.Id,
                new DocumentProcessed(
                    record.DealId ?? "",
                    record.Id,
                    record.DocumentType,
                    result.Text?.Length ?? 0,
                    DocumentService.StorageUrlPrefix + record.Id), ct);
        }
    }
}
