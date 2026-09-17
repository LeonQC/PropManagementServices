using AiService.Business.Events;

namespace AiService.Business.Workers;

/// <summary>What the gate previously recorded for a deal. Null when the deal has never been
/// scored by this service.</summary>
/// <param name="Fingerprint">Hash of the scoring inputs as of the last run.</param>
/// <param name="LastOutputScore">The score the CURRENTLY stored rationale was written at, not
/// the deal's present score. See <see cref="DealScoreGate"/> for why the distinction matters.</param>
public readonly record struct DealScoreState(string Fingerprint, double? LastOutputScore);

/// <summary>The gate's verdict for one snapshot.</summary>
/// <param name="WriteScore">Publish the number back to deals-service.</param>
/// <param name="WriteRationale">Spend a model call on fresh prose.</param>
/// <param name="RecordFingerprint">Store <paramref name="Fingerprint"/>, marking these inputs
/// as handled. False for skips that must stay retryable, so work resumes on the next
/// republish.</param>
/// <param name="Fingerprint">Hash of this snapshot's scoring inputs. Empty when the gate
/// exited before computing one.</param>
/// <param name="Reason">Why, for the log. The cheapest way to answer "why did that deal cost
/// a model call" three weeks from now.</param>
public readonly record struct GateDecision(
    bool WriteScore,
    bool WriteRationale,
    bool RecordFingerprint,
    string Fingerprint,
    string Reason)
{
    public bool DoesNothing => !WriteScore && !WriteRationale && !RecordFingerprint;
}

/// <summary>
/// Decides whether a deal snapshot needs work. Pure — no I/O, no clock, no config reads — so
/// every branch below is directly unit-testable, which matters because this is the code that
/// keeps an event loop from running away and keeps a model call from firing on every comment.
///
/// <para><b>Two thresholds, two baselines.</b> The score is free to recompute, so it is
/// written back whenever it moves more than <see cref="WorkerOptions.ScoreWriteEpsilon"/> from
/// the number already on the deal. The rationale costs money, so it is regenerated only when
/// the score has moved <see cref="WorkerOptions.MaterialScoreDelta"/> from
/// <see cref="DealScoreState.LastOutputScore"/> — the score the stored prose was written for.
/// Comparing the rationale threshold against the deal's <i>present</i> score instead would
/// ratchet: a run of sub-threshold drifts each updates the score, so the gap never opens and
/// the prose goes stale while the number quietly crawls from 30 to 20.</para>
/// </summary>
public static class DealScoreGate
{
    public static GateDecision Decide(
        DealSnapshot snapshot,
        DealScoreState? stored,
        DealScoreResult result,
        WorkerOptions options)
    {
        // Soft-deleted. Deliberately does not record a fingerprint: if the deal comes back,
        // its inputs should look unhandled again.
        if (snapshot.Deleted) return Skip("deal is deleted");

        if (options.SkipStages.Contains(snapshot.Stage, StringComparer.OrdinalIgnoreCase))
            return Skip($"stage {snapshot.Stage} is not scored");

        // Not enough financial data to say anything. Also not recorded — filling in an offer
        // price later must re-open the deal for scoring.
        if (!result.HasScore) return Skip("too few financial inputs to score");

        var fingerprint = Fingerprint.ForDealScore(snapshot);

        var scoreMoved = snapshot.AiScore is null
            || Math.Abs(result.Score - snapshot.AiScore.Value) >= options.ScoreWriteEpsilon;

        var rationaleNeeded = options.RationaleEnabled && RationaleIsStale(snapshot, stored, result, options);

        if (!scoreMoved && !rationaleNeeded)
        {
            // This is the echo of our own write-back, and the loop ends here.
            if (stored?.Fingerprint == fingerprint)
                return Skip("inputs and score unchanged");

            // The inputs moved but the output did not. Record the new fingerprint so the next
            // identical snapshot exits one branch earlier.
            return new GateDecision(false, false, RecordFingerprint: true, fingerprint,
                "inputs changed but score did not move");
        }

        var reason = (scoreMoved, rationaleNeeded) switch
        {
            (true, true) => "score moved and rationale is stale",
            (true, false) => "score moved",
            _ => "rationale is stale",
        };

        return new GateDecision(scoreMoved, rationaleNeeded, RecordFingerprint: true, fingerprint, reason);
    }

    private static bool RationaleIsStale(
        DealSnapshot snapshot, DealScoreState? stored, DealScoreResult result, WorkerOptions options)
    {
        // No prose at all, or prose this service has no record of writing.
        if (string.IsNullOrWhiteSpace(snapshot.AiScoreRationale)) return true;
        if (stored?.LastOutputScore is not { } writtenAt) return true;

        return Math.Abs(result.Score - writtenAt) >= options.MaterialScoreDelta;
    }

    private static GateDecision Skip(string reason) => new(false, false, false, string.Empty, reason);
}
