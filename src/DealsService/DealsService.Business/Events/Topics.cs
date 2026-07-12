namespace DealsService.Business.Events;

/// <summary>
/// Kafka topic names for the deals domain (architecture §2.3). All seven spec'd
/// topics are declared; only Created, StageChanged and OutcomeRecorded are
/// published today — the rest have no consumers yet.
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
}
