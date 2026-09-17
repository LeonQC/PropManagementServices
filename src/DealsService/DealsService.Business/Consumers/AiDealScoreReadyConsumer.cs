using DealsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace DealsService.Business.Consumers;

/// <summary>ai.deal_score_ready → write the score and rationale back onto the deal.</summary>
public sealed class AiDealScoreReadyConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<AiDealScoreReadyConsumer> logger)
    : KafkaConsumerService<AiDealScoreReady>(
        settings, Topics.AiDealScoreReady, scopeFactory, logger, ConsumerGroups.AiDealScoreReady)
{
    protected override Task HandleAsync(AiDealScoreReady message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<DealService>()
            .ApplyAiScoreAsync(message.DealId, message.Score, message.Rationale, ct);
}
