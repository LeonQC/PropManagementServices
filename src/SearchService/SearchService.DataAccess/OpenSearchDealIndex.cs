using System.Reflection;
using System.Text.Json;
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
