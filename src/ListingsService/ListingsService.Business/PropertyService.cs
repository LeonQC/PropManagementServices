using ListingsService.Business.DTOs;
using ListingsService.DataAccess;
using ListingsService.Models;

namespace ListingsService.Business;

public class PropertyService(IPropertyRepository propertyRepository)
{
    public async Task<(List<PropertyDto> Items, int TotalCount)> ListAsync(
        int page, int pageSize,
        string? propertyType, string? status, string? metroArea,
        double? minPrice, double? maxPrice,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await propertyRepository.GetAllAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, ct);
        return (items.Select(MapToDto).ToList(), totalCount);
    }

    public async Task<List<PropertyDto>> GetMapPointsAsync(CancellationToken ct = default)
    {
        var properties = await propertyRepository.GetMapPointsAsync(ct);
        return properties.Select(MapToDto).ToList();
    }

    public async Task<PropertyDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        return property is null ? null : MapToDto(property);
    }

    public async Task<PropertyDto> CreateAsync(CreatePropertyDto input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var property = new Property
        {
            Id = "",
            Title = input.Title,
            Slug = GenerateSlug(input.Title),
            PropertyType = input.PropertyType,
            PropertySubtype = input.PropertySubtype,
            Status = string.IsNullOrEmpty(input.Status) ? "listed" : input.Status,
            TotalSqft = input.TotalSqft,
            LeasableSqft = input.LeasableSqft,
            YearBuilt = input.YearBuilt,
            LotSizeAcres = input.LotSizeAcres,
            UnitCount = input.UnitCount,
            AskingPrice = input.AskingPrice,
            CapRate = input.CapRate,
            Noi = input.Noi,
            OccupancyRate = input.OccupancyRate,
            DescriptionText = input.DescriptionText,
            ListedAt = now,
            UpdatedAt = now,
            Address = new Address
            {
                Id = "",
                PropertyId = "",
                Street = input.Address.Street,
                City = input.Address.City,
                State = input.Address.State,
                Zip = input.Address.Zip,
                MetroArea = input.Address.MetroArea,
                Latitude = input.Address.Latitude,
                Longitude = input.Address.Longitude,
                Neighborhood = input.Address.Neighborhood
            }
        };

        var created = await propertyRepository.CreateAsync(property, ct);

        // TODO: Publish property.created Kafka event
        // payload: property_id, type, subtype, metro_area, asking_price, cap_rate

        return MapToDto(created);
    }

    public async Task<PropertyDto?> UpdateAsync(string id, UpdatePropertyDto input, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return null;

        var oldAskingPrice = property.AskingPrice;
        var oldCapRate = property.CapRate;
        var oldStatus = property.Status;

        if (input.Title is not null) property.Title = input.Title;
        if (input.PropertyType is not null) property.PropertyType = input.PropertyType;
        if (input.PropertySubtype is not null) property.PropertySubtype = input.PropertySubtype;
        if (input.Status is not null) property.Status = input.Status;
        if (input.TotalSqft.HasValue) property.TotalSqft = input.TotalSqft;
        if (input.LeasableSqft.HasValue) property.LeasableSqft = input.LeasableSqft;
        if (input.YearBuilt.HasValue) property.YearBuilt = input.YearBuilt;
        if (input.LotSizeAcres.HasValue) property.LotSizeAcres = input.LotSizeAcres;
        if (input.UnitCount.HasValue) property.UnitCount = input.UnitCount;
        if (input.AskingPrice.HasValue) property.AskingPrice = input.AskingPrice;
        if (input.CapRate.HasValue) property.CapRate = input.CapRate;
        if (input.Noi.HasValue) property.Noi = input.Noi;
        if (input.OccupancyRate.HasValue) property.OccupancyRate = input.OccupancyRate;
        if (input.DescriptionText is not null) property.DescriptionText = input.DescriptionText;

        await propertyRepository.UpdateAsync(property, ct);

        // TODO: Publish property.updated Kafka event if asking_price or cap_rate changed
        //   (oldAskingPrice != property.AskingPrice || oldCapRate != property.CapRate)
        // TODO: Publish property.status_changed Kafka event if status changed
        //   (oldStatus != property.Status)

        return MapToDto(property);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return false;

        await propertyRepository.DeleteAsync(id, ct);

        // TODO: Publish property.status_changed Kafka event (listed -> off_market)

        return true;
    }

    public async Task<List<MediaDto>?> GetMediaAsync(string propertyId, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return null;

        var media = await propertyRepository.GetMediaAsync(propertyId, ct);
        return media.Select(MapToDto).ToList();
    }

    public async Task<MediaDto?> AddMediaAsync(string propertyId, CreateMediaDto input, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return null;

        var media = new PropertyMedia
        {
            Id = "",
            PropertyId = propertyId,
            MediaType = input.MediaType,
            Url = input.Url,
            Caption = input.Caption,
            DisplayOrder = input.DisplayOrder,
            IsPrimary = input.IsPrimary
        };

        var created = await propertyRepository.AddMediaAsync(media, ct);
        return MapToDto(created);
    }

    public async Task<List<FeatureDto>?> GetFeaturesAsync(string propertyId, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return null;

        var features = await propertyRepository.GetFeaturesAsync(propertyId, ct);
        return features.Select(MapToDto).ToList();
    }

    public async Task<FeatureDto?> AddFeatureAsync(string propertyId, CreateFeatureDto input, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return null;

        var feature = new PropertyFeature
        {
            Id = "",
            PropertyId = propertyId,
            FeatureCategory = input.FeatureCategory,
            FeatureName = input.FeatureName,
            FeatureValue = input.FeatureValue
        };

        var created = await propertyRepository.AddFeatureAsync(feature, ct);
        return MapToDto(created);
    }

    public async Task<bool> SuggestPriceAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return false;

        // TODO: Integrate with AI service to generate price suggestion
        return true;
    }

    // ----- entity ↔ business model mapping -----

    private static PropertyDto MapToDto(Property p) => new(
        p.Id, p.Title, p.Slug, p.PropertyType, p.PropertySubtype, p.Status,
        p.TotalSqft, p.LeasableSqft, p.YearBuilt, p.LotSizeAcres, p.UnitCount,
        p.AskingPrice, p.CapRate, p.Noi, p.OccupancyRate,
        p.MarketCapRateBenchmark, p.Year1NoiEstimate,
        p.DescriptionText, p.AiSummary,
        p.Address is null ? null : new AddressDto(
            p.Address.Street, p.Address.City, p.Address.State,
            p.Address.Zip, p.Address.MetroArea,
            p.Address.Latitude, p.Address.Longitude,
            p.Address.Neighborhood),
        p.ListedAt, p.UpdatedAt);

    private static MediaDto MapToDto(PropertyMedia m) => new(
        m.Id, m.MediaType, m.Url, m.Caption, m.DisplayOrder, m.IsPrimary);

    private static FeatureDto MapToDto(PropertyFeature f) => new(
        f.Id, f.FeatureCategory, f.FeatureName, f.FeatureValue);

    private static string GenerateSlug(string title) =>
        title.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace("'", "")
            .Replace("\"", "");
}
