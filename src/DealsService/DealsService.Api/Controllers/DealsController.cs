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
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? stage,
        [FromQuery] string? ownerId,
        [FromQuery] string? priority,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, totalCount) = await service.GetAllAsync(page, pageSize, stage, ownerId, priority, ct);
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
            request.Name, request.Priority, request.OfferPrice, request.ProjectedCapRate,
            request.TargetIrr, request.EquityMultiple, request.ProjectedCloseDate), ActorId, ct);
        return FromResult(Map(result), StatusCodes.Status201Created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDealRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, new UpdateDealDto(
            request.Name, request.Priority, request.OwnerId, request.OfferPrice,
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
        var result = await service.KillAsync(id, request.Reason, request.ExpectedCurrentStage, ActorId, ct);
        return FromResult(Map(result));
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
        d.Stage, d.Priority, d.OwnerId, d.DeadReason,
        d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
        d.AiScore, d.AiScoreRationale, d.RiskFlags,
        d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
        d.TaskCount, d.DoneTaskCount, d.HasOverdueTasks);
}
