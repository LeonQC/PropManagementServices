using System.Reflection;
using System.Text.Json;
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
        // Raw requests here rather than the generated helpers: these are the same two calls
        // you'd make in Dev Tools (HEAD /_alias/x, POST /_aliases), and the alias body is the
        // atomic-swap format we'll reuse when reindexing into properties_v2.
        var aliasExists = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.HEAD, $"/_alias/{settings.Alias}", ct, null);

        if (aliasExists.HttpStatusCode == 200) return;

        var body = $$"""
        { "actions": [ { "add": { "index": "{{settings.PropertiesIndex}}", "alias": "{{settings.Alias}}" } } ] }
        """;

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, "/_aliases", ct, PostData.String(body));

        if (!res.Success)
            throw new InvalidOperationException($"Could not create alias {settings.Alias}: {res.Body}");

        logger.LogInformation("Pointed alias {Alias} at {Index}.", settings.Alias, settings.PropertiesIndex);
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
            OpenSearch.Net.HttpMethod.GET, $"/{settings.Alias}/_count", ct, null);

        if (!res.Success) return -1;

        using var doc = JsonDocument.Parse(res.Body);
        return doc.RootElement.GetProperty("count").GetInt64();
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
