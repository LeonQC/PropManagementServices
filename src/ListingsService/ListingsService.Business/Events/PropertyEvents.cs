namespace ListingsService.Business.Events;

// Outbound event payloads published by listings-service. These are the wire
// contract other services consume — owned here, kept JSON-serializable.

public record PropertyCreated(
    string PropertyId,
    string Type,
    string? Subtype,
    string? MetroArea,
    double? AskingPrice,
    double? CapRate);

public record PropertyUpdated(
    string PropertyId,
    string[] ChangedFields,
    Dictionary<string, object?> OldValues,
    Dictionary<string, object?> NewValues);

public record PropertyStatusChanged(
    string PropertyId,
    string OldStatus,
    string NewStatus,
    string? DealId);
