using System.Text.RegularExpressions;
using ListingsService.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

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
        string? sort = null,
        string? q = null,
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

        // Full-text search over the GIN-indexed generated tsvector (title^A, description^B,
        // address^C — see ListingsDbContext), ANDed with the filters above. BuildTsQuery
        // sanitizes the input to lexemes and makes the last token a prefix (term:*) so the
        // debounced box keeps matching as the user types; the resulting string is passed to
        // to_tsquery as a parameter (no tsquery-syntax injection). The vector is a shadow
        // property, read via EF.Property. AI natural-language / semantic search is a separate
        // later milestone (M7), out of scope here.
        var tsQuery = BuildTsQuery(q);
        if (tsQuery != null)
            query = query.Where(p =>
                EF.Property<NpgsqlTsVector>(p, "SearchVector")
                    .Matches(EF.Functions.ToTsQuery("english", tsQuery)));

        // Sort encodes field + direction in one value (maps to the frontend sort dropdown).
        // Every branch ends with a deterministic ThenBy(p => p.Id) tiebreaker so equal-key
        // rows come back in a stable order across paginated requests. "relevance" ranks by
        // ts_rank and only applies with a keyword present; default (omitted/blank/unknown)
        // preserves the original newest-first behavior.
        var ordered = (sort, tsQuery) switch
        {
            ("price_desc", _) => query.OrderByDescending(p => p.AskingPrice).ThenBy(p => p.Id),
            ("price_asc", _) => query.OrderBy(p => p.AskingPrice).ThenBy(p => p.Id),
            ("cap_desc", _) => query.OrderByDescending(p => p.CapRate).ThenBy(p => p.Id),
            ("relevance", not null) => query
                .OrderByDescending(p => EF.Property<NpgsqlTsVector>(p, "SearchVector")
                    .Rank(EF.Functions.ToTsQuery("english", tsQuery!)))
                .ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.ListedAt).ThenBy(p => p.Id),
        };

        var totalCount = await ordered.CountAsync(ct);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    /// <summary>
    /// Turns a raw keyword string into a Postgres tsquery: sanitized to alphanumeric
    /// lexemes, AND'd together, with the final token made a prefix (term:*) for typeahead.
    /// Returns null when there's nothing searchable (so callers skip the filter).
    /// </summary>
    private static string? BuildTsQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return null;

        var terms = Regex.Split(q.Trim().ToLowerInvariant(), "[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToList();
        if (terms.Count == 0) return null;

        terms[^1] += ":*";                 // last token as a prefix, for as-you-type matching
        return string.Join(" & ", terms);  // AND semantics across tokens
    }

    public async Task<Property> CreateAsync(Property property, CancellationToken ct = default)
    {
        property.Id = Guid.NewGuid().ToString();
        if (property.Address != null)
        {
            property.Address.Id = Guid.NewGuid().ToString();
            property.Address.PropertyId = property.Id;
        }
        property.Version = 1;
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);
        return property;
    }

    public async Task UpdateAsync(Property property, CancellationToken ct = default)
    {
        property.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        property.Version++;   // external version for the search index — see Property.Version
        db.Properties.Update(property);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // Compute the timestamp into a local first: ExecuteUpdateAsync translates its
        // lambda directly to SQL, and DateTime.ToString(...) isn't SQL-translatable.
        // A captured local is sent as a parameter instead.
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await db.Properties
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "off_market")
                .SetProperty(p => p.UpdatedAt, now)
                // Bump in SQL: ExecuteUpdate bypasses the change tracker, so the caller's
                // in-memory entity is stale and can't do this itself.
                .SetProperty(p => p.Version, p => p.Version + 1), ct);
    }

    /// <summary>
    /// Every property with its children, for republishing snapshots (backfill/reindex).
    /// Deliberately unlike GetAllAsync: no off_market filter (soft-deleted rows must still
    /// reach the index, flagged deleted) and Media/Features are included, since the snapshot
    /// projection carries them.
    /// </summary>
    public async Task<(List<Property> Items, int TotalCount)> GetAllForReindexAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Properties
            .Include(p => p.Address)
            .Include(p => p.Media.OrderBy(m => m.DisplayOrder))
            .Include(p => p.Features)
            .OrderBy(p => p.Id)          // stable key order so paging can't skip rows
            .AsQueryable();

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
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
