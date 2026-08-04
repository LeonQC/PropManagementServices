using Microsoft.Extensions.Logging;
using SearchService.Business.Events;
using SearchService.DataAccess;
using SearchService.Models;

namespace SearchService.Business;

/// <summary>
/// Turns a property.snapshot into an indexed document. Holds the projection logic; the consumer
/// is a thin adapter over this, matching how the other services keep their Kafka consumers as
/// one-liners that delegate to a scoped service.
/// </summary>
public class PropertyIndexingService(IPropertyIndex index, ILogger<PropertyIndexingService> logger)
{
    public async Task ApplySnapshotAsync(PropertySnapshot snapshot, CancellationToken ct = default)
    {
        // Soft-deleted (off_market) properties are removed from the index rather than stored
        // with a flag: nothing should surface them, and dropping them keeps every query from
        // having to remember an exclusion filter.
        if (snapshot.Deleted)
        {
            await index.DeleteAsync(snapshot.PropertyId, ct);
            logger.LogInformation("Removed {Id} from the index (deleted).", snapshot.PropertyId);
            return;
        }

        var indexed = await index.IndexAsync(MapToDocument(snapshot), snapshot.Version, ct);

        if (indexed)
            logger.LogInformation("Indexed {Id} at version {Version}.", snapshot.PropertyId, snapshot.Version);
    }

    /// <summary>Live document count in the index — used by the health endpoint to compare
    /// against the source row count when verifying a backfill.</summary>
    public Task<long> GetIndexedCountAsync(CancellationToken ct = default) => index.CountAsync(ct);

    // ----- snapshot → index document -----

    private static PropertyDocument MapToDocument(PropertySnapshot s) => new()
    {
        EntityType = "property",
        EntityId = s.PropertyId,
        Title = s.Title,
        Body = BuildBody(s),
        Slug = s.Slug,
        DescriptionText = s.DescriptionText,
        AiSummary = s.AiSummary,
        PropertyType = s.PropertyType,
        PropertySubtype = s.PropertySubtype,
        Status = s.Status,
        Address = s.Address is null ? null : new DocumentAddress
        {
            Street = s.Address.Street,
            City = s.Address.City,
            State = s.Address.State,
            Zip = s.Address.Zip,
            MetroArea = s.Address.MetroArea,
            Neighborhood = s.Address.Neighborhood,
            // geo_point rejects a half-populated point, so only emit it when both are present.
            Location = s.Address.Latitude is { } lat && s.Address.Longitude is { } lon
                ? new GeoPoint(lat, lon)
                : null,
        },
        AskingPrice = s.AskingPrice,
        CapRate = s.CapRate,
        Noi = s.Noi,
        OccupancyRate = s.OccupancyRate,
        MarketCapRateBenchmark = s.MarketCapRateBenchmark,
        Year1NoiEstimate = s.Year1NoiEstimate,
        TotalSqft = s.TotalSqft,
        LeasableSqft = s.LeasableSqft,
        LotSizeAcres = s.LotSizeAcres,
        YearBuilt = s.YearBuilt,
        UnitCount = s.UnitCount,
        Features = [.. (s.Features ?? []).Select(f => new DocumentFeature(f.Category, f.Name, f.Value))],
        PrimaryImageUrl = s.PrimaryImageUrl,
        ListedAt = s.ListedAt,
        UpdatedAt = s.UpdatedAt,
        Version = s.Version,
    };

    /// <summary>
    /// Concatenated searchable text for the shared `body` field, so a cross-entity query can hit
    /// one field without knowing each entity's shape.
    /// </summary>
    private static string BuildBody(PropertySnapshot s) => string.Join(' ', new[]
    {
        s.Title,
        s.PropertySubtype,
        s.DescriptionText,
        s.AiSummary,
        s.Address?.City,
        s.Address?.MetroArea,
        s.Address?.Neighborhood,
    }.Where(v => !string.IsNullOrWhiteSpace(v)));
}
