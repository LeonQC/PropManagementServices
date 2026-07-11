namespace DealsService.Models;

public class DealComment
{
    public required string Id { get; set; }
    public required string DealId { get; set; }

    /// <summary>Optional parent for threading; the feed renders flat today.</summary>
    public string? ParentId { get; set; }

    public required string Body { get; set; }
    public required string AuthorId { get; set; }

    /// <summary>Flags entries authored by the AI pipeline (design doc §4.3).</summary>
    public bool IsAiGenerated { get; set; }

    public required string CreatedAt { get; set; }
}
