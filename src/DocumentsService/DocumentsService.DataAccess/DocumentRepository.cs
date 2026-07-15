using DocumentsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsService.DataAccess;

public class DocumentRepository(DocumentsDbContext db) : IDocumentRepository
{
    public Task<DocumentRecord?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.DocumentRecords.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<DocumentRecord?> GetByStorageKeyAsync(string storageKey, CancellationToken ct = default) =>
        db.DocumentRecords.FirstOrDefaultAsync(d => d.StorageKey == storageKey, ct);

    public async Task<DocumentRecord> CreateAsync(DocumentRecord record, CancellationToken ct = default)
    {
        record.Id = Guid.NewGuid().ToString();
        db.DocumentRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public Task UpdateAsync(DocumentRecord record, CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
