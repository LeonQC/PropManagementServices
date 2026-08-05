using Microsoft.AspNetCore.Mvc;
using SearchService.Business;

namespace SearchService.Api.Controllers;

[ApiController]
[Route("search/v1")]
public class HealthController(PropertyIndexingService indexingService) : ControllerBase
{
    // GET /search/v1/health — index reachability + live document count. The count is what you
    // compare against the listings row count after a backfill.
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var count = await indexingService.GetIndexedCountAsync(ct);
        return count < 0
            ? StatusCode(503, new { status = "unavailable", indexedProperties = (long?)null })
            : Ok(new { status = "ok", indexedProperties = count });
    }
}
