using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SearchService.Api.DTOs;
using SearchService.Api.Infrastructure;
using SearchService.Business;

namespace SearchService.Api.Controllers;

/// <summary>
/// Cross-entity search: one query, one ranked list, properties and deals together. This is
/// what the shared document envelope was for — the per-entity endpoints answer their own
/// service's contract, and this one answers a search box.
///
/// Authenticated and enveloped, because the results can include deals. Applying the stricter
/// of the two entities' postures is the only safe default for a mixed result set.
/// </summary>
[ApiController]
[Authorize]
[Route("search/v1/all")]
public class SearchController(CrossEntitySearchService service) : ApiControllerBase
{
    /// <param name="entityType">Narrow to one type ("property" or "deal"). Omit for both.</param>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? entityType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, totalCount) = await service.SearchAsync(page, pageSize, q, entityType, ct);

        return Success(new PaginatedResponse<SearchHitResponse>(
            items.Select(h => new SearchHitResponse(h.EntityType, h.EntityId, h.Title, h.Snippet, h.Score))
                .ToList(),
            totalCount, page, pageSize));
    }
}
