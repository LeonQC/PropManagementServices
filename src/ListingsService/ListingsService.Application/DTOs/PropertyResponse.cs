namespace ListingsService.Application.DTOs;

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

public record MediaResponse(
    string Id,
    string? MediaType,
    string? Url,
    string? Caption,
    int DisplayOrder,
    bool IsPrimary);

public record FeatureResponse(
    string Id,
    string? FeatureCategory,
    string? FeatureName,
    string? FeatureValue);

public record GeoJsonResponse(
    string Type,
    List<GeoJsonFeature> Features);

public record GeoJsonFeature(
    string Type,
    GeoJsonGeometry Geometry,
    GeoJsonProperties Properties);

public record GeoJsonGeometry(
    string Type,
    double[] Coordinates);

public record GeoJsonProperties(
    string Id,
    string Title,
    string PropertyType,
    string Status,
    double? AskingPrice,
    double? CapRate);

public record PaginatedResponse<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
