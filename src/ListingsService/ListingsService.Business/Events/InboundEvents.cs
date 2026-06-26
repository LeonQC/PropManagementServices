namespace ListingsService.Business.Events;

// Inbound event payloads consumed by listings-service. Each carries only the
// fields this service acts on — a deliberately narrow view of the producer's
// contract so the services stay loosely coupled.

public record DealCreated(
    string PropertyId,
    string DealId);

/// <summary>
/// Recorded deal outcome. <see cref="Outcome"/> drives the resulting property
/// status: "won"/"closed_won" -> acquired, anything else -> back to listed.
/// </summary>
public record DealOutcomeRecorded(
    string PropertyId,
    string DealId,
    string Outcome);

public record AiPropertySummaryReady(
    string PropertyId,
    string Summary);
