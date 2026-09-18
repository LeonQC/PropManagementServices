using System.Globalization;
using System.Text;
using AiService.Business.Events;
using AiService.DataAccess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropTrack.Messaging;

namespace AiService.Business.Workers;

/// <summary>
/// Writes the prose explaining a deal's score, when the score has moved far enough from the one
/// the stored prose describes.
///
/// <para>Glue only. The decision lives in <see cref="DealRationaleGate"/> and the formula lives
/// in deals-service, both pure and both unit-tested. This class cannot be tested without a
/// database, a broker and a model, so it should hold nothing worth testing.</para>
/// </summary>
public class DealRationaleWorker(
    IAiWorkRecordRepository records,
    IPromptTemplateRepository prompts,
    RationaleClient client,
    ModelCallPolicy policy,
    ModelCallThrottle throttle,
    RequestLedger ledger,
    IEventPublisher publisher,
    IOptions<WorkerOptions> options,
    ILogger<DealRationaleWorker> logger)
{
    private readonly WorkerOptions _options = options.Value;

    public async Task HandleAsync(DealSnapshot snapshot, CancellationToken ct = default)
    {
        var stored = await records.GetAsync(PromptFeatures.DealScore, snapshot.DealId, ct);
        var decision = DealRationaleGate.Decide(snapshot, stored?.LastOutputScore, _options);

        if (!decision.Generate)
        {
            logger.LogDebug("Deal {DealId}: no rationale needed — {Reason}", snapshot.DealId, decision.Reason);
            return;
        }

        var template = await prompts.GetActiveAsync(PromptFeatures.DealScore, ct);
        if (template is null)
        {
            logger.LogWarning("No active prompt for {Feature}; skipping {DealId}",
                PromptFeatures.DealScore, snapshot.DealId);
            return;
        }

        var score = snapshot.AiScore!.Value;
        var correlationId = Guid.NewGuid().ToString();

        // Throttle before the policy, not inside it: the gap between calls is about how fast
        // the worker is allowed to spend overall, and a retry is still a call.
        using var slot = await throttle.AcquireAsync(ct);

        var completion = await policy.ExecuteAsync(
            token => client.CompleteAsync(
                _options.RationaleModel, template.SystemPrompt, BuildUserMessage(snapshot, score),
                _options.MaxRationaleTokens, token),
            $"rationale for deal {snapshot.DealId}",
            ct);

        if (completion is null)
        {
            // Nothing recorded, so the next republish tries again rather than treating a
            // failed deal as done.
            logger.LogWarning("Deal {DealId}: rationale generation failed, leaving it for the next publish",
                snapshot.DealId);
            return;
        }

        await ledger.RecordAsync(
            PromptFeatures.DealScore, _options.RationaleModel, userId: null, entityId: snapshot.DealId,
            correlationId, chunkCount: 0,
            completion.InputTokens, completion.OutputTokens, completion.LatencyMs,
            RatesFor(_options.RationaleModel).InputPerMillion,
            RatesFor(_options.RationaleModel).OutputPerMillion,
            succeeded: true, error: null, ct);

        await publisher.PublishAsync(Topics.AiDealRationaleReady, snapshot.DealId,
            new AiDealRationaleReady(snapshot.DealId, completion.Text), ct);

        // The baseline moves only when prose is actually written, which is what stops a run of
        // small drifts from walking the score away from the sentence describing it.
        await records.UpsertAsync(PromptFeatures.DealScore, snapshot.DealId, score, ct);

        logger.LogInformation("Deal {DealId}: rationale written at score {Score} ({Reason})",
            snapshot.DealId, score, decision.Reason);
    }

    /// <summary>
    /// The deal's own figures, not the formula's internals. The model explains the score in
    /// terms of cap rates and occupancy because that is what a reader recognises, and because
    /// restating the weighting would duplicate deals-service's formula over here.
    ///
    /// <para>Comment and document text is deliberately absent. Both are user-authored, neither
    /// is an input to the score, and feeding them to a model that is describing a number would
    /// invite prose about things the number never considered.</para>
    /// </summary>
    private static string BuildUserMessage(DealSnapshot s, double score)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Inv($"Score: {score:0.#} out of 100."));
        sb.AppendLine(Inv($"Stage: {s.Stage}, entered {s.StageEnteredAt}."));
        sb.AppendLine(Inv($"Property type: {s.PropertyType ?? "unspecified"} in {s.MetroArea ?? "an unspecified market"}."));

        AppendPercent(sb, "Projected cap rate", s.ProjectedCapRate);
        AppendPercent(sb, "Market benchmark cap rate", s.MarketCapRateBenchmark);
        AppendPercent(sb, "Target IRR", s.TargetIrr);
        AppendPercent(sb, "Occupancy", s.OccupancyRate);

        if (s.EquityMultiple is { } em) sb.AppendLine(Inv($"Equity multiple: {em:0.00}x."));
        if (s.OfferPrice is { } price) sb.AppendLine(Inv($"Offer price: ${price:N0}."));

        sb.AppendLine(Inv($"Diligence tasks: {s.DoneTaskCount} of {s.TaskCount} complete."));

        if (s.StageDwellSampleCount >= 3 && s.StageDwellAverageDays is { } avg)
            sb.AppendLine(Inv($"Comparable deals spend about {avg:0.#} days in this stage."));

        return sb.ToString();
    }

    private static void AppendPercent(StringBuilder sb, string label, double? fraction)
    {
        // Fractions on the wire, percents in the prompt: a model shown 0.0609 will happily
        // write "0.06% cap rate".
        if (fraction is { } v) sb.AppendLine(Inv($"{label}: {v * 100:0.##}%."));
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>Per-million rates so the ledger prices this model rather than falling back to
    /// the deliberately-pessimistic default, which would overstate the bill several-fold.</summary>
    private ModelRate RatesFor(string model) =>
        _options.ModelRates.TryGetValue(model, out var rate) ? rate : new ModelRate(0.40, 1.60);
}
