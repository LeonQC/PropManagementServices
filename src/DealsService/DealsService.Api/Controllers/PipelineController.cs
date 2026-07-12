using DealsService.Api.DTOs;
using DealsService.Api.Infrastructure;
using DealsService.Business;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DealsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("deals/v1/pipeline")]
public class PipelineController(DealService service) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var summary = await service.GetPipelineSummaryAsync(ct);
        return Success(new PipelineSummaryResponse(
            summary.TotalActiveDeals,
            summary.TotalPipelineValue,
            summary.Stages.Select(s => new StageSummaryResponse(s.Stage, s.Count, s.TotalValue)).ToList()));
    }
}
