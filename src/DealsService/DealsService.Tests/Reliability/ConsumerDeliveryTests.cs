using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PropTrack.Messaging;
using Xunit;

namespace DealsService.Tests.Reliability;

/// <summary>
/// Delivery guarantees of the shared <see cref="KafkaConsumerService{T}"/>, slices 0 to 2 of
/// docs/kafka-reliability-plan.md. Each test uses its own topic and consumer group, so they
/// cannot see each other's messages or offsets.
/// </summary>
[Collection(nameof(KafkaPostgresCollection))]
[Trait("Category", "Integration")]
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

    [Fact]
    public async Task A_poison_message_is_dead_lettered_and_the_next_message_is_processed()
    {
        fixture.Require();
        var (topic, group) = await PublishOnFreshTopic(new Ping("poison"), new Ping("healthy"));

        var poisonAttempts = 0;
        var processed = NewSignal();
        var consumer = NewConsumer(topic, group, (ping, _) =>
        {
            if (ping.Id == "poison")
            {
                Interlocked.Increment(ref poisonAttempts);
                throw new InvalidOperationException("This message can never be handled.");
            }
            processed.TrySetResult(ping);
            return Task.CompletedTask;
        });

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var winner = await Task.WhenAny(processed.Task, Task.Delay(Patience));
            Assert.True(winner == processed.Task,
                $"The message behind the poison one was never processed ({poisonAttempts} attempt(s) " +
                "on the poison message): one bad message is blocking the partition.");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        Assert.Equal(fixture.Kafka.ConsumerMaxAttempts, poisonAttempts);

        var dead = Assert.Single(fixture.ReadByKey(topic + ".dlt", "poison", Patience));
        Assert.Equal("{\"id\":\"poison\"}", dead.Value);
        Assert.Equal(topic, Header(dead, "dlt.original-topic"));
        Assert.Equal("0", Header(dead, "dlt.original-partition"));
        Assert.Equal("0", Header(dead, "dlt.original-offset"));
        Assert.Equal(group, Header(dead, "dlt.consumer-group"));
        Assert.Equal(fixture.Kafka.ConsumerMaxAttempts.ToString(), Header(dead, "dlt.attempts"));
        Assert.Equal(typeof(InvalidOperationException).FullName, Header(dead, "dlt.error-type"));
        Assert.Equal("This message can never be handled.", Header(dead, "dlt.error-message"));

        // Provisioned by the consumer rather than auto-created, so it never expires messages.
        Assert.Equal("-1", await fixture.TopicConfigAsync(topic + ".dlt", "retention.ms"));
    }

    [Fact]
    public async Task A_malformed_message_goes_straight_to_the_dead_letter_topic()
    {
        fixture.Require();
        var topic = $"test.ping.{Guid.NewGuid():N}";
        var group = $"test-group-{Guid.NewGuid():N}";
        await fixture.ProduceRawAsync(topic, "garbage", "{ this is not json");
        await fixture.ProduceRawAsync(topic, "healthy", "{\"id\":\"healthy\"}");

        var handled = new List<string>();
        var processed = NewSignal();
        var consumer = NewConsumer(topic, group, (ping, _) =>
        {
            lock (handled) handled.Add(ping.Id);
            processed.TrySetResult(ping);
            return Task.CompletedTask;
        });

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await Task.WhenAny(processed.Task, Task.Delay(Patience)) == processed.Task,
                "The message behind the malformed one was never processed.");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        // The handler never saw the malformed payload: there was nothing to hand it.
        Assert.Equal(["healthy"], handled);

        var dead = fixture.ReadByKey(topic + ".dlt", "garbage", Patience);
        Assert.True(dead.Count == 1,
            "The malformed message was skipped without a trace instead of being dead-lettered.");
        Assert.Equal("{ this is not json", dead[0].Value);
        Assert.Equal("0", Header(dead[0], "dlt.attempts"));
        Assert.Equal(topic, Header(dead[0], "dlt.original-topic"));
    }

    private static string? Header(Message<string, string> message, string name) =>
        message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    /// <summary>Publishes before any consumer subscribes, so the topic already exists and
    /// the consumer starts from its first offset rather than waiting on a metadata refresh.</summary>
    private async Task<(string Topic, string Group)> PublishOnFreshTopic(params Ping[] pings)
    {
        var topic = $"test.ping.{Guid.NewGuid():N}";
        using var publisher = new KafkaEventPublisher(fixture.Kafka, NullLogger<KafkaEventPublisher>.Instance);
        foreach (var ping in pings) await publisher.PublishAsync(topic, ping.Id, ping);
        return (topic, $"test-group-{Guid.NewGuid():N}");
    }

    private TestConsumer NewConsumer(string topic, string group, Func<Ping, CancellationToken, Task> handler) =>
        new(fixture.Kafka, topic, group, handler);

    private static TaskCompletionSource<Ping> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wired like a real service: the provider holds what AddKafkaMessaging would
    /// register, so the consumer can provision its dead-letter topic.</summary>
    private sealed class TestConsumer(
        KafkaSettings settings, string topic, string group, Func<Ping, CancellationToken, Task> handler)
        : KafkaConsumerService<Ping>(
            settings, topic,
            new ServiceCollection()
                .AddSingleton(new TopicProvisioner(settings, NullLogger<TopicProvisioner>.Instance))
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance, group)
    {
        protected override Task HandleAsync(Ping message, IServiceProvider services, CancellationToken ct) =>
            handler(message, ct);
    }
}
