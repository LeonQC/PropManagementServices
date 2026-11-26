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

    /// <summary>
    /// How many times a consumer calls its handler for one message before giving up and
    /// moving the message to the dead-letter topic. 1 means no retries.
    /// </summary>
    public int ConsumerMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Pause before the first retry, in milliseconds. It doubles on each further attempt, so
    /// the defaults wait 1s then 2s: long enough to ride out a blip, short enough that a
    /// poison message holds its partition up for seconds rather than minutes.
    /// </summary>
    public int ConsumerRetryBackoffMs { get; set; } = 1000;
}
