using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AiService.Business.Assistant.Clients;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Deal counts and value per pipeline stage — the cheapest tool in the set, and the one
/// that answers "how many deals are in underwriting?" without touching a document.
/// </summary>
public class PipelineSummaryTool(DealRecordClient deals) : IAssistantTool
{
    public string Name => "pipeline_summary";

    public string Label(JsonNode? input) => "Reading the pipeline summary…";

    public Tool Definition { get; } = new Function(
        "pipeline_summary",
        """
        Counts and total value of deals in each pipeline stage, across the whole portfolio.

        Takes no arguments. Use this for questions about how many deals are at a stage, how
        the pipeline is distributed, or what the pipeline is worth. It is much cheaper than
        listing deals, so prefer it whenever a count or a total is all that is being asked
        for.

        The six stages, in board order: InitialInterest, NdaLoi, UnderwritingReview,
        InvestmentCommittee, Acquired, Dead.

        Note that the reported active-deal count and total pipeline value deliberately
        exclude the two terminal stages (Acquired and Dead), while the per-stage rows include
        them. The totals are therefore smaller than the sum of the rows. This is correct —
        do not reconcile it or describe it as an inconsistency.
        """,
        JsonNode.Parse("""{"type":"object","properties":{},"required":[],"additionalProperties":false}"""));

    public async Task<ToolOutcome> InvokeAsync(JsonNode? input, ToolContext context, CancellationToken ct)
    {
        var summary = await deals.GetPipelineSummaryAsync(context.BearerToken, ct);

        var sb = new StringBuilder();
        sb.AppendLine("<pipeline_summary>");
        sb.AppendLine($"Active deals (excludes Acquired and Dead): {summary.TotalActiveDeals}");
        sb.AppendLine($"Total active pipeline value: {Money(summary.TotalPipelineValue)}");
        sb.AppendLine();
        sb.AppendLine("Per stage (all six, including terminal stages):");
        foreach (var stage in summary.Stages)
            sb.AppendLine($"  {stage.Stage}: {stage.Count} deal(s), {Money(stage.TotalValue)}");
        sb.AppendLine("</pipeline_summary>");

        var active = summary.Stages
            .Where(s => s.Stage is not ("Acquired" or "Dead"))
            .Sum(s => s.Count);

        return new ToolOutcome(sb.ToString(), $"{active} active deals across {summary.Stages.Count} stages");
    }

    private static string Money(double value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
}
