namespace DealsService.Models;

public class Deal
{
    public required string Id { get; set; }
    public required string Name { get; set; }

    // Link to the listings-service property plus a small denormalized snapshot
    // captured at creation, so board cards render without cross-service reads.
    public required string PropertyId { get; set; }
    public required string PropertyName { get; set; }
    public string? PropertyType { get; set; }
    public string? MetroArea { get; set; }

    public required string Stage { get; set; }
    public required string Priority { get; set; }
    public required string OwnerId { get; set; }

    /// <summary>Set only when Stage is Dead; one of the DeadReasons values.</summary>
    public string? DeadReason { get; set; }

    public double? OfferPrice { get; set; }
    public double? ProjectedCapRate { get; set; }
    public double? TargetIrr { get; set; }
    public double? EquityMultiple { get; set; }
    public string? ProjectedCloseDate { get; set; }

    // AI columns are first-class per the design doc; populated by a future
    // ai-service via Kafka, never written by user requests.
    public double? AiScore { get; set; }
    public string? AiScoreRationale { get; set; }
    public string? RiskFlags { get; set; }

    public required string StageEnteredAt { get; set; }
    public required string CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }

    public List<DealStageHistory> History { get; set; } = [];
    public List<DealTask> Tasks { get; set; } = [];
    public List<DealComment> Comments { get; set; } = [];
    public List<DealDocument> Documents { get; set; } = [];
}
