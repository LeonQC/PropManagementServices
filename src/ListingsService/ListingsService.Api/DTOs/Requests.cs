namespace ListingsService.Api.DTOs;

public record CreatePropertyRequest(
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
    CreateAddressRequest Address);

public record CreateAddressRequest(
    string Street,
    string City,
    string State,
    string Zip,
    string? MetroArea,
    double? Latitude,
    double? Longitude,
    string? Neighborhood);

public record UpdatePropertyRequest(
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

public record AddMediaRequest(
    string MediaType,
    string Url,
    string? Caption,
    int DisplayOrder = 0,
    bool IsPrimary = false);

public record AddFeatureRequest(
    string FeatureCategory,
    string FeatureName,
    string FeatureValue);
