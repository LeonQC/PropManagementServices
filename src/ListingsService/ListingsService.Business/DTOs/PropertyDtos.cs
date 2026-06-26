namespace ListingsService.Business.DTOs;

// ----- Outgoing (Business → Presentation) -----

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

public record MediaDto(
    string Id,
    string? MediaType,
    string? Url,
    string? Caption,
    int DisplayOrder,
    bool IsPrimary);

public record FeatureDto(
    string Id,
    string? FeatureCategory,
    string? FeatureName,
    string? FeatureValue);

// ----- Incoming (Presentation → Business) -----

public record CreatePropertyDto(
    string Title,
    string PropertyType,
    string? PropertySubtype,
    string? Status,
    double? TotalSqft,
    double? LeasableSqft,
    int? YearBuilt,
    double? LotSizeAcres,
    int? UnitCount,
    double? AskingPrice,
    double? CapRate,
    double? Noi,
    double? OccupancyRate,
    string? DescriptionText,
    CreateAddressDto Address);

public record CreateAddressDto(
    string Street,
    string City,
    string State,
    string Zip,
    string? MetroArea,
    double? Latitude,
    double? Longitude,
    string? Neighborhood);

public record UpdatePropertyDto(
    string? Title,
    string? PropertyType,
    string? PropertySubtype,
    string? Status,
    double? TotalSqft,
    double? LeasableSqft,
    int? YearBuilt,
    double? LotSizeAcres,
    int? UnitCount,
    double? AskingPrice,
    double? CapRate,
    double? Noi,
    double? OccupancyRate,
    string? DescriptionText);

public record CreateMediaDto(
    string MediaType,
    string Url,
    string? Caption,
    int DisplayOrder = 0,
    bool IsPrimary = false);

public record CreateFeatureDto(
    string FeatureCategory,
    string FeatureName,
    string FeatureValue);
