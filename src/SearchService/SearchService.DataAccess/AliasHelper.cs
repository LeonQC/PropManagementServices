using Microsoft.Extensions.Logging;
using OpenSearch.Net;

namespace SearchService.DataAccess;

/// <summary>
/// Alias bookkeeping shared by both index classes. Each index joins two aliases at startup:
/// its own read alias, and the cross-entity group alias.
/// </summary>
internal static class AliasHelper
{
    /// <summary>
    /// Adds <paramref name="index"/> to <paramref name="alias"/> if it isn't already a member.
    ///
    /// <para>The existence check is deliberately scoped to the index — <c>HEAD /{index}/_alias/{alias}</c>,
    /// not <c>HEAD /_alias/{alias}</c>. A group alias spans several indices, and the unscoped
    /// form answers 200 as soon as *any* index has joined, so the second index to start would
    /// see "already exists" and silently never be added. That failure is invisible: the alias
    /// resolves, queries succeed, and half the corpus is just missing.</para>
    ///
    /// <para>Raw requests rather than the generated helpers: these are the same two calls
    /// you'd make in Dev Tools, and the alias body is the atomic-swap format a reindex into
    /// properties_v2 would reuse.</para>
    /// </summary>
    public static async Task EnsureAliasAsync(
        IOpenSearchLowLevelClient client, string index, string alias, ILogger logger, CancellationToken ct)
    {
        var aliasExists = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.HEAD, $"/{index}/_alias/{alias}", ct, null);

        if (aliasExists.HttpStatusCode == 200) return;

        var body = $$"""
        { "actions": [ { "add": { "index": "{{index}}", "alias": "{{alias}}" } } ] }
        """;

        var res = await client.DoRequestAsync<StringResponse>(
            OpenSearch.Net.HttpMethod.POST, "/_aliases", ct, PostData.String(body));

        if (!res.Success)
            throw new InvalidOperationException($"Could not add {index} to alias {alias}: {res.Body}");

        logger.LogInformation("Added {Index} to alias {Alias}.", index, alias);
    }
}
