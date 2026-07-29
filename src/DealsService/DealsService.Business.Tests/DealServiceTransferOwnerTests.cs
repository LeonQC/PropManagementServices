using DealsService.Business.Domain;
using DealsService.DataAccess;
using DealsService.Models;
using NSubstitute;
using PropTrack.Messaging;
using Xunit;

namespace DealsService.Business.Tests;

public class DealServiceTransferOwnerTests
{
    private const string NewOwnerId = "33333333-3333-3333-3333-333333333333";

    private readonly IDealRepository repo = Substitute.For<IDealRepository>();
    private readonly IEventPublisher publisher = Substitute.For<IEventPublisher>();
    private readonly DealService service;

    public DealServiceTransferOwnerTests() => service = new DealService(repo, publisher);

    private Deal ArrangeDeal(string ownerId = TestData.OwnerId, string stage = DealStages.NdaLoi)
    {
        var deal = TestData.MakeDeal(ownerId, stage);
        repo.GetByIdAsync(deal.Id, Arg.Any<CancellationToken>()).Returns(TestData.MakeRow(deal));
        return deal;
    }

    [Fact]
    public async Task TransferOwner_elevated_succeeds_and_writes_same_stage_history()
    {
        var deal = ArrangeDeal();

        var result = await service.TransferOwnerAsync(deal.Id, NewOwnerId,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.True(result.Succeeded);
        Assert.Equal(NewOwnerId, deal.OwnerId);
        await repo.Received(1).TransitionAsync(deal,
            Arg.Is<DealStageHistory>(h =>
                h.FromStage == DealStages.NdaLoi &&
                h.ToStage == DealStages.NdaLoi &&
                h.ChangedById == TestData.OtherUserId &&
                h.Reason == OwnershipTransfer.Reason(TestData.OwnerId, NewOwnerId)),
            Arg.Is<List<DealTask>>(t => t.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferOwner_non_elevated_is_forbidden_even_with_valid_input()
    {
        var deal = ArrangeDeal();

        var result = await service.TransferOwnerAsync(deal.Id, NewOwnerId,
            actorId: TestData.OwnerId, isElevated: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.Forbidden, result.Code);
        Assert.Equal(TestData.OwnerId, deal.OwnerId);
        await repo.DidNotReceive().TransitionAsync(Arg.Any<Deal>(), Arg.Any<DealStageHistory>(),
            Arg.Any<List<DealTask>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task TransferOwner_invalid_target_fails_validation(string newOwnerId)
    {
        ArrangeDeal();

        var result = await service.TransferOwnerAsync("d-1", newOwnerId,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.Validation, result.Code);
        Assert.Equal("newOwnerId", Assert.Single(result.Errors).Field);
    }

    [Fact]
    public async Task TransferOwner_unknown_deal_is_not_found()
    {
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((DealWithTaskStats?)null);

        var result = await service.TransferOwnerAsync("missing", NewOwnerId,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.NotFound, result.Code);
    }

    [Fact]
    public async Task TransferOwner_to_current_owner_is_noop_success()
    {
        var deal = ArrangeDeal();

        var result = await service.TransferOwnerAsync(deal.Id, TestData.OwnerId,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.True(result.Succeeded);
        Assert.Equal(TestData.OwnerId, deal.OwnerId);
        await repo.DidNotReceive().TransitionAsync(Arg.Any<Deal>(), Arg.Any<DealStageHistory>(),
            Arg.Any<List<DealTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferOwner_publishes_no_events()
    {
        var deal = ArrangeDeal();

        await service.TransferOwnerAsync(deal.Id, NewOwnerId,
            actorId: TestData.OtherUserId, isElevated: true);

        Assert.Empty(publisher.ReceivedCalls());
    }
}
