namespace DealsService.Business.Domain;

public static class TaskStatuses
{
    public const string Open = "Open";
    public const string Done = "Done";

    public static readonly string[] All = [Open, Done];
}
