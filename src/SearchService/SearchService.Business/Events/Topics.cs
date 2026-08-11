namespace SearchService.Business.Events;

/// <summary>Kafka topic names this service consumes. Per house convention each service
/// declares its own copy rather than sharing a constants library.</summary>
public static class Topics
{
    // Consumed by search-service. Compacted: replaying it from the beginning rebuilds the
    // whole index without listings-service (or its database) being reachable.
    public const string PropertySnapshot = "property.snapshot";

    // Same contract from deals-service. Consumed under its own group id (see
    // DealSnapshotConsumer) so the two indices can be rebuilt independently.
    public const string DealSnapshot = "deal.snapshot";
}

/// <summary>Consumer group ids. One per topic rather than one per service: offsets commit per
/// group, so sharing a group would mean resetting one topic to earliest rewinds the other's
/// index too — and members of a group with different subscriptions rebalance needlessly.</summary>
public static class ConsumerGroups
{
    /// <summary>Deals run apart from the service-wide group in KafkaSettings, which properties
    /// still use — keeping the existing group id preserves its committed offsets.</summary>
    public const string DealSnapshot = "search-service-deals";
}
