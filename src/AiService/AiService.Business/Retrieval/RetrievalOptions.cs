namespace AiService.Business.Retrieval;

/// <summary>
/// Retrieval tuning, bound from the "Retrieval" config section.
///
/// <para>The score thresholds are calibrated against the real corpus, not the ~0.7
/// figure in architecture §3.3 — that value does not survive contact with the
/// embedding model actually in use. Measured cosine scores from
/// `text-embedding-3-small@1024` over this corpus:</para>
/// <list type="bullet">
///   <item>on-topic questions ("what is the going-in cap rate?"): top hit 0.38–0.53</item>
///   <item>off-domain questions ("best recipe for sourdough bread?"): top hit 0.07–0.08</item>
///   <item>in-domain but absent ("the tenant's dog policy for the rooftop pool?"): top hit 0.29–0.32</item>
/// </list>
/// <para>A 0.7 floor would reject every genuine hit and the feature would decline
/// every question. 0.15 cleanly separates off-domain noise from real matches.</para>
///
/// <para>Note the third band: a question about this asset class whose answer simply
/// isn't in the documents still scores 0.29 — inside the on-topic range. No absolute
/// threshold can separate "answerable" from "in-domain but not covered", so the floor
/// is not what makes the feature decline; the system prompt is. The floor's job is to
/// keep obvious noise out of the prompt, not to judge coverage.</para>
/// </summary>
public class RetrievalOptions
{
    /// <summary>How many chunks to request from ingestion-service. Over-fetch, then
    /// narrow — the extra rows cost one index scan and give the filters room to work.</summary>
    public int FetchTopK { get; set; } = 20;

    /// <summary>How many chunks survive into the prompt, after filtering and dedupe.</summary>
    public int MaxContextChunks { get; set; } = 8;

    /// <summary>Absolute cosine floor. Below this a chunk is noise; if nothing clears
    /// it, the service answers "not in this deal's documents" without calling Claude.</summary>
    public double MinScore { get; set; } = 0.15;

    /// <summary>Relative floor: drop chunks scoring below this fraction of the best
    /// hit. Catches the weak tail of an otherwise good result set, which an absolute
    /// threshold can't — a strong question and a vague one have different top scores.</summary>
    public double RelativeFloor { get; set; } = 0.55;

    /// <summary>Cap on characters of chunk text placed in the prompt, as a backstop
    /// against a pathological document blowing out the context window.</summary>
    public int MaxContextChars { get; set; } = 24_000;
}
