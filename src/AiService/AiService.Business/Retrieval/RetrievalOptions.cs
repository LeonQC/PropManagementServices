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

    /// <summary>How many chunks survive into the prompt, after filtering and dedupe.
    ///
    /// <para>12, raised from 8 when cross-encoder reranking shipped. The sweep in
    /// docs/retrieval-eval.md found this to be the single variable that moves recall
    /// (0.713 at 5, 0.800 at 8, 0.887 at 12 on dense), and reranking turned out to be its
    /// complement rather than its substitute: reranking at 8 reaches 0.975, the same as
    /// 12 without reranking, while the two together reach 1.000 with zero recall lost
    /// between fetch and prompt.</para></summary>
    public int MaxContextChunks { get; set; } = 12;

    /// <summary>Absolute cosine floor. Below this a chunk is noise; if nothing clears
    /// it, the service answers "not in this deal's documents" without calling Claude.
    ///
    /// <para>CALIBRATED PER EMBEDDING MODEL, and not portable — cosine has no absolute
    /// meaning across models. 0.375 for <c>embed-local</c> (bge-m3); 0.15 was the value
    /// for <c>embed-openai</c>, whose scale runs far lower. Carrying 0.15 onto bge-m3
    /// measured off-domain abstention at 0.00 — nothing errors, recall stays at 1.000, and
    /// the service simply starts answering questions it should decline. Changing
    /// EMBEDDING_MODEL means re-sweeping this with scripts/eval_retrieval.py; the numbers
    /// and both models' score distributions are in docs/retrieval-eval.md.</para></summary>
    public double MinScore { get; set; } = 0.375;

    /// <summary>Relative floor: drop chunks scoring below this fraction of the best
    /// hit. Catches the weak tail of an otherwise good result set, which an absolute
    /// threshold can't — a strong question and a vague one have different top scores.</summary>
    public double RelativeFloor { get; set; } = 0.55;

    /// <summary>Cap on characters of chunk text placed in the prompt, as a backstop
    /// against a pathological document blowing out the context window.</summary>
    public int MaxContextChars { get; set; } = 24_000;
}
