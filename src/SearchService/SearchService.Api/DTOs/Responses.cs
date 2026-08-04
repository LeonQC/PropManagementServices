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
