using ListingsService.Business.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropTrack.Messaging;

namespace ListingsService.Business.Consumers;

/// <summary>ai.property_summary_ready → write the generated summary back to the property.</summary>
public sealed class AiPropertySummaryReadyConsumer(
    KafkaSettings settings,
    IServiceScopeFactory scopeFactory,
    ILogger<AiPropertySummaryReadyConsumer> logger)
    : KafkaConsumerService<AiPropertySummaryReady>(settings, Topics.AiPropertySummaryReady, scopeFactory, logger)
{
    protected override Task HandleAsync(AiPropertySummaryReady message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<PropertyService>()
            .ApplyAiSummaryAsync(message.PropertyId, message.Summary, ct);
}
