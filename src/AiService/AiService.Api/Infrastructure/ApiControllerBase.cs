using System.Security.Claims;
using AiService.Business;
using Microsoft.AspNetCore.Mvc;

namespace AiService.Api.Infrastructure;

/// <summary>
/// Base controller that wraps payloads in the standard success/error envelope and
/// translates a <see cref="ServiceResult{T}"/> error code into an HTTP status.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected string RequestId => HttpContext.TraceIdentifier;

    /// <summary>The authenticated user's id from the "sub" claim.</summary>
    protected string ActorId => User.FindFirstValue("sub") ?? "unknown";

    /// <summary>
    /// The caller's raw bearer token, forwarded verbatim to ingestion-service and
    /// deals-service. This service has no credentials of its own, so downstream
    /// calls carry exactly the caller's authority and nothing more.
    /// </summary>
    protected string? BearerToken
    {
        get
        {
            var header = Request.Headers.Authorization.ToString();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
        }
    }

    protected IActionResult Success<T>(T data, int status = StatusCodes.Status200OK) =>
        StatusCode(status, new SuccessEnvelope<T>(data, new Meta(DateTime.UtcNow.ToString("O"), RequestId)));

    /// <summary>Map a service result to an envelope response (success status configurable).</summary>
    protected IActionResult FromResult<T>(ServiceResult<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.Succeeded) return Success(result.Value!, successStatus);

        var status = result.Code switch
        {
            ErrorCodes.Validation => StatusCodes.Status400BadRequest,
            ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.RetrievalFailed => StatusCodes.Status502BadGateway,
            ErrorCodes.AiUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };

        return Error(result.Code!, result.Message!, status,
            [.. result.Errors.Select(e => new FieldErrorResponse(e.Field, e.Message))]);
    }

    protected IActionResult Error(
        string code, string message, int status, IReadOnlyList<FieldErrorResponse>? details = null) =>
        StatusCode(status, new ErrorEnvelope(new ErrorBody(
            code, message, details ?? [], RequestId, DateTime.UtcNow.ToString("O"))));
}
