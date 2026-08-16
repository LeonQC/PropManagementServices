using AiService.Api.DTOs;
using AiService.Business;
using Microsoft.AspNetCore.Mvc;

namespace AiService.Api.Controllers;

/// <summary>
/// Liveness plus the two things that make this service able to answer at all: a
/// database to read the prompt from, and a configured model key. Anonymous, matching
/// the other services' health endpoints.
/// </summary>
[ApiController]
[Route("ai/v1/health")]
public class HealthController(HealthProbe probe) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var checks = await probe.RunAsync(ct);
        var healthy = checks.Values.All(v => v == "ok");
        return StatusCode(
            healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable,
            new HealthResponse(healthy ? "healthy" : "degraded", checks));
    }
}
