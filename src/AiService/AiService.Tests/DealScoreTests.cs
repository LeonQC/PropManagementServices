using AiService.Business.Workers;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The score is the one part of this feature the model never touches, so it has to be right on
/// its own terms: reproducible, insensitive to missing data in the right way, and sensitive to
/// unit errors rather than silently swallowing them.
/// </summary>
public class DealScoreTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Reads one component's sub-score straight off the result, so a band can be
    /// pinned without reasoning about the weighted total.</summary>
    private static double? SubScore(string name, Business.Events.DealSnapshot snapshot) =>
        DealScore.Compute(snapshot, AsOf).Components.Single(c => c.Name == name).Value;

    // ---- cap rate spread -------------------------------------------------------------

    [Theory]
    // Matching the benchmark is the neutral point, not a good or a bad outcome.
    [InlineData(0.0650, 0.0650, 50.0)]
    // +200bps of spread is the top of the range, -100bps the bottom.
    [InlineData(0.0850, 0.0650, 100.0)]
    [InlineData(0.0550, 0.0650, 0.0)]
    // Asymmetric by design: the same 50bps is worth more against you than for you.
    [InlineData(0.0700, 0.0650, 62.5)]
    [InlineData(0.0600, 0.0650, 25.0)]
    // Past either end it clamps rather than running off the scale.
    [InlineData(0.1200, 0.0650, 100.0)]
    [InlineData(0.0300, 0.0650, 0.0)]
    public void Cap_rate_spread_is_centred_at_the_benchmark_and_asymmetric(
        double projected, double benchmark, double expected)
    {
        var snapshot = DealSnapshots.Complete(projectedCapRate: projected, marketCapRateBenchmark: benchmark);
        Assert.Equal(expected, SubScore(DealScore.CapRateSpread, snapshot)!.Value, 3);
    }

    [Fact]
    public void Cap_rate_spread_is_unknown_when_either_side_is_missing()
    {
        Assert.Null(SubScore(DealScore.CapRateSpread, DealSnapshots.Complete(projectedCapRate: null)));
        Assert.Null(SubScore(DealScore.CapRateSpread, DealSnapshots.Complete(marketCapRateBenchmark: null)));
    }

    // ---- bands -----------------------------------------------------------------------

    [Theory]
    [InlineData(0.08, 0.0)]
    [InlineData(0.14, 50.0)]
    [InlineData(0.20, 100.0)]
    [InlineData(0.35, 100.0)]
    [InlineData(0.02, 0.0)]
    public void Target_irr_bands_between_eight_and_twenty_percent(double irr, double expected) =>
        Assert.Equal(expected, SubScore(DealScore.TargetIrr, DealSnapshots.Complete(targetIrr: irr))!.Value, 3);

    [Theory]
    [InlineData(1.20, 0.0)]
    [InlineData(1.85, 50.0)]
    [InlineData(2.50, 100.0)]
    public void Equity_multiple_bands_between_one_point_two_and_two_point_five(double em, double expected) =>
        Assert.Equal(expected, SubScore(DealScore.EquityMultiple, DealSnapshots.Complete(equityMultiple: em))!.Value, 3);

    [Theory]
    [InlineData(0.70, 0.0)]
    [InlineData(0.825, 50.0)]
    [InlineData(0.95, 100.0)]
    public void Occupancy_bands_between_seventy_and_ninety_five_percent(double occ, double expected) =>
        Assert.Equal(expected, SubScore(DealScore.Occupancy, DealSnapshots.Complete(occupancyRate: occ))!.Value, 3);

    /// <summary>
    /// Occupancy and IRR are fractions on the wire. A percent that leaks in must score as
    /// unknown, not clamp to 100 — clamping would read a 12% IRR as a stellar 120% one, which
    /// is the kind of wrong that looks right on a dashboard.
    /// </summary>
    [Theory]
    [InlineData(93.6)]
    [InlineData(12.0)]
    [InlineData(-0.2)]
    public void Values_outside_the_fraction_range_are_unknown_rather_than_clamped(double leaked)
    {
        Assert.Null(SubScore(DealScore.Occupancy, DealSnapshots.Complete(occupancyRate: leaked)));
        Assert.Null(SubScore(DealScore.TargetIrr, DealSnapshots.Complete(targetIrr: leaked)));
    }

    // ---- execution and momentum ------------------------------------------------------

    [Theory]
    [InlineData(10, 0, 0.0)]
    [InlineData(10, 5, 50.0)]
    [InlineData(10, 10, 100.0)]
    public void Execution_is_the_share_of_tasks_done(int total, int done, double expected) =>
        Assert.Equal(expected, SubScore(DealScore.Execution,
            DealSnapshots.Complete(taskCount: total, doneTaskCount: done))!.Value, 3);

    /// <summary>Zero of zero is not zero progress — a deal with no checklist yet should be
    /// scored on its financials, not marked as having completed nothing.</summary>
    [Fact]
    public void Execution_is_unknown_when_the_deal_has_no_tasks() =>
        Assert.Null(SubScore(DealScore.Execution, DealSnapshots.Complete(taskCount: 0, doneTaskCount: 0)));

    [Fact]
    public void Momentum_falls_as_a_deal_sits_in_its_stage()
    {
        var fresh = DealSnapshots.Complete(stageEnteredAt: AsOf.AddDays(-5).ToString("O"));
        var stalling = DealSnapshots.Complete(stageEnteredAt: AsOf.AddDays(-60).ToString("O"));

        Assert.True(SubScore(DealScore.Momentum, fresh) > SubScore(DealScore.Momentum, stalling));
    }

    /// <summary>Below the sample floor the deal's own average means nothing, so a flat 30-day
    /// reference is used instead. One fast transition must not redefine "slow".</summary>
    [Fact]
    public void Momentum_ignores_the_deals_own_average_until_there_are_enough_samples()
    {
        var enteredAt = AsOf.AddDays(-30).ToString("O");
        var thinHistory = DealSnapshots.Complete(
            stageEnteredAt: enteredAt, stageDwellAverageDays: 2, stageDwellSampleCount: 1);
        var noHistory = DealSnapshots.Complete(
            stageEnteredAt: enteredAt, stageDwellAverageDays: null, stageDwellSampleCount: 0);

        Assert.Equal(SubScore(DealScore.Momentum, noHistory), SubScore(DealScore.Momentum, thinHistory));
    }

    [Fact]
    public void Momentum_is_unknown_when_the_stage_timestamp_is_unparseable() =>
        Assert.Null(SubScore(DealScore.Momentum, DealSnapshots.Complete(stageEnteredAt: "not a date")));

    // ---- weighting -------------------------------------------------------------------

    /// <summary>
    /// Weights renormalize over the components that have data, so a partly-filled deal is
    /// scored on what it has. Verified by hand: IRR at 0.14 is 50 with weight 20, occupancy at
    /// 0.95 is 100 with weight 15, so the pair averages (20*50 + 15*100) / 35.
    /// </summary>
    [Fact]
    public void Weights_renormalize_over_the_components_that_have_data()
    {
        var partial = DealSnapshots.Complete(
            projectedCapRate: null,
            marketCapRateBenchmark: null,
            equityMultiple: null,
            targetIrr: 0.14,
            occupancyRate: 0.95,
            taskCount: 0,
            doneTaskCount: 0,
            stageEnteredAt: "not a date");

        var result = DealScore.Compute(partial, AsOf);

        Assert.True(result.HasScore);
        Assert.Equal(
            Math.Round((DealScore.IrrWeight * 50.0 + DealScore.OccupancyWeight * 100.0)
                       / (DealScore.IrrWeight + DealScore.OccupancyWeight), 1),
            result.Score, 3);
    }

    /// <summary>A number built from task counts and stage dwell alone is a progress bar, not a
    /// deal score, and publishing it would put a confident figure on a card that means
    /// nothing.</summary>
    [Fact]
    public void A_deal_with_no_financial_inputs_is_unscorable()
    {
        var result = DealScore.Compute(DealSnapshots.NonFinancialOnly(), AsOf);

        Assert.False(result.HasScore);
        Assert.Same(DealScoreResult.Unscorable, result);
    }

    /// <summary>Occupancy alone carries weight 15, exactly the floor, so it scores.</summary>
    [Fact]
    public void One_financial_input_at_the_weight_floor_is_enough()
    {
        var result = DealScore.Compute(
            DealSnapshots.NonFinancialOnly() with { OccupancyRate = 0.90 }, AsOf);

        Assert.True(result.HasScore);
    }

    [Fact]
    public void Financials_only_is_flagged_when_no_task_or_stage_data_contributes()
    {
        var noOperations = DealSnapshots.Complete(
            taskCount: 0, doneTaskCount: 0, stageEnteredAt: "not a date");

        Assert.True(DealScore.Compute(noOperations, AsOf).FinancialsOnly);
        Assert.False(DealScore.Compute(DealSnapshots.Complete(), AsOf).FinancialsOnly);
    }

    [Fact]
    public void Missing_components_are_reported_by_name()
    {
        var result = DealScore.Compute(DealSnapshots.Complete(equityMultiple: null), AsOf);

        Assert.Contains(DealScore.EquityMultiple, result.MissingNames);
        Assert.DoesNotContain(DealScore.EquityMultiple, result.Contributing.Select(c => c.Name));
    }

    // ---- determinism -----------------------------------------------------------------

    /// <summary>Same snapshot, same clock, same number — this is what makes replaying the
    /// topic free and what lets the gate compare a fresh score against a stored one.</summary>
    [Fact]
    public void Scoring_is_deterministic()
    {
        var snapshot = DealSnapshots.Complete();
        Assert.Equal(DealScore.Compute(snapshot, AsOf).Score, DealScore.Compute(snapshot, AsOf).Score);
    }

    [Fact]
    public void Score_is_rounded_to_one_decimal_place()
    {
        var score = DealScore.Compute(DealSnapshots.Complete(), AsOf).Score;
        Assert.Equal(score, Math.Round(score, 1));
    }

    [Fact]
    public void Score_stays_within_zero_and_one_hundred()
    {
        var best = DealSnapshots.Complete(
            projectedCapRate: 0.20, marketCapRateBenchmark: 0.05, targetIrr: 0.30,
            equityMultiple: 3.0, occupancyRate: 1.0, taskCount: 10, doneTaskCount: 10,
            stageEnteredAt: AsOf.ToString("O"));
        var worst = DealSnapshots.Complete(
            projectedCapRate: 0.01, marketCapRateBenchmark: 0.09, targetIrr: 0.01,
            equityMultiple: 1.0, occupancyRate: 0.10, taskCount: 10, doneTaskCount: 0,
            stageEnteredAt: AsOf.AddDays(-900).ToString("O"));

        Assert.Equal(100.0, DealScore.Compute(best, AsOf).Score, 3);
        Assert.Equal(0.0, DealScore.Compute(worst, AsOf).Score, 3);
    }

    /// <summary>
    /// A golden value over a fully-populated deal, written as a literal rather than recomputed
    /// from the weights — a test that recomputes the formula agrees with the formula whatever
    /// it says. Changing any weight or band shows up here as a visible diff instead of as a
    /// silent re-ranking of the whole pipeline.
    ///
    /// <para>Hand-computed: cap spread +50bps = 62.5 (w30), IRR 0.14 = 50 (w20), equity
    /// multiple 1.85 = 50 (w15), occupancy 0.88 = 72 (w15), execution 6/10 = 60 (w10),
    /// momentum at exactly the 40-day reference = 66.667 (w10). Total weight 100, so
    /// (1875 + 1000 + 750 + 1080 + 600 + 666.67) / 100 = 59.7.</para>
    /// </summary>
    [Fact]
    public void Golden_score_for_a_fully_populated_deal()
    {
        // Exactly at the dwell reference, so momentum sits mid-range rather than clamping and
        // the golden number stays sensitive to a change in any one weight.
        var snapshot = DealSnapshots.Complete(
            stageEnteredAt: AsOf.AddDays(-40).ToString("O"),
            stageDwellAverageDays: 40,
            stageDwellSampleCount: 5);

        Assert.Equal(59.7, DealScore.Compute(snapshot, AsOf).Score, 3);
    }
}
