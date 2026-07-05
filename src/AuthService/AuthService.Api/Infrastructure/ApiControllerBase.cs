using System.Security.Claims;
using AuthService.Business;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Infrastructure;

/// <summary>
/// Base controller that wraps payloads in the standard success/error envelope and
/// translates a <see cref="ServiceResult{T}"/> error code into an HTTP status.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected string RequestId => HttpContext.TraceIdentifier;

    /// <summary>The authenticated user's id from the "sub" claim, or null if absent.</summary>
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

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
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        var body = new ErrorBody(
            result.Code!,
            result.Message!,
            result.Errors.Select(e => new FieldErrorResponse(e.Field, e.Message)).ToList(),
            RequestId,
            DateTime.UtcNow.ToString("O"));

        return StatusCode(status, new ErrorEnvelope(body));
    }
}
