using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenSearch.Net;
using SearchService.Models;

namespace SearchService.DataAccess;

/// <summary>
/// OpenSearch-backed implementation of <see cref="IDealIndex"/>. Mirrors
/// <see cref="OpenSearchPropertyIndex"/> method for method, including the two traps it
/// documents: the alias check goes through a raw request, and the count spells out its URL
/// rather than calling <c>client.CountAsync(alias)</c>.
/// </summary>
public sealed class OpenSearchDealIndex(
    IOpenSearchLowLevelClient client,
    OpenSearchSettings settings,
    ILogger<OpenSearchDealIndex> logger) : IDealIndex
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);   // camelCase, matching the index mapping's field names

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        var exists = await client.Indices.ExistsAsync<StringResponse>(settings.DealsIndex, ctx: ct);
        if (exists.HttpStatusCode == 200)
        {
            logger.LogInformation("Index {Index} already exists.", settings.DealsIndex);
        }
        else
        {
            var mapping = ReadEmbeddedMapping();
            var created = await client.Indices.CreateAsync<StringResponse>(
                settings.DealsIndex, PostData.String(mapping), ctx: ct);

            if (!created.Success)
                throw new InvalidOperationException(
                    $"Could not create index {settings.DealsIndex}: {created.Body}");

            logger.LogInformation("Created index {Index} from the bundled mapping.", settings.DealsIndex);
        }

        await EnsureAliasAsync(ct);
    }

    private async Task EnsureAliasAsync(CancellationToken ct)
    {
        var aliasExists = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.HEAD, $"/_alias/{settings.DealsAlias}", ct, null);

        if (aliasExists.HttpStatusCode == 200) return;

        var body = $$"""
        { "actions": [ { "add": { "index": "{{settings.DealsIndex}}", "alias": "{{settings.DealsAlias}}" } } ] }
        """;

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, "/_aliases", ct, PostData.String(body));

        if (!res.Success)
            throw new InvalidOperationException($"Could not create alias {settings.DealsAlias}: {res.Body}");

        logger.LogInformation("Pointed alias {Alias} at {Index}.", settings.DealsAlias, settings.DealsIndex);
    }

    public async Task<bool> IndexAsync(DealDocument document, long version, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);

        // version_type=external: OpenSearch accepts the write only if `version` is greater than
        // what's stored. A replayed or out-of-order snapshot loses the race and 409s, which is
        // the desired outcome — not an error to retry.
        var res = await client.IndexAsync<StringResponse>(
            settings.DealsIndex,
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
        var res = await client.DeleteAsync<StringResponse>(settings.DealsIndex, entityId, ctx: ct);

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
        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.GET, $"/{settings.DealsAlias}/_count", ct, null);

        if (!res.Success) return -1;

        using var doc = JsonDocument.Parse(res.Body);
        return doc.RootElement.GetProperty("count").GetInt64();
    }

    /// <summary>Fields the keyword query searches, with their boosts. The deals tsvector this
    /// replaces weighted name^A and property_name^B and covered nothing else; comment and
    /// document text reach search for the first time here, through `body`.</summary>
    private static readonly string[] SearchFields =
    [
        "title^3", "title.autocomplete^2", "propertyName.text^2", "metroArea.text",
        "body",
        // Prefix matching over the flattened text, so a half-typed word still matches.
        // Unboosted: a prefix hit should never outrank a real word match.
        "body.autocomplete",
    ];

    public async Task<(List<DealDocument> Items, long TotalCount)> SearchAsync(
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
        bool? hasOverdueTasks = null,
        int? staleDays = null,
        string? q = null,
        CancellationToken ct = default)
    {
        var keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var now = DateTime.UtcNow;

        var must = new JsonArray();
        var should = new JsonArray();
        var filter = new JsonArray();
        var mustNot = new JsonArray();

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
                    ["fuzziness"] = "AUTO",
                    // "up to 2 terms, require them all; beyond that, 70%". A flat 70% rounds
                    // down to 1 term for a two-word query, matching anything with either word.
                    ["minimum_should_match"] = "2<70%",
                },
            });

            // best_fields scores a document by its single best field, so a property-name match
            // adds nothing on top of a name match. This rewards documents whose flattened text
            // contains ALL the terms. It only reorders: recall is unchanged because the clause
            // is `should`, not `must`.
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

        if (!string.IsNullOrEmpty(stage)) filter.Add(Term("stage", stage));
        if (!string.IsNullOrEmpty(ownerId)) filter.Add(Term("ownerId", ownerId));
        if (!string.IsNullOrEmpty(priority)) filter.Add(Term("priority", priority));
        if (!string.IsNullOrEmpty(propertyType)) filter.Add(Term("propertyType", propertyType));
        if (!string.IsNullOrEmpty(metroArea)) filter.Add(Term("metroArea", metroArea));

        // Postgres compares strings with CompareTo(...) < 0 / > 0 — strictly, and only on
        // non-null values. `lt`/`gt` match that, and a range never matches a document missing
        // the field, so the null guard comes for free.
        if (!string.IsNullOrEmpty(closeDateBefore))
            filter.Add(Range("projectedCloseDate", "lt", closeDateBefore));
        if (!string.IsNullOrEmpty(closeDateAfter))
            filter.Add(Range("projectedCloseDate", "gt", closeDateAfter));

        if (offerPriceMin.HasValue || offerPriceMax.HasValue)
            filter.Add(NumericRange("offerPrice", offerPriceMin, offerPriceMax));
        if (capRateMin.HasValue || capRateMax.HasValue)
            filter.Add(NumericRange("projectedCapRate", capRateMin, capRateMax));

        // Overdue = the deal has an open task due before today. Postgres asks that of the
        // child rows; here it's a range over the earliest open due date the snapshot carried,
        // which is the same question asked of the same data.
        if (hasOverdueTasks is bool overdue)
        {
            var clause = Range("earliestOpenTaskDueDate", "lt", now.ToString("yyyy-MM-dd"));
            // must_not also admits documents with no such date at all — deals with no tasks,
            // or none carrying a due date — exactly as `!Tasks.Any(...)` does.
            (overdue ? filter : mustNot).Add(clause);
        }

        // staleDays=N → entered the current stage at least N days ago. The cutoff is computed
        // here rather than handed to OpenSearch as `now-Nd`: date math rounds on the server
        // side, and this has to match the Postgres expression instant for instant.
        if (staleDays is int days && days > 0)
            filter.Add(Range("stageEnteredAt", "lt", now.AddDays(-days).ToString("O")));

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
                    ["must_not"] = mustNot,
                },
            },
            ["sort"] = BuildSort(keyword),
        };

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, $"/{settings.DealsAlias}/_search", ct,
            PostData.String(body.ToJsonString()));

        if (!res.Success)
            throw new InvalidOperationException($"Search failed ({res.HttpStatusCode}): {res.Body}");

        using var doc = JsonDocument.Parse(res.Body);
        var hits = doc.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();

        var items = hits.GetProperty("hits").EnumerateArray()
            .Select(h => h.GetProperty("_source").Deserialize<DealDocument>(JsonOptions)!)
            .ToList();

        return (items, total);
    }

    private static JsonObject Term(string field, string value) =>
        new() { ["term"] = new JsonObject { [field] = value } };

    private static JsonObject Range(string field, string op, string value) =>
        new() { ["range"] = new JsonObject { [field] = new JsonObject { [op] = value } } };

    private static JsonObject NumericRange(string field, double? gte, double? lte)
    {
        var range = new JsonObject();
        if (gte.HasValue) range["gte"] = gte.Value;
        if (lte.HasValue) range["lte"] = lte.Value;
        return new JsonObject { ["range"] = new JsonObject { [field] = range } };
    }

    /// <summary>
    /// The deals list has no sort parameter — ordering is implicit, and this mirrors it:
    /// newest first for a plain browse, relevance once a keyword narrows the set. Both end in
    /// entityId so equal-key rows keep a stable order across pages; without it, items can
    /// repeat or vanish while paging.
    /// </summary>
    private static JsonArray BuildSort(string? keyword)
    {
        JsonNode primary = keyword is null
            // Postgres puts NULLs first on DESC; missing values always sort last here. createdAt
            // is non-nullable, so this only matters if a document ever arrives without it.
            ? new JsonObject { ["createdAt"] = new JsonObject { ["order"] = "desc", ["missing"] = "_last" } }
            : new JsonObject { ["_score"] = new JsonObject { ["order"] = "desc" } };

        return [primary, new JsonObject { ["entityId"] = new JsonObject { ["order"] = "asc" } }];
    }

    private static string ReadEmbeddedMapping()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("deals-index.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded index mapping not found.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
