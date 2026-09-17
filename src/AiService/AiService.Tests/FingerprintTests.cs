using System.Globalization;
using AiService.Business.Workers;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The regression suite for the scoring feedback loop and for the cost gate. Every test here
/// pins one field as either inside or outside the fingerprint, because getting that set wrong
/// fails silently in two expensive ways: include a field the write-back mutates and the worker
/// rescores forever, exclude one the score reads and a real financial edit is ignored.
/// </summary>
public class FingerprintTests
{
    /// <summary>
    /// The loop test. Writing a score back makes deals-service bump Version, restamp UpdatedAt
    /// and set the Ai* columns, then republish. That echo must hash identically to the message
    /// that caused it, or the worker scores the same deal until someone notices the bill.
    /// </summary>
    [Fact]
    public void Fields_the_write_back_mutates_do_not_change_the_hash()
    {
        var before = DealSnapshots.Complete();
        var echo = before with
        {
            Version = before.Version + 1,
            UpdatedAt = "2299-01-01T00:00:00Z",
            AiScore = 72.4,
            AiScoreRationale = "Cap rate sits above benchmark with a stable rent roll.",
            RiskFlags = """[{"type":"stale_stage","severity":"warning","message":"x"}]"""
        };

        Assert.Equal(Fingerprint.ForDealScore(before), Fingerprint.ForDealScore(echo));
    }

    /// <summary>
    /// The cost test. deal.snapshot is republished on every comment and document write, which
    /// are the highest-frequency mutations on a deal and are not scoring inputs. If these
    /// counted, a chatty deal would bill a model call per comment.
    /// </summary>
    [Fact]
    public void Comment_and_document_text_do_not_change_the_hash()
    {
        var before = DealSnapshots.Complete();
        var chatty = before with
        {
            CommentText = "a much longer thread with several new replies",
            DocumentText = "an appraisal and a rent roll were uploaded"
        };

        Assert.Equal(Fingerprint.ForDealScore(before), Fingerprint.ForDealScore(chatty));
    }

    public static TheoryData<string, Business.Events.DealSnapshot> ScoringInputs() => new()
    {
        { "projected cap rate", DealSnapshots.Complete(projectedCapRate: 0.081) },
        { "benchmark cap rate", DealSnapshots.Complete(marketCapRateBenchmark: 0.059) },
        { "target IRR", DealSnapshots.Complete(targetIrr: 0.19) },
        { "equity multiple", DealSnapshots.Complete(equityMultiple: 2.10) },
        { "occupancy", DealSnapshots.Complete(occupancyRate: 0.74) },
        { "task count", DealSnapshots.Complete(taskCount: 11) },
        { "completed tasks", DealSnapshots.Complete(doneTaskCount: 7) },
        { "stage", DealSnapshots.Complete(stage: "Investment Committee") },
        { "stage entered at", DealSnapshots.Complete(stageEnteredAt: "2026-08-01T00:00:00Z") },
        { "stage dwell average", DealSnapshots.Complete(stageDwellAverageDays: 55) },
        { "stage dwell samples", DealSnapshots.Complete(stageDwellSampleCount: 9) },
    };

    /// <summary>The other half of the invariant: everything the score actually reads has to
    /// re-open the deal for scoring when it moves.</summary>
    [Theory]
    [MemberData(nameof(ScoringInputs))]
    public void Scoring_inputs_change_the_hash(string field, Business.Events.DealSnapshot changed)
    {
        Assert.True(
            Fingerprint.ForDealScore(DealSnapshots.Complete()) != Fingerprint.ForDealScore(changed),
            $"changing {field} must invalidate the fingerprint");
    }

    /// <summary>
    /// Numbers are formatted with InvariantCulture. Under a comma-decimal locale a
    /// culture-sensitive format would render 0.07 as "0,07", changing every stored hash at
    /// once — a full unnecessary rescore, triggered by a container's locale, visible nowhere
    /// in the logs. This test is the only place that failure is cheap to catch.
    /// </summary>
    [Fact]
    public void Hash_is_independent_of_the_current_culture()
    {
        var snapshot = DealSnapshots.Complete();
        var invariant = Fingerprint.ForDealScore(snapshot);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(invariant, Fingerprint.ForDealScore(snapshot));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Field framing works: moving a value from one field to its neighbour must not
    /// produce the same canonical string.</summary>
    [Fact]
    public void Adjacent_fields_are_not_interchangeable()
    {
        var a = DealSnapshots.Complete(taskCount: 10, doneTaskCount: 6);
        var b = DealSnapshots.Complete(taskCount: 6, doneTaskCount: 10);

        Assert.NotEqual(Fingerprint.ForDealScore(a), Fingerprint.ForDealScore(b));
    }

    [Fact]
    public void Hash_is_stable_across_calls()
    {
        var snapshot = DealSnapshots.Complete();
        Assert.Equal(Fingerprint.ForDealScore(snapshot), Fingerprint.ForDealScore(snapshot));
    }

    [Fact]
    public void Hash_is_64_lowercase_hex_characters()
    {
        var hash = Fingerprint.ForDealScore(DealSnapshots.Complete());

        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.Contains(c, "0123456789abcdef"));
    }
}
