namespace AiService.Business.Events;

/// <summary>Kafka topic names this service consumes and produces. Per house convention each
/// service declares its own copy rather than sharing a constants library.</summary>
public static class Topics
{
    /// <summary>
    /// Consumed. Owned and published by deals-service, compacted, keyed by deal id, and
    /// republished on every deal mutation — including comment, task and document writes.
    /// It carries every field the score needs, which is why the worker reads no API and
    /// holds no credential.
    /// </summary>
    public const string DealSnapshot = "deal.snapshot";

    /// <summary>
    /// Published. Consumed by deals-service, which stores the prose on the deal row.
    ///
    /// <para>Deliberately a plain event topic, not compacted: it is a stream of "this
    /// changed" notifications whose state lives on the deal, so there is nothing to
    /// rebuild by replaying it. That is why this service has no MessagingStartup shim —
    /// broker auto-creation gives the right cleanup policy here, unlike for the snapshot
    /// topics listings and deals own.</para>
    /// </summary>
    public const string AiDealRationaleReady = "ai.deal_rationale_ready";
}

/// <summary>Consumer group ids. One per topic rather than one per service: offsets commit per
/// group, so sharing a group would mean resetting one topic to earliest rewinds the other, and
/// members of a group with different subscriptions rebalance needlessly.</summary>
public static class ConsumerGroups
{
    /// <summary>Named explicitly rather than defaulting to the service-wide group in
    /// KafkaSettings, so a second consumer added later can be replayed on its own.</summary>
    public const string DealSnapshot = "ai-service-deals";
}
