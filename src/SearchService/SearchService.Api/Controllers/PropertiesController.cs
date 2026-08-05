using Microsoft.AspNetCore.Mvc;
using SearchService.Api.DTOs;
using SearchService.Business;
using SearchService.Business.DTOs;

namespace SearchService.Api.Controllers;

[ApiController]
[Route("search/v1/properties")]
public class PropertiesController(PropertySearchService searchService) : ControllerBase
{
    // GET /search/v1/properties?page=1&pageSize=20&propertyType=Office&status=listed&metroArea=...&minPrice=...&maxPrice=...&sort=relevance&q=warehouse
    //
    // Deliberately the same signature as GET /listings/v1/properties: this is the
    // OpenSearch-backed replacement for that grid query, and the Postgres one stays as the
    // fallback path. Any change here needs the same change there.
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<PropertyResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? propertyType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? metroArea = null,
        [FromQuery] double? minPrice = null,
        [FromQuery] double? maxPrice = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await searchService.SearchAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, sort, q, ct);

        return Ok(new PaginatedResponse<PropertyResponse>(
            items.Select(MapToResponse).ToList(),
            totalCount, page, pageSize));
    }

    // ----- business model ↔ DTO mapping (presentation concern) -----

    private static PropertyResponse MapToResponse(PropertyDto p) => new(
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
}
