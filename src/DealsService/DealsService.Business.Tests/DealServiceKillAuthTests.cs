using DealsService.Business.Domain;
using DealsService.Business.DTOs;
using DealsService.Business.Events;
using DealsService.DataAccess;
using DealsService.Models;
using NSubstitute;
using PropTrack.Messaging;
using Xunit;

namespace DealsService.Business.Tests;

/// <summary>
/// Authorization matrix: Update/Advance are collaborative (no owner check);
/// Kill requires the deal owner or an elevated (Admin/MD) caller.
/// </summary>
public class DealServiceKillAuthTests
{
    private readonly IDealRepository repo = Substitute.For<IDealRepository>();
    private readonly IEventPublisher publisher = Substitute.For<IEventPublisher>();
    private readonly DealService service;

    public DealServiceKillAuthTests() => service = new DealService(repo, publisher);

    private Deal ArrangeDeal(string ownerId = TestData.OwnerId, string stage = DealStages.InitialInterest)
    {
        var deal = TestData.MakeDeal(ownerId, stage);
        repo.GetByIdAsync(deal.Id, Arg.Any<CancellationToken>()).Returns(TestData.MakeRow(deal));
        return deal;
    }

    [Fact]
    public async Task KillAsync_owner_can_kill_own_deal()
    {
        var deal = ArrangeDeal();

        var result = await service.KillAsync(deal.Id, DeadReasons.PricingGap, null,
            actorId: TestData.OwnerId, isElevated: false);

        Assert.True(result.Succeeded);
        Assert.Equal(DealStages.Dead, deal.Stage);
        await repo.Received(1).TransitionAsync(deal, Arg.Any<DealStageHistory>(),
            Arg.Any<List<DealTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KillAsync_non_owner_without_elevation_is_forbidden()
    {
        var deal = ArrangeDeal();

        var result = await service.KillAsync(deal.Id, DeadReasons.PricingGap, null,
            actorId: TestData.OtherUserId, isElevated: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.Forbidden, result.Code);
        Assert.Equal(DealStages.InitialInterest, deal.Stage);
        await repo.DidNotReceive().TransitionAsync(Arg.Any<Deal>(), Arg.Any<DealStageHistory>(),
            Arg.Any<List<DealTask>>(), Arg.Any<CancellationToken>());
        Assert.Empty(publisher.ReceivedCalls());
    }

    [Fact]
    public async Task KillAsync_elevated_non_owner_bypasses_owner_check()
    {
        var deal = ArrangeDeal();

        var result = await service.KillAsync(deal.Id, DeadReasons.SellerWithdrew, null,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.True(result.Succeeded);
        Assert.Equal(DealStages.Dead, deal.Stage);
    }

    [Fact]
    public async Task KillAsync_unknown_deal_is_not_found_before_forbidden()
    {
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((DealWithTaskStats?)null);

        var result = await service.KillAsync("missing", DeadReasons.PricingGap, null,
            actorId: TestData.OtherUserId, isElevated: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_never_changes_owner()
    {
        var deal = ArrangeDeal();

        var result = await service.UpdateAsync(deal.Id, new UpdateDealDto(
            "Renamed", DealPriorities.High, 1_000_000, 0.06, 0.15, 1.8, "2026-12-31"));

        Assert.True(result.Succeeded);
        Assert.Equal(TestData.OwnerId, deal.OwnerId);
        Assert.Equal("Renamed", deal.Name);
    }

    [Fact]
    public async Task AdvanceAsync_non_owner_can_advance_collaboratively()
    {
        var deal = ArrangeDeal();

        var result = await service.AdvanceAsync(deal.Id, null, actorId: TestData.OtherUserId);

        Assert.True(result.Succeeded);
        Assert.Equal(DealStages.NdaLoi, deal.Stage);
        await publisher.Received(1).PublishAsync(Topics.DealStageChanged, deal.Id,
            Arg.Any<DealStageChanged>(), Arg.Any<CancellationToken>());
    }
}
