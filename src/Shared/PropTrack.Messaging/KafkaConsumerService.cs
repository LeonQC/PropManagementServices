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
///
/// <para>A handler that throws is retried a few times with backoff. A message that still
/// fails, or that cannot be deserialized at all, is moved to the dead-letter topic
/// (<see cref="DeadLetter.TopicFor"/>) and committed, so one bad message cannot block the
/// partition behind it.</para>
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

        await EnsureDeadLetterTopicAsync(stoppingToken);

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        using var deadLetters = new DeadLetterPublisher(_settings);
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

                // A null value is a tombstone, not a broken message: there is nothing to
                // handle and nothing worth dead-lettering.
                if (result.Message.Value is not null)
                {
                    var (failure, attempts) = TryDeserialize(result, out var message) is { } malformed
                        ? (malformed, 0)
                        : await HandleWithRetriesAsync(message, result, stoppingToken);

                    if (failure is not null &&
                        !await TryDeadLetterAsync(deadLetters, result, failure, attempts, stoppingToken))
                    {
                        // Neither handled nor safely parked, so it must not be committed.
                        // Rewind and start over on this message after a pause.
                        Rewind(consumer, result);
                        await Task.Delay(RetryBackoff(1), stoppingToken);
                        continue;
                    }
                }

                // Reached once the message is handled, or is safely in the dead-letter topic.
                Commit(consumer, result);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown. Whatever was in flight is uncommitted and will be redelivered.
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>Returns the reason the payload cannot be turned into a <typeparamref name="T"/>,
    /// or null when <paramref name="message"/> is ready to handle.</summary>
    private static Exception? TryDeserialize(ConsumeResult<string, string> result, out T message)
    {
        message = default!;
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(result.Message.Value, JsonOptions);
            if (parsed is null) return new JsonException("Payload deserialized to null.");
            message = parsed;
            return null;
        }
        catch (JsonException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Calls the handler up to <see cref="KafkaSettings.ConsumerMaxAttempts"/> times, pausing
    /// between attempts. Returns a null failure on success, otherwise the last exception.
    ///
    /// <para>The retries happen here, in memory, with the partition waiting — which is what
    /// keeps messages in order. That is only acceptable because the attempts are few and the
    /// pauses short; a message that needs longer than that is the dead-letter topic's job.</para>
    /// </summary>
    private async Task<(Exception? Failure, int Attempts)> HandleWithRetriesAsync(
        T message, ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        var maxAttempts = Math.Max(1, _settings.ConsumerMaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await HandleAsync(message, scope.ServiceProvider, stoppingToken);
                return (null, attempt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down mid-handling. Leave the offset uncommitted so the message
                // is redelivered to whichever consumer picks the partition up.
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogError(ex,
                        "Giving up on message on {Topic} at offset {Offset} after {Attempts} attempt(s)",
                        _topic, result.Offset.Value, attempt);
                    return (ex, attempt);
                }

                var delay = RetryBackoff(attempt);
                _logger.LogWarning(ex,
                    "Attempt {Attempt} of {MaxAttempts} failed on {Topic} at offset {Offset}; retrying in {Delay}",
                    attempt, maxAttempts, _topic, result.Offset.Value, delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    /// <summary>The pause after the given failed attempt: the base backoff, doubling each time.</summary>
    private TimeSpan RetryBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Max(0, _settings.ConsumerRetryBackoffMs) * Math.Pow(2, attempt - 1));

    private async Task<bool> TryDeadLetterAsync(
        DeadLetterPublisher deadLetters, ConsumeResult<string, string> result, Exception failure,
        int attempts, CancellationToken stoppingToken)
    {
        try
        {
            await deadLetters.PublishAsync(result, _groupId, failure, attempts, stoppingToken);
            _logger.LogWarning(
                "Dead-lettered message from {Topic} at offset {Offset} to {DeadLetterTopic}: {Error}",
                _topic, result.Offset.Value, DeadLetter.TopicFor(_topic), failure.Message);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not dead-letter message from {Topic} at offset {Offset}; it stays uncommitted",
                _topic, result.Offset.Value);
            return false;
        }
    }

    /// <summary>
    /// Declares this consumer's dead-letter topic before it can be needed. The provisioner is
    /// looked up rather than injected so the eight existing consumers keep their constructors;
    /// every service registers it through AddKafkaMessaging. Without it the broker still
    /// auto-creates the topic on first use, just with default retention.
    /// </summary>
    private async Task EnsureDeadLetterTopicAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var provisioner = scope.ServiceProvider.GetService<TopicProvisioner>();
        if (provisioner is not null) await provisioner.EnsureDeadLetterTopicAsync(_topic, ct: ct);
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
