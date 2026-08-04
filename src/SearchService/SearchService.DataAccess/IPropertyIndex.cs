using SearchService.Models;

namespace SearchService.DataAccess;

/// <summary>
/// The property search index. Sits where a repository would in the other services — same role
/// (the layer that talks to the store), different store.
/// </summary>
public interface IPropertyIndex
{
    /// <summary>Creates the index and alias from the bundled mapping if they don't exist yet.</summary>
    Task EnsureCreatedAsync(CancellationToken ct = default);

    /// <summary>
    /// Indexes a document using <paramref name="version"/> as OpenSearch's external version.
    /// Returns false when the write was rejected as stale (an older or duplicate snapshot),
    /// which is an expected outcome, not an error.
    /// </summary>
    Task<bool> IndexAsync(PropertyDocument document, long version, CancellationToken ct = default);

    /// <summary>Removes a document. Missing documents are treated as already removed.</summary>
    Task DeleteAsync(string entityId, CancellationToken ct = default);

    /// <summary>Live document count, for health/verification.</summary>
    Task<long> CountAsync(CancellationToken ct = default);
}
