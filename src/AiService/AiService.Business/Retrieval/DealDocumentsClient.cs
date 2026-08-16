using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Retrieval;

/// <summary>File name and type for one document attached to a deal.</summary>
public record DealDocumentInfo(string DocumentId, string FileName, string? FileType);

/// <summary>
/// Reads a deal's document list from deals-service, to turn the opaque documentIds
/// that come back from retrieval into file names.
///
/// <para>Not cosmetic: the system prompt requires figures to be attributed to the
/// document they came from ("the appraisal concludes $68.8m"), and Claude can only
/// do that if the file name is in the context. Without this the model can cite a
/// page but not name a source, which is precisely what's needed to make two
/// conflicting numbers legible.</para>
///
/// <para>Forwards the caller's bearer token, like every other outbound call here.</para>
/// </summary>
public class DealDocumentsClient(HttpClient http, ILogger<DealDocumentsClient> logger)
{
    private record DocumentResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("fileType")] string? FileType,
        [property: JsonPropertyName("storageUrl")] string? StorageUrl);

    private record Envelope(List<DocumentResponse> Data);

    private const string StorageUrlPrefix = "/documents/v1/";

    /// <summary>
    /// documentId → info, keyed by the id retrieval reports. Never throws — a missing
    /// file name degrades attribution but must not fail the question.
    ///
    /// <para>Returns null when the list could not be fetched, which is deliberately
    /// distinct from an empty map. "This deal has no documents" and "I couldn't ask
    /// deals-service" produce very different messages to the user, and collapsing
    /// them tells someone their ten-document deal is empty whenever a downstream call
    /// hiccups.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DealDocumentInfo>?> GetByDealAsync(
        string dealId, string bearerToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/deals/v1/deals/{Uri.EscapeDataString(dealId)}/documents");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        try
        {
            using var response = await http.SendAsync(request, ct);

            // A 404 is an answer: the deal doesn't exist, so it has no documents.
            if (response.StatusCode == HttpStatusCode.NotFound) return new Dictionary<string, DealDocumentInfo>();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("deals-service returned {Status} listing documents for deal {DealId}.",
                    response.StatusCode, dealId);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<Envelope>(ct);
            var map = new Dictionary<string, DealDocumentInfo>();
            foreach (var doc in body?.Data ?? [])
            {
                // deal_documents rows carry a pointer, not the documents-service id, so
                // the id retrieval reports is the tail of storageUrl. Rows without a
                // pointer were never uploaded through the storage flow and have nothing
                // ingested, so they can't appear in retrieval results anyway.
                if (doc.StorageUrl is null || !doc.StorageUrl.StartsWith(StorageUrlPrefix)) continue;
                var documentId = doc.StorageUrl[StorageUrlPrefix.Length..].TrimEnd('/');
                if (documentId.Length > 0)
                    map[documentId] = new DealDocumentInfo(documentId, doc.FileName, doc.FileType);
            }
            return map;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not list documents for deal {DealId}; citations will lack file names.", dealId);
            return null;
        }
    }
}
