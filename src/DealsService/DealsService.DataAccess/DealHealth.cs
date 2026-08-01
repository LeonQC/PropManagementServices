using DealsService.Models;

namespace DealsService.DataAccess;

/// <summary>
/// Evaluates the deterministic deal health flags (design doc §6.6). Deliberately
/// computed on read rather than persisted: stale-stage and expiring-LOI fire as days
/// pass, with no state change to hang a Kafka event on, so an event-driven design
/// cannot express them and a scheduler would only add a moving part. Everything here
/// is pure arithmetic over the deal row plus one pre-fetched aggregate, so it costs
/// nothing to re-derive per request.
///
/// It lives beside the repository because that is where <see cref="DealWithTaskStats"/>
/// is built and where HasOverdueTasks is already computed. The LLM-derived judgment
/// flags are a later phase and land in Deal.RiskFlags instead — not here.
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

    // Stage literals rather than a reference to Business.Domain.DealStages: DataAccess
    // sits below Business and cannot see it. Same trade-off the property_id partial
    // index and HasActiveDealForPropertyAsync already make.
    private const string NdaLoi = "NdaLoi";
    private const string Acquired = "Acquired";
    private const string Dead = "Dead";

    /// <summary>
    /// Flags for one deal. <paramref name="dwellAverages"/> is the whole per-stage /
    /// per-type table, fetched once per request by the repository and shared across the
    /// page. Terminal deals (Acquired, Dead) get no flags — they are done, so there is
    /// no health left to monitor.
    /// </summary>
    public static IReadOnlyList<HealthFlag> Evaluate(
        Deal deal, bool hasOverdueTasks, IReadOnlyList<StageDwellAverage> dwellAverages, DateTime nowUtc)
    {
        if (deal.Stage is Acquired or Dead) return [];

        var flags = new List<HealthFlag>();
        var daysInStage = DaysSince(deal.StageEnteredAt, nowUtc);

        // Stale stage — longer here than peers of the same type historically took.
        if (daysInStage is int days)
        {
            var average = dwellAverages.FirstOrDefault(a =>
                a.Stage == deal.Stage && a.PropertyType == deal.PropertyType);

            if (average is { SampleCount: >= StaleStageMinSamples } && average.AverageDays > 0)
            {
                var threshold = average.AverageDays * StaleStageMultiplier;
                if (days > threshold)
                    flags.Add(new HealthFlag("stale_stage", Warning,
                        $"{days} days in this stage — {days / average.AverageDays:0.0}× the " +
                        $"{average.AverageDays:0.#}-day average for {deal.PropertyType ?? "similar"} deals here."));
            }
        }

        if (hasOverdueTasks)
            flags.Add(new HealthFlag("overdue_tasks", Warning,
                "One or more checklist tasks are past their due date."));

        // Expiring LOI — pure arithmetic on time in the NDA/LOI stage.
        if (deal.Stage == NdaLoi && daysInStage is int loiDays && loiDays > LoiExpiryDays)
            flags.Add(new HealthFlag("expiring_loi", Critical,
                $"LOI has been outstanding {loiDays} days, past the {LoiExpiryDays}-day window."));

        // Cap rate compression — the deal is priced below what the market pays.
        if (deal.ProjectedCapRate is double capRate &&
            deal.MarketCapRateBenchmark is double benchmark &&
            capRate < benchmark)
            flags.Add(new HealthFlag("cap_rate_compression", Critical,
                $"Projected cap rate {capRate * 100:0.00}% is below the " +
                $"{benchmark * 100:0.00}% market benchmark."));

        if (deal.OccupancyRate is double occupancy && occupancy < LowOccupancyThreshold)
            flags.Add(new HealthFlag("low_occupancy", Warning,
                $"Property occupancy {occupancy * 100:0}% is below the " +
                $"{LowOccupancyThreshold * 100:0}% threshold."));

        return flags;
    }

    /// <summary>Whole days between an ISO-8601 round-trip timestamp and now, or null when
    /// the stored value doesn't parse.</summary>
    private static int? DaysSince(string isoTimestamp, DateTime nowUtc) =>
        DateTime.TryParse(isoTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var from)
            ? Math.Max(0, (int)(nowUtc - from.ToUniversalTime()).TotalDays)
            : null;
}
