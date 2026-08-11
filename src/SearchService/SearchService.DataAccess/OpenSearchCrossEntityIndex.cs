using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSearch.Net;
using SearchService.Models;

namespace SearchService.DataAccess;

/// <summary>Cross-entity search over the group alias.</summary>
public interface ICrossEntityIndex
{
    Task<(List<SearchHit> Items, long TotalCount)> SearchAsync(
        int page, int pageSize, string? q = null, string? entityType = null,
        CancellationToken ct = default);
}

/// <summary>
/// Queries every entity index at once through the <c>proptrack_search</c> group alias.
///
/// <para>This only works because both mappings declare <c>title</c> and <c>body</c>
/// identically, with the same analyzers — OpenSearch resolves a field name across all the
/// indices an alias spans, so a field that means different things in each is a silent
/// correctness problem, not an error. Which is why this queries the shared envelope only:
/// entity-specific fields belong to the per-entity endpoints. Note in particular that
/// <c>updatedAt</c> exists in both with *different* date formats, so it must never be sorted
/// or ranged on here.</para>
/// </summary>
public sealed class OpenSearchCrossEntityIndex(
    IOpenSearchLowLevelClient client,
    OpenSearchSettings settings) : ICrossEntityIndex
{
    /// <summary>The shared envelope, and nothing else. Same boosts and analyzers on both
    /// sides, so a property and a deal matching equally well score equally well.</summary>
    private static readonly string[] SearchFields =
    [
        "title^3", "title.autocomplete^2", "body", "body.autocomplete",
    ];

    /// <summary>How much of `body` comes back as the preview line.</summary>
    private const int SnippetLength = 200;

    public async Task<(List<SearchHit> Items, long TotalCount)> SearchAsync(
        int page, int pageSize, string? q = null, string? entityType = null,
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
                    ["fuzziness"] = "AUTO",
                    ["minimum_should_match"] = "2<70%",
                },
            });

            // Same rank-only boost the per-entity queries apply: reward documents whose
            // flattened text contains all the terms, without changing recall.
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

        if (!string.IsNullOrEmpty(entityType))
            filter.Add(new JsonObject { ["term"] = new JsonObject { ["entityType"] = entityType } });

        var body = new JsonObject
        {
            ["from"] = Math.Max(0, (page - 1) * pageSize),
            ["size"] = pageSize,
            ["track_total_hits"] = true,
            ["_source"] = new JsonArray("entityType", "entityId", "title", "body"),
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["must"] = must,
                    ["should"] = should,
                    ["filter"] = filter,
                },
            },
            // Score only. A field sort would have to be mapped compatibly in every index the
            // alias spans; entityId is the one tiebreaker that is, so it does that job.
            ["sort"] = new JsonArray(
                new JsonObject { ["_score"] = new JsonObject { ["order"] = "desc" } },
                new JsonObject { ["entityId"] = new JsonObject { ["order"] = "asc" } }),
        };

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, $"/{settings.GroupAlias}/_search", ct,
            PostData.String(body.ToJsonString()));

        if (!res.Success)
            throw new InvalidOperationException($"Search failed ({res.HttpStatusCode}): {res.Body}");

        using var doc = JsonDocument.Parse(res.Body);
        var hits = doc.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();

        var items = hits.GetProperty("hits").EnumerateArray().Select(h =>
        {
            var src = h.GetProperty("_source");
            var text = Read(src, "body");
            return new SearchHit(
                Read(src, "entityType") ?? "unknown",
                Read(src, "entityId") ?? "",
                Read(src, "title") ?? "",
                text is null ? null : text[..Math.Min(text.Length, SnippetLength)],
                // A match_all query scores every document 0, which serialises as null.
                h.GetProperty("_score").ValueKind == JsonValueKind.Null ? 0 : h.GetProperty("_score").GetDouble());
        }).ToList();

        return (items, total);
    }

    private static string? Read(JsonElement source, string name) =>
        source.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
