namespace SearchService.Business.DTOs;

// Business-layer view of a search result. Field-for-field the same information the listings
// service returns for a property, so the two grid endpoints stay interchangeable.

public record PropertyDto(
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
    AddressDto? Address,
    string? ListedAt,
    string? UpdatedAt);

public record AddressDto(
    string? Street,
    string? City,
    string? State,
    string? Zip,
    string? MetroArea,
    double? Latitude,
    double? Longitude,
    string? Neighborhood);
