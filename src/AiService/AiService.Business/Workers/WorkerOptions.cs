namespace AiService.Business.Workers;

/// <summary>Background rationale worker settings, bound from the "Worker" section.</summary>
public class WorkerOptions
{
    /// <summary>
    /// Whether the Kafka consumer is registered at all. Off leaves the HTTP half of the
    /// service untouched, which is what <c>scripts/eval_ragas.py</c> runs against — and with
    /// no reachable broker the Confluent consumer retries the connection forever and floods
    /// the log, so this is a real switch rather than a formality.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to spend on prose at all. Off is a hard stop: the worker still consumes and
    /// still logs what it would have done, so the gate can be watched in production before it
    /// is allowed to bill anything.
    /// </summary>
    public bool RationaleEnabled { get; set; }

    /// <summary>
    /// How far the score must move from the one the CURRENT prose was written at before it is
    /// rewritten. Compared against the stored <c>AiWorkFingerprint.LastOutputScore</c>, not
    /// against the deal's present score — see DealRationaleGate for why the present score
    /// ratchets.
    /// </summary>
    public double MaterialScoreDelta { get; set; } = 5.0;

    /// <summary>
    /// Model calls allowed at once. One by default: the worker is a single consumer and a
    /// cold start should drip rather than fan out across the whole pipeline at once.
    /// </summary>
    public int MaxConcurrentModelCalls { get; set; } = 1;

    /// <summary>Minimum gap between model calls, so a backfill spreads over minutes instead
    /// of arriving as a burst.</summary>
    public int MinCallIntervalMs { get; set; } = 1500;

    /// <summary>Model that writes the prose, resolved by the LiteLLM proxy. Threaded through
    /// the call rather than read at the call site, so comparing candidates is a config change
    /// and the cost ledger records which model actually served each row.</summary>
    public string RationaleModel { get; set; } = "rationale-default";

    /// <summary>Ceiling on the generated prose. The prompt asks for roughly forty words; this
    /// is the backstop for a model that ignores it.</summary>
    public int MaxRationaleTokens { get; set; } = 160;

    /// <summary>
    /// USD per million tokens, keyed by model. Separate from AnthropicOptions.ModelRates
    /// because these are LiteLLM route names rather than Anthropic model ids, and because the
    /// two are configured independently.
    ///
    /// <para>Keyed by model rather than by feature, for the reason AnthropicOptions documents
    /// at length: pointing a feature at a different model while its rates stayed pinned made
    /// the ledger quietly report the wrong number, which is the one failure a cost ledger
    /// exists to prevent. An unpriced model falls back to the 4.1-mini rate below, which is
    /// close enough not to mislead while still being visibly a default.</para>
    /// </summary>
    public Dictionary<string, ModelRate> ModelRates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rationale-default"] = new(0.40, 1.60),
        ["gpt-4.1-mini"] = new(0.40, 1.60),
        ["gpt-4o-mini"] = new(0.15, 0.60),
    };
}
