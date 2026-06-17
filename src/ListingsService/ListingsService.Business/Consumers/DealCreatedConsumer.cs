using ListingsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace ListingsService.Business.Consumers;

/// <summary>deal.created → mark the property under_contract.</summary>
public sealed class DealCreatedConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealCreatedConsumer> logger)
    : KafkaConsumerService<DealCreated>(settings, Topics.DealCreated, scopeFactory, logger)
{
    protected override Task HandleAsync(DealCreated message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<PropertyService>()
            .ApplyDealCreatedAsync(message.PropertyId, message.DealId, ct);
}
