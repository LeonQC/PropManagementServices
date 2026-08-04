using SearchService.Business.DTOs;
using SearchService.DataAccess;
using SearchService.Models;

namespace SearchService.Business;

/// <summary>
/// Serves the listings grid from the search index. Thin by design — the query lives in the
/// index layer, this maps the results into business DTOs, mirroring how
/// ListingsService.PropertyService sits over its repository.
/// </summary>
public class PropertySearchService(IPropertyIndex index)
{
    public async Task<(List<PropertyDto> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? propertyType, string? status, string? metroArea,
        double? minPrice, double? maxPrice,
        string? sort = null, string? q = null,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await index.SearchAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, sort, q, ct);

        // The listings contract exposes totalCount as an int; the index reports a long.
        return (items.Select(MapToDto).ToList(), (int)Math.Min(totalCount, int.MaxValue));
    }

    // ----- index document ↔ business model mapping -----

    private static PropertyDto MapToDto(PropertyDocument d) => new(
        d.EntityId, d.Title, d.Slug, d.PropertyType, d.PropertySubtype, d.Status,
        d.TotalSqft, d.LeasableSqft, d.YearBuilt, d.LotSizeAcres, d.UnitCount,
        d.AskingPrice, d.CapRate, d.Noi, d.OccupancyRate,
        d.MarketCapRateBenchmark, d.Year1NoiEstimate,
        d.DescriptionText, d.AiSummary,
        d.Address is null ? null : new AddressDto(
            d.Address.Street, d.Address.City, d.Address.State,
            d.Address.Zip, d.Address.MetroArea,
            d.Address.Location?.Lat, d.Address.Location?.Lon,
            d.Address.Neighborhood),
        d.ListedAt, d.UpdatedAt);
}
