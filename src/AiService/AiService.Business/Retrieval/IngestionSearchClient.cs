using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Retrieval;

/// <summary>One chunk as ingestion-service returns it from POST /ingestion/v1/search.
///
/// <para><see cref="Rank"/> and <see cref="RerankScore"/> are nullable for a reason: a
/// response from a server that predates them, or any search where reranking did not run,
/// leaves them null and every consumer falls back to the score-only behaviour that shipped
/// before. Defaults keep existing positional construction compiling.</para>
/// </summary>
public record RetrievedChunk(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("chunkIndex")] int ChunkIndex,
    [property: JsonPropertyName("pageNo")] int? PageNo,
    [property: JsonPropertyName("text")] string Text,
    /// <summary>Cosine similarity. Deliberately unchanged in meaning:
    /// <see cref="RetrievalOptions.MinScore"/>, <see cref="RetrievalOptions.RelativeFloor"/>
    /// and the off-domain abstain behaviour are all calibrated against this scale. Any
    /// other score here would look like a harmless field change and would quietly break
    /// the feature's ability to decline.</summary>
    [property: JsonPropertyName("score")] double Score,
    /// <summary>1-based position in the server's ranking: cosine order, or the reranked
    /// order when a cross-encoder ran. Null from a server that predates it — order by
    /// Score then.</summary>
    [property: JsonPropertyName("rank")] int? Rank = null,
    /// <summary>Cross-encoder relevance, when ingestion-service reranked. Diagnostic, for
    /// the same reason as the two above — the ordering is already in <see cref="Rank"/>,
    /// and this service deliberately ranks on nothing it computes itself. Null whenever
    /// reranking did not run, which is the default.</summary>
    [property: JsonPropertyName("rerankScore")] double? RerankScore = null);

/// <summary>Raised when retrieval fails upstream; the controller turns this into a 502.</summary>
public class RetrievalException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Typed client over ingestion-service's vector search. Embedding the query has to
/// happen there — the query must be embedded by the same model that embedded the
/// chunks (architecture §2.5) — so this service never talks to an embedding API or
/// to rag-db directly.
///
/// <para>Every call carries the caller's own bearer token. ai-service holds no
/// service account and no database credentials for another service's store, so a
/// question can never reach a document the user couldn't already fetch. Do not add a
/// service token here for convenience: ingestion-service accepts any valid
/// auth-service token, which means a service token would silently widen the blast
/// radius of this endpoint to the entire corpus.</para>
/// </summary>
public class IngestionSearchClient(HttpClient http, ILogger<IngestionSearchClient> logger)
{
    private record SearchRequest(string Query, string? DealId, string? DocumentId, int TopK);
    private record SearchData(string Query, string EmbeddingModel, bool Rerank,
                              List<RetrievedChunk> Chunks);
    private record SearchEnvelope(SearchData Data);

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, string? dealId, string? documentId, int topK, string bearerToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ingestion/v1/search")
        {
            Content = JsonContent.Create(new SearchRequest(query, dealId, documentId, topK)),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Retrieval request to ingestion-service failed.");
            throw new RetrievalException("Could not reach the document search service.", ex);
        }

        using (response)
        {
            // A 401/403 here means the caller's own token was rejected downstream.
            // Surface it as a retrieval fault rather than masking it as "no results",
            // which would read to the user as "this deal has no documents".
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning("ingestion-service rejected the caller's token ({Status}).", response.StatusCode);
                throw new RetrievalException("Not authorized to search this deal's documents.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("ingestion-service returned {Status} for a search request.", response.StatusCode);
                throw new RetrievalException($"Document search failed ({(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<SearchEnvelope>(ct);
            return body?.Data?.Chunks ?? [];
        }
    }
}
