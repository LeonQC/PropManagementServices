using DocumentsService.Models;

namespace DocumentsService.DataAccess;

public interface IDocumentRepository
{
    Task<DocumentRecord?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<DocumentRecord?> GetByStorageKeyAsync(string storageKey, CancellationToken ct = default);
    Task<DocumentRecord> CreateAsync(DocumentRecord record, CancellationToken ct = default);
    Task UpdateAsync(DocumentRecord record, CancellationToken ct = default);
}
