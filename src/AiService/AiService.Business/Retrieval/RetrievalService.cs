using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiService.Business.Retrieval;

/// <summary>A chunk that survived filtering, with the source marker Claude will cite.</summary>
public record ContextChunk(int SourceNumber, RetrievedChunk Chunk);

/// <summary>
/// Turns a question into the handful of chunks that go in the prompt. Over-fetch
/// from ingestion-service, then narrow in three passes — floor, truncate, re-order.
/// Ideas ported from RAG-Challenge-2's retrieval.py; the structure is not, since that
/// project reads FAISS files offline with one document per company and this one
/// queries pgvector over many documents per deal.
///
/// <para>There is deliberately no page-level dedupe, though retrieval.py has one
/// (`seen_pages`) and this service did too. Its premise doesn't hold here: that
/// project's chunks come from a sliding window whose neighbours really do repeat each
/// other, while ingestion-service splits Docling markdown on structure
/// (<c>RecursiveCharacterTextSplitter</c>, 300/50) and emits oversized tables
/// atomically — so the 50-token overlap almost never materialises. Measured across
/// every same-page pair in the corpus, adjacent chunks share a median Jaccard of
/// 0.000 (max 0.074), statistically indistinguishable from non-adjacent pairs
/// (max 0.071). Deduping by page therefore discarded distinct content.</para>
///
/// <para>Concretely, it broke the feature's headline case: on a deal whose rent roll
/// reports 86.7% occupancy and whose OM reports 76.80%, the chunk carrying 86.7%
/// scored 0.429 against its page-neighbour's 0.432 and was dropped as a duplicate.
/// The model cannot report a disagreement when one side never reaches its context.
/// A per-document cap was considered as a replacement and rejected: the only queries
/// that concentrate on one or two documents are ones that should (environmental
/// questions hitting the Phase I ESA), and capping cost relevance on every other
/// query. The floor and the context budget do the narrowing.</para>
/// </summary>
public class RetrievalService(
    IngestionSearchClient client,
    IOptions<RetrievalOptions> options,
    ILogger<RetrievalService> logger)
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<IReadOnlyList<ContextChunk>> RetrieveAsync(
        string question, string dealId, string? documentId, string bearerToken,
        CancellationToken ct = default)
    {
        var chunks = await client.SearchAsync(
            question, dealId, documentId, _options.FetchTopK, bearerToken, ct);

        if (chunks.Count == 0)
        {
            logger.LogInformation("No chunks at all for deal {DealId} — the deal has no ingested documents.", dealId);
            return [];
        }

        var kept = ApplyRelevanceFloor(chunks);
        if (kept.Count == 0)
        {
            logger.LogInformation(
                "All {Count} chunk(s) for deal {DealId} fell below the relevance floor (best score {Best:F3}).",
                chunks.Count, dealId, chunks.Max(c => c.Score));
            return [];
        }

        kept = TakeWithinBudget(kept);
        return RestoreReadingOrder(kept);
    }

    /// <summary>
    /// Absolute floor plus a relative one. The absolute floor removes off-domain
    /// noise; the relative floor removes the weak tail of an otherwise good result
    /// set, which a fixed number can't do — a precise question and a vague one
    /// produce different top scores, so "much worse than the best hit" travels
    /// better than any single constant.
    /// </summary>
    private List<RetrievedChunk> ApplyRelevanceFloor(IReadOnlyList<RetrievedChunk> chunks)
    {
        var best = chunks.Max(c => c.Score);
        var relative = best * _options.RelativeFloor;
        return [.. chunks.Where(c => c.Score >= _options.MinScore && c.Score >= relative)
                         .OrderByDescending(c => c.Score)];
    }

    /// <summary>Highest-scoring chunks first, up to the count and character budget.</summary>
    private List<RetrievedChunk> TakeWithinBudget(IEnumerable<RetrievedChunk> chunks)
    {
        List<RetrievedChunk> kept = [];
        var chars = 0;
        foreach (var chunk in chunks)
        {
            if (kept.Count >= _options.MaxContextChunks) break;
            if (chars + chunk.Text.Length > _options.MaxContextChars && kept.Count > 0) break;
            kept.Add(chunk);
            chars += chunk.Text.Length;
        }
        return kept;
    }

    /// <summary>
    /// Re-sort by document, then by position within it (architecture §3.3). Relevance
    /// order is the right order to *select* in and the wrong order to *read* in: a
    /// deal-scoped query pulls chunks from several PDFs at once, so ranked order hands
    /// Claude page 2 of the appraisal, then page 1 of the rent roll, then page 1 of the
    /// appraisal. Grouping restores something that reads like prose.
    ///
    /// <para>Documents are ordered by their best-scoring chunk, so the most relevant
    /// document still leads.</para>
    /// </summary>
    private static List<ContextChunk> RestoreReadingOrder(IReadOnlyList<RetrievedChunk> chunks)
    {
        var documentRank = chunks
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.Max(c => c.Score));

        return [.. chunks
            .OrderByDescending(c => documentRank[c.DocumentId])
            .ThenBy(c => c.DocumentId)
            .ThenBy(c => c.PageNo ?? int.MaxValue)
            .ThenBy(c => c.ChunkIndex)
            .Select((chunk, i) => new ContextChunk(i + 1, chunk))];
    }
}
