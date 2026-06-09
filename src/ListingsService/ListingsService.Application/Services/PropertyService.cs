using ListingsService.Application.DTOs;
using ListingsService.Application.Interfaces;
using ListingsService.Domain.Entities;

namespace ListingsService.Application.Services;

public class PropertyService(IPropertyRepository propertyRepository)
{
    public async Task<PaginatedResponse<PropertyResponse>> ListAsync(
        int page, int pageSize,
        string? propertyType, string? status, string? metroArea,
        double? minPrice, double? maxPrice,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await propertyRepository.GetAllAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, ct);

        return new PaginatedResponse<PropertyResponse>(
            items.Select(MapToResponse).ToList(),
            totalCount, page, pageSize);
    }

    public async Task<GeoJsonResponse> GetMapAsync(CancellationToken ct = default)
    {
        var properties = await propertyRepository.GetMapPointsAsync(ct);

        var features = properties
            .Where(p => p.Address?.Latitude != null && p.Address?.Longitude != null)
            .Select(p => new GeoJsonFeature(
                Type: "Feature",
                Geometry: new GeoJsonGeometry(
                    Type: "Point",
                    Coordinates: [p.Address!.Longitude!.Value, p.Address!.Latitude!.Value]),
                Properties: new GeoJsonProperties(
                    p.Id, p.Title, p.PropertyType, p.Status,
                    p.AskingPrice, p.CapRate)
            )).ToList();

        return new GeoJsonResponse("FeatureCollection", features);
    }

    public async Task<PropertyResponse?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        return property is null ? null : MapToResponse(property);
    }

    public async Task<PropertyResponse> CreateAsync(CreatePropertyRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var property = new Property
        {
            Id = "",
            Title = request.Title,
            Slug = GenerateSlug(request.Title),
            PropertyType = request.PropertyType,
            PropertySubtype = request.PropertySubtype,
            Status = request.Status ?? "listed",
            TotalSqft = request.TotalSqft,
            LeasableSqft = request.LeasableSqft,
            YearBuilt = request.YearBuilt,
            LotSizeAcres = request.LotSizeAcres,
            UnitCount = request.UnitCount,
            AskingPrice = request.AskingPrice,
            CapRate = request.CapRate,
            Noi = request.Noi,
            OccupancyRate = request.OccupancyRate,
            DescriptionText = request.DescriptionText,
            ListedAt = now,
            UpdatedAt = now,
            Address = new Address
            {
                Id = "",
                PropertyId = "",
                Street = request.Address.Street,
                City = request.Address.City,
                State = request.Address.State,
                Zip = request.Address.Zip,
                MetroArea = request.Address.MetroArea,
                Latitude = request.Address.Latitude,
                Longitude = request.Address.Longitude,
                Neighborhood = request.Address.Neighborhood
            }
        };

        var created = await propertyRepository.CreateAsync(property, ct);

        // TODO: Publish property.created Kafka event
        // payload: property_id, type, subtype, metro_area, asking_price, cap_rate

        return MapToResponse(created);
    }

    public async Task<PropertyResponse?> UpdateAsync(string id, UpdatePropertyRequest request, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var oldAskingPrice = property.AskingPrice;
        var oldCapRate = property.CapRate;
        var oldStatus = property.Status;

        if (request.Title is not null) property.Title = request.Title;
        if (request.PropertyType is not null) property.PropertyType = request.PropertyType;
        if (request.PropertySubtype is not null) property.PropertySubtype = request.PropertySubtype;
        if (request.Status is not null) property.Status = request.Status;
        if (request.TotalSqft.HasValue) property.TotalSqft = request.TotalSqft;
        if (request.LeasableSqft.HasValue) property.LeasableSqft = request.LeasableSqft;
        if (request.YearBuilt.HasValue) property.YearBuilt = request.YearBuilt;
        if (request.LotSizeAcres.HasValue) property.LotSizeAcres = request.LotSizeAcres;
        if (request.UnitCount.HasValue) property.UnitCount = request.UnitCount;
        if (request.AskingPrice.HasValue) property.AskingPrice = request.AskingPrice;
        if (request.CapRate.HasValue) property.CapRate = request.CapRate;
        if (request.Noi.HasValue) property.Noi = request.Noi;
        if (request.OccupancyRate.HasValue) property.OccupancyRate = request.OccupancyRate;
        if (request.DescriptionText is not null) property.DescriptionText = request.DescriptionText;

        await propertyRepository.UpdateAsync(property, ct);

        // TODO: Publish property.updated Kafka event if asking_price or cap_rate changed
        //   (oldAskingPrice != property.AskingPrice || oldCapRate != property.CapRate)
        //   payload: property_id, changed_fields, old_values, new_values
        // TODO: Publish property.status_changed Kafka event if status changed
        //   (oldStatus != property.Status)
        //   payload: property_id, old_status, new_status, deal_id

        return MapToResponse(property);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return false;

        await propertyRepository.DeleteAsync(id, ct);

        // TODO: Publish property.status_changed Kafka event (listed -> off_market)

        return true;
    }

    public async Task<List<MediaResponse>?> GetMediaAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var media = await propertyRepository.GetMediaAsync(id, ct);
        return media.Select(MapToResponse).ToList();
    }

    public async Task<MediaResponse?> AddMediaAsync(string id, AddMediaRequest request, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var media = new PropertyMedia
        {
            Id = "",
            PropertyId = id,
            MediaType = request.MediaType,
            Url = request.Url,
            Caption = request.Caption,
            DisplayOrder = request.DisplayOrder,
            IsPrimary = request.IsPrimary
        };

        var created = await propertyRepository.AddMediaAsync(media, ct);
        return MapToResponse(created);
    }

    public async Task<List<FeatureResponse>?> GetFeaturesAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var features = await propertyRepository.GetFeaturesAsync(id, ct);
        return features.Select(MapToResponse).ToList();
    }

    public async Task<FeatureResponse?> AddFeatureAsync(string id, AddFeatureRequest request, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var feature = new PropertyFeature
        {
            Id = "",
            PropertyId = id,
            FeatureCategory = request.FeatureCategory,
            FeatureName = request.FeatureName,
            FeatureValue = request.FeatureValue
        };

        var created = await propertyRepository.AddFeatureAsync(feature, ct);
        return MapToResponse(created);
    }

    public async Task<bool> SuggestPriceAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return false;

        // TODO: Integrate with AI service to generate price suggestion
        return true;
    }

    // ----- mapping helpers -----

    private static PropertyResponse MapToResponse(Property p) => new(
        p.Id, p.Title, p.Slug, p.PropertyType, p.PropertySubtype, p.Status,
        p.TotalSqft, p.LeasableSqft, p.YearBuilt, p.LotSizeAcres, p.UnitCount,
        p.AskingPrice, p.CapRate, p.Noi, p.OccupancyRate,
        p.MarketCapRateBenchmark, p.Year1NoiEstimate,
        p.DescriptionText, p.AiSummary,
        p.Address is null ? null : new AddressResponse(
            p.Address.Street, p.Address.City, p.Address.State,
            p.Address.Zip, p.Address.MetroArea,
            p.Address.Latitude, p.Address.Longitude,
            p.Address.Neighborhood),
        p.ListedAt, p.UpdatedAt);

    private static MediaResponse MapToResponse(PropertyMedia m) => new(
        m.Id, m.MediaType, m.Url, m.Caption, m.DisplayOrder, m.IsPrimary);

    private static FeatureResponse MapToResponse(PropertyFeature f) => new(
        f.Id, f.FeatureCategory, f.FeatureName, f.FeatureValue);

    private static string GenerateSlug(string title) =>
        title.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace("'", "")
            .Replace("\"", "");
}
