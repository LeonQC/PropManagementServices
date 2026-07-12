using DealsService.Api.DTOs;
using DealsService.Api.Infrastructure;
using DealsService.Business;
using DealsService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DealsService.Api.Controllers;

[ApiController]
[Authorize]
[Route("deals/v1/deals/{dealId}/tasks")]
public class DealTasksController(DealTaskService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(string dealId, CancellationToken ct)
    {
        var tasks = await service.GetByDealAsync(dealId, ct);
        if (tasks is null) return NotFoundError("Deal not found.");
        return Success(tasks.Select(MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(string dealId, [FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(dealId,
            new CreateTaskDto(request.Title, request.AssigneeId, request.DueDate), ct);
        return FromResult(Map(result), StatusCodes.Status201Created);
    }

    [HttpPut("{taskId}")]
    public async Task<IActionResult> Update(string dealId, string taskId,
        [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(dealId, taskId,
            new UpdateTaskDto(request.Title, request.Status, request.AssigneeId, request.DueDate), ct);
        return FromResult(Map(result));
    }

    private static ServiceResult<TaskResponse> Map(ServiceResult<TaskDto> result) =>
        result.Succeeded
            ? ServiceResult<TaskResponse>.Ok(MapToResponse(result.Value!))
            : ServiceResult<TaskResponse>.Fail(result.Code!, result.Message!, result.Errors);

    private static TaskResponse MapToResponse(TaskDto t) => new(
        t.Id, t.DealId, t.Title, t.Stage, t.Status, t.AssigneeId, t.DueDate,
        t.IsFromTemplate, t.CreatedAt, t.CompletedAt);
}
