namespace AiService.Models;

/// <summary>
/// One row per user question, where <see cref="AiRequestLog"/> is one row per model turn.
///
/// <para>The two tables answer different questions and neither can answer the other's.
/// Summing <see cref="AiRequestLog.LatencyMs"/> across a question gives model time only —
/// measured at 35.6s against 45s of actual wall clock, because the gap is tool execution:
/// query embedding, cross-encoder reranking, and HTTP round trips to deals, search and
/// ingestion. "How long did the user wait" is this table's <see cref="TotalLatencyMs"/>.</para>
///
/// <para>The loop outcome lives here for the same reason. <see cref="Truncated"/>,
/// <see cref="Iterations"/> and <see cref="ToolCalls"/> are properties of the question, not
/// of any one turn, and a truncated answer is a different failure from a slow one — the
/// loop stopped early and the model reported what it could not check.</para>
///
/// <para>Cost stays where it is: join ai_request_log on <see cref="CorrelationId"/> and sum.
/// Duplicating it here would give two numbers that drift.</para>
///
/// <para>Stores no prompt or answer text, for the same reason <see cref="AiRequestLog"/>
/// does not — see that type's remarks.</para>
/// </summary>
public class AiQuestionLog
{
    public required string Id { get; set; }

    /// <summary>Ties this question to its model turns in ai_request_log. Unique: one row
    /// per question is the whole point of the table.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Which feature answered it, e.g. "deal_assistant".</summary>
    public required string Feature { get; set; }

    /// <summary>Null when the question was rejected before a model was chosen.</summary>
    public string? Model { get; set; }

    /// <summary>The authenticated caller ("sub"), for per-user attribution.</summary>
    public string? UserId { get; set; }

    /// <summary>The entity the question was scoped to — the deal id, when there was one.</summary>
    public string? EntityId { get; set; }

    /// <summary>Wall clock for the whole question, from the first byte of work to the last.
    /// This is the figure AssistantService already reports in the SSE "done" event, and the
    /// one to take percentiles over.</summary>
    public required int TotalLatencyMs { get; set; }

    /// <summary>Model turns the loop took. Always 1 for the single-call features.</summary>
    public required int Iterations { get; set; }

    /// <summary>Tool calls made across every turn. Always 0 for the single-call features.</summary>
    public required int ToolCalls { get; set; }

    /// <summary>True when a budget stopped the loop before the model was finished —
    /// iterations, tool calls, context chars or the wall clock. See
    /// <see cref="TruncationReason"/> for which one.</summary>
    public required bool Truncated { get; set; }

    /// <summary>Which budget bound first, when <see cref="Truncated"/> is true. The flag
    /// alone cannot say, and the four budgets call for different fixes.</summary>
    public string? TruncationReason { get; set; }

    public required bool Succeeded { get; set; }

    /// <summary>Failure reason when <see cref="Succeeded"/> is false — the error code the
    /// caller was given, never response content.</summary>
    public string? Error { get; set; }

    public required string CreatedAt { get; set; }
}
