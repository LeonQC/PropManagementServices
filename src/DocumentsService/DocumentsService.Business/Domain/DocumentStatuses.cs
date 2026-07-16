namespace DocumentsService.Business.Domain;

/// <summary>Lifecycle of a document_records row.</summary>
public static class DocumentStatuses
{
    /// <summary>Upload URL issued; bytes not yet confirmed in storage.</summary>
    public const string Pending = "pending";

    /// <summary>Upload confirmed; blob exists and is downloadable.</summary>
    public const string Active = "active";

    /// <summary>Soft-deleted; blob marked for deletion but not yet removed.</summary>
    public const string Deleted = "deleted";
}

/// <summary>Lifecycle of a document_text row.</summary>
public static class TextExtractionStatuses
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string SkippedNotPdf = "skipped_not_pdf";
}
