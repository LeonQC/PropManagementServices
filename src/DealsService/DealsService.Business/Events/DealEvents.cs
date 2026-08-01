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

/// <summary>
/// Deal fields changed through the update endpoint (architecture §2.3). ChangedFields
/// carries the camelCase names that actually moved, so a consumer can decide whether
/// the change is worth acting on — §3.1 recalculates the score only when offerPrice or
/// projectedCapRate changed. Published only when something really changed, so an
/// idempotent PUT doesn't emit a no-op event.
/// </summary>
public record DealUpdated(
    string DealId,
    string PropertyId,
    IReadOnlyList<string> ChangedFields,
    string UpdatedAt);

/// <summary>A checklist task moved to Done. Time-based flags are computed on read, but
/// task completion is a real state change, so it gets an event.</summary>
public record DealTaskCompleted(
    string DealId,
    string TaskId,
    string Stage,
    string CompletedById,
    string CompletedAt);

/// <summary>Terminal outcome. Listings maps "won"/"closed_won" to acquired and
/// anything else back to listed.</summary>
public record DealOutcomeRecorded(
    string PropertyId,
    string DealId,
    string Outcome);

/// <summary>A document was attached to a deal. Consumed by documents-service,
/// which resolves its own record via StorageUrl (the UI writes the
/// "/documents/v1/{documentId}" pointer there) and runs PDF text extraction.
/// Must stay field-compatible with its DocumentsService.Business.Events twin.</summary>
public record DealDocumentUploaded(
    string DealId,
    string DocumentId,
    string FileName,
    string FileType,
    string? StorageUrl,
    string UploadedById,
    string UploadedAt);
