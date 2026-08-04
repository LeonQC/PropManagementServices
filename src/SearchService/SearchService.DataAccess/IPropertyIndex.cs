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

    /// <summary>
    /// The listings-grid query. Signature deliberately mirrors
    /// ListingsService's IPropertyRepository.GetAllAsync so the two paths stay swappable and
    /// diff-testable. Soft-deleted properties are never in the index, so there's no
    /// off_market exclusion to apply here.
    /// </summary>
    Task<(List<PropertyDocument> Items, long TotalCount)> SearchAsync(
        int page, int pageSize,
        string? propertyType = null,
        string? status = null,
        string? metroArea = null,
        double? minPrice = null,
        double? maxPrice = null,
        string? sort = null,
        string? q = null,
        CancellationToken ct = default);
}
