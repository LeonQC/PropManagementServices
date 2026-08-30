using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AiService.Business.Assistant.Clients;
using Microsoft.Extensions.Options;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Property-listing search — the market side of the portfolio, as opposed to the deals
/// being worked on it.
/// </summary>
public class SearchPropertiesTool(SearchClient search, IOptions<AssistantOptions> options) : IAssistantTool
{
    private readonly AssistantOptions _options = options.Value;

    public string Name => "search_properties";

    public string Label(JsonNode? input) => "Searching properties…";

    public Tool Definition { get; } = new Function(
        "search_properties",
        $"""
        Find property listings by type, location, price and free text. Returns size,
        asking price, cap rate, occupancy and address.

        Properties are the LISTINGS. Deals are the transactions being pursued against them.
        A question about pipeline, stage, tasks or offers is a deal question — use
        search_deals. A question about what is on the market, or about an asset's physical
        or financial characteristics independent of any deal, is a property question.

        UNITS: minPrice / maxPrice are whole dollars. capRate and occupancyRate come back
        as FRACTIONS (0.065 is 6.5%), and neither is filterable — filter the returned rows
        yourself if you need to, and say so if it changes the count.

        VOCABULARY (exact, case-sensitive downstream):
          - propertyType: {DealVocabulary.List(DealVocabulary.PropertyTypes)}
          - status: {DealVocabulary.List(DealVocabulary.PropertyStatuses)}
          - metroArea: formatted "<City> Metro", e.g. "Austin Metro"
          - sort: {DealVocabulary.List(DealVocabulary.PropertySorts)}; "relevance" only
            does anything when q is given. Omitted, results are newest-listed first.

        Results are capped, and the result says how many matched versus how many came back.
        Reflect that in your answer.
        """,
        JsonNode.Parse($$"""
        {
          "type": "object",
          "properties": {
            "q":            {"type": "string",  "description": "Free text over title and description."},
            "propertyType": {"type": "string",  "enum": {{Json(DealVocabulary.PropertyTypes)}}},
            "status":       {"type": "string",  "enum": {{Json(DealVocabulary.PropertyStatuses)}}},
            "metroArea":    {"type": "string",  "description": "Formatted '<City> Metro'."},
            "minPrice":     {"type": "number",  "description": "Whole dollars."},
            "maxPrice":     {"type": "number",  "description": "Whole dollars."},
            "sort":         {"type": "string",  "enum": {{Json(DealVocabulary.PropertySorts)}}},
            "limit":        {"type": "integer", "description": "1-25."}
          },
          "required": [],
          "additionalProperties": false
        }
        """));

    private static string Json(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => $"\"{v}\"")) + "]";

    public async Task<ToolOutcome> InvokeAsync(JsonNode? input, ToolContext context, CancellationToken ct)
    {
        var args = ToolArguments.Read(Name, input,
            "q", "propertyType", "status", "metroArea", "minPrice", "maxPrice", "sort", "limit");

        var limit = args.Count("limit", _options.CandidateCap, 1, 25);

        List<(string, string)> filters = [];
        void Add(string key, string? value) { if (value is { Length: > 0 }) filters.Add((key, value)); }

        Add("q", args.Text("q", 200));
        Add("propertyType", args.OneOf("propertyType", DealVocabulary.PropertyTypes));
        Add("status", args.OneOf("status", DealVocabulary.PropertyStatuses));
        Add("metroArea", args.Text("metroArea", 80));
        Add("sort", args.OneOf("sort", DealVocabulary.PropertySorts));
        Add("minPrice", args.Number("minPrice", 0, 1e12)?.ToString("0.##", CultureInfo.InvariantCulture));
        Add("maxPrice", args.Number("maxPrice", 0, 1e12)?.ToString("0.##", CultureInfo.InvariantCulture));

        // This endpoint does NOT clamp pageSize server-side, unlike its two neighbours, so
        // the cap has to hold here or an over-large limit becomes an over-large response.
        filters.Add(("pageSize", limit.ToString(CultureInfo.InvariantCulture)));

        var page = await search.SearchPropertiesAsync(filters, context.BearerToken, ct);

        if (page.Items.Count == 0)
            return new ToolOutcome(
                ToolFormat.Block("property_search", "No properties matched those filters."),
                "no properties matched");

        var sb = new StringBuilder();
        sb.AppendLine(ToolFormat.Cap(page.Items.Count, page.TotalCount, "properties"));
        sb.AppendLine();

        foreach (var property in page.Items)
        {
            var source = context.Sources.RegisterProperty(property.Id, property.Title);
            sb.AppendLine(Describe(source.Number, property));
        }

        var capped = page.Items.Count < page.TotalCount;
        return new ToolOutcome(
            ToolFormat.Block("property_search", sb.ToString()),
            capped
                ? $"{page.TotalCount} properties matched; examining {page.Items.Count}"
                : $"{page.TotalCount} propert(ies) matched",
            Capped: capped);
    }

    private static string Describe(int sourceNumber, PropertyRecord property)
    {
        var where = property.Address is { } a
            ? string.Join(", ", new[] { a.City, a.State, a.MetroArea }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : "";

        var sb = new StringBuilder();
        sb.AppendLine($"[S{sourceNumber}] {property.Title}");
        sb.AppendLine($"  propertyId: {property.Id}");
        sb.AppendLine($"  type: {ToolFormat.Text(property.PropertyType)}" +
                      $"{(property.PropertySubtype is { Length: > 0 } sub ? $" / {sub}" : "")}" +
                      $"   status: {ToolFormat.Text(property.Status)}");
        if (where.Length > 0) sb.AppendLine($"  location: {where}");
        sb.AppendLine($"  askingPrice: {ToolFormat.Money(property.AskingPrice)}   capRate: {ToolFormat.Rate(property.CapRate)}   NOI: {ToolFormat.Money(property.Noi)}");
        sb.AppendLine($"  occupancy: {ToolFormat.Rate(property.OccupancyRate)}" +
                      $"   totalSqft: {(property.TotalSqft is { } sq ? sq.ToString("N0", CultureInfo.InvariantCulture) : "unknown")}" +
                      $"   yearBuilt: {(property.YearBuilt is { } yb ? yb.ToString(CultureInfo.InvariantCulture) : "unknown")}");
        return sb.ToString();
    }
}
