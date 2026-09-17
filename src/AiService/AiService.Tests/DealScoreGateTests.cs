using AiService.Business.Workers;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The gate decides whether a snapshot costs anything. Two failures matter and both are quiet:
/// a gate that never says "skip" rescores in a loop, and a gate that says "skip" too eagerly
/// leaves a stale number on a card nobody questions.
/// </summary>
public class DealScoreGateTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    private static WorkerOptions Options(bool rationaleEnabled = false) => new()
    {
        Enabled = true,
        RationaleEnabled = rationaleEnabled,
        ScoreWriteEpsilon = 0.5,
        MaterialScoreDelta = 5.0,
        SkipStages = ["Dead"],
    };

    private static GateDecision Decide(
        Business.Events.DealSnapshot snapshot, DealScoreState? stored = null, WorkerOptions? options = null)
    {
        var opts = options ?? Options();
        return DealScoreGate.Decide(snapshot, stored, DealScore.Compute(snapshot, AsOf), opts);
    }

    /// <summary>Score it once, then leave it alone. A deal nobody has scored has no
    /// fingerprint and no AiScore, so the first snapshot always does work.</summary>
    [Fact]
    public void First_sight_of_a_deal_writes_the_score()
    {
        var decision = Decide(DealSnapshots.Complete(aiScore: null));

        Assert.True(decision.WriteScore);
        Assert.True(decision.RecordFingerprint);
        Assert.NotEqual(string.Empty, decision.Fingerprint);
    }

    /// <summary>
    /// The loop terminator, and the single most important assertion in the suite. After the
    /// score is written back, deals-service republishes the snapshot with the number now on
    /// it. That echo must do nothing at all.
    /// </summary>
    [Fact]
    public void The_echo_of_our_own_write_back_does_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;

        // What deals-service republishes after applying the score.
        var echo = snapshot with { Version = snapshot.Version + 1, AiScore = score };
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), null);

        var decision = Decide(echo, stored);

        Assert.True(decision.DoesNothing);
        Assert.Equal("inputs and score unchanged", decision.Reason);
    }

    /// <summary>A comment changes CommentText and nothing else the score reads, so it must
    /// not even reach the point of publishing.</summary>
    [Fact]
    public void Adding_a_comment_does_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var scored = snapshot with { AiScore = DealScore.Compute(snapshot, AsOf).Score };
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), null);

        var chatty = scored with
        {
            Version = scored.Version + 5,
            CommentText = "a long new thread about the rent roll"
        };

        Assert.True(Decide(chatty, stored).DoesNothing);
    }

    [Fact]
    public void A_deleted_deal_is_skipped_without_recording_a_fingerprint()
    {
        var decision = Decide(DealSnapshots.Complete(deleted: true));

        Assert.True(decision.DoesNothing);
        Assert.False(decision.RecordFingerprint);
    }

    [Fact]
    public void A_dead_deal_is_skipped()
    {
        var decision = Decide(DealSnapshots.Complete(stage: "Dead"));

        Assert.True(decision.DoesNothing);
        Assert.Contains("Dead", decision.Reason);
    }

    /// <summary>
    /// Not recording a fingerprint for an unscorable deal is deliberate: filling in an offer
    /// price later has to re-open it for scoring, and a recorded fingerprint would mean the
    /// only thing that ever revives it is a change to some other field.
    /// </summary>
    [Fact]
    public void An_unscorable_deal_is_skipped_and_stays_retryable()
    {
        var decision = Decide(DealSnapshots.NonFinancialOnly());

        Assert.True(decision.DoesNothing);
        Assert.False(decision.RecordFingerprint);
    }

    /// <summary>Float noise must not republish the deal. A change under the epsilon is not a
    /// change worth a Kafka message and a version bump.</summary>
    [Fact]
    public void A_drift_below_the_epsilon_does_not_write_the_score()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), null);

        var decision = Decide(snapshot with { AiScore = score + 0.2 }, stored);

        Assert.False(decision.WriteScore);
    }

    [Fact]
    public void A_drift_above_the_epsilon_writes_the_score()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), null);

        var decision = Decide(snapshot with { AiScore = score + 1.5 }, stored);

        Assert.True(decision.WriteScore);
        Assert.Equal("score moved", decision.Reason);
    }

    /// <summary>
    /// Inputs moved but the number came out the same. Nothing to publish, but the new
    /// fingerprint is worth storing so the next identical snapshot exits one branch earlier.
    /// </summary>
    [Fact]
    public void Changed_inputs_that_do_not_move_the_score_only_record_the_fingerprint()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;
        var scored = snapshot with { AiScore = score };

        var stored = new DealScoreState("a-fingerprint-from-some-earlier-shape", null);
        var decision = Decide(scored, stored);

        Assert.False(decision.WriteScore);
        Assert.True(decision.RecordFingerprint);
        Assert.Equal(Fingerprint.ForDealScore(scored), decision.Fingerprint);
    }

    // ---- rationale gating ------------------------------------------------------------

    /// <summary>Through slice A there is no model client, so the gate must never ask for
    /// prose however stale it looks.</summary>
    [Fact]
    public void No_rationale_is_requested_while_the_feature_is_off()
    {
        var decision = Decide(DealSnapshots.Complete(aiScore: null, aiScoreRationale: null));

        Assert.False(decision.WriteRationale);
    }

    [Fact]
    public void A_blank_rationale_is_requested_once_the_feature_is_on()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null, aiScoreRationale: null);

        Assert.True(Decide(snapshot, options: Options(rationaleEnabled: true)).WriteRationale);
    }

    [Fact]
    public void A_fresh_rationale_is_left_alone()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;
        var scored = snapshot with { AiScore = score, AiScoreRationale = "Cap rate clears the benchmark." };
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), score);

        Assert.True(Decide(scored, stored, Options(rationaleEnabled: true)).DoesNothing);
    }

    /// <summary>
    /// The ratchet. Each drift is under the material delta, and each one updates AiScore — so a
    /// gate that measured staleness against the deal's present score would never regenerate,
    /// and the prose would still describe a 60 while the number reads 48. Measuring against
    /// the score the prose was written at is what closes the gap.
    /// </summary>
    [Fact]
    public void Successive_small_drifts_still_eventually_refresh_the_rationale()
    {
        var options = Options(rationaleEnabled: true);
        var baseline = DealSnapshots.Complete(aiScore: null);
        var rationaleWrittenAt = DealScore.Compute(baseline, AsOf).Score;

        // Each step is a real input change that moves the score by roughly two points, which
        // is over the write epsilon but under the material delta.
        var occupancies = new[] { 0.85, 0.82, 0.79, 0.76 };
        var refreshed = false;

        foreach (var occupancy in occupancies)
        {
            var drifted = DealSnapshots.Complete(occupancyRate: occupancy);
            var current = DealScore.Compute(drifted, AsOf).Score;

            // The deal always carries the latest score; the stored baseline never moves,
            // because no rationale has been rewritten.
            var snapshot = drifted with { AiScore = current, AiScoreRationale = "written at the baseline" };
            var stored = new DealScoreState(Fingerprint.ForDealScore(drifted), rationaleWrittenAt);

            if (DealScoreGate.Decide(snapshot, stored, DealScore.Compute(snapshot, AsOf), options).WriteRationale)
            {
                refreshed = true;
                break;
            }
        }

        Assert.True(refreshed, "cumulative drift past the material delta must refresh the rationale");
    }

    /// <summary>Prose exists but this service has no record of writing it — a restored
    /// database, or a row edited by hand. Regenerate rather than trust it.</summary>
    [Fact]
    public void A_rationale_with_no_recorded_baseline_is_regenerated()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null);
        var score = DealScore.Compute(snapshot, AsOf).Score;
        var scored = snapshot with { AiScore = score, AiScoreRationale = "from somewhere else" };
        var stored = new DealScoreState(Fingerprint.ForDealScore(snapshot), LastOutputScore: null);

        Assert.True(Decide(scored, stored, Options(rationaleEnabled: true)).WriteRationale);
    }
}
