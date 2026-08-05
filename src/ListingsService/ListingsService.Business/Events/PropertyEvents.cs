namespace ListingsService.Business.Events;

// Outbound event payloads published by listings-service. These are the wire
// contract other services consume — owned here, kept JSON-serializable.

public record PropertyCreated(
    string PropertyId,
    string Type,
    string? Subtype,
    string? MetroArea,
    double? AskingPrice,
    double? CapRate);

public record PropertyUpdated(
    string PropertyId,
    string[] ChangedFields,
    Dictionary<string, object?> OldValues,
    Dictionary<string, object?> NewValues);

public record PropertyStatusChanged(
    string PropertyId,
    string OldStatus,
    string NewStatus,
    string? DealId);

/// <summary>
/// Full searchable projection of a property (Topics.PropertySnapshot) — event-carried state
/// transfer, published on every mutation regardless of which fields moved. This is the search
/// index's only input, which is why it must not share the price/cap-rate gate that
/// PropertyUpdated deliberately applies.
///
/// <para><b>Version</b> is a monotonic counter used as OpenSearch's external version, so
/// replayed or out-of-order deliveries can be rejected rather than overwriting newer state.
/// UpdatedAt is unusable for this — it's a 1-second-resolution string.</para>
///
/// <para><b>Deleted</b> marks a soft delete (status = off_market) rather than using a null
/// Kafka tombstone: the shared KafkaConsumerService skips null message values, so a tombstone
/// would be silently ignored.</para>
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
    IReadOnlyList<SnapshotFeature> Features,
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

public record SnapshotFeature(
    string? Category,
    string? Name,
    string? Value);
