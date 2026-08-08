using DealsService.Api.DTOs;
using DealsService.Api.Infrastructure;
using DealsService.Business;
using DealsService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DealsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("deals/v1/deals")]
public class DealsController(DealService service) : ApiControllerBase
{
    /// <summary>
    /// Lists deals. Every filter is optional and they AND together. Dates are
    /// "yyyy-MM-dd"; offer prices are dollars and cap rates fractions (0.065 = 6.5%),
    /// matching how the values are stored. staleDays is the minimum whole days a deal
    /// has sat in its current stage, and q is free-text search over the deal name and
    /// its snapshotted property name.
    /// </summary>
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
        var (items, totalCount) = await service.GetAllAsync(page, pageSize, new DealFilterDto(
            stage, ownerId, priority, propertyType, metroArea, closeDateBefore, closeDateAfter,
            offerPriceMin, offerPriceMax, capRateMin, capRateMax, hasOverdueTasks, staleDays, q), ct);
        return Success(new PaginatedResponse<DealResponse>(
            items.Select(MapToResponse).ToList(), totalCount, page, pageSize));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var dto = await service.GetByIdAsync(id, ct);
        return dto is null ? NotFoundError("Deal not found.") : Success(MapToResponse(dto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDealRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(new CreateDealDto(
            request.PropertyId, request.PropertyName, request.PropertyType, request.MetroArea,
            request.OccupancyRate, request.MarketCapRateBenchmark,
            request.Name, request.Priority, request.OfferPrice, request.ProjectedCapRate,
            request.TargetIrr, request.EquityMultiple, request.ProjectedCloseDate), ActorId, ct);
        return FromResult(Map(result), StatusCodes.Status201Created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDealRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, new UpdateDealDto(
            request.Name, request.Priority, request.OfferPrice,
            request.ProjectedCapRate, request.TargetIrr, request.EquityMultiple,
            request.ProjectedCloseDate), ct);
        return FromResult(Map(result));
    }

    [HttpPost("{id}/advance")]
    public async Task<IActionResult> Advance(string id, [FromBody] AdvanceDealRequest? request, CancellationToken ct)
    {
        var result = await service.AdvanceAsync(id, request?.ExpectedCurrentStage, ActorId, ct);
        return FromResult(Map(result));
    }

    [HttpPost("{id}/kill")]
    [Authorize(Roles = AuthRoles.KillDeal)]
    public async Task<IActionResult> Kill(string id, [FromBody] KillDealRequest request, CancellationToken ct)
    {
        var result = await service.KillAsync(id, request.Reason, request.ExpectedCurrentStage, ActorId, IsElevated, ct);
        return FromResult(Map(result));
    }

    [HttpPost("{id}/transfer-owner")]
    [Authorize(Roles = AuthRoles.DealAdmin)]
    public async Task<IActionResult> TransferOwner(string id, [FromBody] TransferOwnerRequest request, CancellationToken ct)
    {
        var result = await service.TransferOwnerAsync(id, request.NewOwnerId, ActorId, IsElevated, ct);
        return FromResult(Map(result));
    }

    /// <summary>Republishes every deal's snapshot onto deal.snapshot, for backfilling the
    /// search index or repairing drift. Safe to re-run: unchanged documents lose the
    /// external-version comparison and are rejected rather than rewritten.</summary>
    [HttpPost("republish")]
    public async Task<IActionResult> Republish(CancellationToken ct)
    {
        var count = await service.RepublishAllAsync(ct);
        return Success(new { message = "Republished deal snapshots", count });
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(string id, CancellationToken ct)
    {
        var history = await service.GetHistoryAsync(id, ct);
        if (history is null) return NotFoundError("Deal not found.");
        return Success(history.Select(h => new StageHistoryResponse(
            h.Id, h.FromStage, h.ToStage, h.ChangedById, h.ChangedAt, h.DaysInStage, h.Reason)).ToList());
    }

    private static ServiceResult<DealResponse> Map(ServiceResult<DealDto> result) =>
        result.Succeeded
            ? ServiceResult<DealResponse>.Ok(MapToResponse(result.Value!))
            : ServiceResult<DealResponse>.Fail(result.Code!, result.Message!, result.Errors);

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
