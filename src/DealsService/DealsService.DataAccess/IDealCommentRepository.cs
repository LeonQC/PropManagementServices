using DealsService.Models;

namespace DealsService.DataAccess;

public interface IDealCommentRepository
{
    Task<List<DealComment>> GetByDealAsync(string dealId, CancellationToken ct = default);
    Task<DealComment> CreateAsync(DealComment comment, CancellationToken ct = default);
}
