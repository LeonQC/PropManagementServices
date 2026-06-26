using ListingsService.Api.DTOs;
using ListingsService.Business;
using ListingsService.Business.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ListingsService.Api.Controllers;

[ApiController]
[Route("listings/v1/properties")]
public class PropertiesController(PropertyService propertyService) : ControllerBase
{
    // GET /listings/v1/properties?page=1&pageSize=20&propertyType=Office&status=listed&metroArea=...&minPrice=...&maxPrice=...&sort=price_desc&q=warehouse
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
        var (items, totalCount) = await propertyService.ListAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, sort, q, ct);
        return Ok(new PaginatedResponse<PropertyResponse>(
            items.Select(MapToResponse).ToList(),
            totalCount, page, pageSize));
    }

    // GET /listings/v1/properties/map
    [HttpGet("map")]
    public async Task<ActionResult<GeoJsonResponse>> Map(CancellationToken ct)
    {
        var properties = await propertyService.GetMapPointsAsync(ct);

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

        return Ok(new GeoJsonResponse("FeatureCollection", features));
    }

    // GET /listings/v1/properties/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<PropertyResponse>> Get(string id, CancellationToken ct)
    {
        var property = await propertyService.GetByIdAsync(id, ct);
        return property is null ? NotFound() : Ok(MapToResponse(property));
    }

    // POST /listings/v1/properties
    [HttpPost]
    public async Task<ActionResult<PropertyResponse>> Create(
        [FromBody] CreatePropertyRequest request, CancellationToken ct)
    {
        var input = new CreatePropertyDto(
            request.Title,
            request.PropertyType,
            request.PropertySubtype,
            request.Status,
            request.TotalSqft,
            request.LeasableSqft,
            request.YearBuilt,
            request.LotSizeAcres,
            request.UnitCount,
            request.AskingPrice,
            request.CapRate,
            request.Noi,
            request.OccupancyRate,
            request.DescriptionText,
            new CreateAddressDto(
                request.Address.Street,
                request.Address.City,
                request.Address.State,
                request.Address.Zip,
                request.Address.MetroArea,
                request.Address.Latitude,
                request.Address.Longitude,
                request.Address.Neighborhood));

        var created = await propertyService.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, MapToResponse(created));
    }

    // PUT /listings/v1/properties/{id}
    [HttpPut("{id}")]
    public async Task<ActionResult<PropertyResponse>> Update(
        string id, [FromBody] UpdatePropertyRequest request, CancellationToken ct)
    {
        var input = new UpdatePropertyDto(
            request.Title,
            request.PropertyType,
            request.PropertySubtype,
            request.Status,
            request.TotalSqft,
            request.LeasableSqft,
            request.YearBuilt,
            request.LotSizeAcres,
            request.UnitCount,
            request.AskingPrice,
            request.CapRate,
            request.Noi,
            request.OccupancyRate,
            request.DescriptionText);

        var updated = await propertyService.UpdateAsync(id, input, ct);
        return updated is null ? NotFound() : Ok(MapToResponse(updated));
    }

    // DELETE /listings/v1/properties/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await propertyService.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    // GET /listings/v1/properties/{id}/media
    [HttpGet("{id}/media")]
    public async Task<ActionResult<List<MediaResponse>>> ListMedia(string id, CancellationToken ct)
    {
        var media = await propertyService.GetMediaAsync(id, ct);
        return media is null ? NotFound() : Ok(media.Select(MapToResponse).ToList());
    }

    // POST /listings/v1/properties/{id}/media
    [HttpPost("{id}/media")]
    public async Task<ActionResult<MediaResponse>> AddMedia(
        string id, [FromBody] AddMediaRequest request, CancellationToken ct)
    {
        var input = new CreateMediaDto(
            request.MediaType, request.Url, request.Caption,
            request.DisplayOrder, request.IsPrimary);

        var created = await propertyService.AddMediaAsync(id, input, ct);
        return created is null
            ? NotFound()
            : Created($"listings/v1/properties/{id}/media", MapToResponse(created));
    }

    // GET /listings/v1/properties/{id}/features
    [HttpGet("{id}/features")]
    public async Task<ActionResult<List<FeatureResponse>>> ListFeatures(string id, CancellationToken ct)
    {
        var features = await propertyService.GetFeaturesAsync(id, ct);
        return features is null ? NotFound() : Ok(features.Select(MapToResponse).ToList());
    }

    // POST /listings/v1/properties/{id}/features
    [HttpPost("{id}/features")]
    public async Task<ActionResult<FeatureResponse>> AddFeature(
        string id, [FromBody] AddFeatureRequest request, CancellationToken ct)
    {
        var input = new CreateFeatureDto(
            request.FeatureCategory, request.FeatureName, request.FeatureValue);

        var created = await propertyService.AddFeatureAsync(id, input, ct);
        return created is null
            ? NotFound()
            : Created($"listings/v1/properties/{id}/features", MapToResponse(created));
    }

    // POST /listings/v1/properties/{id}/suggest-price
    [HttpPost("{id}/suggest-price")]
    public async Task<IActionResult> SuggestPrice(string id, CancellationToken ct)
    {
        var ok = await propertyService.SuggestPriceAsync(id, ct);
        return ok
            ? Ok(new { message = "Price suggestion request submitted", propertyId = id })
            : NotFound();
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

    private static MediaResponse MapToResponse(MediaDto m) => new(
        m.Id, m.MediaType, m.Url, m.Caption, m.DisplayOrder, m.IsPrimary);

    private static FeatureResponse MapToResponse(FeatureDto f) => new(
        f.Id, f.FeatureCategory, f.FeatureName, f.FeatureValue);
}
