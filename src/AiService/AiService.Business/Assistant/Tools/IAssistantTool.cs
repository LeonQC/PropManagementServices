using System.Text.Json.Nodes;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Everything a tool needs that the model does not get to choose.
///
/// <para><paramref name="BearerToken"/> is the caller's own, forwarded verbatim.
/// <paramref name="PinnedDealId"/> and <paramref name="PinnedDocumentId"/> come from the
/// request body, not the model: when the deal panel asks a question, the deal is a hard
/// scope the model cannot widen, which is why it is applied here rather than suggested in
/// the prompt.</para>
/// </summary>
public sealed record ToolContext(
    string BearerToken,
    string? PinnedDealId,
    string? PinnedDocumentId,
    SourceRegistry Sources);

/// <summary>
/// What one tool call produced. <paramref name="Text"/> goes back to the model verbatim;
/// <paramref name="Summary"/> is the human-readable line the UI shows in the progress
/// list. <paramref name="Capped"/> says the result set was truncated, which the model is
/// told to disclose and the UI surfaces too.
/// </summary>
public sealed record ToolOutcome(string Text, string Summary, bool Capped = false, bool IsError = false)
{
    public static ToolOutcome Error(string message) => new(message, message, IsError: true);
}

/// <summary>
/// One read-only capability exposed to the model. Implementations own their JSON schema,
/// their argument validation, and the shape of the text handed back — because those three
/// have to agree, and splitting them across layers is how a tool ends up accepting an
/// argument its downstream call silently ignores.
/// </summary>
public interface IAssistantTool
{
    /// <summary>The name the model calls, e.g. <c>pipeline_summary</c>.</summary>
    string Name { get; }

    /// <summary>Name, description and input schema, as sent in the request's tools array.</summary>
    Tool Definition { get; }

    /// <summary>The progress line shown while this call runs ("Reading the pipeline summary…").</summary>
    string Label(JsonNode? input);

    Task<ToolOutcome> InvokeAsync(JsonNode? input, ToolContext context, CancellationToken ct);
}
