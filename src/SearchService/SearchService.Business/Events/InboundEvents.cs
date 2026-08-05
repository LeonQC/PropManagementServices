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
