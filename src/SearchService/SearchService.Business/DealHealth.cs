using SearchService.Business.DTOs;
using SearchService.Models;

namespace SearchService.Business;

/// <summary>
/// Evaluates the deterministic deal health flags (design doc §6.6) from an indexed document.
/// A port of DealsService.DataAccess.DealHealth — same thresholds, same message strings, same
/// order — kept identical on purpose so a deal renders the same whichever endpoint served it.
///
/// <para>The reason it is re-derived here rather than indexed: stale-stage and expiring-LOI
/// fire as days pass, with no state change to hang a Kafka event on. Indexing the answers
/// would freeze them at write time, so the document carries the inputs and this runs per
/// query, exactly as the repository runs it per read.</para>
///
/// <para>One divergence, and it is the reason this is a port rather than a copy: the dwell
/// baseline is a fleet-wide aggregate that moves whenever any deal transitions, but the
/// document carries whatever it was when the deal was last published. So `stale_stage` can
/// disagree with Postgres until that deal is republished. The other four flags read only
/// fields of the deal itself and are exact.</para>
/// </summary>
public static class DealHealth
{
    /// <summary>Below this occupancy the low-occupancy flag fires. Fraction, not percent
    /// (0.75 = 75%), matching the listings-service column. Design doc §6.6.</summary>
    public const double LowOccupancyThreshold = 0.75;

    /// <summary>An LOI outstanding longer than this many days is treated as expiring.</summary>
    public const int LoiExpiryDays = 45;

    /// <summary>A deal is stale once it has sat in its stage longer than this multiple of
    /// the historical average for that stage and property type.</summary>
    public const double StaleStageMultiplier = 1.5;

    /// <summary>Minimum completed transitions before an average is trusted. Below this the
    /// stale-stage flag stays off rather than firing off one or two data points.</summary>
    public const int StaleStageMinSamples = 3;

    public const string Warning = "warning";
    public const string Critical = "critical";

    // Stage literals rather than a reference to the deals service's DealStages — services
    // don't share code, and these are wire values that arrive over Kafka anyway.
    private const string NdaLoi = "NdaLoi";
    private const string Acquired = "Acquired";
    private const string Dead = "Dead";

    /// <summary>
    /// Flags for one deal. Terminal deals (Acquired, Dead) get none — they are done, so there
    /// is no health left to monitor.
    /// </summary>
    public static IReadOnlyList<HealthFlagDto> Evaluate(DealDocument deal, DateTime nowUtc)
    {
        if (deal.Stage is Acquired or Dead) return [];

        var flags = new List<HealthFlagDto>();
        var daysInStage = DaysSince(deal.StageEnteredAt, nowUtc);

        // Stale stage — longer here than peers of the same type historically took.
        if (daysInStage is int days &&
            deal.StageDwellSampleCount >= StaleStageMinSamples &&
            deal.StageDwellAverageDays is > 0 and double average)
        {
            var threshold = average * StaleStageMultiplier;
            if (days > threshold)
                flags.Add(new HealthFlagDto("stale_stage", Warning,
                    $"{days} days in this stage — {days / average:0.0}× the " +
                    $"{average:0.#}-day average for {deal.PropertyType ?? "similar"} deals here."));
        }

        if (HasOverdueTasks(deal, nowUtc))
            flags.Add(new HealthFlagDto("overdue_tasks", Warning,
                "One or more checklist tasks are past their due date."));

        // Expiring LOI — pure arithmetic on time in the NDA/LOI stage.
        if (deal.Stage == NdaLoi && daysInStage is int loiDays && loiDays > LoiExpiryDays)
            flags.Add(new HealthFlagDto("expiring_loi", Critical,
                $"LOI has been outstanding {loiDays} days, past the {LoiExpiryDays}-day window."));

        // Cap rate compression — the deal is priced below what the market pays.
        if (deal.ProjectedCapRate is double capRate &&
            deal.MarketCapRateBenchmark is double benchmark &&
            capRate < benchmark)
            flags.Add(new HealthFlagDto("cap_rate_compression", Critical,
                $"Projected cap rate {capRate * 100:0.00}% is below the " +
                $"{benchmark * 100:0.00}% market benchmark."));

        if (deal.OccupancyRate is double occupancy && occupancy < LowOccupancyThreshold)
            flags.Add(new HealthFlagDto("low_occupancy", Warning,
                $"Property occupancy {occupancy * 100:0}% is below the " +
                $"{LowOccupancyThreshold * 100:0}% threshold."));

        return flags;
    }

    /// <summary>Whether any open task is past due, from the earliest open due date the snapshot
    /// carried. Same "yyyy-MM-dd" ordinal comparison the repository's SQL predicate makes, and
    /// the same one the hasOverdueTasks query filter makes.</summary>
    public static bool HasOverdueTasks(DealDocument deal, DateTime nowUtc) =>
        deal.EarliestOpenTaskDueDate is string due &&
        string.CompareOrdinal(due, nowUtc.ToString("yyyy-MM-dd")) < 0;

    /// <summary>Whole days between an ISO-8601 round-trip timestamp and now, or null when
    /// the stored value doesn't parse.</summary>
    private static int? DaysSince(string isoTimestamp, DateTime nowUtc) =>
        DateTime.TryParse(isoTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var from)
            ? Math.Max(0, (int)(nowUtc - from.ToUniversalTime()).TotalDays)
            : null;
}
