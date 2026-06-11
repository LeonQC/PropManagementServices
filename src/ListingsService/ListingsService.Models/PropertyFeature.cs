namespace ListingsService.Models;

public class PropertyFeature
{
    public required string Id { get; set; }
    public required string PropertyId { get; set; }
    public string? FeatureCategory { get; set; }
    public string? FeatureName { get; set; }
    public string? FeatureValue { get; set; }

    public Property? Property { get; set; }
}
