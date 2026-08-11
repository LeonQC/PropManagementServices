namespace SearchService.Business.Events;

/// <summary>
/// Wire contract for property.snapshot, published by listings-service. Declared here rather
/// than shared, matching the house convention for inbound events — but note this one is a full
/// projection, so unlike the thin business events it must stay field-compatible with
/// ListingsService.Business.Events.PropertySnapshot. Serialized camelCase by the shared
/// publisher (JsonSerializerDefaults.Web).
/// </summary>
public record PropertySnapshot(
    string PropertyId,
    long Version,
    string Title,
    string? Slug,
    string PropertyType,
    string? PropertySubtype,
    string Status,
    double? TotalSqft,
    double? LeasableSqft,
    int? YearBuilt,
    double? LotSizeAcres,
    int? UnitCount,
    double? AskingPrice,
    double? CapRate,
    double? Noi,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    double? Year1NoiEstimate,
    string? DescriptionText,
    string? AiSummary,
    SnapshotAddress? Address,
    IReadOnlyList<SnapshotFeature>? Features,
    string? PrimaryImageUrl,
    string? ListedAt,
    string? UpdatedAt,
    bool Deleted);

public record SnapshotAddress(
    string? Street,
    string? City,
    string? State,
    string? Zip,
    string? MetroArea,
    double? Latitude,
    double? Longitude,
    string? Neighborhood);

public record SnapshotFeature(string? Category, string? Name, string? Value);

/// <summary>
/// Wire contract for deal.snapshot, published by deals-service. Same deal as PropertySnapshot
/// above: declared here per the house convention for inbound events, but a full projection
/// rather than a thin business event, so it must stay field-compatible with
/// DealsService.Business.Events.DealSnapshot.
///
/// <para>EarliestOpenTaskDueDate, StageDwellAverageDays and StageDwellSampleCount are raw
/// inputs, not answers: hasOverdueTasks and the stale-stage flag both move with the clock, so
/// this service re-derives them per query instead of indexing a value that would freeze.</para>
/// </summary>
public record DealSnapshot(
    string DealId,
    long Version,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    string Stage,
    string Priority,
    string OwnerId,
    string? DeadReason,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate,
    double? AiScore,
    string? AiScoreRationale,
    string? RiskFlags,
    string StageEnteredAt,
    string CreatedAt,
    string? UpdatedAt,
    int TaskCount,
    int DoneTaskCount,
    string? EarliestOpenTaskDueDate,
    double? StageDwellAverageDays,
    int StageDwellSampleCount,
    string? CommentText,
    string? DocumentText,
    bool Deleted);
