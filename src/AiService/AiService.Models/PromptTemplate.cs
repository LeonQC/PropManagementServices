namespace AiService.Models;

/// <summary>
/// A system prompt, stored rather than compiled in so it can be tuned without a
/// redeploy (architecture §3.4). One row is active per <see cref="Feature"/>;
/// <see cref="Version"/> exists so an edit can be rolled forward as a new row
/// instead of destroying the prompt that produced yesterday's answers.
/// </summary>
public class PromptTemplate
{
    public required string Id { get; set; }

    /// <summary>The feature this prompt serves, e.g. "deal_qa". Matches the
    /// feature recorded on <see cref="AiRequestLog"/>.</summary>
    public required string Feature { get; set; }

    public required int Version { get; set; }

    /// <summary>False on superseded rows; exactly one row per feature is active.</summary>
    public required bool IsActive { get; set; }

    public required string SystemPrompt { get; set; }

    /// <summary>Why this version exists — read by whoever is deciding whether to change it again.</summary>
    public string? Notes { get; set; }

    public required string CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}
