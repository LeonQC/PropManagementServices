namespace ListingsService.Models;

public class Property
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Slug { get; set; }
    public required string PropertyType { get; set; }
    public string? PropertySubtype { get; set; }
    public required string Status { get; set; }
    public double? TotalSqft { get; set; }
    public double? LeasableSqft { get; set; }
    public int? YearBuilt { get; set; }
    public double? LotSizeAcres { get; set; }
    public int? UnitCount { get; set; }
    public double? AskingPrice { get; set; }
    public double? CapRate { get; set; }
    public double? Noi { get; set; }
    public double? OccupancyRate { get; set; }
    public double? MarketCapRateBenchmark { get; set; }
    public double? Year1NoiEstimate { get; set; }
    public string? DescriptionText { get; set; }
    public string? AiSummary { get; set; }
    public string? ListedAt { get; set; }
    public string? UpdatedAt { get; set; }

    public Address? Address { get; set; }
    public List<PropertyMedia> Media { get; set; } = [];
    public List<PropertyFeature> Features { get; set; } = [];
}
