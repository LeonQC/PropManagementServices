using Microsoft.AspNetCore.Mvc;

namespace SearchService.Api.Infrastructure;

/// <summary>
/// Base for the controllers whose source service wraps responses in the {data, meta}
/// envelope. Only the deals controller does — properties mirror listings-service, which
/// returns raw JSON, so PropertiesController stays a plain ControllerBase.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected string RequestId => HttpContext.TraceIdentifier;

    protected IActionResult Success<T>(T data, int status = StatusCodes.Status200OK) =>
        StatusCode(status, new SuccessEnvelope<T>(data, new Meta(DateTime.UtcNow.ToString("O"), RequestId)));
}
