namespace SearchService.DataAccess;

public class OpenSearchSettings
{
    public const string SectionName = "OpenSearch";

    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>
    /// Physical index name. Documents are written here; reads go through <see cref="Alias"/>.
    /// Bump the suffix (properties_v2) to reindex under a new mapping, then repoint the alias.
    /// </summary>
    public string PropertiesIndex { get; set; } = "properties_v1";

    /// <summary>
    /// Read alias. Queries target this rather than the physical index so the index can be
    /// rebuilt and swapped underneath them without downtime or a code change.
    /// </summary>
    public string PropertiesAlias { get; set; } = "properties";

    /// <summary>Physical deals index — same write-here/read-through-the-alias split as
    /// <see cref="PropertiesIndex"/>.</summary>
    public string DealsIndex { get; set; } = "deals_v1";

    /// <summary>Read alias for deals.</summary>
    public string DealsAlias { get; set; } = "deals";
}
