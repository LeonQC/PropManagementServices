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

    /// <summary>
    /// The deals-list query. Parameters deliberately mirror DealsService's
    /// IDealRepository.GetAllAsync / DealQuery so the two paths stay swappable and
    /// diff-testable — including the absence of a sort parameter: deals order by newest
    /// first, or by relevance when a keyword is present, and nothing else.
    /// </summary>
    Task<(List<DealDocument> Items, long TotalCount)> SearchAsync(
        int page, int pageSize,
        string? stage = null,
        string? ownerId = null,
        string? priority = null,
        string? propertyType = null,
        string? metroArea = null,
        string? closeDateBefore = null,
        string? closeDateAfter = null,
        double? offerPriceMin = null,
        double? offerPriceMax = null,
        double? capRateMin = null,
        double? capRateMax = null,
        double? occupancyMin = null,
        double? occupancyMax = null,
        bool? hasOverdueTasks = null,
        int? staleDays = null,
        string? q = null,
        CancellationToken ct = default);
}
