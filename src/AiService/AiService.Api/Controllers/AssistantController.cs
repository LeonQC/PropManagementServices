using AiService.Api.DTOs;
using AiService.Api.Infrastructure;
using AiService.Business;
using AiService.Business.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiService.Api.Controllers;

/// <summary>
/// The Deal Assistant (design doc §6.8). Ask a natural-language question about the
/// portfolio and get a streamed answer, grounded in whatever the model's read-only tool
/// calls returned.
///
/// <para>SSE rather than JSON, unlike Deal Q&amp;A: a question that runs three tool calls
/// cannot produce answer text in the first second, so the tool progress is streamed
/// ahead of it. A five-second silent wait reads as a broken page.</para>
///
/// <para>Authenticated, and the caller's own token is what reaches every downstream
/// service. This service holds no service account, so the assistant can never surface a
/// record the caller couldn't fetch themselves.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("ai/v1")]
public class AssistantController(AssistantService assistant) : ApiControllerBase
{
    [HttpPost("ask")]
    public async Task Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        // Failures before the first byte are ordinary enveloped errors with a real status
        // code. Once the stream is open the 200 has already gone out, so everything after
        // this point has to arrive as an `error` event instead — which is why this check
        // happens before Start().
        var token = BearerToken;
        if (token is null)
        {
            await WriteEnvelopeErrorAsync(
                ErrorCodes.Unauthorized, "A bearer token is required.", StatusCodes.Status401Unauthorized, ct);
            return;
        }

        var input = new AskInput(
            request.Question ?? "",
            request.History?.Select(t => new HistoryTurn(t.Role ?? "user", t.Content ?? "")).ToList(),
            Trimmed(request.Context?.DealId),
            Trimmed(request.Context?.DocumentId));

        var sse = new SseWriter(Response);
        var started = false;

        await foreach (var e in assistant.AskAsync(input, token, ActorId, ct))
        {
            // A failure on the very first event means nothing has been written yet, so it
            // can still be a proper HTTP error rather than a 200 carrying bad news.
            if (!started && e is AssistantEvent.Failed failure)
            {
                await WriteEnvelopeErrorAsync(failure.Code, failure.Message, StatusFor(failure.Code), ct);
                return;
            }

            if (!started)
            {
                sse.Start();
                started = true;
            }

            switch (e)
            {
                case AssistantEvent.Status s:
                    await sse.SendAsync("status", new { s.Iteration, s.Tool, s.Label }, ct);
                    break;

                case AssistantEvent.ToolFinished t:
                    await sse.SendAsync("tool",
                        new { t.Iteration, t.Tool, t.Summary, t.Capped, Failed = t.IsError }, ct);
                    break;

                case AssistantEvent.Delta d:
                    // Last hop of the streaming relay — see AssistantService's class doc
                    // for the full chain from Claude's raw stream down to this SSE frame.
                    await sse.SendAsync("delta", new { d.Text }, ct);
                    break;

                case AssistantEvent.Citations c:
                    await sse.SendAsync("citations", new
                    {
                        Citations = c.Sources.Select(source => new AssistantCitationResponse(
                            source.Number,
                            source.Kind.ToString().ToLowerInvariant(),
                            source.Id,
                            source.DealId,
                            source.Title,
                            source.PageNo,
                            source.Score,
                            source.Snippet,
                            source.Href)),
                    }, ct);
                    break;

                case AssistantEvent.Done done:
                    await sse.SendAsync("done",
                        new { done.Model, done.Iterations, done.ToolCalls, done.LatencyMs, done.Truncated }, ct);
                    break;

                case AssistantEvent.Failed f:
                    await sse.SendAsync("error", new { f.Code, f.Message }, ct);
                    break;
            }
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int StatusFor(string code) => code switch
    {
        ErrorCodes.Validation => StatusCodes.Status400BadRequest,
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.RetrievalFailed => StatusCodes.Status502BadGateway,
        ErrorCodes.AiUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// Writes the standard error envelope by hand. The action returns void so it can own
    /// the response body for streaming, which means IActionResult is not available here.
    /// </summary>
    private async Task WriteEnvelopeErrorAsync(string code, string message, int status, CancellationToken ct)
    {
        Response.StatusCode = status;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsJsonAsync(
            new ErrorEnvelope(new ErrorBody(code, message, [], RequestId, DateTime.UtcNow.ToString("O"))),
            ct);
    }
}
