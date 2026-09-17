using AiService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace AiService.Business.Workers;

/// <summary>
/// Watches deals for a score that has drifted away from the prose describing it, by consuming
/// deal.snapshot. Runs under its own consumer group so a regenerate-everything replay does not
/// rewind whatever this service consumes next.
///
/// <para>The base class auto-commits offsets and swallows handler exceptions, so a message that
/// fails is dropped rather than retried. ModelCallPolicy covers transient faults inside the
/// handler, and beyond that the recovery path is cheap and already exists:
/// <c>POST /deals/v1/deals/republish</c> re-emits every snapshot, and deals whose prose is
/// still current cost one indexed lookup each.</para>
/// </summary>
public sealed class DealRationaleConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealRationaleConsumer> logger)
    : KafkaConsumerService<DealSnapshot>(
        settings, Topics.DealSnapshot, scopeFactory, logger, ConsumerGroups.DealSnapshot)
{
    protected override Task HandleAsync(
        DealSnapshot message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<DealRationaleWorker>().HandleAsync(message, ct);
}
