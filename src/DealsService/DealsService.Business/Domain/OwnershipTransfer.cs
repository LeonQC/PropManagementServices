namespace DealsService.Business.Domain;

/// <summary>
/// Ownership transfers are recorded as same-stage DealStageHistory rows whose
/// Reason carries this machine-parsable sentinel: "OWNER_TRANSFER:{from}:{to}".
/// No schema change; the UI history panel special-cases the prefix.
/// </summary>
public static class OwnershipTransfer
{
    public const string ReasonPrefix = "OWNER_TRANSFER:";

    public static string Reason(string fromOwnerId, string toOwnerId) =>
        $"{ReasonPrefix}{fromOwnerId}:{toOwnerId}";
}
