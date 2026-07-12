using DealsService.Models;

namespace DealsService.DataAccess;

public interface IDealTaskRepository
{
    Task<List<DealTask>> GetByDealAsync(string dealId, CancellationToken ct = default);
    Task<DealTask?> GetByIdAsync(string dealId, string taskId, CancellationToken ct = default);
    Task<DealTask> CreateAsync(DealTask task, CancellationToken ct = default);
    Task UpdateAsync(DealTask task, CancellationToken ct = default);
}
