using SearchService.Models;

namespace SearchService.DataAccess;

/// <summary>
/// The deal search index. Twin of <see cref="IPropertyIndex"/> — same role, same contract,
/// a separate index because the two entities have genuinely different mappings. The shared
/// envelope fields are what a cross-entity query spans, not a shared interface.
/// </summary>
public interface IDealIndex
{
    /// <summary>Creates the index and alias from the bundled mapping if they don't exist yet.</summary>
    Task EnsureCreatedAsync(CancellationToken ct = default);

    /// <summary>
    /// Indexes a document using <paramref name="version"/> as OpenSearch's external version.
    /// Returns false when the write was rejected as stale (an older or duplicate snapshot),
    /// which is an expected outcome, not an error.
    /// </summary>
    Task<bool> IndexAsync(DealDocument document, long version, CancellationToken ct = default);

    /// <summary>Removes a document. Missing documents are treated as already removed.</summary>
    Task DeleteAsync(string entityId, CancellationToken ct = default);

    /// <summary>Live document count, for health/verification.</summary>
    Task<long> CountAsync(CancellationToken ct = default);
}
