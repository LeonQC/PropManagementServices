using DealsService.Models;

namespace DealsService.Tests;

/// <summary>
/// Builds <see cref="Deal"/> values for the scoring tests. The entity has more required
/// members than any one test cares about, so constructing them inline would bury the single
/// field a test is actually about.
/// </summary>
public static class Deals
{
    /// <summary>The clock every scoring test runs against, so stage momentum is a fixed
    /// input rather than a function of when the suite happens to run.</summary>
    public static readonly DateTime AsOf = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A scorable deal with every component populated. Values sit off the band edges on
    /// purpose, so a test that moves one field changes the score instead of tripping a clamp.
    /// </summary>
    public static Deal Complete(
        double? projectedCapRate = 0.070,
        double? marketCapRateBenchmark = 0.065,
        double? targetIrr = 0.14,
        double? equityMultiple = 1.85,
        double? occupancyRate = 0.88,
        string stage = "UnderwritingReview",
        DateTime? stageEnteredAt = null,
        string? propertyType = "Industrial") => new()
        {
            Id = "deal-1",
            Name = "Metro Pkwy Warehouse Acquisition",
            PropertyId = "prop-1",
            PropertyName = "Metro Pkwy Warehouse Complex",
            PropertyType = propertyType,
            MetroArea = "New York Metro",
            OccupancyRate = occupancyRate,
            MarketCapRateBenchmark = marketCapRateBenchmark,
            Stage = stage,
            Priority = "High",
            OwnerId = "user-1",
            OfferPrice = 15_800_000,
            ProjectedCapRate = projectedCapRate,
            TargetIrr = targetIrr,
            EquityMultiple = equityMultiple,
            ProjectedCloseDate = "2026-12-15",
            StageEnteredAt = (stageEnteredAt ?? AsOf.AddDays(-12)).ToString("O"),
            CreatedAt = AsOf.AddDays(-104).ToString("O"),
        };

    /// <summary>A deal with no financial inputs at all — only task and stage data.</summary>
    public static Deal NoFinancials() => Complete(
        projectedCapRate: null,
        marketCapRateBenchmark: null,
        targetIrr: null,
        equityMultiple: null,
        occupancyRate: null);

    /// <summary>A dwell baseline for this deal's (stage, property type), with enough samples
    /// to be trusted. Below DealHealth.StaleStageMinSamples the formula ignores it.</summary>
    public static IReadOnlyList<DataAccess.StageDwellAverage> Dwell(
        double averageDays = 40, int sampleCount = 5, string stage = "UnderwritingReview",
        string? propertyType = "Industrial") =>
        [new(stage, propertyType, averageDays, sampleCount)];
}
