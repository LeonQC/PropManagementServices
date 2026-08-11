using SearchService.Business.DTOs;
using SearchService.DataAccess;

namespace SearchService.Business;

/// <summary>
/// Searches every entity at once. The payoff of the shared document envelope: properties and
/// deals are ranked against each other in one result list, which is what a single search box
/// (and, later, a natural-language query) needs.
/// </summary>
public class CrossEntitySearchService(ICrossEntityIndex index)
{
    public async Task<(List<SearchHitDto> Items, int TotalCount)> SearchAsync(
        int page, int pageSize, string? q = null, string? entityType = null,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await index.SearchAsync(page, pageSize, q, entityType, ct);

        return (
            [.. items.Select(h => new SearchHitDto(h.EntityType, h.EntityId, h.Title, h.Snippet, h.Score))],
            (int)Math.Min(totalCount, int.MaxValue));
    }
}
