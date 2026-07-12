namespace DealsService.Business.Domain;

/// <summary>
/// The fixed pipeline stages (design doc §5.2). A deal advances strictly along
/// <see cref="Sequence"/> or moves to <see cref="Dead"/> from any active stage.
/// Values are stored as-is in the DB and used verbatim on the wire and in the UI.
/// </summary>
public static class DealStages
{
    public const string InitialInterest = "InitialInterest";
    public const string NdaLoi = "NdaLoi";
    public const string UnderwritingReview = "UnderwritingReview";
    public const string InvestmentCommittee = "InvestmentCommittee";
    public const string Acquired = "Acquired";
    public const string Dead = "Dead";

    /// <summary>The advancement order; Dead is reachable from any active stage but never advanced into.</summary>
    public static readonly string[] Sequence =
        [InitialInterest, NdaLoi, UnderwritingReview, InvestmentCommittee, Acquired];

    public static readonly string[] All = [.. Sequence, Dead];

    /// <summary>The next stage in sequence, or null when the stage is terminal or unknown.</summary>
    public static string? Next(string stage)
    {
        var i = Array.IndexOf(Sequence, stage);
        return i >= 0 && i < Sequence.Length - 1 ? Sequence[i + 1] : null;
    }

    /// <summary>Acquired and Dead are terminal: no further transitions.</summary>
    public static bool IsTerminal(string stage) => stage is Acquired or Dead;
}
