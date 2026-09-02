namespace AiService.Api.DTOs;

/// <summary>
/// A question about one deal's documents.
///
/// <para>There is deliberately no dealId here. It comes from the route, so the scope
/// of a question is server-controlled and a client cannot widen it by editing a
/// payload. That isn't an escalation guard today — ingestion-service filters by
/// dealId without authorizing it, and deals-service doesn't scope its list per user
/// either, so Deal Q&amp;A inherits the same "any authenticated user" posture as the
/// rest of the app — but keeping scope on the server means none of this has to be
/// revisited if that ever tightens.</para>
/// </summary>
public record AskDealQuestionRequest(string Question, string? DocumentId);

/// <summary>One prior turn of the conversation, supplied by the client.</summary>
/// <remarks>There is no threads table in v1: history lives in the client and is replayed
/// on each request. The server still trims and normalises it — a client is free to send
/// a hundred turns, and something has to stop that from crowding out the tool results of
/// the question actually being asked.</remarks>
public record ChatTurnRequest(string Role, string Content);

/// <summary>
/// Scope the server pins onto every tool call, independent of the question text.
///
/// <para>Sent by the deal panel, which is asking about one deal and must stay there. It
/// is applied in the tool layer rather than described in the prompt, so the scope is a
/// constraint rather than a suggestion the model could reason its way out of.</para>
/// </summary>
public record AskContextRequest(string? DealId, string? DocumentId);

/// <summary>A question for the Deal Assistant (§6.8). Answered over SSE.</summary>
public record AskRequest(
    string Question,
    IReadOnlyList<ChatTurnRequest>? History,
    AskContextRequest? Context);
