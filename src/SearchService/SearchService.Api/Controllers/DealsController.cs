using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SearchService.Api.DTOs;
using SearchService.Api.Infrastructure;
using SearchService.Business;
using SearchService.Business.DTOs;

namespace SearchService.Api.Controllers;

/// <summary>
/// The deals list, served from OpenSearch. Mirrors GET /deals/v1/deals exactly — same query
/// parameters, same defaults and clamps, same {data, meta} envelope — so switching to it is a
/// base-path change. Structured filters and ordering match; keyword results deliberately
/// don't, since matching more is the point of being here.
///
/// Authenticated, unlike the properties endpoints: deals are authorized data. Authentication
/// only, no per-user filtering — deals-service doesn't scope its list to the caller either
/// (ownerId is an ordinary optional filter there), and this must not answer differently.
/// </summary>
[ApiController]
[Authorize]
[Route("search/v1/deals")]
public class DealsController(DealSearchService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? stage,
        [FromQuery] string? ownerId,
        [FromQuery] string? priority,
        [FromQuery] string? propertyType,
        [FromQuery] string? metroArea,
        [FromQuery] string? closeDateBefore,
        [FromQuery] string? closeDateAfter,
        [FromQuery] double? offerPriceMin,
        [FromQuery] double? offerPriceMax,
        [FromQuery] double? capRateMin,
        [FromQuery] double? capRateMax,
        [FromQuery] bool? hasOverdueTasks,
        [FromQuery] int? staleDays,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, totalCount) = await service.SearchAsync(
            page, pageSize, stage, ownerId, priority, propertyType, metroArea,
            closeDateBefore, closeDateAfter, offerPriceMin, offerPriceMax,
            capRateMin, capRateMax, hasOverdueTasks, staleDays, q, ct);

        return Success(new PaginatedResponse<DealResponse>(
            items.Select(MapToResponse).ToList(), totalCount, page, pageSize));
    }

    private static DealResponse MapToResponse(DealDto d) => new(
        d.Id, d.Name, d.PropertyId, d.PropertyName, d.PropertyType, d.MetroArea,
        d.OccupancyRate, d.MarketCapRateBenchmark,
        d.Stage, d.Priority, d.OwnerId, d.DeadReason,
        d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
        d.AiScore, d.AiScoreRationale, d.RiskFlags,
        d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
        d.TaskCount, d.DoneTaskCount, d.HasOverdueTasks,
        d.HealthFlags.Select(f => new HealthFlagResponse(f.Type, f.Severity, f.Message)).ToList());
}
