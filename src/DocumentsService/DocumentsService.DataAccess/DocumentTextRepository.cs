using DocumentsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsService.DataAccess;

public class DocumentTextRepository(DocumentsDbContext db) : IDocumentTextRepository
{
    public Task<DocumentText?> GetByDocumentIdAsync(string documentId, CancellationToken ct = default) =>
        db.DocumentTexts.FirstOrDefaultAsync(t => t.DocumentId == documentId, ct);

    public async Task UpsertAsync(DocumentText text, CancellationToken ct = default)
    {
        var existing = await db.DocumentTexts.FirstOrDefaultAsync(t => t.DocumentId == text.DocumentId, ct);
        if (existing is null)
        {
            db.DocumentTexts.Add(text);
        }
        else
        {
            existing.Text = text.Text;
            existing.Status = text.Status;
            existing.Error = text.Error;
            existing.ExtractedAt = text.ExtractedAt;
            existing.PageCount = text.PageCount;
        }
        await db.SaveChangesAsync(ct);
    }
}
