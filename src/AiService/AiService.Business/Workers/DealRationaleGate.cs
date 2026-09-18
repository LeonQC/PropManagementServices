using AiService.Business.Events;

namespace AiService.Business.Workers;

/// <summary>The gate's verdict for one snapshot.</summary>
/// <param name="Generate">Spend a model call on fresh prose.</param>
/// <param name="Reason">Why, for the log. The cheapest way to answer "why did that deal cost
/// a model call" three weeks from now.</param>
public readonly record struct RationaleDecision(bool Generate, string Reason);

/// <summary>
/// Decides whether a deal's rationale is stale enough to be worth rewriting. Pure — no I/O, no
/// clock, no config reads — because this is the only thing standing between a high-frequency
/// topic and a bill.
///
/// <para><b>The baseline is deliberate.</b> Staleness is measured against
/// <paramref name="lastOutputScore"/>, the score the stored prose was written for, not against
/// the deal's present score. Measuring against the present score ratchets: a run of
/// sub-threshold drifts each moves the number a little, the gap never opens, and the prose
/// still describes a 60 while the score reads 48.</para>
///
/// <para>The score arriving on the snapshot is computed by deals-service on publish, so this
/// worker never recomputes it and the two services cannot disagree about what the number is.</para>
/// </summary>
public static class DealRationaleGate
{
    public static RationaleDecision Decide(
        DealSnapshot snapshot, double? lastOutputScore, WorkerOptions options)
    {
        if (!options.RationaleEnabled) return No("rationale generation is disabled");
        if (snapshot.Deleted) return No("deal is deleted");

        // Unscorable, or terminal. deals-service sends no score for either, and prose
        // explaining a number that does not exist would be fabrication.
        if (snapshot.AiScore is not { } score) return No("deal has no score");

        if (string.IsNullOrWhiteSpace(snapshot.AiScoreRationale))
            return Yes("no rationale yet");

        // Prose exists that this service has no record of writing: a restored database, or a
        // row edited by hand. Rewrite rather than trust it.
        if (lastOutputScore is not { } writtenAt)
            return Yes("rationale has no recorded baseline");

        var drift = Math.Abs(score - writtenAt);
        return drift >= options.MaterialScoreDelta
            ? Yes($"score moved {drift:0.#} from the {writtenAt:0.#} the rationale was written at")
            : No($"score within {options.MaterialScoreDelta:0.#} of the {writtenAt:0.#} the rationale describes");
    }

    private static RationaleDecision Yes(string reason) => new(true, reason);
    private static RationaleDecision No(string reason) => new(false, reason);
}
