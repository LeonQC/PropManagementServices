namespace SearchService.Models;

/// <summary>
/// A property as stored in the OpenSearch index. This is the *index* shape, deliberately not
/// the listings entity: it carries the cross-entity envelope (EntityType/EntityId/Title/Body)
/// that every searchable document shares, so a future cross-entity query over the group alias
/// can rank and render properties, deals, and documents uniformly.
/// </summary>
public class PropertyDocument
{
    // ----- common envelope, identical across every entity type -----
    public string EntityType { get; set; } = "property";
    public required string EntityId { get; set; }
    public required string Title { get; set; }

    /// <summary>Flattened searchable text — title, subtype, description, summary, address.
    /// Gives cross-entity search a single field to query without knowing each entity's shape.</summary>
    public string? Body { get; set; }

    // ----- property-specific -----
    public string? Slug { get; set; }
    public string? DescriptionText { get; set; }
    public string? AiSummary { get; set; }
    public required string PropertyType { get; set; }
    public string? PropertySubtype { get; set; }
    public required string Status { get; set; }

    public DocumentAddress? Address { get; set; }

    public double? AskingPrice { get; set; }
    public double? CapRate { get; set; }
    public double? Noi { get; set; }
    public double? OccupancyRate { get; set; }
    public double? MarketCapRateBenchmark { get; set; }
    public double? Year1NoiEstimate { get; set; }
    public double? TotalSqft { get; set; }
    public double? LeasableSqft { get; set; }
    public double? LotSizeAcres { get; set; }
    public int? YearBuilt { get; set; }
    public int? UnitCount { get; set; }

    public List<DocumentFeature> Features { get; set; } = [];
    public string? PrimaryImageUrl { get; set; }

    public string? ListedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public long Version { get; set; }
}

public class DocumentAddress
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? MetroArea { get; set; }
    public string? Neighborhood { get; set; }

    /// <summary>geo_point in the mapping; null unless both coordinates are present.</summary>
    public GeoPoint? Location { get; set; }
}

public record GeoPoint(double Lat, double Lon);

public record DocumentFeature(string? Category, string? Name, string? Value);
