namespace SearchService.Models;

/// <summary>
/// A deal as stored in the OpenSearch index. Like <see cref="PropertyDocument"/> this is the
/// *index* shape, not the deals entity, and it opens with the same cross-entity envelope
/// (EntityType/EntityId/Title/Body) so the group alias can rank both types together.
///
/// <para>Two fields are here specifically because their derived forms can't be indexed.
/// <see cref="EarliestOpenTaskDueDate"/> stands in for hasOverdueTasks, and
/// <see cref="StageDwellAverageDays"/> / <see cref="StageDwellSampleCount"/> for the
/// stale-stage health flag: both answers move with the clock, so the query re-derives them
/// from these against the current date rather than reading a frozen boolean.</para>
/// </summary>
public class DealDocument
{
    // ----- common envelope, identical across every entity type -----
    public string EntityType { get; set; } = "deal";
    public required string EntityId { get; set; }

    /// <summary>The deal name — the deal's equivalent of a property's title.</summary>
    public required string Title { get; set; }

    /// <summary>Flattened searchable text: name, property name/type/metro, and the text of the
    /// deal's recent comments and documents. The cross-entity query hits this one field
    /// without knowing either entity's shape.</summary>
    public string? Body { get; set; }

    // ----- deal-specific -----
    public required string Name { get; set; }
    public required string PropertyId { get; set; }
    public required string PropertyName { get; set; }
    public string? PropertyType { get; set; }
    public string? MetroArea { get; set; }

    public double? OccupancyRate { get; set; }
    public double? MarketCapRateBenchmark { get; set; }

    public required string Stage { get; set; }
    public required string Priority { get; set; }
    public required string OwnerId { get; set; }
    public string? DeadReason { get; set; }

    public double? OfferPrice { get; set; }
    public double? ProjectedCapRate { get; set; }
    public double? TargetIrr { get; set; }
    public double? EquityMultiple { get; set; }
    public string? ProjectedCloseDate { get; set; }

    public double? AiScore { get; set; }
    public string? AiScoreRationale { get; set; }
    public string? RiskFlags { get; set; }

    public required string StageEnteredAt { get; set; }
    public required string CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }

    public int TaskCount { get; set; }
    public int DoneTaskCount { get; set; }

    /// <summary>Earliest due date among the deal's still-open tasks, or null when it has none.
    /// The hasOverdueTasks filter is a range against today over this.</summary>
    public string? EarliestOpenTaskDueDate { get; set; }

    /// <summary>Historical mean dwell time for this deal's (stage, property type), and how many
    /// completed transitions it was averaged over. Snapshotted by deals-service — a fleet-wide
    /// aggregate, so this copy drifts until the deal is republished.</summary>
    public double? StageDwellAverageDays { get; set; }
    public int StageDwellSampleCount { get; set; }

    public long Version { get; set; }
}
