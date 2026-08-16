using AiService.Models;
using Microsoft.EntityFrameworkCore;

namespace AiService.DataAccess;

public interface IPromptTemplateRepository
{
    /// <summary>The active prompt for a feature, or null when none is seeded.</summary>
    Task<PromptTemplate?> GetActiveAsync(string feature, CancellationToken ct = default);
}

public class PromptTemplateRepository(AiDbContext db) : IPromptTemplateRepository
{
    public Task<PromptTemplate?> GetActiveAsync(string feature, CancellationToken ct = default) =>
        db.PromptTemplates
            .AsNoTracking()
            .Where(t => t.Feature == feature && t.IsActive)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
}
