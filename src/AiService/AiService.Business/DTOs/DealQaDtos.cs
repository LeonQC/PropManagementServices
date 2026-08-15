namespace AiService.Business.DTOs;

/// <summary>A question about one deal. The deal id is not here on purpose — it comes
/// from the route so scoping stays server-controlled.</summary>
public record AskDealQuestionDto(string Question, string? DocumentId);

/// <summary>
/// One retrieved chunk the answer was built from. <paramref name="SourceNumber"/> is
/// the marker Claude cites inline ([S1], [S2]), so the UI can tie a sentence to a page.
/// </summary>
public record CitationDto(
    int SourceNumber,
    string DocumentId,
    string? FileName,
    int? PageNo,
    double Score,
    string Snippet);

/// <summary>
/// The answer, plus everything needed to check it. <paramref name="Citations"/> holds
/// only the sources Claude actually referenced; <paramref name="RetrievedChunkCount"/>
/// is how many were offered, so a large gap between the two is visible rather than
/// hidden — that gap is the signal that retrieval is padding the prompt.
/// </summary>
public record DealAnswerDto(
    string Answer,
    IReadOnlyList<CitationDto> Citations,
    int RetrievedChunkCount,
    bool AnsweredFromDocuments,
    string? Model,
    int? LatencyMs);
