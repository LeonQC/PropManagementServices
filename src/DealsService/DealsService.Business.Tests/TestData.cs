using DealsService.Business.Domain;
using DealsService.DataAccess;
using DealsService.Models;

namespace DealsService.Business.Tests;

/// <summary>Builders for the seeded demo-user GUID shapes the service works with.</summary>
public static class TestData
{
    public const string OwnerId = "11111111-1111-1111-1111-111111111111";
    public const string OtherUserId = "44444444-4444-4444-4444-444444444444";

    public static Deal MakeDeal(string ownerId = OwnerId, string stage = DealStages.InitialInterest) => new()
    {
        Id = "d-1",
        Name = "Test Deal",
        PropertyId = "p-1",
        PropertyName = "Test Property",
        Stage = stage,
        Priority = DealPriorities.Medium,
        OwnerId = ownerId,
        StageEnteredAt = "2026-07-01T00:00:00.0000000Z",
        CreatedAt = "2026-07-01T00:00:00.0000000Z",
    };

    public static DealWithTaskStats MakeRow(Deal deal) => new(deal, 0, 0, false);
}
