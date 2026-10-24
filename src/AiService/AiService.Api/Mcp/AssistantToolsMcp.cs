using System.Text.Json;
using System.Text.Json.Nodes;
using AiService.Business;
using AiService.Business.Assistant.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiService.Api.Mcp;

/// <summary>
/// Serves every assistant tool to outside MCP clients, straight from
/// <see cref="ToolDispatcher"/>: tools/list reads the dispatcher's definitions, and
/// tools/call runs the named tool through it.
///
/// <para>Bridged rather than redeclared. Each tool already owns its name, description and
/// JSON schema, and search_deals alone has seventeen parameters with enums built from
/// DealVocabulary. A second, hand-written copy for MCP would drift from the first the
/// next time either changed; reading the one definition keeps the in-app assistant and
/// MCP clients looking at the same contract.</para>
///
/// <para>Going through the dispatcher also keeps one error policy for both callers: a bad
/// argument, a 403 or a dead downstream comes back as a tool error the client's model can
/// read, exactly as it does for the in-app assistant.</para>
/// </summary>
public static class AssistantToolsMcp
{
    public static ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request, CancellationToken ct)
    {
        var dispatcher = request.Services!.GetRequiredService<ToolDispatcher>();

        var tools = dispatcher.Definitions.Select(definition => new Tool
        {
            Name = definition.Function.Name,
            Description = definition.Function.Description,
            InputSchema = JsonSerializer.SerializeToElement(definition.Function.Parameters),
            // Every assistant tool is a read. Clients use the hint to skip confirmation prompts.
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        });

        return ValueTask.FromResult(new ListToolsResult { Tools = [.. tools] });
    }

    public static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        var services = request.Services!;
        var dispatcher = services.GetRequiredService<ToolDispatcher>();
        var name = request.Params!.Name;

        // MCP hands arguments over as a dictionary of JSON values; the tools read a JSON
        // object. A call with no arguments (pipeline_summary) is an empty object, not null —
        // the dispatcher treats null as "the arguments were not valid JSON".
        var input = request.Params.Arguments is { } arguments
            ? JsonSerializer.SerializeToNode(arguments)
            : new JsonObject();

        var context = new ToolContext(
            BearerToken(services), PinnedDealId: null, PinnedDocumentId: null, new SourceRegistry());

        var outcome = await dispatcher.InvokeAsync(name, input, context, ct);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = outcome.Text }],
            IsError = outcome.IsError,
        };
    }

    /// <summary>
    /// The caller's own token, forwarded downstream as the controllers do. /mcp requires
    /// authorization, so a request that reaches here always has one.
    /// </summary>
    private static string BearerToken(IServiceProvider services)
    {
        var header = services.GetRequiredService<IHttpContextAccessor>()
            .HttpContext!.Request.Headers.Authorization.ToString();
        return header["Bearer ".Length..].Trim();
    }
}
