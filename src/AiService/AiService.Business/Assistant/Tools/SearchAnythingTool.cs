using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AiService.Business.Assistant.Clients;
using Microsoft.Extensions.Options;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Ranked free-text search across deals and properties at once.
///
/// <para>The landing spot for a vague question. Without it, "what do we have on
/// Riverside?" leaves the model choosing between <c>search_deals</c> and
/// <c>search_properties</c> with nothing to base the choice on, and the cheap move is to
/// fire both — two tool calls to discover which corpus the question was even about.</para>
///
/// <para>It returns identifiers and a 200-character snippet, not records. That is the
/// point: it is for finding out <i>what exists</i>, after which the specific tool reads it
/// properly.</para>
/// </summary>
public class SearchAnythingTool(SearchClient search, IOptions<AssistantOptions> options) : IAssistantTool
{
    private readonly AssistantOptions _options = options.Value;

    public string Name => "search_anything";

    public string Label(JsonNode? input) => "Searching everything…";

    public Tool Definition { get; } = new Function(
        "search_anything",
        $"""
        Free-text search across deals AND properties together, ranked by relevance.

        Use this when you do not yet know whether a question is about a deal or a property
        — an unfamiliar name, a place, an open-ended "what do we have on X". It is the
        cheapest way to find out what exists.

        It returns only an entity type, an id, a title and a short snippet. It does NOT
        return deal financials, stages, tasks or property details. Follow up with get_deal
        or search_properties once you know what you are looking at.

        Prefer search_deals or search_properties directly when the question already makes
        the entity type clear — they filter properly and return full records.

        entityType, if given, must be one of: {DealVocabulary.List(DealVocabulary.EntityTypes)}.
        Omit it to search both.
        """,
        JsonNode.Parse($$"""
        {
          "type": "object",
          "properties": {
            "q":          {"type": "string",  "description": "Free text. Required."},
            "entityType": {"type": "string",  "enum": {{Json(DealVocabulary.EntityTypes)}}},
            "limit":      {"type": "integer", "description": "1-25."}
          },
          "required": ["q"],
          "additionalProperties": false
        }
        """));

    private static string Json(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => $"\"{v}\"")) + "]";

    public async Task<ToolOutcome> InvokeAsync(JsonNode? input, ToolContext context, CancellationToken ct)
    {
        var args = ToolArguments.Read(Name, input, "q", "entityType", "limit");

        var query = args.Required("q", 200);
        var limit = args.Count("limit", _options.CandidateCap, 1, 25);

        List<(string, string)> filters = [("q", query), ("pageSize", limit.ToString(CultureInfo.InvariantCulture))];
        if (args.OneOf("entityType", DealVocabulary.EntityTypes) is { } entityType)
            filters.Add(("entityType", entityType));

        var page = await search.SearchAllAsync(filters, context.BearerToken, ct);

        if (page.Items.Count == 0)
            return new ToolOutcome(
                ToolFormat.Block("cross_entity_search", $"Nothing matched: {query}"),
                "nothing matched");

        var sb = new StringBuilder();
        sb.AppendLine(ToolFormat.Cap(page.Items.Count, page.TotalCount, "results"));
        sb.AppendLine();

        foreach (var hit in page.Items)
        {
            var source = hit.EntityType.Equals("deal", StringComparison.OrdinalIgnoreCase)
                ? context.Sources.RegisterDeal(hit.Id, hit.Title)
                : context.Sources.RegisterProperty(hit.Id, hit.Title);

            sb.AppendLine($"[S{source.Number}] {hit.EntityType}: {hit.Title}");
            sb.AppendLine($"  id: {hit.Id}");
            if (hit.Snippet is { Length: > 0 } snippet)
                sb.AppendLine($"  snippet: {snippet.Trim()}");
            sb.AppendLine();
        }

        var capped = page.Items.Count < page.TotalCount;
        return new ToolOutcome(
            ToolFormat.Block("cross_entity_search", sb.ToString()),
            capped ? $"{page.TotalCount} results; showing {page.Items.Count}" : $"{page.TotalCount} result(s)",
            Capped: capped);
    }
}
