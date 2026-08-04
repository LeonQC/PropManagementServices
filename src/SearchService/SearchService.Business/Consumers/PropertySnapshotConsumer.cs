using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;
using SearchService.Business.Events;

namespace SearchService.Business.Consumers;

/// <summary>
/// Keeps the property index in step with listings-service by consuming property.snapshot.
/// Runs under search-service's own consumer group, so replaying the topic to rebuild the index
/// never disturbs any other service's offsets.
/// </summary>
public sealed class PropertySnapshotConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<PropertySnapshotConsumer> logger)
    : KafkaConsumerService<PropertySnapshot>(settings, Topics.PropertySnapshot, scopeFactory, logger)
{
    protected override Task HandleAsync(
        PropertySnapshot message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<PropertyIndexingService>().ApplySnapshotAsync(message, ct);
}
