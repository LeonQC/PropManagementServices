using AiService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace AiService.Business.Workers;

/// <summary>
/// Scores deals as they change, by consuming deal.snapshot. Runs under its own consumer group
/// so a rescore-everything replay does not rewind whatever this service consumes next.
///
/// <para>The base class auto-commits offsets and swallows handler exceptions, so a message
/// that fails is dropped rather than retried. That is survivable here because the recovery
/// path already exists and is cheap: <c>POST /deals/v1/deals/republish</c> re-emits every
/// snapshot, and deals whose fingerprint already matches skip immediately, so only the dropped
/// ones do any work.</para>
/// </summary>
public sealed class DealScoreConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealScoreConsumer> logger)
    : KafkaConsumerService<DealSnapshot>(
        settings, Topics.DealSnapshot, scopeFactory, logger, ConsumerGroups.DealSnapshot)
{
    protected override Task HandleAsync(
        DealSnapshot message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<DealScoreWorker>().HandleAsync(message, ct);
}
