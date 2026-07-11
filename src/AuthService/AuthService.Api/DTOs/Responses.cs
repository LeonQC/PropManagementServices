namespace AuthService.Api.DTOs;

/// <summary>Access token returned in the body. The refresh token is delivered out-of-band
/// as an HttpOnly cookie (architecture §6.1), never in the response body.</summary>
public record TokenResponse(string AccessToken, int ExpiresIn, string TokenType);

public record UserProfileResponse(Guid Id, string Email, string? FullName, string? AvatarUrl, string Role, string? CreatedAt);

public record UserResponse(Guid Id, string Email, string? FullName, string Role);

/// <summary>Directory entry: the minimal id→name mapping any authenticated user may read.</summary>
public record DirectoryEntryResponse(Guid Id, string? FullName);
