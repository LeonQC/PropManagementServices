using DealsService.Models;

namespace DealsService.DataAccess;

public interface IDealDocumentRepository
{
    Task<List<DealDocument>> GetByDealAsync(string dealId, CancellationToken ct = default);
    Task<DealDocument> CreateAsync(DealDocument document, CancellationToken ct = default);
}
