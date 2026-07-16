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
