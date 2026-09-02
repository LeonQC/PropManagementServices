using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Function = Anthropic.SDK.Common.Function;
using Tool = Anthropic.SDK.Common.Tool;

namespace AiService.Business;

/// <summary>One tool call the model asked for, with its arguments already parsed.</summary>
public sealed record ClaudeToolUse(string Id, string Name, JsonNode? Input);

/// <summary>A finished assistant turn: what it said, what it wants to call next, and why it stopped.</summary>
public sealed record ClaudeTurn(
    string Text,
    IReadOnlyList<ClaudeToolUse> ToolUses,
    string? StopReason,
    Message AssistantMessage,
    int InputTokens,
    int OutputTokens,
    int LatencyMs)
{
    /// <summary>True when the model stopped to call tools rather than to finish answering.</summary>
    public bool WantsTools => ToolUses.Count > 0;
}

/// <summary>What a streaming turn emits: text as it arrives, then the assembled turn.</summary>
public abstract record ClaudeTurnEvent
{
    public sealed record TextDelta(string Text) : ClaudeTurnEvent;

    public sealed record Completed(ClaudeTurn Turn) : ClaudeTurnEvent;
}

/// <summary>
/// One question's worth of model calls, owning a single <see cref="AnthropicClient"/>
/// across every turn of the tool-use loop.
///
/// <para>Per-session rather than per-call, unlike <see cref="ClaudeClient.CompleteAsync"/>:
/// that path makes exactly one request, where this one makes up to six, and standing up
/// six HttpClients and six SDK clients per question buys nothing but socket churn.</para>
///
/// <para>Every turn writes its own ai_request_log row under a shared correlation id, so
/// "what did this question cost" and "how many turns did the loop take" are both a
/// group-by rather than a guess.</para>
/// </summary>
public sealed class ClaudeSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly RequestLedger _ledger;
    private readonly ILogger _logger;
    private readonly string _feature;
    private readonly string? _userId;
    private readonly string? _entityId;

    public string Model { get; }

    public string CorrelationId { get; }

    internal ClaudeSession(
        AnthropicOptions options, RequestLedger ledger, ILogger logger,
        string model, string feature, string? userId, string? entityId, string correlationId)
    {
        _options = options;
        _ledger = ledger;
        _logger = logger;
        _feature = feature;
        _userId = userId;
        _entityId = entityId;
        Model = model;
        CorrelationId = correlationId;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        _client = new AnthropicClient(new APIAuthentication(options.ApiKey), _http)
        {
            // Cleared for the same reason as the non-streaming client: the SDK opts every
            // request into nine betas by default, and the API rejects a request that
            // declares the skills beta without the code-execution tool. We use none of them.
            AnthropicBetaVersion = "",
        };
    }

    /// <summary>
    /// Streams one turn. Text arrives as <see cref="ClaudeTurnEvent.TextDelta"/> while the
    /// model writes it; the final <see cref="ClaudeTurnEvent.Completed"/> carries the
    /// assembled turn including any tool calls.
    /// </summary>
    public async IAsyncEnumerable<ClaudeTurnEvent> StreamTurnAsync(
        string systemPrompt,
        IReadOnlyList<Message> messages,
        IList<Tool>? tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var parameters = new MessageParameters
        {
            Model = Model,
            MaxTokens = _options.AssistantMaxTokens,
            Stream = true,
            System = [new SystemMessage(systemPrompt, Ephemeral())],
            Messages = [.. messages],

            // FineGrained, not AutomaticToolsAndSystem. The automatic mode caches only the
            // system prompt and the tool definitions — measured at ~5.6k tokens, which is
            // 7% of a question's input. The other 93% is the conversation, and it is
            // replayed in full on every turn: one measured question sent 77,089 input
            // tokens to reach a final context of 19,624, so 75% of what we pay for is
            // re-reading text the model was already shown.
            //
            // FineGrained keeps the automatic breakpoint on the tools and lets us put one
            // on the newest content too (see MarkCacheBreakpoint), so each turn reads the
            // whole prior conversation at a tenth of the price instead of full freight.
            PromptCaching = PromptCacheType.FineGrained,
        };

        MarkCacheBreakpoint(parameters.Messages);

        if (tools is { Count: > 0 })
        {
            parameters.Tools = tools;
            parameters.ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto };
        }

        if (_options.Temperature is { } temperature) parameters.Temperature = temperature;

        var text = new StringBuilder();
        List<ClaudeToolUse> toolUses = [];
        string? stopReason = null;
        var inputTokens = 0;
        var outputTokens = 0;
        var cacheReadTokens = 0;
        var cacheWriteTokens = 0;
        var stopwatch = Stopwatch.StartNew();

        // The enumerator is stepped by hand so a mid-stream failure can be caught and
        // recorded: C# forbids `yield return` inside a try that has a catch clause.
        var stream = _client.Messages.StreamClaudeMessageAsync(parameters, ct);
        await using var events = stream.GetAsyncEnumerator(ct);

        while (true)
        {
            bool moved;
            try
            {
                moved = await events.MoveNextAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Streaming model call failed for feature {Feature}.", _feature);
                await RecordAsync(0, 0, (int)stopwatch.ElapsedMilliseconds, false,
                    $"{ex.GetType().Name}: {ex.Message}", ct);
                throw new ClaudeException("The model call failed.", ex);
            }

            if (!moved) break;

            var e = events.Current;

            // message_start carries input tokens; message_delta carries output tokens and
            // the stop reason. Both are read defensively rather than by switching on the
            // event name, so a change in how the SDK labels events can't silently zero the
            // ledger.
            inputTokens = Math.Max(inputTokens,
                Math.Max(e.StreamStartMessage?.Usage?.InputTokens ?? 0, e.Usage?.InputTokens ?? 0));
            outputTokens = Math.Max(outputTokens, e.Usage?.OutputTokens ?? 0);

            // Cached input is reported in its own two bands and is NOT included in
            // InputTokens, so it has to be read separately or a cache hit looks like the
            // prompt shrank. Read is the discount; write is the turn that populated it.
            cacheReadTokens = Math.Max(cacheReadTokens,
                Math.Max(e.StreamStartMessage?.Usage?.CacheReadInputTokens ?? 0,
                         e.Usage?.CacheReadInputTokens ?? 0));
            cacheWriteTokens = Math.Max(cacheWriteTokens,
                Math.Max(e.StreamStartMessage?.Usage?.CacheCreationInputTokens ?? 0,
                         e.Usage?.CacheCreationInputTokens ?? 0));
            stopReason = e.Delta?.StopReason ?? e.StopReason ?? stopReason;

            // Tool calls arrive fully assembled on the final event — the SDK reassembles
            // the input_json_delta fragments itself, so there is nothing to accumulate here.
            if (e.ToolCalls is { Count: > 0 } calls)
                toolUses = [.. calls.Select(ToToolUse)];

            if (e.Delta?.Text is { Length: > 0 } delta)
            {
                // First hop of the streaming relay — see AssistantService's class doc for
                // the full chain from here to the browser's SSE frame.
                text.Append(delta);
                yield return new ClaudeTurnEvent.TextDelta(delta);
            }
        }

        stopwatch.Stop();

        if (stopReason == "tool_use" && toolUses.Count == 0)
            _logger.LogWarning(
                "Model stopped with stop_reason=tool_use but no tool calls were parsed from the stream.");

        await RecordAsync(inputTokens, outputTokens, (int)stopwatch.ElapsedMilliseconds, true, null, ct,
                          cacheReadTokens, cacheWriteTokens);

        if (cacheReadTokens + cacheWriteTokens > 0)
            _logger.LogDebug(
                "Turn cache usage: {Read} read, {Write} written, {Fresh} uncached.",
                cacheReadTokens, cacheWriteTokens, inputTokens);

        yield return new ClaudeTurnEvent.Completed(new ClaudeTurn(
            text.ToString(), toolUses, stopReason,
            BuildAssistantMessage(text.ToString(), toolUses),
            inputTokens, outputTokens, (int)stopwatch.ElapsedMilliseconds));
    }


    private static CacheControl Ephemeral() => new() { Type = CacheControlType.ephemeral };

    /// <summary>
    /// Puts a single cache breakpoint on the last content block of the conversation, so the
    /// entire prefix up to that point is cached and the next turn reads it back at a tenth
    /// of the input price.
    ///
    /// <para>Existing breakpoints are cleared first, and that is not tidiness. The API
    /// allows at most four, and the breakpoint has to move to the newest content each turn
    /// — leaving the previous one in place would accumulate one per turn and fail the
    /// request outright on turn five, midway through a question.</para>
    ///
    /// <para>The block being marked is almost always the last tool result, which is exactly
    /// the boundary we want: everything before it is settled history, everything after it is
    /// the model's next turn.</para>
    /// </summary>
    private static void MarkCacheBreakpoint(List<Message> messages)
    {
        foreach (var message in messages)
            foreach (var content in message.Content ?? [])
                content.CacheControl = null;

        var lastBlock = messages.LastOrDefault()?.Content?.LastOrDefault();
        if (lastBlock is not null) lastBlock.CacheControl = Ephemeral();
    }

    /// <summary>
    /// Rebuilds the assistant turn as content blocks so it can be replayed on the next
    /// request. The tool_use blocks have to go back verbatim — the API pairs a
    /// tool_result to its tool_use by id, and an assistant turn missing them is rejected.
    /// </summary>
    private static Message BuildAssistantMessage(string text, IReadOnlyList<ClaudeToolUse> toolUses)
    {
        List<ContentBase> content = [];
        if (text.Trim().Length > 0) content.Add(new TextContent { Text = text });
        foreach (var use in toolUses)
            content.Add(new ToolUseContent { Id = use.Id, Name = use.Name, Input = use.Input ?? new JsonObject() });

        // An assistant turn must not be empty. This only happens if the model returns
        // nothing at all, which the caller treats as a failed turn.
        if (content.Count == 0) content.Add(new TextContent { Text = "" });

        return new Message { Role = RoleType.Assistant, Content = content };
    }

    /// <summary>
    /// Normalises the SDK's tool-call arguments.
    ///
    /// <para><c>Function.Arguments</c> is not the object it looks like: the SDK hands back
    /// a JSON <em>string</em> node whose content is the argument JSON, so it has to be
    /// parsed a second time. Both shapes are handled because relying on that staying true
    /// would turn an SDK upgrade into every tool call silently arriving with no
    /// arguments.</para>
    /// </summary>
    private static ClaudeToolUse ToToolUse(Function call)
    {
        JsonNode? input = null;
        try
        {
            input = call.Arguments switch
            {
                JsonValue value when value.TryGetValue<string>(out var raw) =>
                    raw.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(raw),
                { } node => node,
                _ => new JsonObject(),
            };
        }
        catch (System.Text.Json.JsonException)
        {
            // Leave input null; the dispatcher reports it to the model as a bad-argument
            // tool error, which it can retry, rather than failing the whole question.
        }

        return new ClaudeToolUse(call.Id ?? "", call.Name ?? "", input);
    }

    private Task RecordAsync(
        int inputTokens, int outputTokens, int latencyMs, bool succeeded, string? error, CancellationToken ct,
        int cacheReadTokens = 0, int cacheWriteTokens = 0) =>
        _ledger.RecordAsync(
            _feature, Model, _userId, _entityId, CorrelationId,
            chunkCount: 0, inputTokens, outputTokens, latencyMs,
            _options.RatesFor(Model).InputPerMillion, _options.RatesFor(Model).OutputPerMillion,
            succeeded, error, ct, cacheReadTokens, cacheWriteTokens);

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
    }
}
