namespace AiService.Api.DTOs;

/// <summary>One source the answer drew on. <paramref name="SourceNumber"/> matches the
/// [S1]-style markers in the answer text, so the UI can link a sentence to a page.</summary>
public record CitationResponse(
    int SourceNumber,
    string DocumentId,
    string? FileName,
    int? PageNo,
    double Score,
    string Snippet);

/// <summary>
/// The answer plus its sources. <paramref name="AnsweredFromDocuments"/> is false when
/// the service answered without calling the model — no documents on the deal, or
/// nothing that cleared the relevance floor — which lets the UI render that as an
/// empty state rather than as a model response.
/// </summary>
public record DealAnswerResponse(
    string Answer,
    IReadOnlyList<CitationResponse> Citations,
    int RetrievedChunkCount,
    bool AnsweredFromDocuments,
    string? Model,
    int? LatencyMs);

/// <summary>
/// One source the assistant's answer drew on. <paramref name="Kind"/> is
/// "document", "deal" or "property", and <paramref name="Href"/> is the route the chip
/// navigates to — computed on the server so the route shape lives in one place.
///
/// <para>Wider than <see cref="CitationResponse"/>'s Deal Q&amp;A counterpart because the
/// assistant can cite a deal record or a listing, neither of which has a page number or
/// a retrieval score.</para>
/// </summary>
public record AssistantCitationResponse(
    int SourceNumber,
    string Kind,
    string Id,
    string? DealId,
    string? Title,
    int? PageNo,
    double? Score,
    string? Snippet,
    string? Href);

/// <summary>Health payload: dependency reachability, so a 503 says which part is down.</summary>
public record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);
