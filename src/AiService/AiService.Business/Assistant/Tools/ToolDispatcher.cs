using System.Text.Json.Nodes;
using AiService.Business.Assistant.Clients;
using Microsoft.Extensions.Logging;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business.Assistant.Tools;

/// <summary>
/// Resolves a tool call to its implementation and runs it, turning every foreseeable
/// failure into a tool error the model can read and retry rather than an exception that
/// ends the question.
///
/// <para>That distinction is the whole design. A question may involve several tool calls;
/// one of them hitting a 403, a bad argument, or a dead downstream is a fact to report,
/// not grounds for abandoning the other five. The only thing that stops the loop is the
/// model call itself failing.</para>
/// </summary>
public class ToolDispatcher(IEnumerable<IAssistantTool> tools, ILogger<ToolDispatcher> logger)
{
    private readonly Dictionary<string, IAssistantTool> _byName =
        tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The tools array sent with every request, in a stable order.</summary>
    public IList<Tool> Definitions => [.. _byName.Values.OrderBy(t => t.Name, StringComparer.Ordinal)
                                                        .Select(t => t.Definition)];

    public string Label(string name, JsonNode? input) =>
        _byName.TryGetValue(name, out var tool) ? tool.Label(input) : $"Running {name}…";

    public async Task<ToolOutcome> InvokeAsync(
        ClaudeToolUse call, ToolContext context, CancellationToken ct)
    {
        if (!_byName.TryGetValue(call.Name, out var tool))
        {
            logger.LogWarning("Model called an unknown tool {Tool}.", call.Name);
            return ToolOutcome.Error(
                $"No tool named '{call.Name}' exists. Available tools: " +
                $"{string.Join(", ", _byName.Keys.Order(StringComparer.Ordinal))}.");
        }

        // A null input means the arguments were not valid JSON at all — the model gets told
        // that rather than the tool being run with nothing.
        if (call.Input is null)
            return ToolOutcome.Error(
                $"The arguments for '{call.Name}' were not valid JSON. Send the arguments again as a JSON object.");

        try
        {
            return await tool.InvokeAsync(call.Input, context, ct);
        }
        catch (ToolArgumentException ex)
        {
            logger.LogInformation("Rejected arguments for {Tool}: {Message}", call.Name, ex.Message);
            return ToolOutcome.Error(ex.Message);
        }
        catch (DownstreamException ex)
        {
            logger.LogWarning(ex, "Tool {Tool} failed downstream (denied={Denied}).", call.Name, ex.Denied);
            return ToolOutcome.Error(ex.Denied
                ? $"Access denied: {ex.Message} Tell the user you do not have access to this, and do not guess at its contents."
                : ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tool {Tool} threw an unexpected exception.", call.Name);
            return ToolOutcome.Error($"The '{call.Name}' tool failed unexpectedly. Do not retry it.");
        }
    }
}

/// <summary>
/// A tool call's arguments did not satisfy its schema. The message is written for the
/// model, not for a log: it names the offending field and the accepted values, because
/// the model's next move is to fix the call and try again.
/// </summary>
public class ToolArgumentException(string message) : Exception(message);
