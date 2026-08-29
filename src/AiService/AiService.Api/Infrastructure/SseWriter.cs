using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace AiService.Api.Infrastructure;

/// <summary>
/// Writes Server-Sent Events to an open response.
///
/// <para>Every frame is flushed as it is written, and buffering is turned off in the two
/// places that would otherwise reintroduce it: Kestrel's own response buffer, and any
/// reverse proxy honouring <c>X-Accel-Buffering</c>. Without both, the whole answer
/// arrives at once when the response completes — which is a working endpoint that has
/// silently lost the entire point of streaming, and looks identical in a test that only
/// checks the final body.</para>
/// </summary>
public sealed class SseWriter(HttpResponse response)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public void Start()
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    public async Task SendAsync<T>(string eventName, T payload, CancellationToken ct = default)
    {
        var data = JsonSerializer.Serialize(payload, Json);

        var frame = new StringBuilder();
        frame.Append("event: ").Append(eventName).Append('\n');

        // A data field cannot span lines, so a payload containing a newline has to be
        // split across several. JSON serialisation escapes newlines, so this only bites
        // if a future payload is ever sent as raw text — cheap to handle, expensive to
        // debug the one time it happens.
        foreach (var line in data.Split('\n'))
            frame.Append("data: ").Append(line).Append('\n');

        frame.Append('\n');

        await response.WriteAsync(frame.ToString(), Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }
}
