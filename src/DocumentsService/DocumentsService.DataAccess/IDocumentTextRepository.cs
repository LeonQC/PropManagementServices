using DocumentsService.Models;

namespace DocumentsService.DataAccess;

public interface IDocumentTextRepository
{
    Task<DocumentText?> GetByDocumentIdAsync(string documentId, CancellationToken ct = default);
    Task UpsertAsync(DocumentText text, CancellationToken ct = default);
}
