using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropTrack.Messaging;
using Xunit;

namespace DealsService.Tests.Reliability;

/// <summary>
/// Delivery guarantees of the shared <see cref="KafkaConsumerService{T}"/>, slices 0 and 1 of
/// docs/kafka-reliability-plan.md. Each test uses its own topic and consumer group, so they
/// cannot see each other's messages or offsets.
/// </summary>
[Collection(nameof(KafkaPostgresCollection))]
public class ConsumerDeliveryTests(KafkaPostgresFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public record Ping(string Id);

    [Fact]
    public async Task A_message_whose_handler_throws_once_is_still_processed()
    {
        fixture.Require();
        var (topic, group) = await PublishOnFreshTopic(new Ping("p-1"));

        var attempts = 0;
        var processed = NewSignal();
        var consumer = NewConsumer(topic, group, (ping, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("Simulated transient failure.");
            processed.TrySetResult(ping);
            return Task.CompletedTask;
        });

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var winner = await Task.WhenAny(processed.Task, Task.Delay(Patience));
            Assert.True(winner == processed.Task,
                $"The handler threw on attempt 1 and was never called again ({attempts} attempt(s)): " +
                "the message was dropped instead of redelivered.");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_message_in_flight_when_the_consumer_stops_is_redelivered()
    {
        fixture.Require();
        var (topic, group) = await PublishOnFreshTopic(new Ping("p-1"));

        // First consumer: picks the message up and is stopped while still handling it, which
        // is as close to a crash mid-handling as one process can get.
        var started = NewSignal();
        var first = NewConsumer(topic, group, async (ping, ct) =>
        {
            started.TrySetResult(ping);
            await Task.Delay(Timeout.Infinite, ct);
        });
        await first.StartAsync(CancellationToken.None);
        Assert.True(await Task.WhenAny(started.Task, Task.Delay(Patience)) == started.Task,
            "Precondition: the first consumer never received the message.");
        await first.StopAsync(CancellationToken.None);

        // Second consumer, same group: resumes from whatever offset the first one committed.
        var redelivered = NewSignal();
        var second = NewConsumer(topic, group, (ping, _) =>
        {
            redelivered.TrySetResult(ping);
            return Task.CompletedTask;
        });
        await second.StartAsync(CancellationToken.None);
        try
        {
            var winner = await Task.WhenAny(redelivered.Task, Task.Delay(Patience));
            Assert.True(winner == redelivered.Task,
                "The first consumer stopped before its handler finished, yet the offset was " +
                "committed: the replacement consumer never saw the message.");
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Publishes before any consumer subscribes, so the topic already exists and
    /// the consumer starts from its first offset rather than waiting on a metadata refresh.</summary>
    private async Task<(string Topic, string Group)> PublishOnFreshTopic(Ping ping)
    {
        var topic = $"test.ping.{Guid.NewGuid():N}";
        using var publisher = new KafkaEventPublisher(fixture.Kafka, NullLogger<KafkaEventPublisher>.Instance);
        await publisher.PublishAsync(topic, ping.Id, ping);
        return (topic, $"test-group-{Guid.NewGuid():N}");
    }

    private TestConsumer NewConsumer(string topic, string group, Func<Ping, CancellationToken, Task> handler) =>
        new(fixture.Kafka, topic, group, handler);

    private static TaskCompletionSource<Ping> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestConsumer(
        KafkaSettings settings, string topic, string group, Func<Ping, CancellationToken, Task> handler)
        : KafkaConsumerService<Ping>(
            settings, topic,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance, group)
    {
        protected override Task HandleAsync(Ping message, IServiceProvider services, CancellationToken ct) =>
            handler(message, ct);
    }
}
