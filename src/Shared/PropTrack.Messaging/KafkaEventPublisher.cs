using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace PropTrack.Messaging;

/// <summary>
/// Confluent.Kafka-backed <see cref="IEventPublisher"/>. Holds one long-lived
/// producer (thread-safe, registered as a singleton) and JSON-serializes payloads.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(KafkaSettings settings, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            Acks = Acks.All
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
    {
        var value = JsonSerializer.Serialize(payload, JsonOptions);
        var result = await _producer.ProduceAsync(
            topic, new Message<string, string> { Key = key, Value = value }, ct);

        _logger.LogInformation(
            "Published {Topic} key={Key} to partition {Partition} offset {Offset}",
            topic, key, result.Partition.Value, result.Offset.Value);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
