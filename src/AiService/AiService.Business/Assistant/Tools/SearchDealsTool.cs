using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AiService.Business.Assistant.Clients;
using Microsoft.Extensions.Options;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Structured deal search — the narrowing tool, and the one the prompt insists runs before
/// any document retrieval.
///
/// <para>Backed by <c>GET /search/v1/deals</c> rather than the identical deals-service
/// endpoint, for the free-text half: OpenSearch tolerates the typos and near-misses a
/// model produces when it turns "the Denver warehouse deal" into a query.</para>
/// </summary>
public class SearchDealsTool(SearchClient search, IOptions<AssistantOptions> options) : IAssistantTool
{
    private readonly AssistantOptions _options = options.Value;

    public string Name => "search_deals";

    public string Label(JsonNode? input) => "Searching deals…";

    public Tool Definition { get; } = new Function(
        "search_deals",
        $"""
        Find deals by structured filters and/or free text. Returns a ranked page of deal
        records with their stage, financials, occupancy and health flags.

        USE THIS FIRST. It is the cheap way to narrow the portfolio down to a handful of
        deals. Document search is far more expensive, so any question that combines a deal
        property with something from the documents should filter here first and only then
        search the documents of the deals that survived.

        Filters are combined with AND. All are optional; with none, you get the newest deals.

        UNITS — read carefully:
          - capRateMin / capRateMax are FRACTIONS, not percentages. 6.5% is 0.065.
            Passing 6.5 matches nothing and returns an empty result, not an error.
          - offerPriceMin / offerPriceMax are whole dollars, e.g. 25000000.
          - occupancyMin / occupancyMax are FRACTIONS too. 70% occupancy is 0.70.
            Filter here rather than retrieving deals and sorting them yourself: the filter
            runs across every matching deal, where your own filtering can only see the page
            you were given, and would silently miss the rest.

        VOCABULARY — these values are exact and case-sensitive downstream:
          - stage: {DealVocabulary.List(DealVocabulary.Stages)}
          - priority: {DealVocabulary.List(DealVocabulary.Priorities)}
          - propertyType: {DealVocabulary.List(DealVocabulary.PropertyTypes)}
          - metroArea: formatted "<City> Metro", e.g. "Austin Metro", "Denver Metro"

        OTHER FILTERS:
          - q: free text over the deal name and its property name. Use for a named deal or
            an asset you only know by description.
          - staleDays: deals that have sat in their current stage at least this many days.
            This is the filter for "stalling" or "stuck" questions.
          - hasOverdueTasks: true for deals with at least one overdue task.
          - closeDateBefore / closeDateAfter: yyyy-MM-dd, on the projected close date.
          - ownerId: a user id (a GUID), not a person's name. Omit it unless you were given
            an actual id — there is no name lookup here.
          - limit: how many deals to return, 1-25.

        Results are capped. The result states how many matched in total versus how many were
        returned, and you must reflect that count in your answer rather than implying you
        saw every match.
        """,
        JsonNode.Parse($$"""
        {
          "type": "object",
          "properties": {
            "q":               {"type": "string",  "description": "Free text over deal name and property name."},
            "stage":           {"type": "string",  "enum": {{Json(DealVocabulary.Stages)}}},
            "priority":        {"type": "string",  "enum": {{Json(DealVocabulary.Priorities)}}},
            "propertyType":    {"type": "string",  "enum": {{Json(DealVocabulary.PropertyTypes)}}},
            "metroArea":       {"type": "string",  "description": "Formatted '<City> Metro', e.g. 'Denver Metro'."},
            "capRateMin":      {"type": "number",  "description": "FRACTION, not percent. 6.5% is 0.065."},
            "capRateMax":      {"type": "number",  "description": "FRACTION, not percent. 6.5% is 0.065."},
            "occupancyMin":    {"type": "number",  "description": "FRACTION, not percent. 70% is 0.70."},
            "occupancyMax":    {"type": "number",  "description": "FRACTION, not percent. 70% is 0.70."},
            "offerPriceMin":   {"type": "number",  "description": "Whole dollars."},
            "offerPriceMax":   {"type": "number",  "description": "Whole dollars."},
            "closeDateBefore": {"type": "string",  "description": "yyyy-MM-dd."},
            "closeDateAfter":  {"type": "string",  "description": "yyyy-MM-dd."},
            "staleDays":       {"type": "integer", "description": "At least this many days in the current stage."},
            "hasOverdueTasks": {"type": "boolean"},
            "ownerId":         {"type": "string",  "description": "A user GUID. There is no lookup by name."},
            "limit":           {"type": "integer", "description": "1-25. Defaults to the server's candidate cap."}
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
            "q", "stage", "priority", "propertyType", "metroArea", "capRateMin", "capRateMax",
            "occupancyMin", "occupancyMax", "offerPriceMin", "offerPriceMax",
            "closeDateBefore", "closeDateAfter", "staleDays", "hasOverdueTasks", "ownerId", "limit");

        var limit = args.Count("limit", _options.CandidateCap, 1, 25);

        List<(string, string)> filters = [];
        void Add(string key, string? value) { if (value is { Length: > 0 }) filters.Add((key, value)); }

        Add("q", args.Text("q", 200));
        Add("stage", args.OneOf("stage", DealVocabulary.Stages));
        Add("priority", args.OneOf("priority", DealVocabulary.Priorities));
        Add("propertyType", args.OneOf("propertyType", DealVocabulary.PropertyTypes));
        Add("metroArea", args.Text("metroArea", 80));
        Add("closeDateBefore", args.Date("closeDateBefore"));
        Add("closeDateAfter", args.Date("closeDateAfter"));
        Add("ownerId", args.Text("ownerId", 80));

        // Cap rates are stored as fractions. A model that sends 6.5 meaning 6.5% would
        // otherwise get a silent empty result; the upper bound of 1.0 turns that into a
        // tool error naming the correct form.
        Add("capRateMin", Invariant(args.Number("capRateMin", 0, 1)));
        Add("capRateMax", Invariant(args.Number("capRateMax", 0, 1)));
        Add("occupancyMin", Invariant(args.Number("occupancyMin", 0, 1)));
        Add("occupancyMax", Invariant(args.Number("occupancyMax", 0, 1)));
        Add("offerPriceMin", Invariant(args.Number("offerPriceMin", 0, 1e12)));
        Add("offerPriceMax", Invariant(args.Number("offerPriceMax", 0, 1e12)));

        if (args.Count("staleDays", 0, 0, 3650) is var stale and > 0)
            Add("staleDays", stale.ToString(CultureInfo.InvariantCulture));
        if (args.Flag("hasOverdueTasks") is { } overdue)
            Add("hasOverdueTasks", overdue ? "true" : "false");

        filters.Add(("pageSize", limit.ToString(CultureInfo.InvariantCulture)));

        var page = await search.SearchDealsAsync(filters, context.BearerToken, ct);

        if (page.Items.Count == 0)
            return new ToolOutcome(
                ToolFormat.Block("deal_search", "No deals matched those filters."),
                "no deals matched");

        var sb = new StringBuilder();
        sb.AppendLine(ToolFormat.Cap(page.Items.Count, page.TotalCount, "deals"));
        sb.AppendLine();

        foreach (var deal in page.Items)
        {
            var source = context.Sources.RegisterDeal(deal.Id, deal.Name);
            sb.AppendLine(Describe(source.Number, deal));
        }

        var capped = page.Items.Count < page.TotalCount;
        return new ToolOutcome(
            ToolFormat.Block("deal_search", sb.ToString()),
            capped
                ? $"{page.TotalCount} deals matched; examining {page.Items.Count}"
                : $"{page.TotalCount} deal(s) matched",
            Capped: capped);
    }

    /// <summary>
    /// One deal as a compact labelled block. Days-in-stage is computed rather than left as
    /// a timestamp because "stalling" questions turn entirely on it, and asking the model
    /// to subtract dates is a needless place for it to go wrong.
    /// </summary>
    internal static string Describe(int sourceNumber, DealRecord deal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[S{sourceNumber}] {deal.Name}");
        sb.AppendLine($"  dealId: {deal.Id}");
        sb.AppendLine($"  property: {ToolFormat.Text(deal.PropertyName)} ({ToolFormat.Text(deal.PropertyType)}, {ToolFormat.Text(deal.MetroArea)})");

        var days = ToolFormat.DaysSince(deal.StageEnteredAt);
        sb.AppendLine($"  stage: {deal.Stage}{(days is { } d ? $" (in this stage {d} day(s))" : "")}");
        sb.AppendLine($"  priority: {ToolFormat.Text(deal.Priority)}");
        sb.AppendLine($"  offerPrice: {ToolFormat.Money(deal.OfferPrice)}   projectedCapRate: {ToolFormat.Rate(deal.ProjectedCapRate)}");
        sb.AppendLine($"  occupancy: {ToolFormat.Rate(deal.OccupancyRate)}   marketCapRateBenchmark: {ToolFormat.Rate(deal.MarketCapRateBenchmark)}");

        if (deal.ProjectedCloseDate is { Length: > 0 } close)
            sb.AppendLine($"  projectedCloseDate: {close}");
        if (deal.DeadReason is { Length: > 0 } dead)
            sb.AppendLine($"  deadReason: {dead}");

        sb.AppendLine($"  tasks: {deal.DoneTaskCount}/{deal.TaskCount} done{(deal.HasOverdueTasks ? ", SOME OVERDUE" : "")}");

        if (deal.HealthFlags is { Count: > 0 } flags)
            foreach (var flag in flags)
                sb.AppendLine($"  healthFlag[{flag.Severity}] {flag.Type}: {flag.Message}");

        return sb.ToString();
    }

    private static string? Invariant(double? value) =>
        value?.ToString("0.############", CultureInfo.InvariantCulture);
}
