namespace DealsService.Models;

public class DealTask
{
    public required string Id { get; set; }
    public required string DealId { get; set; }
    public required string Title { get; set; }

    /// <summary>The pipeline stage this task belongs to on the checklist.</summary>
    public required string Stage { get; set; }
    public required string Status { get; set; }
    public string? AssigneeId { get; set; }
    public string? DueDate { get; set; }

    /// <summary>True when auto-generated from the stage template on a transition.</summary>
    public bool IsFromTemplate { get; set; }

    public required string CreatedAt { get; set; }
    public string? CompletedAt { get; set; }
}
