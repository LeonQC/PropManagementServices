using DealsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DealsService.DataAccess;

public class DealCommentRepository(DealsDbContext db) : IDealCommentRepository
{
    public Task<List<DealComment>> GetByDealAsync(string dealId, CancellationToken ct = default)
        => db.DealComments
            .Where(c => c.DealId == dealId)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public async Task<DealComment> CreateAsync(DealComment comment, CancellationToken ct = default)
    {
        comment.Id = Guid.NewGuid().ToString();
        db.DealComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }
}
