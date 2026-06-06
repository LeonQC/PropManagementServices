namespace ListingsService.Domain.Entities;

public class PropertyMedia
{
    public required string Id { get; set; }
    public required string PropertyId { get; set; }
    public string? MediaType { get; set; }
    public string? Url { get; set; }
    public string? Caption { get; set; }
    public int DisplayOrder { get; set; }
    public int IsPrimary { get; set; }

    public Property? Property { get; set; }
}
