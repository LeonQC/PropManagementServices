namespace DocumentsService.Models;

/// <summary>
/// Extracted PDF text, kept out of document_records so the main table stays lean
/// (text blobs are large and rarely read alongside the metadata).
/// </summary>
public class DocumentText
{
    public required string DocumentId { get; set; }
    public string? Text { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
    public string? ExtractedAt { get; set; }
    public int? PageCount { get; set; }
}
