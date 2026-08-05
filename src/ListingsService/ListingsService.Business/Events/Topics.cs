namespace ListingsService.Business.Events;

/// <summary>Kafka topic names this service produces to and consumes from.</summary>
public static class Topics
{
    // Published by listings-service
    public const string PropertyCreated = "property.created";
    public const string PropertyUpdated = "property.updated";
    public const string PropertyStatusChanged = "property.status_changed";

    /// <summary>
    /// Full searchable projection of a property, published on every mutation. Unlike the
    /// business events above (which describe *what changed*), this carries current state so
    /// a consumer can rebuild a derived store without calling back. Created with
    /// cleanup.policy=compact, so Kafka retains exactly one current message per property and
    /// replaying the topic reconstructs the whole search index. See EnsureTopicAsync — Kafka
    /// auto-creation would give this the default delete policy and silently break that.
    /// </summary>
    public const string PropertySnapshot = "property.snapshot";

    // Consumed by listings-service
    public const string DealCreated = "deal.created";
    public const string DealOutcomeRecorded = "deal.outcome_recorded";
    public const string AiPropertySummaryReady = "ai.property_summary_ready";
}
