namespace SearchService.Api.DTOs;

// Wire contract for the listings grid. Field-for-field identical to
// ListingsService.Api.DTOs.PropertyResponse / PaginatedResponse so the frontend can switch
// between /search/v1/properties and /listings/v1/properties by changing only the base path —
// same query params in, same JSON out. Keep the two in step.

public record PropertyResponse(
    string Id,
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
    AddressResponse? Address,
    string? ListedAt,
    string? UpdatedAt);

public record AddressResponse(
    string? Street,
    string? City,
    string? State,
    string? Zip,
    string? MetroArea,
    double? Latitude,
    double? Longitude,
    string? Neighborhood);

public record PaginatedResponse<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

// Wire contract for the deals list. Same deal as PropertyResponse above, against
// DealsService.Api.DTOs this time — except the deals endpoints also wrap this in the
// {data, meta} envelope, because deals-service does.

/// <summary>A deterministic health signal on a deal (design doc §6.6). Type is one of
/// stale_stage, overdue_tasks, expiring_loi, cap_rate_compression, low_occupancy;
/// severity is "warning" or "critical". Computed per request, never persisted.</summary>
public record HealthFlagResponse(string Type, string Severity, string Message);

public record DealResponse(
    string Id,
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
    bool HasOverdueTasks,
    IReadOnlyList<HealthFlagResponse> HealthFlags);

/// <summary>One cross-entity result from GET /search/v1/all. EntityType is "property" or
/// "deal"; follow it plus Id to that entity's own endpoint for the full record.</summary>
public record SearchHitResponse(
    string EntityType,
    string Id,
    string Title,
    string? Snippet,
    double Score);
