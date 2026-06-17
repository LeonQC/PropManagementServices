using ListingsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace ListingsService.Business.Consumers;

/// <summary>deal.outcome_recorded → acquire the property or return it to listed.</summary>
public sealed class DealOutcomeRecordedConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<DealOutcomeRecordedConsumer> logger)
    : KafkaConsumerService<DealOutcomeRecorded>(settings, Topics.DealOutcomeRecorded, scopeFactory, logger)
{
    protected override Task HandleAsync(DealOutcomeRecorded message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<PropertyService>()
            .ApplyDealOutcomeAsync(message.PropertyId, message.DealId, message.Outcome, ct);
}
