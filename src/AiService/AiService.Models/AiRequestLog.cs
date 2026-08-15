namespace AiService.Models;

/// <summary>
/// One row per Claude call — including the ones that failed, so an outage shows up
/// here rather than only in the logs. Token counts and latency are what make cost
/// attributable per feature (architecture §3.4).
///
/// <para>Deliberately stores no prompt or answer text. The prompt embeds retrieved
/// chunks from user-uploaded documents, and this table is the sort of thing that
/// gets dumped into a spreadsheet — keeping content out of it means the cost ledger
/// never becomes a second, unguarded copy of the corpus.</para>
/// </summary>
public class AiRequestLog
{
    public required string Id { get; set; }

    /// <summary>Which feature spent the tokens, e.g. "deal_qa".</summary>
    public required string Feature { get; set; }

    public required string Model { get; set; }

    /// <summary>The authenticated caller ("sub"), for per-user attribution.</summary>
    public string? UserId { get; set; }

    /// <summary>The entity the request was scoped to — the deal id, for Deal Q&amp;A.</summary>
    public string? EntityId { get; set; }

    public required int InputTokens { get; set; }
    public required int OutputTokens { get; set; }

    /// <summary>Wall-clock duration of the model call itself, not the whole request.</summary>
    public required int LatencyMs { get; set; }

    /// <summary>Computed from the token counts and the model's per-token rates at
    /// call time, so a later price change doesn't rewrite history.</summary>
    public double? CostUsd { get; set; }

    /// <summary>How many retrieved chunks were placed in the prompt. Zero means the
    /// relevance floor rejected everything and no call was made.</summary>
    public int? ChunkCount { get; set; }

    public required bool Succeeded { get; set; }

    /// <summary>Failure reason when <see cref="Succeeded"/> is false. Exception type
    /// and message only — never response content.</summary>
    public string? Error { get; set; }

    public required string CreatedAt { get; set; }
}
