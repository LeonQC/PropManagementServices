using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiService.Business.Retrieval;

/// <summary>One chunk as ingestion-service returns it from POST /ingestion/v1/search.
///
/// <para>The last three properties arrived with hybrid retrieval and are nullable for a
/// reason: a response from a server that predates them — or from any request in dense
/// mode — leaves them null, and every consumer falls back to the score-only behaviour
/// that shipped before. Defaults keep existing positional construction compiling.</para>
/// </summary>
public record RetrievedChunk(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("chunkIndex")] int ChunkIndex,
    [property: JsonPropertyName("pageNo")] int? PageNo,
    [property: JsonPropertyName("text")] string Text,
    /// <summary>Cosine similarity — always, in every retrieval mode. Deliberately
    /// unchanged in meaning: <see cref="RetrievalOptions.MinScore"/>,
    /// <see cref="RetrievalOptions.RelativeFloor"/> and the off-domain abstain behaviour
    /// are all calibrated against this scale. A fused score here would look like a
    /// harmless field change and would quietly break the feature's ability to decline.</summary>
    [property: JsonPropertyName("score")] double Score,
    /// <summary>1-based position in the server's ranking: cosine order in dense mode, RRF
    /// order in hybrid mode. Null from a pre-hybrid server — order by Score then.</summary>
    [property: JsonPropertyName("rank")] int? Rank = null,
    /// <summary>BM25 score, when a lexical retriever contributed this chunk. Diagnostic
    /// only — nothing ranks or filters on it.</summary>
    [property: JsonPropertyName("lexScore")] double? LexScore = null,
    /// <summary>Reciprocal-rank-fusion score, hybrid mode only. Diagnostic: the ordering
    /// it produced is already expressed by <see cref="Rank"/>.</summary>
    [property: JsonPropertyName("fusedScore")] double? FusedScore = null);

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
public class IngestionSearchClient(
    HttpClient http,
    IOptions<RetrievalOptions> options,
    ILogger<IngestionSearchClient> logger)
{
    private readonly RetrievalOptions _options = options.Value;

    private record SearchRequest(string Query, string? DealId, string? DocumentId, int TopK, string? Mode);
    private record SearchData(string Query, string EmbeddingModel, string? Mode, bool Degraded,
                              List<RetrievedChunk> Chunks);
    private record SearchEnvelope(SearchData Data);

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, string dealId, string? documentId, int topK, string bearerToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ingestion/v1/search")
        {
            // A null Mode sends nothing and lets ingestion-service apply its own default,
            // so the retrieval strategy stays a deployment decision rather than a rebuild.
            Content = JsonContent.Create(new SearchRequest(query, dealId, documentId, topK, _options.Mode)),
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
            if (body?.Data?.Degraded == true)
            {
                // Not an error — dense results are still good results, and failing the
                // question would be worse. But it must not pass silently: a hybrid
                // deployment quietly serving dense is exactly the kind of drift that
                // shows up months later as "retrieval got worse and nobody knows when".
                logger.LogWarning(
                    "ingestion-service degraded to {ActualMode} (requested {RequestedMode}) — " +
                    "the lexical index is unavailable.", body.Data.Mode, _options.Mode);
            }
            return body?.Data?.Chunks ?? [];
        }
    }
}
