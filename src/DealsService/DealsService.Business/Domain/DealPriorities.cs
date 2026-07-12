namespace DealsService.Business.Domain;

public static class DealPriorities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";

    public static readonly string[] All = [Low, Medium, High];
}
