namespace AuthService.Api.DTOs;

public record RegisterRequest(string Email, string Password, string? FullName, string Role);

public record LoginRequest(string Email, string Password);

/// <summary>Refresh token may be supplied in the body or (preferred) the HttpOnly cookie.</summary>
public record RefreshRequest(string? RefreshToken);

public record LogoutRequest(string? RefreshToken);

public record UpdateProfileRequest(string? FullName, string? AvatarUrl);

public record ChangeRoleRequest(string Role);
