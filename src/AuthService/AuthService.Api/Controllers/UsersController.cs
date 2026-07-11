using AuthService.Api.DTOs;
using AuthService.Api.Infrastructure;
using AuthService.Business;
using AuthService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

[ApiController]
[Route("auth/v1/users")]
[Authorize]
public class UsersController(AccountService account) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Roles = $"{AuthRoles.Admin},{AuthRoles.ManagingDirector}")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await account.ListUsersAsync(ct);
        return FromResult(MapList(result));
    }

    // Minimal id→name directory for any authenticated user (deals UI renders
    // owner/assignee names from this). No emails or roles — the full listing
    // above stays Admin/MD-gated.
    [HttpGet("directory")]
    public async Task<IActionResult> Directory(CancellationToken ct)
    {
        var result = await account.ListDirectoryAsync(ct);
        return FromResult(result.Succeeded
            ? ServiceResult<IReadOnlyList<DirectoryEntryResponse>>.Ok(
                result.Value!.Select(d => new DirectoryEntryResponse(d.Id, d.FullName)).ToList())
            : ServiceResult<IReadOnlyList<DirectoryEntryResponse>>.Fail(result.Code!, result.Message!, result.Errors));
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeRoleRequest req, CancellationToken ct)
    {
        var result = await account.ChangeRoleAsync(id, new ChangeRoleDto(req.Role), ClientIp, ct);
        if (!result.Succeeded) return FromResult(result);

        var u = result.Value!;
        return Success(new UserResponse(u.Id, u.Email, u.FullName, u.Role));
    }

    // Soft-delete (deactivate) a user. Admins can't deactivate themselves.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (id == CurrentUserId)
            return FromResult(ServiceResult<bool>.Fail(ErrorCodes.Validation, "You can't deactivate your own account."));

        var result = await account.DeactivateAsync(id, ClientIp, ct);
        return FromResult(result);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static ServiceResult<IReadOnlyList<UserResponse>> MapList(ServiceResult<IReadOnlyList<UserDto>> r) =>
        r.Succeeded
            ? ServiceResult<IReadOnlyList<UserResponse>>.Ok(
                r.Value!.Select(u => new UserResponse(u.Id, u.Email, u.FullName, u.Role)).ToList())
            : ServiceResult<IReadOnlyList<UserResponse>>.Fail(r.Code!, r.Message!, r.Errors);
}
