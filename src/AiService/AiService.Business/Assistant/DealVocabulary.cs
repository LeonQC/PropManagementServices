namespace AiService.Business.Assistant;

/// <summary>
/// The enum values deals-service and search-service actually accept, re-declared here
/// because services never reference each other's assemblies — the same reason
/// <c>DealsService.Business.AuthRoles</c> re-declares auth-service's role names.
///
/// <para>These exist to be interpolated into tool descriptions and matched against what
/// the model sends. Both matter: a filter value the model invents ("Underwriting",
/// "industrial") does not error downstream, it silently matches nothing and the assistant
/// reports an empty result as fact. Listing the vocabulary in the schema and normalising
/// what comes back is what turns that silent wrong answer into either a correct call or a
/// tool error the model can fix.</para>
/// </summary>
public static class DealVocabulary
{
    /// <summary>The six pipeline stages, in board order.</summary>
    public static readonly string[] Stages =
        ["InitialInterest", "NdaLoi", "UnderwritingReview", "InvestmentCommittee", "Acquired", "Dead"];

    /// <summary>Stages a deal can no longer move out of; excluded from "active" totals.</summary>
    public static readonly string[] TerminalStages = ["Acquired", "Dead"];

    public static readonly string[] Priorities = ["Low", "Medium", "High"];

    public static readonly string[] TaskStatuses = ["Open", "Done"];

    /// <summary>Property types as seeded. Note "Mixed-Use" carries a hyphen.</summary>
    public static readonly string[] PropertyTypes =
        ["Apartment", "Industrial", "Mixed-Use", "Office", "Retail"];

    /// <summary>Listing statuses on a property (not a deal stage).</summary>
    public static readonly string[] PropertyStatuses =
        ["listed", "under_contract", "acquired", "off_market"];

    /// <summary>Sort keys accepted by GET /search/v1/properties. Anything else silently
    /// falls back to newest-first, so the set is stated rather than passed through.</summary>
    public static readonly string[] PropertySorts =
        ["price_desc", "price_asc", "cap_desc", "relevance"];

    /// <summary>Cross-entity filter on GET /search/v1/all.</summary>
    public static readonly string[] EntityTypes = ["deal", "property"];

    public static string List(IEnumerable<string> values) => string.Join(", ", values);
}
