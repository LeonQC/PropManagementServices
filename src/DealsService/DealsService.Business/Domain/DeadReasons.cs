namespace DealsService.Business.Domain;

/// <summary>Predefined reasons required when a deal moves to Dead (architecture §2.3).</summary>
public static class DeadReasons
{
    public const string PricingGap = "PricingGap";
    public const string FailedDueDiligence = "FailedDueDiligence";
    public const string FinancingFellThrough = "FinancingFellThrough";
    public const string SellerWithdrew = "SellerWithdrew";
    public const string BetterDealFound = "BetterDealFound";

    public static readonly string[] All =
        [PricingGap, FailedDueDiligence, FinancingFellThrough, SellerWithdrew, BetterDealFound];
}
