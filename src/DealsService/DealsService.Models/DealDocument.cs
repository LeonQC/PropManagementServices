namespace DealsService.Models;

/// <summary>
/// Document metadata only — the file itself lives in blob storage owned by a
/// future documents-service. StorageUrl is a pointer, not a managed upload.
/// </summary>
public class DealDocument
{
    public required string Id { get; set; }
    public required string DealId { get; set; }
    public required string FileName { get; set; }
    public required string FileType { get; set; }
    public string? StorageUrl { get; set; }
    public string? AiSummary { get; set; }
    public required string UploadedById { get; set; }
    public required string UploadedAt { get; set; }
}
