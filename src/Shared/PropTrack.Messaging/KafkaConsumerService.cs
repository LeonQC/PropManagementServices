using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropTrack.Messaging;

/// <summary>
/// Base class for a background Kafka consumer of a single topic. Subclasses set the
/// topic and implement <see cref="HandleAsync"/>; this base owns the consume loop,
/// JSON deserialization, per-message DI scoping, and offset commits. Delivery is
/// at-least-once: an offset is committed only after its message has been handled, so
/// handlers must tolerate seeing the same message twice.
/// </summary>
/// <typeparam name="T">The event payload type carried by the topic.</typeparam>
public abstract class KafkaConsumerService<T> : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KafkaSettings _settings;
    private readonly string _topic;
    private readonly string _groupId;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    /// <summary>Pause before re-reading a message whose handler threw.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <param name="groupId">
    /// Overrides the service-wide <see cref="KafkaSettings.ConsumerGroupId"/>. Pass one when a
    /// service hosts several consumers: members of a single group with different subscriptions
    /// rebalance against each other for no reason, and — more practically — offsets are
    /// committed per group, so replaying one topic from the beginning would rewind the others
    /// too. Defaults to the service-wide group.
    /// </param>
    protected KafkaConsumerService(
        KafkaSettings settings,
        string topic,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string? groupId = null)
    {
        _settings = settings;
        _topic = topic;
        _groupId = groupId ?? settings.ConsumerGroupId;
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
            GroupId = _groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // At-least-once: the offset is committed by hand, and only after HandleAsync has
            // returned. With auto-commit the client commits on a timer (and again on Close)
            // whatever the handler is doing, so a crash mid-handling skips the message.
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_topic);
        _logger.LogInformation("Subscribed to {Topic} as group {Group}", _topic, _groupId);

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

                if (result?.Message is null) continue;

                if (TryDeserialize(result, out var message))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await HandleAsync(message, scope.ServiceProvider, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Shutting down mid-handling. Leave the offset uncommitted so the
                        // message is redelivered to whichever consumer picks the partition up.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Not committed, so not lost: rewind to this message and try it again
                        // after a pause. A handler that never succeeds blocks its partition
                        // here; bounding the retries and dead-lettering is the next step.
                        _logger.LogError(ex,
                            "Failed handling message on {Topic} at offset {Offset}; retrying in {Delay}",
                            _topic, result.Offset.Value, RetryDelay);
                        Rewind(consumer, result);
                        await Task.Delay(RetryDelay, stoppingToken);
                        continue;
                    }
                }

                // Reached for handled messages and for ones that can never be handled
                // (null or malformed payloads, which a retry would not fix).
                Commit(consumer, result);
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

    private bool TryDeserialize(ConsumeResult<string, string> result, out T message)
    {
        message = default!;
        if (result.Message.Value is null) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(result.Message.Value, JsonOptions);
            if (parsed is null)
            {
                _logger.LogWarning("Null payload on {Topic} at offset {Offset}", _topic, result.Offset.Value);
                return false;
            }

            message = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Skipping undeserializable message on {Topic} at offset {Offset}",
                _topic, result.Offset.Value);
            return false;
        }
    }

    private void Commit(IConsumer<string, string> consumer, ConsumeResult<string, string> result)
    {
        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            // Typically a rebalance took the partition away. The work is done but unrecorded,
            // so the message will be delivered again — the "at least" in at-least-once.
            _logger.LogWarning(ex, "Offset commit failed on {Topic} at offset {Offset}; expect a redelivery",
                _topic, result.Offset.Value);
        }
    }

    private void Rewind(IConsumer<string, string> consumer, ConsumeResult<string, string> result)
    {
        try
        {
            consumer.Seek(result.TopicPartitionOffset);
        }
        catch (KafkaException ex)
        {
            // The partition was revoked meanwhile; its new owner resumes from the last
            // committed offset, which is still before this message.
            _logger.LogWarning(ex, "Could not rewind {Topic} to offset {Offset}", _topic, result.Offset.Value);
        }
    }
}
