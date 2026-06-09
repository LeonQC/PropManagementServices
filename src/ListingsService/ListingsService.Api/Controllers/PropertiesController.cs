using ListingsService.Application.DTOs;
using ListingsService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace ListingsService.Api.Controllers;

[ApiController]
[Route("listings/v1/properties")]
public class PropertiesController(PropertyService propertyService) : ControllerBase
{
    // GET /listings/v1/properties?page=1&pageSize=20&propertyType=Office&status=listed&metroArea=...&minPrice=...&maxPrice=...
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<PropertyResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? propertyType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? metroArea = null,
        [FromQuery] double? minPrice = null,
        [FromQuery] double? maxPrice = null,
        CancellationToken ct = default)
    {
        var response = await propertyService.ListAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, ct);
        return Ok(response);
    }

    // GET /listings/v1/properties/map
    [HttpGet("map")]
    public async Task<ActionResult<GeoJsonResponse>> Map(CancellationToken ct)
    {
        return Ok(await propertyService.GetMapAsync(ct));
    }

    // GET /listings/v1/properties/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<PropertyResponse>> Get(string id, CancellationToken ct)
    {
        var property = await propertyService.GetByIdAsync(id, ct);
        return property is null ? NotFound() : Ok(property);
    }

    // POST /listings/v1/properties
    [HttpPost]
    public async Task<ActionResult<PropertyResponse>> Create(
        [FromBody] CreatePropertyRequest request, CancellationToken ct)
    {
        var created = await propertyService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    // PUT /listings/v1/properties/{id}
    [HttpPut("{id}")]
    public async Task<ActionResult<PropertyResponse>> Update(
        string id, [FromBody] UpdatePropertyRequest request, CancellationToken ct)
    {
        var updated = await propertyService.UpdateAsync(id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
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
        return media is null ? NotFound() : Ok(media);
    }

    // POST /listings/v1/properties/{id}/media
    [HttpPost("{id}/media")]
    public async Task<ActionResult<MediaResponse>> AddMedia(
        string id, [FromBody] AddMediaRequest request, CancellationToken ct)
    {
        var created = await propertyService.AddMediaAsync(id, request, ct);
        return created is null
            ? NotFound()
            : Created($"listings/v1/properties/{id}/media", created);
    }

    // GET /listings/v1/properties/{id}/features
    [HttpGet("{id}/features")]
    public async Task<ActionResult<List<FeatureResponse>>> ListFeatures(string id, CancellationToken ct)
    {
        var features = await propertyService.GetFeaturesAsync(id, ct);
        return features is null ? NotFound() : Ok(features);
    }

    // POST /listings/v1/properties/{id}/features
    [HttpPost("{id}/features")]
    public async Task<ActionResult<FeatureResponse>> AddFeature(
        string id, [FromBody] AddFeatureRequest request, CancellationToken ct)
    {
        var created = await propertyService.AddFeatureAsync(id, request, ct);
        return created is null
            ? NotFound()
            : Created($"listings/v1/properties/{id}/features", created);
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
}
