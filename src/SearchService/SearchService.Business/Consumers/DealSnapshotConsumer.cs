using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;
using SearchService.Business.Events;

namespace SearchService.Business.Consumers;

/// <summary>
/// Keeps the deal index in step with deals-service by consuming deal.snapshot. Runs under its
/// own consumer group rather than the service-wide one, so rebuilding the deal index from the
/// start of the topic doesn't rewind the property index with it.
/// </summary>
public sealed class DealSnapshotConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealSnapshotConsumer> logger)
    : KafkaConsumerService<DealSnapshot>(
        settings, Topics.DealSnapshot, scopeFactory, logger, ConsumerGroups.DealSnapshot)
{
    protected override Task HandleAsync(
        DealSnapshot message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<DealIndexingService>().ApplySnapshotAsync(message, ct);
}
