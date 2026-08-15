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

/// <summary>Health payload: dependency reachability, so a 503 says which part is down.</summary>
public record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);
