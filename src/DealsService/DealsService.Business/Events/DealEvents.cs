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

/// <summary>
/// The whole searchable projection of a deal, published on every mutation — event-carried
/// state transfer rather than a business event, and the only payload search-service needs to
/// build its index. It must not pick up the changed-fields gate <see cref="DealUpdated"/>
/// applies: a no-op edit publishes nothing because nothing changed, but every real write
/// republishes in full, whatever moved.
///
/// <para><see cref="Version"/> is the OpenSearch external version. UpdatedAt can't serve —
/// it's a string, and task/comment/document writes leave it untouched while still changing
/// this projection.</para>
///
/// <para><see cref="Deleted"/> is always false today: deals are never deleted, and terminal
/// (Acquired/Dead) deals stay listable, so they stay indexed. The field exists for symmetry
/// with property.snapshot, and because a delete has to be expressed as a flag on a normal
/// message rather than a null-valued Kafka tombstone — the shared KafkaConsumerService skips
/// null message values, so a tombstone would be swallowed without a trace.</para>
///
/// <para><see cref="EarliestOpenTaskDueDate"/>, <see cref="StageDwellAverageDays"/> and
/// <see cref="StageDwellSampleCount"/> are the raw inputs behind hasOverdueTasks and the
/// stale-stage health flag. Both derived values move with the clock and have no event to
/// hang a reindex on, so the consumer re-derives them per query from these.</para>
/// </summary>
public record DealSnapshot(
    string DealId,
    long Version,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    string Stage,
    string Priority,
    string OwnerId,
    string? DeadReason,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate,
    double? AiScore,
    string? AiScoreRationale,
    string? RiskFlags,
    string StageEnteredAt,
    string CreatedAt,
    string? UpdatedAt,
    int TaskCount,
    int DoneTaskCount,
    string? EarliestOpenTaskDueDate,
    double? StageDwellAverageDays,
    int StageDwellSampleCount,
    string? CommentText,
    string? DocumentText,
    bool Deleted);
