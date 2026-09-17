using System.Globalization;
using AiService.Business.Events;

namespace AiService.Business.Workers;

/// <summary>One input to the score. <paramref name="Value"/> is null when the deal has no data
/// for it, which excludes it from the weighting rather than scoring it as zero.</summary>
public readonly record struct ScoreComponent(string Name, double Weight, double? Value);

/// <summary>The outcome of scoring one deal.</summary>
public sealed record DealScoreResult(
    bool HasScore,
    double Score,
    bool FinancialsOnly,
    IReadOnlyList<ScoreComponent> Components)
{
    /// <summary>Too little data to say anything. The worker skips these rather than
    /// publishing a number built from nothing.</summary>
    public static readonly DealScoreResult Unscorable = new(false, 0, false, []);

    public IEnumerable<ScoreComponent> Contributing => Components.Where(c => c.Value is not null);

    public IEnumerable<string> MissingNames => Components.Where(c => c.Value is null).Select(c => c.Name);
}

/// <summary>
/// The 0–100 deal score (design doc §6.3). Deterministic, pure, and free to recompute: the
/// model writes the rationale prose but never the number, so the score is reproducible,
/// unit-testable, and identical on every replay of the same snapshot.
///
/// <para>Weights renormalize over the components that actually have data. A null is "unknown",
/// not "bad" — a deal with three of six inputs scores on what it has instead of being punished
/// for what nobody has filled in yet. The floor on financial weight is what stops that
/// generosity from producing a "deal score" made entirely of task counts.</para>
/// </summary>
public static class DealScore
{
    public const double CapRateSpreadWeight = 30;
    public const double IrrWeight = 20;
    public const double EquityMultipleWeight = 15;
    public const double OccupancyWeight = 15;
    public const double ExecutionWeight = 10;
    public const double MomentumWeight = 10;

    public const string CapRateSpread = "cap_rate_spread";
    public const string TargetIrr = "target_irr";
    public const string EquityMultiple = "equity_multiple";
    public const string Occupancy = "occupancy";
    public const string Execution = "execution";
    public const string Momentum = "momentum";

    /// <summary>At least one real financial input must be present. Below this the number
    /// would be a progress bar wearing a deal score's clothes.</summary>
    private const double MinFinancialWeight = 15;

    /// <summary>Values above this are a percent that leaked into a fraction field. Treated as
    /// unknown rather than clamped — clamping would read a 12% IRR as a 120% one.</summary>
    private const double FractionCeiling = 1.5;

    public static DealScoreResult Compute(DealSnapshot d, DateTimeOffset asOf)
    {
        ScoreComponent[] components =
        [
            new(CapRateSpread, CapRateSpreadWeight, SpreadVsBenchmark(d.ProjectedCapRate, d.MarketCapRateBenchmark)),
            new(TargetIrr, IrrWeight, Band(AsFraction(d.TargetIrr), 0.08, 0.20)),
            new(EquityMultiple, EquityMultipleWeight, Band(d.EquityMultiple, 1.20, 2.50)),
            new(Occupancy, OccupancyWeight, Band(AsFraction(d.OccupancyRate), 0.70, 0.95)),
            new(Execution, ExecutionWeight, TaskCompletion(d.TaskCount, d.DoneTaskCount)),
            new(Momentum, MomentumWeight,
                StageMomentum(d.StageEnteredAt, d.StageDwellAverageDays, d.StageDwellSampleCount, asOf)),
        ];

        var scored = components.Where(c => c.Value is not null).ToArray();
        var totalWeight = scored.Sum(c => c.Weight);

        var financialWeight = scored
            .Where(c => c.Name is CapRateSpread or TargetIrr or EquityMultiple or Occupancy)
            .Sum(c => c.Weight);

        if (totalWeight <= 0 || financialWeight < MinFinancialWeight) return DealScoreResult.Unscorable;

        var score = scored.Sum(c => c.Weight * c.Value!.Value) / totalWeight;
        var financialsOnly = !scored.Any(c => c.Name is Execution or Momentum);

        // Rounded to one place so float noise never reaches the wire and two runs over the
        // same snapshot compare equal.
        return new DealScoreResult(true, Math.Round(score, 1), financialsOnly, components);
    }

    /// <summary>
    /// Projected cap rate against the market benchmark. Centred at 50 because matching the
    /// benchmark is neither good nor bad, and asymmetric on purpose: 200bps of upside is a
    /// thesis, 100bps of downside is a problem, so the downside slope is twice as steep.
    /// </summary>
    private static double? SpreadVsBenchmark(double? projected, double? benchmark)
    {
        if (projected is null || benchmark is null) return null;

        var spread = projected.Value - benchmark.Value;
        return spread >= 0
            ? Clamp(50.0 + spread / 0.0200 * 50.0)
            : Clamp(50.0 + spread / 0.0100 * 50.0);
    }

    /// <summary>Share of the deal's checklist that is done. Null when no tasks exist, because
    /// zero of zero is not zero progress.</summary>
    private static double? TaskCompletion(int taskCount, int doneTaskCount) =>
        taskCount <= 0 ? null : Clamp((double)doneTaskCount / taskCount * 100.0);

    /// <summary>
    /// How long the deal has sat in its current stage, measured against the pace it has
    /// actually kept. Falls back to a flat 30-day reference until there is enough history for
    /// the deal's own average to mean anything, and floors that average at 7 days so one fast
    /// transition cannot make every later day look like a stall.
    ///
    /// <para>100 at half the reference or faster, 0 at twice it or slower.</para>
    /// </summary>
    private static double? StageMomentum(
        string stageEnteredAt, double? avgDwellDays, int dwellSamples, DateTimeOffset asOf)
    {
        if (!DateTimeOffset.TryParse(stageEnteredAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var entered))
            return null;

        var daysInStage = Math.Max((asOf - entered).TotalDays, 0);
        var reference = dwellSamples >= 3 && avgDwellDays is > 0
            ? Math.Max(avgDwellDays.Value, 7.0)
            : 30.0;

        return Clamp((2.0 - daysInStage / reference) / 1.5 * 100.0);
    }

    /// <summary>Linear 0–100 between two reference points, clamped at both ends.</summary>
    private static double? Band(double? value, double low, double high) =>
        value is null ? null : Clamp((value.Value - low) / (high - low) * 100.0);

    /// <summary>Occupancy and IRR are fractions on the wire — see DealSnapshot. Anything
    /// outside [0, 1.5] is a unit error, and guessing at it would be worse than not scoring
    /// that component.</summary>
    private static double? AsFraction(double? v) => v is null or > FractionCeiling or < 0 ? null : v;

    private static double Clamp(double v) => Math.Clamp(v, 0.0, 100.0);
}
