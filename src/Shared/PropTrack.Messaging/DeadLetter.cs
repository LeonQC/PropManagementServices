namespace PropTrack.Messaging;

/// <summary>
/// Naming for dead-letter topics and the headers a dead letter carries. Public so that
/// tooling outside this library (a replay script, a dashboard) reads the same names.
/// </summary>
public static class DeadLetter
{
    /// <summary>
    /// One dead-letter topic per source topic, shared by every consumer group that reads it;
    /// <see cref="ConsumerGroup"/> says whose failure a given dead letter is.
    /// </summary>
    public static string TopicFor(string topic) => topic + ".dlt";

    public const string OriginalTopic = "dlt.original-topic";
    public const string OriginalPartition = "dlt.original-partition";
    public const string OriginalOffset = "dlt.original-offset";
    public const string ConsumerGroup = "dlt.consumer-group";

    /// <summary>Handler invocations before giving up; 0 for a message that could not be
    /// deserialized and so never reached the handler.</summary>
    public const string Attempts = "dlt.attempts";

    public const string ErrorType = "dlt.error-type";
    public const string ErrorMessage = "dlt.error-message";
    public const string FailedAt = "dlt.failed-at";
}
