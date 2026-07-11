namespace DealsService.Business.Events;

// Outbound event payloads. Serialized camelCase by the shared publisher
// (JsonSerializerDefaults.Web). DealCreated and DealOutcomeRecorded must stay
// field-compatible with listings-service's InboundEvents — it consumes both.

public record DealCreated(
    string PropertyId,
    string DealId);

public record DealStageChanged(
    string DealId,
    string PropertyId,
    string? FromStage,
    string ToStage,
    string ChangedById,
    string ChangedAt,
    string? Reason,
    int? DaysInPriorStage);

/// <summary>Terminal outcome. Listings maps "won"/"closed_won" to acquired and
/// anything else back to listed.</summary>
public record DealOutcomeRecorded(
    string PropertyId,
    string DealId,
    string Outcome);
