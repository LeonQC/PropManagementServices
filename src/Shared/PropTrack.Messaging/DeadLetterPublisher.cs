using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace PropTrack.Messaging;

/// <summary>
/// Moves a message a consumer has given up on to its dead-letter topic, so the consumer can
/// commit past it and keep the partition moving without the message being lost.
///
/// <para>The key and value are copied byte for byte, and the original headers are kept, so a
/// dead letter can be replayed onto its source topic unchanged. What went wrong, and where
/// the message came from, travel as extra <c>dlt.*</c> headers rather than in a wrapper
/// around the payload.</para>
/// </summary>
internal sealed class DeadLetterPublisher(KafkaSettings settings) : IDisposable
{
    /// <summary>Kafka headers have no hard limit, but a stack-trace-sized error message
    /// does not belong in one.</summary>
    private const int MaxErrorMessageLength = 1000;

    private readonly IProducer<string, string> _producer = new ProducerBuilder<string, string>(
        new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();

    /// <summary>
    /// Returns once the broker has acknowledged the dead letter. Throws if it could not be
    /// written — the caller must then not commit the original message.
    /// </summary>
    /// <param name="attempts">How many times the handler ran; 0 when the message never
    /// reached it because it could not be deserialized.</param>
    public async Task PublishAsync(
        ConsumeResult<string, string> failed, string consumerGroup, Exception error, int attempts,
        CancellationToken ct)
    {
        var headers = new Headers();
        foreach (var header in failed.Message.Headers ?? [])
            headers.Add(header.Key, header.GetValueBytes());

        // Remove before adding: a message that was replayed from the dead-letter topic and
        // failed again already carries these, and the latest failure is the one that matters.
        Set(headers, DeadLetter.OriginalTopic, failed.Topic);
        Set(headers, DeadLetter.OriginalPartition, failed.Partition.Value.ToString(CultureInfo.InvariantCulture));
        Set(headers, DeadLetter.OriginalOffset, failed.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Set(headers, DeadLetter.ConsumerGroup, consumerGroup);
        Set(headers, DeadLetter.Attempts, attempts.ToString(CultureInfo.InvariantCulture));
        Set(headers, DeadLetter.ErrorType, error.GetType().FullName ?? error.GetType().Name);
        Set(headers, DeadLetter.ErrorMessage, Truncate(error.Message));
        Set(headers, DeadLetter.FailedAt, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await _producer.ProduceAsync(
            DeadLetter.TopicFor(failed.Topic),
            new Message<string, string> { Key = failed.Message.Key, Value = failed.Message.Value, Headers = headers },
            ct);
    }

    private static void Set(Headers headers, string name, string value)
    {
        headers.Remove(name);
        headers.Add(name, Encoding.UTF8.GetBytes(value));
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
