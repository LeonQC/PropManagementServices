namespace ListingsService.Domain.Entities;

public class Address
{
    public required string Id { get; set; }
    public required string PropertyId { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? MetroArea { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Neighborhood { get; set; }

    public Property? Property { get; set; }
}
