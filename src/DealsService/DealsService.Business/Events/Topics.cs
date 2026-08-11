namespace DealsService.Business.Events;

/// <summary>
/// Kafka topic names for the deals domain (architecture §2.3). All seven spec'd
/// topics are declared; only CommentAdded is still unpublished. Consumers arrive
/// with the ai-service — the events are emitted now so nothing has to be
/// backfilled later.
/// </summary>
public static class Topics
{
    public const string DealCreated = "deal.created";
    public const string DealStageChanged = "deal.stage_changed";
    public const string DealUpdated = "deal.updated";
    public const string DealTaskCompleted = "deal.task_completed";
    public const string DealDocumentUploaded = "deal.document_uploaded";
    public const string DealCommentAdded = "deal.comment_added";
    public const string DealOutcomeRecorded = "deal.outcome_recorded";

    /// <summary>
    /// Event-carried state transfer for the search index, not a business event: the whole
    /// searchable projection of a deal, keyed by deal id, republished on every mutation.
    ///
    /// The topic must be <c>cleanup.policy=compact</c> so the log retains the latest message
    /// per key indefinitely — that is what lets search-service rebuild its entire index by
    /// replaying from the beginning, with this service and its database offline. Kafka's
    /// auto-create would give it the default <c>delete</c> policy and silently destroy that
    /// guarantee, so MessagingStartup provisions it explicitly at startup.
    /// </summary>
    public const string DealSnapshot = "deal.snapshot";
}
