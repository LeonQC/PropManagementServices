using ListingsService.Models;
using Microsoft.EntityFrameworkCore;

namespace ListingsService.DataAccess;

public class PropertyRepository(ListingsDbContext db) : IPropertyRepository
{
    public async Task<Property?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await db.Properties
            .Include(p => p.Address)
            .Include(p => p.Media.OrderBy(m => m.DisplayOrder))
            .Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<(List<Property> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize,
        string? propertyType = null,
        string? status = null,
        string? metroArea = null,
        double? minPrice = null,
        double? maxPrice = null,
        CancellationToken ct = default)
    {
        var query = db.Properties
            .Include(p => p.Address)
            .Where(p => p.Status != "off_market")
            .AsQueryable();

        if (!string.IsNullOrEmpty(propertyType))
            query = query.Where(p => p.PropertyType == propertyType);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrEmpty(metroArea))
            query = query.Where(p => p.Address != null && p.Address.MetroArea == metroArea);

        if (minPrice.HasValue)
            query = query.Where(p => p.AskingPrice >= minPrice.Value);

        if (maxPrice.HasValue)
            query = query.Where(p => p.AskingPrice <= maxPrice.Value);

        var ordered = query.OrderByDescending(p => p.ListedAt);

        var totalCount = await ordered.CountAsync(ct);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<Property> CreateAsync(Property property, CancellationToken ct = default)
    {
        property.Id = Guid.NewGuid().ToString();
        if (property.Address != null)
        {
            property.Address.Id = Guid.NewGuid().ToString();
            property.Address.PropertyId = property.Id;
        }
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);
        return property;
    }

    public async Task UpdateAsync(Property property, CancellationToken ct = default)
    {
        property.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        db.Properties.Update(property);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await db.Properties
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "off_market")
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")), ct);
    }

    // Media
    public async Task<List<PropertyMedia>> GetMediaAsync(string propertyId, CancellationToken ct = default)
    {
        return await db.PropertyMedia
            .Where(m => m.PropertyId == propertyId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<PropertyMedia> AddMediaAsync(PropertyMedia media, CancellationToken ct = default)
    {
        media.Id = Guid.NewGuid().ToString();
        db.PropertyMedia.Add(media);
        await db.SaveChangesAsync(ct);
        return media;
    }

    // Features
    public async Task<List<PropertyFeature>> GetFeaturesAsync(string propertyId, CancellationToken ct = default)
    {
        return await db.PropertyFeatures
            .Where(f => f.PropertyId == propertyId)
            .OrderBy(f => f.FeatureCategory)
            .ThenBy(f => f.FeatureName)
            .ToListAsync(ct);
    }

    public async Task<PropertyFeature> AddFeatureAsync(PropertyFeature feature, CancellationToken ct = default)
    {
        feature.Id = Guid.NewGuid().ToString();
        db.PropertyFeatures.Add(feature);
        await db.SaveChangesAsync(ct);
        return feature;
    }

    // Map
    public async Task<List<Property>> GetMapPointsAsync(CancellationToken ct = default)
    {
        return await db.Properties
            .Include(p => p.Address)
            .Where(p => p.Status != "off_market"
                && p.Address != null
                && p.Address.Latitude != null
                && p.Address.Longitude != null)
            .ToListAsync(ct);
    }
}
