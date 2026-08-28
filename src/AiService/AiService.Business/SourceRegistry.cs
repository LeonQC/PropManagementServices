namespace AiService.Business;

/// <summary>What a source marker points at, which decides where its chip navigates.</summary>
public enum SourceKind
{
    /// <summary>An excerpt from a document uploaded to a deal.</summary>
    Document,

    /// <summary>A deal record — the structured half, from get_deal or search_deals.</summary>
    Deal,

    /// <summary>A property listing.</summary>
    Property,
}

/// <summary>
/// One thing the model was shown and may cite, carrying everything the UI needs to
/// render a chip and route from it.
///
/// <para><see cref="Href"/> is computed here rather than in the client so the route
/// shape lives in one place. Note the real routes are <c>/acquisitions/:dealId</c> and
/// <c>/listings/:propertyId</c> — the App.tsx route table, not the <c>/deals/:id</c>
/// that the design notes assume.</para>
/// </summary>
public record Source(
    int Number,
    SourceKind Kind,
    string Id,
    string? DealId,
    string? Title,
    int? PageNo,
    double? Score,
    string? Snippet)
{
    /// <summary>Where the citation chip navigates, or null when nothing can be linked —
    /// a document whose owning deal is unknown has a page to quote but no page to open.</summary>
    public string? Href => Kind switch
    {
        SourceKind.Document => DealId is { Length: > 0 } dealId ? $"/acquisitions/{dealId}" : null,
        SourceKind.Deal => $"/acquisitions/{Id}",
        SourceKind.Property => $"/listings/{Id}",
        _ => null,
    };
}

/// <summary>
/// Hands out the [S1], [S2], … markers for one assistant question and remembers what
/// each one stands for.
///
/// <para>Numbering is the server's job, never the model's. The model chooses which
/// sources to reference; it never decides what a reference resolves to. That split is
/// what makes a citation checkable — every marker in a finished answer either names a
/// row this registry issued, or is dropped.</para>
///
/// <para>Registration is idempotent per underlying item, and this matters more than it
/// looks. A deal surfaced by <c>search_deals</c> and then read again by <c>get_deal</c>
/// is one source, and giving it a second number would put two chips for the same deal
/// under one answer and let the model cite [S3] and [S9] for the same fact.</para>
///
/// <para>Not thread-safe, and deliberately so: tool calls within an iteration are
/// dispatched concurrently, so callers register from the single-threaded assembly step
/// after the results are back, in a fixed order. Numbers assigned by whichever task
/// happened to finish first would make an answer's citations unreproducible.</para>
/// </summary>
public class SourceRegistry
{
    private readonly List<Source> _sources = [];
    private readonly Dictionary<string, Source> _byKey = [];

    /// <summary>Everything registered, in the order it was issued.</summary>
    public IReadOnlyList<Source> All => _sources;

    public int Count => _sources.Count;

    /// <summary>
    /// Registers one retrieved chunk. Keyed on document plus chunk index rather than
    /// page: ingestion-service splits on structure and emits oversized tables whole, so
    /// one page routinely carries several distinct chunks, and collapsing them would
    /// point two different quotes at one snippet.
    /// </summary>
    public Source RegisterDocument(
        string documentId, string? dealId, string? fileName, int? pageNo, int chunkIndex,
        double score, string text) =>
        Register($"doc:{documentId}:{chunkIndex}", n => new Source(
            n, SourceKind.Document, documentId, dealId, fileName, pageNo, score,
            Grounding.Snippet(text)));

    public Source RegisterDeal(string dealId, string? name, string? snippet = null) =>
        Register($"deal:{dealId}", n => new Source(
            n, SourceKind.Deal, dealId, dealId, name, null, null, snippet));

    public Source RegisterProperty(string propertyId, string? title, string? snippet = null) =>
        Register($"property:{propertyId}", n => new Source(
            n, SourceKind.Property, propertyId, null, title, null, null, snippet));

    private Source Register(string key, Func<int, Source> build)
    {
        if (_byKey.TryGetValue(key, out var existing)) return existing;

        var source = build(_sources.Count + 1);
        _sources.Add(source);
        _byKey[key] = source;
        return source;
    }

    /// <summary>
    /// The sources an answer actually referenced. Markers naming a number that was
    /// never issued are dropped rather than trusted — a fabricated [S47] is the one
    /// citation failure that would otherwise look completely legitimate.
    /// </summary>
    public IReadOnlyList<Source> Cited(string answer)
    {
        var byNumber = _sources.ToDictionary(s => s.Number);
        return [.. Grounding.CitedSourceNumbers(answer)
                 .Where(byNumber.ContainsKey)
                 .Select(n => byNumber[n])];
    }
}
