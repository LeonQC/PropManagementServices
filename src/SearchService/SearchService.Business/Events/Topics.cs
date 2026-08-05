namespace SearchService.Business.Events;

/// <summary>Kafka topic names this service consumes. Per house convention each service
/// declares its own copy rather than sharing a constants library.</summary>
public static class Topics
{
    // Consumed by search-service. Compacted: replaying it from the beginning rebuilds the
    // whole index without listings-service (or its database) being reachable.
    public const string PropertySnapshot = "property.snapshot";
}
