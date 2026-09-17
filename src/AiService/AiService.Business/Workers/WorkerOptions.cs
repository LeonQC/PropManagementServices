namespace AiService.Business.Workers;

/// <summary>Background scoring worker settings, bound from the "Worker" section.</summary>
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
    /// Whether to generate the model-written rationale prose. False through slice A, where
    /// the worker computes and publishes the deterministic score only and never spends.
    /// Turning it on without a configured rationale model is safe: the call is skipped.
    /// </summary>
    public bool RationaleEnabled { get; set; }

    /// <summary>
    /// How far the score must move from the one already on the deal before it is written
    /// back. Guards against float noise republishing the deal for a change of 0.0000001,
    /// which would be a write amplification loop rather than an infinite one.
    /// </summary>
    public double ScoreWriteEpsilon { get; set; } = 0.5;

    /// <summary>
    /// How far the score must move from the score the CURRENT rationale was written at
    /// before the prose is regenerated. Compared against the stored
    /// <c>AiWorkFingerprint.LastOutputScore</c>, not against the deal's present score — see
    /// DealScoreGate for why comparing against the present score ratchets.
    /// </summary>
    public double MaterialScoreDelta { get; set; } = 5.0;

    /// <summary>
    /// Stages that are never scored. A dead deal's number would be noise on a card nobody
    /// acts on, and rescoring it on every archival comment would be pure cost.
    /// </summary>
    public string[] SkipStages { get; set; } = ["Dead"];
}
