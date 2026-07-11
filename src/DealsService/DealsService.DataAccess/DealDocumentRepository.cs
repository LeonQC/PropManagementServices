using DealsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DealsService.DataAccess;

public class DealDocumentRepository(DealsDbContext db) : IDealDocumentRepository
{
    public Task<List<DealDocument>> GetByDealAsync(string dealId, CancellationToken ct = default)
        => db.DealDocuments
            .Where(d => d.DealId == dealId)
            .OrderByDescending(d => d.UploadedAt).ThenBy(d => d.Id)
            .ToListAsync(ct);

    public async Task<DealDocument> CreateAsync(DealDocument document, CancellationToken ct = default)
    {
        document.Id = Guid.NewGuid().ToString();
        db.DealDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }
}
