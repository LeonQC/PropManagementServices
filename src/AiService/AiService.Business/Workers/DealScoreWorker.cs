using AiService.Business.Events;
using AiService.DataAccess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropTrack.Messaging;

namespace AiService.Business.Workers;

/// <summary>
/// Scores one deal snapshot and publishes the result. Glue only — the formula lives in
/// <see cref="DealScore"/> and every skip/proceed branch in <see cref="DealScoreGate"/>, both
/// pure and both unit-tested. Keeping the decisions out of here is deliberate: this class
/// cannot be tested without a database and a broker, so it should contain nothing worth
/// testing.
/// </summary>
public class DealScoreWorker(
    IAiWorkFingerprintRepository fingerprints,
    IEventPublisher publisher,
    IOptions<WorkerOptions> options,
    ILogger<DealScoreWorker> logger,
    TimeProvider? timeProvider = null)
{
    private readonly WorkerOptions _options = options.Value;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task HandleAsync(DealSnapshot snapshot, CancellationToken ct = default)
    {
        var result = DealScore.Compute(snapshot, _clock.GetUtcNow());

        var stored = await fingerprints.GetAsync(PromptFeatures.DealScore, snapshot.DealId, ct);
        var state = stored is null
            ? (DealScoreState?)null
            : new DealScoreState(stored.InputFingerprint, stored.LastOutputScore);

        var decision = DealScoreGate.Decide(snapshot, state, result, _options);

        if (decision.DoesNothing)
        {
            logger.LogDebug("Deal {DealId}: no scoring work — {Reason}", snapshot.DealId, decision.Reason);
            return;
        }

        // WriteRationale stays false until the rationale slice wires a model client. The gate
        // already returns false for it whenever WorkerOptions.RationaleEnabled is off, so
        // nothing here has to guard against spending by accident.
        string? rationale = null;

        if (decision.WriteScore || rationale is not null)
        {
            await publisher.PublishAsync(
                Topics.AiDealScoreReady,
                snapshot.DealId,
                new AiDealScoreReady(snapshot.DealId, result.Score, rationale),
                ct);

            logger.LogInformation(
                "Deal {DealId} scored {Score} ({Reason}); components {Components}, missing {Missing}",
                snapshot.DealId, result.Score, decision.Reason,
                string.Join(",", result.Contributing.Select(c => c.Name)),
                string.Join(",", result.MissingNames));
        }

        if (decision.RecordFingerprint)
        {
            // Carry the previous rationale baseline forward: this run wrote no prose, so the
            // score the stored prose was written at has not changed.
            var lastOutputScore = rationale is not null ? result.Score : stored?.LastOutputScore;

            await fingerprints.UpsertAsync(
                PromptFeatures.DealScore, snapshot.DealId, decision.Fingerprint, lastOutputScore, ct);
        }
    }
}
