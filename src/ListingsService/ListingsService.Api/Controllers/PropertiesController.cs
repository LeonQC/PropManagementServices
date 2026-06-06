using ListingsService.Application.DTOs;
using ListingsService.Application.Interfaces;
using ListingsService.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ListingsService.Api.Controllers;

[ApiController]
[Route("listings/v1/properties")]
public class PropertiesController(IPropertyRepository propertyRepository) : ControllerBase
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
        var (items, totalCount) = await propertyRepository.GetAllAsync(
            page, pageSize, propertyType, status, metroArea, minPrice, maxPrice, ct);
        var response = new PaginatedResponse<PropertyResponse>(
            items.Select(MapToResponse).ToList(),
            totalCount, page, pageSize);
        return Ok(response);
    }

    // GET /listings/v1/properties/map
    [HttpGet("map")]
    public async Task<ActionResult<GeoJsonResponse>> Map(CancellationToken ct)
    {
        var properties = await propertyRepository.GetMapPointsAsync(ct);

        var features = properties.Select(p => new GeoJsonFeature(
            Type: "Feature",
            Geometry: new GeoJsonGeometry(
                Type: "Point",
                Coordinates: [p.Address!.Longitude!.Value, p.Address.Latitude!.Value]),
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
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();
        return Ok(MapToResponse(property));
    }

    // POST /listings/v1/properties
    [HttpPost]
    public async Task<ActionResult<PropertyResponse>> Create(
        [FromBody] CreatePropertyRequest request, CancellationToken ct)
    {
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
            ListedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
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

        return CreatedAtAction(nameof(Get), new { id = created.Id }, MapToResponse(created));
    }

    // PUT /listings/v1/properties/{id}
    [HttpPut("{id}")]
    public async Task<ActionResult<PropertyResponse>> Update(
        string id, [FromBody] UpdatePropertyRequest request, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        // Save old values for Kafka later
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
        // TODO: Publish property.status_changed Kafka event if status changed

        return Ok(MapToResponse(property));
    }

    // DELETE /listings/v1/properties/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();
        await propertyRepository.DeleteAsync(id, ct);

        // TODO: Publish property.status_changed Kafka event (listed -> off_market)

        return NoContent();
    }

    // GET /listings/v1/properties/{id}/media
    [HttpGet("{id}/media")]
    public async Task<ActionResult<List<MediaResponse>>> ListMedia(string id, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        var media = await propertyRepository.GetMediaAsync(id, ct);
        return Ok(media.Select(m => new MediaResponse(
            m.Id, m.MediaType, m.Url, m.Caption,
            m.DisplayOrder, m.IsPrimary == 1)).ToList());
    }

    // POST /listings/v1/properties/{id}/media
    [HttpPost("{id}/media")]
    public async Task<ActionResult<MediaResponse>> AddMedia(
        string id, [FromBody] AddMediaRequest request, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        var media = new PropertyMedia
        {
            Id = "",
            PropertyId = id,
            MediaType = request.MediaType,
            Url = request.Url,
            Caption = request.Caption,
            DisplayOrder = request.DisplayOrder,
            IsPrimary = request.IsPrimary ? 1 : 0
        };

        var created = await propertyRepository.AddMediaAsync(media, ct);
        return Created($"listings/v1/properties/{id}/media", new MediaResponse(
            created.Id, created.MediaType, created.Url, created.Caption,
            created.DisplayOrder, created.IsPrimary == 1));
    }

    // GET /listings/v1/properties/{id}/features
    [HttpGet("{id}/features")]
    public async Task<ActionResult<List<FeatureResponse>>> ListFeatures(string id, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        var features = await propertyRepository.GetFeaturesAsync(id, ct);
        return Ok(features.Select(f => new FeatureResponse(
            f.Id, f.FeatureCategory, f.FeatureName, f.FeatureValue)).ToList());
    }

    // POST /listings/v1/properties/{id}/features
    [HttpPost("{id}/features")]
    public async Task<ActionResult<FeatureResponse>> AddFeature(
        string id, [FromBody] AddFeatureRequest request, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        var feature = new PropertyFeature
        {
            Id = "",
            PropertyId = id,
            FeatureCategory = request.FeatureCategory,
            FeatureName = request.FeatureName,
            FeatureValue = request.FeatureValue
        };

        var created = await propertyRepository.AddFeatureAsync(feature, ct);
        return Created($"listings/v1/properties/{id}/features", new FeatureResponse(
            created.Id, created.FeatureCategory, created.FeatureName, created.FeatureValue));
    }

    // POST /listings/v1/properties/{id}/suggest-price
    [HttpPost("{id}/suggest-price")]
    public async Task<IActionResult> SuggestPrice(string id, CancellationToken ct)
    {
        var property = await propertyRepository.GetByIdAsync(id, ct);
        if (property is null) return NotFound();

        // TODO: Integrate with AI service to generate price suggestion
        return Ok(new { message = "Price suggestion request submitted", propertyId = id });
    }

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

    private static string GenerateSlug(string title) =>
        title.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace("'", "")
            .Replace("\"", "");
}
