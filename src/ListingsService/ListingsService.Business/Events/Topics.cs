namespace ListingsService.Business.Events;

/// <summary>Kafka topic names this service produces to and consumes from.</summary>
public static class Topics
{
    // Published by listings-service
    public const string PropertyCreated = "property.created";
    public const string PropertyUpdated = "property.updated";
    public const string PropertyStatusChanged = "property.status_changed";

    // Consumed by listings-service
    public const string DealCreated = "deal.created";
    public const string DealOutcomeRecorded = "deal.outcome_recorded";
    public const string AiPropertySummaryReady = "ai.property_summary_ready";
}
