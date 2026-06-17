namespace PropTrack.Messaging;

/// <summary>
/// Publishes a domain event to a Kafka topic. The payload is serialized to JSON.
/// Services depend on this abstraction; the Kafka implementation stays in this
/// shared infrastructure library (interface lives with its impl, N-tier style).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default);
}
