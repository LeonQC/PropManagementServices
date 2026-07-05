using AuthService.Api.DTOs;
using AuthService.Api.Infrastructure;
using AuthService.Business;
using AuthService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

[ApiController]
[Route("auth/v1")]
[Authorize] // default: authenticated; individual actions relax/tighten this
public class AuthController(AccountService account) : ApiControllerBase
{
    private const string RefreshCookie = "refresh_token";

    [HttpPost("register")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var result = await account.RegisterAsync(
            new RegisterDto(req.Email, req.Password, req.FullName, req.Role), ClientIp, ct);
        if (!result.Succeeded) return FromResult(result);

        var u = result.Value!;
        return Success(new UserResponse(u.Id, u.Email, u.FullName, u.Role), StatusCodes.Status201Created);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await account.LoginAsync(new LoginDto(req.Email, req.Password), ClientIp, ct);
        return TokenResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? req, CancellationToken ct)
    {
        var raw = req?.RefreshToken ?? Request.Cookies[RefreshCookie] ?? string.Empty;
        var result = await account.RefreshAsync(raw, ClientIp, ct);
        return TokenResult(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? req, CancellationToken ct)
    {
        var raw = req?.RefreshToken ?? Request.Cookies[RefreshCookie] ?? string.Empty;
        var result = await account.LogoutAsync(raw, CurrentUserId ?? Guid.Empty, ClientIp, ct);
        Response.Cookies.Delete(RefreshCookie);
        return FromResult(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (CurrentUserId is not { } id) return Unauthorized();
        var result = await account.GetProfileAsync(id, ct);
        return FromResult(MapProfile(result));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not { } id) return Unauthorized();
        var result = await account.UpdateProfileAsync(id, new UpdateProfileDto(req.FullName, req.AvatarUrl), ct);
        return FromResult(MapProfile(result));
    }

    // -- helpers -------------------------------------------------------------

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>On success, drop the refresh token into an HttpOnly cookie and return
    /// only the access token in the body.</summary>
    private IActionResult TokenResult(ServiceResult<TokenResultDto> result)
    {
        if (!result.Succeeded) return FromResult(result);

        var t = result.Value!;
        Response.Cookies.Append(RefreshCookie, t.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/auth/v1",
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });

        return Success(new TokenResponse(t.AccessToken, t.ExpiresIn, t.TokenType));
    }

    private static ServiceResult<UserProfileResponse> MapProfile(ServiceResult<UserProfileDto> r) =>
        r.Succeeded
            ? ServiceResult<UserProfileResponse>.Ok(new UserProfileResponse(
                r.Value!.Id, r.Value.Email, r.Value.FullName, r.Value.AvatarUrl, r.Value.Role, r.Value.CreatedAt))
            : ServiceResult<UserProfileResponse>.Fail(r.Code!, r.Message!, r.Errors);
}
