using System.Globalization;
using AuthService.Business.DTOs;
using AuthService.Business.Security;
using AuthService.DataAccess;
using AuthService.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Business;

/// <summary>
/// The auth use-case layer: registration, login, refresh-token rotation, logout,
/// profile, and user/role management. Speaks only business DTOs; Identity managers
/// and entities are internal details. Writes an audit_log entry for every event.
/// </summary>
public class AccountService(
    UserManager<ApplicationUser> users,
    IRefreshTokenRepository refreshTokens,
    IAuditLogRepository audit,
    TokenService tokens,
    IValidator<RegisterDto> registerValidator,
    IValidator<UpdateProfileDto> profileValidator,
    IValidator<ChangeRoleDto> roleValidator)
{
    public async Task<ServiceResult<UserDto>> RegisterAsync(RegisterDto input, string? ip, CancellationToken ct = default)
    {
        var validation = await registerValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return ValidationFail<UserDto>(validation);

        if (await users.FindByEmailAsync(input.Email) is not null)
            return ServiceResult<UserDto>.Fail(ErrorCodes.Conflict, "A user with that email already exists.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = input.Email,
            Email = input.Email,
            EmailConfirmed = true,
            FullName = input.FullName,
            CreatedAt = UtcNow(),
        };

        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
            return ServiceResult<UserDto>.Fail(ErrorCodes.Validation, "Could not create user.",
                created.Errors.Select(e => new FieldError(e.Code, e.Description)).ToList());

        await users.AddToRoleAsync(user, input.Role);
        await Audit("user.registered", user.Id, user.Email, ip, $"role={input.Role}", ct);

        return ServiceResult<UserDto>.Ok(new UserDto(user.Id, user.Email!, user.FullName, input.Role));
    }

    public async Task<ServiceResult<TokenResultDto>> LoginAsync(LoginDto input, string? ip, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(input.Email);
        if (user is null || !await users.CheckPasswordAsync(user, input.Password))
        {
            await Audit("login.failure", user?.Id, input.Email, ip, "invalid credentials", ct);
            return ServiceResult<TokenResultDto>.Fail(ErrorCodes.Unauthorized, "Invalid email or password.");
        }

        var result = await IssueTokensAsync(user, ct);
        await Audit("login.success", user.Id, user.Email, ip, null, ct);
        return ServiceResult<TokenResultDto>.Ok(result);
    }

    public async Task<ServiceResult<TokenResultDto>> RefreshAsync(string rawToken, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return ServiceResult<TokenResultDto>.Fail(ErrorCodes.Unauthorized, "Missing refresh token.");

        var stored = await refreshTokens.GetByHashAsync(TokenService.Hash(rawToken), ct);
        if (stored is null || stored.RevokedAt is not null || IsExpired(stored.ExpiresAt))
            return ServiceResult<TokenResultDto>.Fail(ErrorCodes.Unauthorized, "Invalid or expired refresh token.");

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            return ServiceResult<TokenResultDto>.Fail(ErrorCodes.Unauthorized, "Invalid refresh token.");

        // Rotate: issue a new pair, then revoke the old token and link it to its successor.
        var result = await IssueTokensAsync(user, ct);
        stored.RevokedAt = UtcNow();
        stored.ReplacedByTokenHash = TokenService.Hash(result.RefreshToken);
        await refreshTokens.UpdateAsync(stored, ct);

        await Audit("token.refreshed", user.Id, user.Email, ip, null, ct);
        return ServiceResult<TokenResultDto>.Ok(result);
    }

    public async Task<ServiceResult<bool>> LogoutAsync(string rawToken, Guid userId, string? ip, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var stored = await refreshTokens.GetByHashAsync(TokenService.Hash(rawToken), ct);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = UtcNow();
                await refreshTokens.UpdateAsync(stored, ct);
            }
        }

        await Audit("logout", userId, null, ip, null, ct);
        return ServiceResult<bool>.Ok(true); // idempotent
    }

    public async Task<ServiceResult<UserProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult<UserProfileDto>.Fail(ErrorCodes.NotFound, "User not found.");
        return ServiceResult<UserProfileDto>.Ok(await MapProfileAsync(user));
    }

    public async Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(Guid userId, UpdateProfileDto input, CancellationToken ct = default)
    {
        var validation = await profileValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return ValidationFail<UserProfileDto>(validation);

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult<UserProfileDto>.Fail(ErrorCodes.NotFound, "User not found.");

        if (input.FullName is not null) user.FullName = input.FullName;
        if (input.AvatarUrl is not null) user.AvatarUrl = input.AvatarUrl;
        await users.UpdateAsync(user);

        return ServiceResult<UserProfileDto>.Ok(await MapProfileAsync(user));
    }

    public async Task<ServiceResult<IReadOnlyList<UserDto>>> ListUsersAsync(CancellationToken ct = default)
    {
        var all = await users.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var list = new List<UserDto>(all.Count);
        foreach (var u in all)
            list.Add(new UserDto(u.Id, u.Email!, u.FullName, await GetRoleAsync(u)));
        return ServiceResult<IReadOnlyList<UserDto>>.Ok(list);
    }

    public async Task<ServiceResult<UserDto>> ChangeRoleAsync(Guid userId, ChangeRoleDto input, string? ip, CancellationToken ct = default)
    {
        var validation = await roleValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return ValidationFail<UserDto>(validation);

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult<UserDto>.Fail(ErrorCodes.NotFound, "User not found.");

        var current = await users.GetRolesAsync(user);
        if (current.Count > 0) await users.RemoveFromRolesAsync(user, current);
        await users.AddToRoleAsync(user, input.Role);

        await Audit("role.changed", user.Id, user.Email, ip, $"{string.Join(",", current)} -> {input.Role}", ct);
        return ServiceResult<UserDto>.Ok(new UserDto(user.Id, user.Email!, user.FullName, input.Role));
    }

    // -- helpers -------------------------------------------------------------

    private async Task<TokenResultDto> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var role = await GetRoleAsync(user);
        var (accessToken, expiresIn) = tokens.CreateAccessToken(user.Id, user.Email!, role);
        var (raw, hash, expiresAt) = tokens.CreateRefreshToken();

        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            CreatedAt = UtcNow(),
            ExpiresAt = expiresAt,
        }, ct);

        return new TokenResultDto(accessToken, raw, expiresIn);
    }

    private async Task<string> GetRoleAsync(ApplicationUser user) =>
        (await users.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

    private async Task<UserProfileDto> MapProfileAsync(ApplicationUser user) =>
        new(user.Id, user.Email!, user.FullName, user.AvatarUrl, await GetRoleAsync(user), user.CreatedAt);

    private Task Audit(string evt, Guid? userId, string? email, string? ip, string? detail, CancellationToken ct) =>
        audit.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = evt,
            Email = email,
            IpAddress = ip,
            Detail = detail,
            CreatedAt = UtcNow(),
        }, ct);

    private static ServiceResult<T> ValidationFail<T>(FluentValidation.Results.ValidationResult v) =>
        ServiceResult<T>.Fail(ErrorCodes.Validation, "Validation failed.",
            v.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)).ToList());

    private static bool IsExpired(string isoUtc) =>
        DateTime.Parse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) <= DateTime.UtcNow;

    private static string UtcNow() => DateTime.UtcNow.ToString("O");
}
