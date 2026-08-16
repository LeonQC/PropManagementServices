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
