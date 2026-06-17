using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropTrack.Messaging;

/// <summary>
/// Base class for a background Kafka consumer of a single topic. Subclasses set the
/// topic and implement <see cref="HandleAsync"/>; this base owns the consume loop,
/// JSON deserialization, per-message DI scoping, and error isolation.
/// </summary>
/// <typeparam name="T">The event payload type carried by the topic.</typeparam>
public abstract class KafkaConsumerService<T> : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KafkaSettings _settings;
    private readonly string _topic;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected KafkaConsumerService(
        KafkaSettings settings,
        string topic,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        _settings = settings;
        _topic = topic;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Handle one deserialized message. <paramref name="services"/> is a fresh DI
    /// scope — resolve scoped services (e.g. the domain service) from it.
    /// </summary>
    protected abstract Task HandleAsync(T message, IServiceProvider services, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Confluent's Consume() is blocking, so run the loop on a dedicated thread
        // rather than tying up the host startup path.
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_topic);
        _logger.LogInformation("Subscribed to {Topic} as group {Group}", _topic, _settings.ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume error on {Topic}", _topic);
                    continue;
                }

                if (result?.Message?.Value is null) continue;

                try
                {
                    var message = JsonSerializer.Deserialize<T>(result.Message.Value, JsonOptions);
                    if (message is null)
                    {
                        _logger.LogWarning("Null payload on {Topic} at offset {Offset}", _topic, result.Offset.Value);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    await HandleAsync(message, scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Log and move on so one poison message can't stall the topic.
                    _logger.LogError(ex, "Failed handling message on {Topic} at offset {Offset}",
                        _topic, result.Offset.Value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close();
        }
    }
}
