using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenSearch.Net;
using SearchService.Models;

namespace SearchService.DataAccess;

/// <summary>
/// OpenSearch-backed implementation of <see cref="IPropertyIndex"/>, using the low-level client
/// so request bodies are the same JSON you'd paste into Dashboards → Dev Tools.
/// </summary>
public sealed class OpenSearchPropertyIndex(
    IOpenSearchLowLevelClient client,
    OpenSearchSettings settings,
    ILogger<OpenSearchPropertyIndex> logger) : IPropertyIndex
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);   // camelCase, matching the index mapping's field names

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        var exists = await client.Indices.ExistsAsync<StringResponse>(settings.PropertiesIndex, ctx: ct);
        if (exists.HttpStatusCode == 200)
        {
            logger.LogInformation("Index {Index} already exists.", settings.PropertiesIndex);
        }
        else
        {
            var mapping = ReadEmbeddedMapping();
            var created = await client.Indices.CreateAsync<StringResponse>(
                settings.PropertiesIndex, PostData.String(mapping), ctx: ct);

            if (!created.Success)
                throw new InvalidOperationException(
                    $"Could not create index {settings.PropertiesIndex}: {created.Body}");

            logger.LogInformation("Created index {Index} from the bundled mapping.", settings.PropertiesIndex);
        }

        await EnsureAliasAsync(ct);
    }

    private async Task EnsureAliasAsync(CancellationToken ct)
    {
        await AliasHelper.EnsureAliasAsync(
            client, settings.PropertiesIndex, settings.PropertiesAlias, logger, ct);
        await AliasHelper.EnsureAliasAsync(
            client, settings.PropertiesIndex, settings.GroupAlias, logger, ct);
    }

    public async Task<bool> IndexAsync(PropertyDocument document, long version, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);

        // version_type=external: OpenSearch accepts the write only if `version` is greater than
        // what's stored. A replayed or out-of-order snapshot loses the race and 409s, which is
        // the desired outcome — not an error to retry.
        var res = await client.IndexAsync<StringResponse>(
            settings.PropertiesIndex,
            document.EntityId,
            PostData.String(json),
            new IndexRequestParameters
            {
                QueryString = { ["version"] = version.ToString(), ["version_type"] = "external" },
            },
            ct);

        if (res.Success) return true;

        if (res.HttpStatusCode == 409)
        {
            logger.LogDebug(
                "Skipped stale snapshot for {Id} (version {Version} is not newer than the indexed one).",
                document.EntityId, version);
            return false;
        }

        throw new InvalidOperationException(
            $"Indexing {document.EntityId} failed ({res.HttpStatusCode}): {res.Body}");
    }

    public async Task DeleteAsync(string entityId, CancellationToken ct = default)
    {
        var res = await client.DeleteAsync<StringResponse>(settings.PropertiesIndex, entityId, ctx: ct);

        // 404 just means it was never indexed, or is already gone.
        if (res.Success || res.HttpStatusCode == 404) return;

        throw new InvalidOperationException(
            $"Deleting {entityId} failed ({res.HttpStatusCode}): {res.Body}");
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        // Explicit path rather than client.CountAsync(alias): PostData defines an implicit
        // conversion from string, so a bare string argument silently binds to the
        // (PostData body) overload and posts the alias name as the request body against all
        // indices. Spelling the URL out avoids that trap entirely.
        //
        // Note this counts root documents only — `_cat/indices` reports a much larger number
        // because it also counts the nested `features` sub-documents.
        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.GET, $"/{settings.PropertiesAlias}/_count", ct, null);

        if (!res.Success) return -1;

        using var doc = JsonDocument.Parse(res.Body);
        return doc.RootElement.GetProperty("count").GetInt64();
    }

    /// <summary>Fields the keyword query searches, with their boosts. Mirrors the A/B/C
    /// weighting the Postgres tsvector used: title first, then subtype/address, then prose.</summary>
    private static readonly string[] SearchFields =
    [
        "title^3", "title.autocomplete^2", "propertySubtype.text^2",
        "address.city.text^2", "address.metroArea.text^2", "address.neighborhood.text^2",
        "descriptionText", "body",
        // Prefix matching over the flattened text, so a half-typed word still matches
        // something in the description — not just the title. Without this, search-as-you-type
        // had *worse* recall than the Postgres tsvector, whose `term:*` covered every field.
        // Unboosted: a prefix hit should never outrank a real word match.
        "body.autocomplete",
    ];

    public async Task<(List<PropertyDocument> Items, long TotalCount)> SearchAsync(
        int page, int pageSize,
        string? propertyType = null,
        string? status = null,
        string? metroArea = null,
        double? minPrice = null,
        double? maxPrice = null,
        string? sort = null,
        string? q = null,
        CancellationToken ct = default)
    {
        var keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var must = new JsonArray();
        var should = new JsonArray();
        var filter = new JsonArray();

        if (keyword is null)
        {
            must.Add(new JsonObject { ["match_all"] = new JsonObject() });
        }
        else
        {
            must.Add(new JsonObject
            {
                ["multi_match"] = new JsonObject
                {
                    ["query"] = keyword,
                    ["fields"] = new JsonArray([.. SearchFields.Select(f => (JsonNode)f)]),
                    ["type"] = "best_fields",
                    // Tolerates typos: "warehse" still finds warehouses.
                    ["fuzziness"] = "AUTO",
                    // "up to 2 terms, require them all; beyond that, 70%". A flat 70% rounds
                    // down to 1 term for a two-word query, so "cold storage" would match
                    // anything merely containing "storage" (64 hits instead of 9). Longer
                    // queries still tolerate a junk word rather than returning nothing.
                    ["minimum_should_match"] = "2<70%",
                },
            });

            // best_fields scores a document by its single best field, so a city match adds
            // nothing on top of a title match — "industrial park portland" ranked Austin above
            // Portland. This rewards documents whose flattened text contains ALL the terms,
            // which is what actually makes them a better answer. It only reorders: recall is
            // unchanged because the clause is `should`, not `must`.
            should.Add(new JsonObject
            {
                ["match"] = new JsonObject
                {
                    ["body"] = new JsonObject
                    {
                        ["query"] = keyword,
                        ["operator"] = "and",
                        ["boost"] = 4,
                    },
                },
            });
        }

        if (!string.IsNullOrEmpty(propertyType)) filter.Add(Term("propertyType", propertyType));
        if (!string.IsNullOrEmpty(status)) filter.Add(Term("status", status));
        if (!string.IsNullOrEmpty(metroArea)) filter.Add(Term("address.metroArea", metroArea));

        if (minPrice.HasValue || maxPrice.HasValue)
        {
            var range = new JsonObject();
            if (minPrice.HasValue) range["gte"] = minPrice.Value;
            if (maxPrice.HasValue) range["lte"] = maxPrice.Value;
            filter.Add(new JsonObject
            {
                ["range"] = new JsonObject { ["askingPrice"] = range },
            });
        }

        var body = new JsonObject
        {
            ["from"] = Math.Max(0, (page - 1) * pageSize),
            ["size"] = pageSize,
            // Without this OpenSearch stops counting at 10,000 and totalCount goes wrong
            // (and pagination with it) once the corpus grows.
            ["track_total_hits"] = true,
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["must"] = must,
                    ["should"] = should,
                    ["filter"] = filter,
                },
            },
            ["sort"] = BuildSort(sort, keyword),
        };

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, $"/{settings.PropertiesAlias}/_search", ct,
            PostData.String(body.ToJsonString()));

        if (!res.Success)
            throw new InvalidOperationException($"Search failed ({res.HttpStatusCode}): {res.Body}");

        using var doc = JsonDocument.Parse(res.Body);
        var hits = doc.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();

        var items = hits.GetProperty("hits").EnumerateArray()
            .Select(h => h.GetProperty("_source").Deserialize<PropertyDocument>(JsonOptions)!)
            .ToList();

        return (items, total);
    }

    private static JsonObject Term(string field, string value) =>
        new() { ["term"] = new JsonObject { [field] = value } };

    /// <summary>
    /// Sort encodes field + direction in one value, matching the listings API. Every branch
    /// ends with entityId so equal-key rows keep a stable order across pages — without it,
    /// items can repeat or vanish while paging. "relevance" only applies with a keyword;
    /// otherwise every document scores the same and it degrades to the default.
    /// </summary>
    private static JsonArray BuildSort(string? sort, string? keyword)
    {
        // Postgres puts NULLs first on DESC; here missing values always sort last, which
        // keeps price-less properties off the top of "price: high to low".
        JsonObject By(string field, string dir) => new()
        {
            [field] = new JsonObject { ["order"] = dir, ["missing"] = "_last" },
        };

        var primary = (sort, keyword) switch
        {
            ("price_desc", _) => By("askingPrice", "desc"),
            ("price_asc", _) => By("askingPrice", "asc"),
            ("cap_desc", _) => By("capRate", "desc"),
            ("relevance", not null) => new JsonObject { ["_score"] = new JsonObject { ["order"] = "desc" } },
            _ => By("listedAt", "desc"),
        };

        return [primary, new JsonObject { ["entityId"] = new JsonObject { ["order"] = "asc" } }];
    }

    private static string ReadEmbeddedMapping()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("properties-index.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded index mapping not found.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
