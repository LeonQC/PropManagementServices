using Microsoft.AspNetCore.Mvc;
using SearchService.Business;

namespace SearchService.Api.Controllers;

[ApiController]
[Route("search/v1")]
public class HealthController(
    PropertyIndexingService propertyIndexing, DealIndexingService dealIndexing) : ControllerBase
{
    // GET /search/v1/health — index reachability + live document counts. The counts are what
    // you compare against the source row counts after a backfill.
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var properties = await propertyIndexing.GetIndexedCountAsync(ct);
        var deals = await dealIndexing.GetIndexedCountAsync(ct);

        // Either index being unreachable makes the service unhealthy, but still report
        // whichever count did come back rather than blanking both.
        return properties < 0 || deals < 0
            ? StatusCode(503, new
            {
                status = "unavailable",
                indexedProperties = properties < 0 ? (long?)null : properties,
                indexedDeals = deals < 0 ? (long?)null : deals,
            })
            : Ok(new { status = "ok", indexedProperties = properties, indexedDeals = deals });
    }
}
