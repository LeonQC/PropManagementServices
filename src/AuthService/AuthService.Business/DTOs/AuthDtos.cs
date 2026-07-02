namespace AuthService.Business.DTOs;

// Inbound use-case DTOs ------------------------------------------------------

public record RegisterDto(string Email, string Password, string? FullName, string Role);

public record LoginDto(string Email, string Password);

public record UpdateProfileDto(string? FullName, string? AvatarUrl);

public record ChangeRoleDto(string Role);

// Outbound DTOs --------------------------------------------------------------

/// <summary>The token pair returned by login/refresh. Refresh token is the raw
/// opaque value (only its hash is persisted server-side).</summary>
public record TokenResultDto(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType = "Bearer");

public record UserProfileDto(Guid Id, string Email, string? FullName, string? AvatarUrl, string Role, string? CreatedAt);

public record UserDto(Guid Id, string Email, string? FullName, string Role);

// JWKS -----------------------------------------------------------------------

public record JwkDto(string Kty, string Use, string Kid, string Alg, string N, string E);

public record JwksDto(IReadOnlyList<JwkDto> Keys);
