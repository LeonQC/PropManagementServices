using DealsService.Business.Domain;
using DealsService.Business.DTOs;
using DealsService.DataAccess;
using DealsService.Models;

namespace DealsService.Business;

public class DealTaskService(IDealRepository dealRepo, IDealTaskRepository taskRepo)
{
    public async Task<List<TaskDto>?> GetByDealAsync(string dealId, CancellationToken ct = default)
    {
        if (!await dealRepo.ExistsAsync(dealId, ct)) return null;
        var tasks = await taskRepo.GetByDealAsync(dealId, ct);
        return tasks.Select(MapToDto).ToList();
    }

    public async Task<ServiceResult<TaskDto>> CreateAsync(string dealId, CreateTaskDto input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return ServiceResult<TaskDto>.Fail(ErrorCodes.Validation, "Invalid task.",
                [new FieldError("title", "title is required.")]);

        var deal = await dealRepo.GetByIdAsync(dealId, ct);
        if (deal is null)
            return ServiceResult<TaskDto>.Fail(ErrorCodes.NotFound, "Deal not found.");

        var task = new DealTask
        {
            Id = "",
            DealId = dealId,
            Title = input.Title.Trim(),
            // Custom tasks attach to the deal's current stage on the checklist.
            Stage = deal.Deal.Stage,
            Status = TaskStatuses.Open,
            AssigneeId = input.AssigneeId,
            DueDate = input.DueDate,
            IsFromTemplate = false,
            CreatedAt = DateTime.UtcNow.ToString("O"),
        };

        var created = await taskRepo.CreateAsync(task, ct);
        return ServiceResult<TaskDto>.Ok(MapToDto(created));
    }

    public async Task<ServiceResult<TaskDto>> UpdateAsync(string dealId, string taskId, UpdateTaskDto input,
        CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(dealId, taskId, ct);
        if (task is null)
            return ServiceResult<TaskDto>.Fail(ErrorCodes.NotFound, "Task not found.");

        if (input.Status is not null && !TaskStatuses.All.Contains(input.Status))
            return ServiceResult<TaskDto>.Fail(ErrorCodes.Validation, "Invalid task.",
                [new FieldError("status", $"status must be one of: {string.Join(", ", TaskStatuses.All)}.")]);

        if (input.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Title))
                return ServiceResult<TaskDto>.Fail(ErrorCodes.Validation, "Invalid task.",
                    [new FieldError("title", "title cannot be empty.")]);
            task.Title = input.Title.Trim();
        }

        if (input.AssigneeId is not null)
            task.AssigneeId = input.AssigneeId.Length == 0 ? null : input.AssigneeId;
        if (input.DueDate is not null)
            task.DueDate = input.DueDate.Length == 0 ? null : input.DueDate;

        if (input.Status is not null && input.Status != task.Status)
        {
            task.Status = input.Status;
            task.CompletedAt = input.Status == TaskStatuses.Done ? DateTime.UtcNow.ToString("O") : null;
        }

        await taskRepo.UpdateAsync(task, ct);
        return ServiceResult<TaskDto>.Ok(MapToDto(task));
    }

    private static TaskDto MapToDto(DealTask t) => new(
        t.Id, t.DealId, t.Title, t.Stage, t.Status, t.AssigneeId, t.DueDate,
        t.IsFromTemplate, t.CreatedAt, t.CompletedAt);
}
