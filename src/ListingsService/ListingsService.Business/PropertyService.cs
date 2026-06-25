using ListingsService.Business.DTOs;
using ListingsService.Business.Events;
using ListingsService.DataAccess;
using ListingsService.Models;
using PropTrack.Messaging;

namespace ListingsService.Business;

public class PropertyService(IPropertyRepository propertyRepository, IEventPublisher eventPublisher)
{
    public async Task<(List<PropertyDto> Items, int TotalCount)> ListAsync(
        int page, int pageSize,
        string? propertyType, string? status, string? metroArea,
        double? minPrice, double? maxPrice,
        string? sort = null, string? q = null,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await propertyRepository.GetAllAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, sort, q, ct);
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

        await eventPublisher.PublishAsync(Topics.PropertyCreated, created.Id, new PropertyCreated(
            created.Id, created.PropertyType, created.PropertySubtype,
            created.Address?.MetroArea, created.AskingPrice, created.CapRate), ct);

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

        // property.updated — only when asking price or cap rate actually changed.
        var changedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        if (oldAskingPrice != property.AskingPrice)
        {
            changedFields.Add("asking_price");
            oldValues["asking_price"] = oldAskingPrice;
            newValues["asking_price"] = property.AskingPrice;
        }
        if (oldCapRate != property.CapRate)
        {
            changedFields.Add("cap_rate");
            oldValues["cap_rate"] = oldCapRate;
            newValues["cap_rate"] = property.CapRate;
        }
        if (changedFields.Count > 0)
        {
            await eventPublisher.PublishAsync(Topics.PropertyUpdated, property.Id, new PropertyUpdated(
                property.Id, [.. changedFields], oldValues, newValues), ct);
        }

        // property.status_changed — when the status field changed (manual edit, no deal).
        if (oldStatus != property.Status)
        {
            await eventPublisher.PublishAsync(Topics.PropertyStatusChanged, property.Id,
                new PropertyStatusChanged(property.Id, oldStatus, property.Status, null), ct);
        }

        return MapToDto(property);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return false;

        var oldStatus = property.Status;
        await propertyRepository.DeleteAsync(id, ct);

        await eventPublisher.PublishAsync(Topics.PropertyStatusChanged, id,
            new PropertyStatusChanged(id, oldStatus, "off_market", null), ct);

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

    // ----- inbound event handlers (called by Kafka consumers) -----

    /// <summary>deal.created — a deal was opened on this property: mark it under_contract.</summary>
    public async Task ApplyDealCreatedAsync(string propertyId, string dealId, CancellationToken ct = default) =>
        await TransitionStatusAsync(propertyId, "under_contract", dealId, ct);

    /// <summary>
    /// deal.outcome_recorded — the deal closed: a winning outcome acquires the
    /// property, anything else returns it to the market as listed.
    /// </summary>
    public async Task ApplyDealOutcomeAsync(string propertyId, string dealId, string outcome, CancellationToken ct = default)
    {
        var newStatus = outcome is "won" or "closed_won" ? "acquired" : "listed";
        await TransitionStatusAsync(propertyId, newStatus, dealId, ct);
    }

    /// <summary>ai.property_summary_ready — write the generated summary back to the record.</summary>
    public async Task ApplyAiSummaryAsync(string propertyId, string summary, CancellationToken ct = default)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return;

        property.AiSummary = summary;
        await propertyRepository.UpdateAsync(property, ct);
    }

    private async Task TransitionStatusAsync(string propertyId, string newStatus, string? dealId, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(propertyId, ct);
        if (property is null) return;

        var oldStatus = property.Status;
        if (oldStatus == newStatus) return;

        property.Status = newStatus;
        await propertyRepository.UpdateAsync(property, ct);

        await eventPublisher.PublishAsync(Topics.PropertyStatusChanged, propertyId,
            new PropertyStatusChanged(propertyId, oldStatus, newStatus, dealId), ct);
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
