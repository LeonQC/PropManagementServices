namespace SearchService.Models;

/// <summary>
/// One result from a cross-entity query — the common envelope every indexed document shares,
/// plus its score. Deliberately not a union of PropertyDocument and DealDocument: a
/// cross-entity result list has one shape, and a caller that wants the full entity follows
/// EntityType + EntityId to that entity's own endpoint.
/// </summary>
/// <param name="Snippet">The head of the flattened `body` text, for a one-line preview.</param>
public record SearchHit(
    string EntityType,
    string EntityId,
    string Title,
    string? Snippet,
    double Score);
