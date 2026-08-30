namespace AiService.Business.Assistant;

/// <summary>
/// The budgets that bound one assistant question, from the "Assistant" config section.
///
/// <para>Four of them, because no one of them bounds the others. An iteration cap alone
/// does not bound work: the model may issue many tool calls in a single turn, so forty
/// scoped document searches fit inside iteration one. A tool-call cap alone does not
/// bound context: a handful of calls over a large rent roll can still fill the window.
/// And neither bounds latency, which is what the user actually experiences.</para>
/// </summary>
public class AssistantOptions
{
    /// <summary>Model turns per question. Six is enough for the deepest planned pattern
    /// — narrow with a structured filter, retrieve per candidate, then answer — with
    /// room for one correction after a rejected tool argument.</summary>
    public int MaxIterations { get; set; } = 6;

    /// <summary>Tool calls per question, across every iteration. This is the fan-out
    /// control: <see cref="MaxIterations"/> is not, because parallel tool use puts many
    /// calls inside one turn.</summary>
    public int MaxToolCalls { get; set; } = 12;

    /// <summary>Rows handed to the model from a search tool. The rest are withheld and
    /// the cap is stated in the result, so an answer can say it examined ten of forty
    /// rather than implying it saw all forty.</summary>
    public int CandidateCap { get; set; } = 10;

    /// <summary>
    /// Characters of tool-result text allowed into the conversation. Same units as
    /// RetrievalOptions.MaxContextChars, and for the same reason: characters are what we
    /// can count without a tokenizer.
    ///
    /// <para>48k (~16k tokens), double the ~8k the design doc specifies. Measured: the
    /// feature's headline question — narrow to four stalled deals, then read each one's
    /// documents — spends ~4k characters on the structured search and ~4-6k per document
    /// retrieval, so 24k ran out after three deals and the fourth was never read. The old
    /// figure was written when the plan was one retrieval per answer; it does not describe
    /// a loop that retrieves per candidate.</para>
    ///
    /// <para>Deliberately not larger. This is the budget that directly buys input tokens,
    /// and input is roughly three quarters of the cost of a question — the context window
    /// is nowhere near the limit here, the bill is.</para>
    /// </summary>
    public int MaxContextChars { get; set; } = 48_000;

    /// <summary>
    /// Wall clock for the whole question. Checked between turns rather than enforced
    /// mid-stream, so a long final answer is never truncated halfway — which also means a
    /// turn starting just inside the budget may finish well outside it.
    ///
    /// <para>90, not the 30 the design doc specifies. 30 was written before the loop
    /// existed and does not survive contact with the pattern this feature is built around:
    /// "which industrial deals are stalling, and what do their documents say?" measured at
    /// 64s of entirely legitimate work — one structured search plus four scoped document
    /// retrievals, each embedding a query and running a cross-encoder. At 30s that question
    /// returns truncated every time, having silently skipped deals it had already
    /// identified as candidates. A budget that the feature's headline use case cannot meet
    /// is not a safety limit, it is a guarantee of partial answers.</para>
    /// </summary>
    public int WallClockSeconds { get; set; } = 90;

    /// <summary>Client-supplied conversation turns kept, most recent first. History is
    /// sent by the client (there is no threads table in v1), so this is what stops an
    /// old conversation from crowding out the tool results of the current question.</summary>
    public int MaxHistoryTurns { get; set; } = 10;

    /// <summary>Longest question accepted, matching Deal Q&amp;A's limit.</summary>
    public int MaxQuestionChars { get; set; } = 2_000;
}
