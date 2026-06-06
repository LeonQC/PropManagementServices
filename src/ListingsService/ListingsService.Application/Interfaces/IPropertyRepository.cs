using ListingsService.Domain.Entities;

namespace ListingsService.Application.Interfaces;

public interface IPropertyRepository
{
    Task<Property?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<(List<Property> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize,
        string? propertyType = null,
        string? status = null,
        string? metroArea = null,
        double? minPrice = null,
        double? maxPrice = null,
        CancellationToken ct = default);
    Task<Property> CreateAsync(Property property, CancellationToken ct = default);
    Task UpdateAsync(Property property, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    // Media
    Task<List<PropertyMedia>> GetMediaAsync(string propertyId, CancellationToken ct = default);
    Task<PropertyMedia> AddMediaAsync(PropertyMedia media, CancellationToken ct = default);

    // Features
    Task<List<PropertyFeature>> GetFeaturesAsync(string propertyId, CancellationToken ct = default);
    Task<PropertyFeature> AddFeatureAsync(PropertyFeature feature, CancellationToken ct = default);

    // Map
    Task<List<Property>> GetMapPointsAsync(CancellationToken ct = default);
}
