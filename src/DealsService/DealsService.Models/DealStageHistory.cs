namespace DealsService.Models;

/// <summary>
/// Append-only log of stage transitions. Rows are never updated or deleted
/// (architecture §4.1) — the audit trail is the point.
/// </summary>
public class DealStageHistory
{
    public required string Id { get; set; }
    public required string DealId { get; set; }

    /// <summary>Null for the row written at deal creation.</summary>
    public string? FromStage { get; set; }
    public required string ToStage { get; set; }
    public required string ChangedById { get; set; }
    public required string ChangedAt { get; set; }

    /// <summary>Whole days spent in FromStage; null on the creation row.</summary>
    public int? DaysInStage { get; set; }

    /// <summary>Dead reason when ToStage is Dead.</summary>
    public string? Reason { get; set; }
}
