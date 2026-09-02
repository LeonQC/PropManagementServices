using System.Text;
using System.Text.Json.Nodes;
using AiService.Business.Retrieval;
using Microsoft.Extensions.Options;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Semantic retrieval over the text of uploaded deal documents — the expensive tool, and
/// the only one whose results are untrusted content.
///
/// <para>Reuses <see cref="RetrievalService"/> unchanged, so the assistant inherits Deal
/// Q&amp;A's calibrated relevance floors and its reading-order reconstruction rather than
/// growing a second retrieval path that would drift from the first.</para>
///
/// <para>The important limit is one the prompt has to state and this tool cannot fix:
/// top-k retrieval returns the most relevant passages, never all of them. "How many
/// tenants are there" cannot be answered exhaustively from chunks, and an answer that
/// implies otherwise is wrong even when every quoted figure is right.</para>
/// </summary>
public class SearchDealDocumentsTool(
    RetrievalService retrieval,
    DealDocumentsClient documents,
    IOptions<RetrievalOptions> retrievalOptions) : IAssistantTool
{
    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;

    public string Name => "search_deal_documents";

    public string Label(JsonNode? input) =>
        input?["dealId"]?.GetValue<string>() is { Length: > 0 }
            ? "Reading this deal's documents…"
            : "Searching deal documents…";

    public Tool Definition { get; } = new Function(
        "search_deal_documents",
        """
        Semantic search over the text of documents uploaded to deals — offering memoranda,
        rent rolls, appraisals, letters of intent, Phase I environmental reports.

        THIS IS THE EXPENSIVE TOOL. Every call embeds the query and searches a vector
        index. Narrow with search_deals FIRST, then call this once per surviving deal.
        Calling it across the whole portfolio before narrowing will exhaust the tool budget
        before you have an answer.

        ARGUMENTS:
          - question: what you want to find, in natural language.

            WRITING A GOOD QUERY MATTERS. Retrieval is semantic and matches your phrasing
            against passage text, so:
              * Write a full question or descriptive phrase, never bare keywords.
              * Ask about content, not about documents. "what is the going-in cap rate and
                occupancy" retrieves; "rent roll and appraisal" does not — document types
                are metadata, they are not what the passages say.
              * Use the words a document would actually use, and include both forms when a
                term has two ("cap rate" and "capitalization rate", "NOI" and "net
                operating income").
              * One subject per search. A query asking about environmental risk AND lease
                rollover AND pricing matches passages about none of them well. Run separate
                searches instead.
              * Search for EVIDENCE, not for CONCLUSIONS. Documents state facts; they never
                state judgments about the deal. "why has this deal stalled" retrieves almost
                nothing, because no appraisal contains that sentence. Ask instead for the
                facts a stall would show up in — lease expiries, deferred maintenance,
                environmental findings, pricing versus benchmark — and draw the conclusion
                yourself from what comes back.
            An empty result means nothing cleared the relevance floor. Before reporting
            that the documents do not cover something, consider whether a differently
            phrased query would find it — a weak query and a genuine absence look
            identical from here.
          - dealId: restrict to one deal. Strongly preferred. Without it the search runs
            across every deal's documents at once, which is rarely what a question means.
          - topK: how many passages to return, 1-25. Leave it alone unless you specifically
            need more coverage.

        WHAT COMES BACK: the most relevant passages, each labelled [S#] with its document
        name and page number. Cite those markers.

        WHAT DOES NOT COME BACK: everything else in the document. This is top-k retrieval,
        so it CANNOT support exhaustive claims — totals across all tenants, "the only
        environmental issue", counts of anything. If a question needs completeness, say
        that you are working from the passages retrieved rather than the full document.

        If nothing clears the relevance floor you get an empty result. That means the
        documents do not cover it, or the deal has no documents — it does not mean the
        answer is no.
        """,
        JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "question": {"type": "string",  "description": "A natural-language question about document content."},
            "dealId":   {"type": "string",  "description": "Restrict to this deal's documents. Strongly preferred."},
            "topK":     {"type": "integer", "description": "Passages to return, 1-25."}
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """));

    public async Task<ToolOutcome> InvokeAsync(JsonNode? input, ToolContext context, CancellationToken ct)
    {
        var args = ToolArguments.Read(Name, input, "question", "dealId", "topK");

        var question = args.Required("question", 500);

        // Pinned scope wins. When the deal panel asks a question, the deal and the document
        // are the server's decision; the model may not widen them.
        var dealId = context.PinnedDealId ?? args.Text("dealId", 80);
        var documentId = context.PinnedDocumentId;

        var topK = args.Count("topK", _retrieval.MaxContextChunks, 1, 25);

        IReadOnlyList<ContextChunk> chunks;
        try
        {
            chunks = await retrieval.RetrieveAsync(question, dealId ?? "", documentId, context.BearerToken, topK, ct);
        }
        catch (RetrievalException ex)
        {
            return ToolOutcome.Error($"Document search failed: {ex.Message}");
        }

        if (chunks.Count == 0)
            return new ToolOutcome(
                ToolFormat.Block("document_search",
                    $"No passages in {(dealId is { Length: > 0 } ? "this deal's documents" : "any deal's documents")} " +
                    $"were relevant to: {question}\n" +
                    "This means the documents do not cover it, or there are no documents. It is not a 'no' answer."),
                "no relevant passages");

        // File names turn an opaque documentId into an attributable source ("the appraisal
        // says…"), which the prompt requires whenever two documents disagree. Looked up per
        // distinct deal in the results, which is one call for a scoped search.
        var fileNames = await FileNamesAsync(chunks, context.BearerToken, ct);

        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var c = chunk.Chunk;
            fileNames.TryGetValue(c.DocumentId, out var info);

            var source = context.Sources.RegisterDocument(
                c.DocumentId, c.DealId, info?.FileName, c.PageNo, c.ChunkIndex, c.Score, c.Text);

            var page = c.PageNo is { } p ? $" page=\"{p}\"" : "";
            var type = info?.FileType is { Length: > 0 } t ? $" type=\"{Grounding.Escape(t)}\"" : "";
            var deal = c.DealId is { Length: > 0 } d ? $" deal=\"{d}\"" : "";
            var name = Grounding.Escape(info?.FileName ?? "Unknown document");

            sb.AppendLine($"<excerpt id=\"S{source.Number}\" document=\"{name}\"{type}{deal}{page}>");
            sb.AppendLine(c.Text.Trim());
            sb.AppendLine("</excerpt>");
            sb.AppendLine();
        }

        var documentCount = chunks.Select(c => c.Chunk.DocumentId).Distinct().Count();
        return new ToolOutcome(
            ToolFormat.Untrusted("document_search", "text quoted from uploaded deal documents", sb.ToString()),
            $"{chunks.Count} passage(s) from {documentCount} document(s)");
    }

    private async Task<IReadOnlyDictionary<string, DealDocumentInfo>> FileNamesAsync(
        IReadOnlyList<ContextChunk> chunks, string bearerToken, CancellationToken ct)
    {
        var names = new Dictionary<string, DealDocumentInfo>();

        // Bounded deliberately: an unscoped search can span many deals, and one name lookup
        // per deal would turn a single tool call into a fan-out of its own.
        var dealIds = chunks
            .Select(c => c.Chunk.DealId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Take(5);

        foreach (var dealId in dealIds)
        {
            var byDeal = await documents.GetByDealAsync(dealId!, bearerToken, ct);
            if (byDeal is null) continue;
            foreach (var (documentId, info) in byDeal) names[documentId] = info;
        }

        return names;
    }
}
