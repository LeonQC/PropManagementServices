using AiService.Business.Events;

namespace AiService.Tests;

/// <summary>
/// Builds <see cref="DealSnapshot"/> values for the scoring tests. The record has 32
/// positional parameters and every test cares about three or four of them, so constructing
/// them inline would bury the one field a test is actually about.
/// </summary>
public static class DealSnapshots
{
    /// <summary>
    /// A scorable deal with every component populated. Numbers chosen to sit off the band
    /// edges, so a test that changes one field moves the score without tripping a clamp.
    /// </summary>
    public static DealSnapshot Complete(
        string dealId = "deal-1",
        double? projectedCapRate = 0.070,
        double? marketCapRateBenchmark = 0.065,
        double? targetIrr = 0.14,
        double? equityMultiple = 1.85,
        double? occupancyRate = 0.88,
        int taskCount = 10,
        int doneTaskCount = 6,
        string stageEnteredAt = "2026-09-01T00:00:00Z",
        double? stageDwellAverageDays = 40,
        int stageDwellSampleCount = 5,
        string stage = "Underwriting Review",
        double? aiScore = null,
        string? aiScoreRationale = null,
        bool deleted = false) =>
        new(
            DealId: dealId,
            Version: 7,
            Name: "Metro Pkwy Warehouse Acquisition",
            PropertyId: "prop-1",
            PropertyName: "Metro Pkwy Warehouse Complex",
            PropertyType: "Industrial",
            MetroArea: "New York Metro",
            OccupancyRate: occupancyRate,
            MarketCapRateBenchmark: marketCapRateBenchmark,
            Stage: stage,
            Priority: "High",
            OwnerId: "user-1",
            DeadReason: null,
            OfferPrice: 15_800_000,
            ProjectedCapRate: projectedCapRate,
            TargetIrr: targetIrr,
            EquityMultiple: equityMultiple,
            ProjectedCloseDate: "2026-12-15",
            AiScore: aiScore,
            AiScoreRationale: aiScoreRationale,
            RiskFlags: null,
            StageEnteredAt: stageEnteredAt,
            CreatedAt: "2026-06-01T00:00:00Z",
            UpdatedAt: "2026-09-10T00:00:00Z",
            TaskCount: taskCount,
            DoneTaskCount: doneTaskCount,
            EarliestOpenTaskDueDate: "2026-10-01",
            StageDwellAverageDays: stageDwellAverageDays,
            StageDwellSampleCount: stageDwellSampleCount,
            CommentText: "team notes",
            DocumentText: "phase i esa summary",
            Deleted: deleted);

    /// <summary>A deal with no financial inputs at all — only task and stage data.</summary>
    public static DealSnapshot NonFinancialOnly() => Complete(
        projectedCapRate: null,
        marketCapRateBenchmark: null,
        targetIrr: null,
        equityMultiple: null,
        occupancyRate: null);
}
