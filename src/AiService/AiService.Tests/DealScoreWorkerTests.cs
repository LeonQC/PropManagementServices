using AiService.Business.Events;
using AiService.Business.Workers;
using AiService.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The worker is glue, so these tests cover the wiring rather than the decisions: that a
/// verdict from the gate turns into the right publish and the right row, and — the one that
/// matters operationally — that feeding it its own echo settles instead of oscillating.
/// </summary>
public class DealScoreWorkerTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        DealScoreWorker Worker,
        FakeWorkFingerprintRepository Fingerprints,
        FakeEventPublisher Publisher);

    private static Harness Build()
    {
        var fingerprints = new FakeWorkFingerprintRepository();
        var publisher = new FakeEventPublisher();
        var options = Options.Create(new WorkerOptions
        {
            Enabled = true,
            RationaleEnabled = false,
            ScoreWriteEpsilon = 0.5,
            MaterialScoreDelta = 5.0,
            SkipStages = ["Dead"],
        });

        var worker = new DealScoreWorker(
            fingerprints, publisher, options,
            NullLogger<DealScoreWorker>.Instance,
            new FixedTimeProvider(AsOf));

        return new Harness(worker, fingerprints, publisher);
    }

    private static AiDealScoreReady SolePublished(FakeEventPublisher publisher)
    {
        var (topic, key, payload) = Assert.Single(publisher.Published);
        Assert.Equal(Topics.AiDealScoreReady, topic);
        var message = Assert.IsType<AiDealScoreReady>(payload);
        Assert.Equal(message.DealId, key); // keyed by deal so compaction and ordering line up
        return message;
    }

    [Fact]
    public async Task An_unscored_deal_publishes_its_score_and_records_a_fingerprint()
    {
        var harness = Build();
        var snapshot = DealSnapshots.Complete(aiScore: null);

        await harness.Worker.HandleAsync(snapshot);

        var message = SolePublished(harness.Publisher);
        Assert.Equal(DealScore.Compute(snapshot, AsOf).Score, message.Score);

        // Slice A never spends, so the prose is always absent and the consumer leaves whatever
        // is stored alone.
        Assert.Null(message.Rationale);

        var row = harness.Fingerprints.Row(PromptFeatures.DealScore, snapshot.DealId);
        Assert.NotNull(row);
        Assert.Equal(Fingerprint.ForDealScore(snapshot), row.InputFingerprint);
        Assert.Null(row.LastOutputScore);
    }

    /// <summary>
    /// The operational guarantee behind "it stops at 85 and does not restart". Feeding the
    /// worker the snapshot its own write-back produces must publish nothing the second time.
    /// </summary>
    [Fact]
    public async Task Replaying_the_write_back_echo_publishes_nothing()
    {
        var harness = Build();
        var snapshot = DealSnapshots.Complete(aiScore: null);

        await harness.Worker.HandleAsync(snapshot);
        var score = SolePublished(harness.Publisher).Score;

        // deals-service applies the score, bumps the version, republishes.
        await harness.Worker.HandleAsync(snapshot with { Version = snapshot.Version + 1, AiScore = score });

        Assert.Single(harness.Publisher.Published);
    }

    /// <summary>A cold replay of the compacted topic re-delivers every deal. With the
    /// fingerprint already stored, each one costs a read and nothing else.</summary>
    [Fact]
    public async Task A_cold_replay_of_settled_deals_does_no_work()
    {
        var harness = Build();
        var snapshot = DealSnapshots.Complete(aiScore: null);

        await harness.Worker.HandleAsync(snapshot);
        var settled = snapshot with { Version = snapshot.Version + 1, AiScore = SolePublished(harness.Publisher).Score };
        await harness.Worker.HandleAsync(settled);

        var writesBefore = harness.Fingerprints.Writes;

        for (var i = 0; i < 5; i++) await harness.Worker.HandleAsync(settled);

        Assert.Single(harness.Publisher.Published);
        Assert.Equal(writesBefore, harness.Fingerprints.Writes);
    }

    [Fact]
    public async Task A_real_financial_edit_republishes_the_score()
    {
        var harness = Build();
        var snapshot = DealSnapshots.Complete(aiScore: null);

        await harness.Worker.HandleAsync(snapshot);
        var first = SolePublished(harness.Publisher).Score;

        var repriced = DealSnapshots.Complete(projectedCapRate: 0.085, aiScore: first);
        await harness.Worker.HandleAsync(repriced);

        Assert.Equal(2, harness.Publisher.Published.Count);
        Assert.NotEqual(first, ((AiDealScoreReady)harness.Publisher.Published[1].Payload).Score);
    }

    [Fact]
    public async Task A_skipped_deal_publishes_nothing_and_records_nothing()
    {
        var harness = Build();

        await harness.Worker.HandleAsync(DealSnapshots.Complete(stage: "Dead"));
        await harness.Worker.HandleAsync(DealSnapshots.Complete(deleted: true));
        await harness.Worker.HandleAsync(DealSnapshots.NonFinancialOnly());

        Assert.Empty(harness.Publisher.Published);
        Assert.Equal(0, harness.Fingerprints.Writes);
    }
}
