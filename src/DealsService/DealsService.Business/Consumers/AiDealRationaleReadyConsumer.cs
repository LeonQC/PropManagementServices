using DealsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace DealsService.Business.Consumers;

/// <summary>ai.deal_rationale_ready → store the prose explaining a deal's score.</summary>
public sealed class AiDealRationaleReadyConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<AiDealRationaleReadyConsumer> logger)
    : KafkaConsumerService<AiDealRationaleReady>(
        settings, Topics.AiDealRationaleReady, scopeFactory, logger, ConsumerGroups.AiDealRationaleReady)
{
    protected override Task HandleAsync(AiDealRationaleReady message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<DealService>()
            .ApplyAiRationaleAsync(message.DealId, message.Rationale, ct);
}
