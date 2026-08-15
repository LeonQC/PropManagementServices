using AiService.Api.DTOs;
using AiService.Api.Infrastructure;
using AiService.Business;
using AiService.Business.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiService.Api.Controllers;

/// <summary>
/// Deal Q&amp;A (design doc §6.4). Ask a natural-language question about one deal and
/// get an answer grounded in that deal's uploaded documents, with page citations.
///
/// <para>Plain JSON, not SSE — streaming arrives in Phase 2 with the assistant.</para>
///
/// <para>Authenticated, and the caller's own token is what reaches ingestion-service
/// and deals-service: this service holds no service account, so it can never surface
/// a document the caller couldn't fetch themselves.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("ai/v1/deals/{dealId}")]
public class DealQaController(DealQaService service) : ApiControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> Ask(
        string dealId, [FromBody] AskDealQuestionRequest request, CancellationToken ct)
    {
        // The token validated by the middleware is the one forwarded downstream. Its
        // absence here would mean [Authorize] passed without a bearer scheme, which
        // can't happen — but the downstream calls need the raw string, so fail loudly
        // rather than call them unauthenticated.
        var token = BearerToken;
        if (token is null)
            return Error(ErrorCodes.Unauthorized, "A bearer token is required.",
                StatusCodes.Status401Unauthorized);

        var result = await service.AskAsync(
            dealId, new AskDealQuestionDto(request.Question, request.DocumentId), token, ActorId, ct);

        return FromResult(Map(result));
    }

    private static ServiceResult<DealAnswerResponse> Map(ServiceResult<DealAnswerDto> result) =>
        result.Succeeded
            ? ServiceResult<DealAnswerResponse>.Ok(MapToResponse(result.Value!))
            : ServiceResult<DealAnswerResponse>.Fail(result.Code!, result.Message!, result.Errors);

    private static DealAnswerResponse MapToResponse(DealAnswerDto d) => new(
        d.Answer,
        [.. d.Citations.Select(c => new CitationResponse(
            c.SourceNumber, c.DocumentId, c.FileName, c.PageNo, c.Score, c.Snippet))],
        d.RetrievedChunkCount,
        d.AnsweredFromDocuments,
        d.Model,
        d.LatencyMs);
}
