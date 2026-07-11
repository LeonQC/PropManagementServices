using DealsService.Models;

namespace DealsService.Business.Domain;

/// <summary>
/// Template checklist items auto-generated when a deal enters a stage
/// (design doc §5.2: "template tasks for the new stage are auto-generated").
/// </summary>
public static class StageTaskTemplates
{
    private static readonly Dictionary<string, string[]> Templates = new()
    {
        [DealStages.InitialInterest] =
            ["Review offering memorandum", "Pull market comps", "Run quick financial screen"],
        [DealStages.NdaLoi] =
            ["Execute NDA", "Draft letter of intent", "Submit LOI to seller"],
        [DealStages.UnderwritingReview] =
            ["Build full underwriting model", "Order third-party reports", "Complete site visit"],
        [DealStages.InvestmentCommittee] =
            ["Prepare IC memo", "Schedule IC session", "Circulate deal summary to committee"],
        [DealStages.Acquired] =
            ["Open escrow", "Complete closing checklist", "Hand off to asset management"],
        // Dead: no checklist — the deal is over.
    };

    /// <summary>Builds template task entities for a stage; empty for stages without templates.</summary>
    public static List<DealTask> Materialize(string stage, string now)
    {
        if (!Templates.TryGetValue(stage, out var titles)) return [];
        return titles.Select(title => new DealTask
        {
            Id = "",
            DealId = "",
            Title = title,
            Stage = stage,
            Status = TaskStatuses.Open,
            IsFromTemplate = true,
            CreatedAt = now,
        }).ToList();
    }
}
