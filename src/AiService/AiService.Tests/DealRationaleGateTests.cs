using AiService.Business.Workers;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The gate is the only thing standing between a high-frequency topic and a bill. Two failures
/// matter and both are quiet: a gate that never says no regenerates prose on every comment, and
/// one that says no too eagerly leaves a sentence describing a score the deal no longer has.
/// </summary>
public class DealRationaleGateTests
{
    private static WorkerOptions Options(bool enabled = true, double materialDelta = 5.0) => new()
    {
        Enabled = true,
        RationaleEnabled = enabled,
        MaterialScoreDelta = materialDelta,
    };

    // ---- the off switch --------------------------------------------------------------

    /// <summary>A hard stop, checked before anything else: the worker can be left consuming and
    /// logging in production without being allowed to spend.</summary>
    [Fact]
    public void Nothing_is_generated_while_the_feature_is_off()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: null);

        Assert.False(DealRationaleGate.Decide(snapshot, null, Options(enabled: false)).Generate);
    }

    // ---- when prose is owed ----------------------------------------------------------

    [Fact]
    public void A_deal_with_no_rationale_gets_one()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: null);

        var decision = DealRationaleGate.Decide(snapshot, null, Options());

        Assert.True(decision.Generate);
        Assert.Equal("no rationale yet", decision.Reason);
    }

    [Fact]
    public void Blank_prose_counts_as_missing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: "   ");

        Assert.True(DealRationaleGate.Decide(snapshot, null, Options()).Generate);
    }

    /// <summary>Prose exists that this service has no record of writing: a restored database, or
    /// a row edited by hand. Rewrite rather than trust it.</summary>
    [Fact]
    public void Prose_with_no_recorded_baseline_is_rewritten()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: "from somewhere else");

        var decision = DealRationaleGate.Decide(snapshot, lastOutputScore: null, Options());

        Assert.True(decision.Generate);
        Assert.Contains("baseline", decision.Reason);
    }

    // ---- when it is not ---------------------------------------------------------------

    /// <summary>The cost gate. deal.snapshot is republished on every comment, task edit and
    /// document upload; none of those move the score, so none of them may spend.</summary>
    [Fact]
    public void A_deal_whose_score_has_not_moved_generates_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: "Cap rate clears the benchmark.");

        Assert.False(DealRationaleGate.Decide(snapshot, lastOutputScore: 61.0, Options()).Generate);
    }

    [Fact]
    public void A_drift_below_the_material_delta_generates_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 63.5, aiScoreRationale: "Cap rate clears the benchmark.");

        Assert.False(DealRationaleGate.Decide(snapshot, lastOutputScore: 61.0, Options()).Generate);
    }

    [Fact]
    public void A_drift_at_or_above_the_material_delta_regenerates()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 56.0, aiScoreRationale: "Cap rate clears the benchmark.");

        var decision = DealRationaleGate.Decide(snapshot, lastOutputScore: 61.0, Options());

        Assert.True(decision.Generate);
        Assert.Contains("score moved", decision.Reason);
    }

    /// <summary>Drift is symmetric. A score climbing five points has stale prose just as surely
    /// as one falling five.</summary>
    [Fact]
    public void Drift_upward_regenerates_too()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 66.5, aiScoreRationale: "Cap rate clears the benchmark.");

        Assert.True(DealRationaleGate.Decide(snapshot, lastOutputScore: 61.0, Options()).Generate);
    }

    // ---- no score to explain -----------------------------------------------------------

    /// <summary>deals-service sends no score for an unscorable or terminal deal. Prose about a
    /// number that does not exist would be pure fabrication.</summary>
    [Fact]
    public void A_deal_with_no_score_generates_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: null, aiScoreRationale: null);

        var decision = DealRationaleGate.Decide(snapshot, null, Options());

        Assert.False(decision.Generate);
        Assert.Equal("deal has no score", decision.Reason);
    }

    [Fact]
    public void A_deleted_deal_generates_nothing()
    {
        var snapshot = DealSnapshots.Complete(aiScore: 61.0, aiScoreRationale: null, deleted: true);

        Assert.False(DealRationaleGate.Decide(snapshot, null, Options()).Generate);
    }

    // ---- the ratchet -------------------------------------------------------------------

    /// <summary>
    /// The reason the baseline is the score the prose was written at, rather than the deal's
    /// present score. Each step here drifts less than the material delta, so a gate comparing
    /// against the present score would never fire — and the sentence describing a 61 would stay
    /// attached while the number walked down to 49.
    /// </summary>
    [Fact]
    public void Cumulative_drift_past_the_delta_eventually_regenerates()
    {
        const double writtenAt = 61.0;
        var options = Options();
        double[] driftingScores = [59.0, 56.5, 53.0, 49.0];

        var regenerated = driftingScores.Any(score => DealRationaleGate.Decide(
            DealSnapshots.Complete(aiScore: score, aiScoreRationale: "written at 61"),
            lastOutputScore: writtenAt,
            options).Generate);

        Assert.True(regenerated, "a score that walks away from its prose must eventually refresh it");
    }

    /// <summary>The companion to the ratchet test: each individual step really is under the
    /// threshold, so the test above is not passing for a trivial reason.</summary>
    [Fact]
    public void Each_step_of_that_drift_is_individually_below_the_delta()
    {
        double[] driftingScores = [59.0, 56.5, 53.0, 49.0];
        double previous = 61.0;

        foreach (var score in driftingScores)
        {
            Assert.True(Math.Abs(score - previous) < 5.0);
            previous = score;
        }
    }
}
