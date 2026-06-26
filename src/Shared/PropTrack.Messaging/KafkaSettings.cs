namespace PropTrack.Messaging;

/// <summary>
/// Kafka connection settings, bound from the "Kafka" configuration section.
/// Shared by every PropTrack service that talks to the broker.
/// </summary>
public class KafkaSettings
{
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated broker list, e.g. "localhost:29092" or "kafka:9092".</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Consumer group id — one per service so each service reads every topic once.</summary>
    public string ConsumerGroupId { get; set; } = "";
}
