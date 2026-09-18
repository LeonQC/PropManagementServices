using System.Globalization;
using DealsService.Models;

namespace DealsService.DataAccess;

/// <summary>One input to the score. <see cref="Value"/> is null when the deal has no data for
/// it, which drops it from the weighting rather than scoring it as zero.</summary>
public record ScoreComponent(string Name, double Weight, double? Value);

/// <summary>The outcome of scoring one deal. <see cref="HasScore"/> is false when there is too
/// little financial data for a number to mean anything.</summary>
public record DealScoreResult(
    bool HasScore,
    double Score,
    bool FinancialsOnly,
    IReadOnlyList<ScoreComponent> Components)
{
    public static readonly DealScoreResult Unscorable = new(false, 0, false, []);

    public IEnumerable<ScoreComponent> Contributing => Components.Where(c => c.Value is not null);

    public IEnumerable<string> MissingNames => Components.Where(c => c.Value is null).Select(c => c.Name);
}

/// <summary>
/// The 0–100 deal score (design doc §6.3). A deterministic formula over the deal's own metrics,
/// so it is reproducible, unit-testable and free to recompute. No model is involved: §6.3 uses
/// Claude only to write the rationale prose, never to produce the number.
///
/// <para>Computed on read, for the same reason as <see cref="DealHealth"/> and stated in its
/// doc: the stage-momentum component moves as days pass, with no state change to hang a Kafka
/// event on. Persisting the number would freeze momentum at whatever it was when the deal was
/// last written, so a deal stalling for six months would keep scoring as though it were fresh.
/// That is precisely the failure an event-driven design cannot express, so this is derived per
/// request from the deal row plus the same pre-fetched aggregate the health flags already use.</para>
///
/// <para>Lives beside the repository for the same reason DealHealth does: this is where
/// <see cref="DealWithTaskStats"/> is assembled and where the task rollups already exist.</para>
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

    /// <summary>At least one real financial input must be present. Below this the number would
    /// be a progress bar wearing a deal score's clothes.</summary>
    public const double MinFinancialWeight = 15;

    /// <summary>Values above this are a percent that leaked into a fraction field. Treated as
    /// unknown rather than clamped: clamping would read a 12% IRR as a 120% one.</summary>
    private const double FractionCeiling = 1.5;

    /// <summary>Reference dwell used until a deal's own stage history has enough samples to
    /// mean anything.</summary>
    private const double DefaultDwellReferenceDays = 30.0;

    /// <summary>Floor on the dwell reference, so one fast transition cannot make every later
    /// day look like a stall.</summary>
    private const double MinDwellReferenceDays = 7.0;

    // Stage literals rather than a reference to Business.Domain.DealStages: DataAccess sits
    // below Business and cannot see it. Same trade-off DealHealth makes.
    private const string Acquired = "Acquired";
    private const string Dead = "Dead";

    /// <summary>
    /// Scores one deal. <paramref name="dwellAverages"/> is the whole per-stage, per-type table,
    /// fetched once per request by the repository and shared across the page — the same instance
    /// the health flags are evaluated against.
    ///
    /// <para>Terminal deals score nothing. An acquired or dead deal is done, so a number
    /// projecting how well it is going would be noise on a card nobody acts on.</para>
    /// </summary>
    public static DealScoreResult Compute(
        Deal deal,
        int taskCount,
        int doneTaskCount,
        IReadOnlyList<StageDwellAverage> dwellAverages,
        DateTime nowUtc)
    {
        var average = dwellAverages.FirstOrDefault(a =>
            a.Stage == deal.Stage && a.PropertyType == deal.PropertyType);

        return Compute(deal, taskCount, doneTaskCount, average?.AverageDays, average?.SampleCount ?? 0, nowUtc);
    }

    /// <summary>
    /// The same score from an already-matched dwell baseline, for callers that hold one value
    /// rather than the whole table — the snapshot publisher, which carries exactly these two
    /// figures per deal so the search index can re-derive time-dependent values per query.
    /// </summary>
    public static DealScoreResult Compute(
        Deal deal,
        int taskCount,
        int doneTaskCount,
        double? dwellAverageDays,
        int dwellSampleCount,
        DateTime nowUtc)
    {
        if (deal.Stage is Acquired or Dead) return DealScoreResult.Unscorable;

        ScoreComponent[] components =
        [
            new(CapRateSpread, CapRateSpreadWeight,
                SpreadVsBenchmark(deal.ProjectedCapRate, deal.MarketCapRateBenchmark)),
            new(TargetIrr, IrrWeight, Band(AsFraction(deal.TargetIrr), 0.08, 0.20)),
            new(EquityMultiple, EquityMultipleWeight, Band(deal.EquityMultiple, 1.20, 2.50)),
            new(Occupancy, OccupancyWeight, Band(AsFraction(deal.OccupancyRate), 0.70, 0.95)),
            new(Execution, ExecutionWeight, TaskCompletion(taskCount, doneTaskCount)),
            new(Momentum, MomentumWeight,
                StageMomentum(deal.StageEnteredAt, dwellAverageDays, dwellSampleCount, nowUtc)),
        ];

        var scored = components.Where(c => c.Value is not null).ToArray();
        var totalWeight = scored.Sum(c => c.Weight);

        var financialWeight = scored
            .Where(c => c.Name is CapRateSpread or TargetIrr or EquityMultiple or Occupancy)
            .Sum(c => c.Weight);

        if (totalWeight <= 0 || financialWeight < MinFinancialWeight) return DealScoreResult.Unscorable;

        var score = scored.Sum(c => c.Weight * c.Value!.Value) / totalWeight;
        var financialsOnly = !scored.Any(c => c.Name is Execution or Momentum);

        // Rounded to one place so two reads of an unchanged deal compare equal and the number
        // on the wire carries no float noise.
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
    /// How long the deal has sat in its current stage, against the pace its peers actually kept.
    /// Uses the same (stage, property type) baseline as the stale-stage health flag, so the two
    /// never disagree about what "slow" means for this deal.
    ///
    /// <para>100 at half the reference or faster, 0 at twice it or slower. This is the component
    /// that makes the score time-dependent, and the reason it is computed on read.</para>
    /// </summary>
    private static double? StageMomentum(
        string stageEnteredAt, double? dwellAverageDays, int dwellSampleCount, DateTime nowUtc)
    {
        if (DaysSince(stageEnteredAt, nowUtc) is not int daysInStage) return null;

        var reference = dwellSampleCount >= DealHealth.StaleStageMinSamples && dwellAverageDays is > 0
            ? Math.Max(dwellAverageDays.Value, MinDwellReferenceDays)
            : DefaultDwellReferenceDays;

        return Clamp((2.0 - daysInStage / reference) / 1.5 * 100.0);
    }

    /// <summary>Linear 0–100 between two reference points, clamped at both ends.</summary>
    private static double? Band(double? value, double low, double high) =>
        value is null ? null : Clamp((value.Value - low) / (high - low) * 100.0);

    /// <summary>Occupancy and IRR are fractions on the deal row (0.936 is 93.6%). Anything
    /// outside [0, 1.5] is a unit error, and guessing at it would be worse than leaving that
    /// component unscored.</summary>
    private static double? AsFraction(double? v) => v is null or > FractionCeiling or < 0 ? null : v;

    private static double Clamp(double v) => Math.Clamp(v, 0.0, 100.0);

    /// <summary>Whole days between an ISO-8601 round-trip timestamp and now, or null when the
    /// stored value doesn't parse. Mirrors DealHealth.DaysSince.</summary>
    private static int? DaysSince(string isoTimestamp, DateTime nowUtc) =>
        DateTime.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            ? Math.Max(0, (int)(nowUtc - from.ToUniversalTime()).TotalDays)
            : null;
}
