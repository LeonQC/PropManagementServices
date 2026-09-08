using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AiService.Business.Assistant.Tools;
using AiService.DataAccess;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiService.Business.Assistant;

/// <summary>One turn of client-supplied conversation history.</summary>
public sealed record HistoryTurn(string Role, string Content);

/// <summary>
/// What the caller asked, and the scope the server pins onto it. The deal and document
/// ids come from the request body rather than the question text, so a scoped panel
/// cannot have its scope talked away by the model.
/// </summary>
public sealed record AskInput(
    string Question,
    IReadOnlyList<HistoryTurn>? History,
    string? DealId,
    string? DocumentId);

/// <summary>
/// The Deal Assistant (design doc §6.8): a bounded tool-use loop over read-only tools.
///
/// <para>The model does the routing. There is no intent classifier and no per-intent
/// handler, because a question like "which industrial deals are stalling, and what did
/// their Phase I reports say?" is a sequence of tool calls the model composes itself,
/// and hand-writing that sequencing is both more code and worse at the compositions
/// nobody anticipated.</para>
///
/// <para>What this class owns is everything the model must not: the budgets, the
/// forwarded token, the source numbering, and the decision to keep going.</para>
///
/// <para>Answer text reaches the browser through a chain of pull-based async-enumerables,
/// one fragment at a time, nothing buffered end-to-end:
///
/// <code>
/// AnthropicClient's raw stream
///   -&gt; ClaudeSession.StreamTurnAsync    yields ClaudeTurnEvent.TextDelta
///   -&gt; AssistantService.AskAsync        yields AssistantEvent.Delta      (this class)
///   -&gt; AssistantController.Ask          writes SSE "event: delta"
/// </code>
///
/// Each arrow is a caller doing <c>MoveNextAsync</c> on the layer below, not a callback —
/// nobody pushes an event down; every layer pulls the next one and immediately re-yields
/// its own version upward. A layer may also keep a local copy in passing (this class
/// appends every delta to <c>answer</c>, so the full text is there once the turn ends for
/// <see cref="SourceRegistry.Cited"/> to scan for citation markers).</para>
/// </summary>
public class AssistantService(
    ClaudeClient claude,
    ToolDispatcher tools,
    IPromptTemplateRepository prompts,
    RequestLedger ledger,
    IOptions<AssistantOptions> options,
    ILogger<AssistantService> logger)
{
    private readonly AssistantOptions _options = options.Value;

    public async IAsyncEnumerable<AssistantEvent> AskAsync(
        AskInput input,
        string bearerToken,
        string? userId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Both are taken before validation so that a question rejected at the door is
        // still one identifiable, timed row in ai_question_log. An outage that never
        // reaches the model is exactly the thing the ledger should make visible.
        var correlationId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        var question = input.Question?.Trim() ?? "";
        if (question.Length == 0)
        {
            await RecordRejectionAsync(correlationId, input, userId, stopwatch, ErrorCodes.Validation, ct);
            yield return new AssistantEvent.Failed(ErrorCodes.Validation, "A question is required.");
            yield break;
        }

        if (question.Length > _options.MaxQuestionChars)
        {
            await RecordRejectionAsync(correlationId, input, userId, stopwatch, ErrorCodes.Validation, ct);
            yield return new AssistantEvent.Failed(
                ErrorCodes.Validation,
                $"That question is too long — {_options.MaxQuestionChars} characters or fewer.");
            yield break;
        }

        if (!claude.IsConfigured)
        {
            await RecordRejectionAsync(correlationId, input, userId, stopwatch, ErrorCodes.AiUnavailable, ct);
            yield return new AssistantEvent.Failed(
                ErrorCodes.AiUnavailable, "The assistant is unavailable: no model API key is configured.");
            yield break;
        }

        var template = await prompts.GetActiveAsync(PromptFeatures.DealAssistant, ct);
        if (template is null)
        {
            logger.LogError("No active prompt template for feature {Feature}.", PromptFeatures.DealAssistant);
            await RecordRejectionAsync(correlationId, input, userId, stopwatch, ErrorCodes.AiUnavailable, ct);
            yield return new AssistantEvent.Failed(
                ErrorCodes.AiUnavailable, "The assistant is not configured on this server.");
            yield break;
        }

        var registry = new SourceRegistry();
        var context = new ToolContext(bearerToken, input.DealId, input.DocumentId, registry);

        List<Message> messages = [.. History(input), new Message(RoleType.User, BuildQuestion(question, input))];

        using var session = claude.StartSession(PromptFeatures.DealAssistant, userId, input.DealId, correlationId);
        logger.LogInformation(
            "Assistant question {CorrelationId} started (deal={DealId}, model={Model}).",
            correlationId, input.DealId ?? "-", session.Model);

        var answer = new StringBuilder();
        var iterations = 0;
        var toolCalls = 0;
        var contextChars = 0;
        var truncated = false;

        // Which budget bound first. The flag alone cannot say, and the four budgets call
        // for four different fixes — a wall-clock stop means the tools are slow, a
        // context stop means the results are fat.
        string? truncationReason = null;
        var owesAnswer = false;

        while (iterations < _options.MaxIterations)
        {
            if (stopwatch.Elapsed.TotalSeconds > _options.WallClockSeconds)
            {
                logger.LogInformation("Assistant question {CorrelationId} hit the wall clock.", correlationId);
                truncated = true;
                truncationReason ??= TruncationReasons.WallClock;
                break;
            }

            iterations++;

            // The enumerator is stepped by hand so a model failure can be caught: C#
            // forbids `yield return` inside a try that has a catch clause.
            var turnEvents = session.StreamTurnAsync(template.SystemPrompt, messages, tools.Definitions, ct);
            await using var turn = turnEvents.GetAsyncEnumerator(ct);

            ClaudeTurn? completed = null;
            while (true)
            {
                bool moved;
                ClaudeException? failure = null;
                try
                {
                    moved = await turn.MoveNextAsync();
                }
                catch (ClaudeException ex)
                {
                    failure = ex;
                    moved = false;
                }

                if (failure is not null)
                {
                    logger.LogWarning(failure, "Assistant question {CorrelationId} failed mid-turn.", correlationId);
                    stopwatch.Stop();
                    await ledger.RecordQuestionAsync(
                        PromptFeatures.DealAssistant, session.Model, userId, input.DealId, correlationId,
                        (int)stopwatch.ElapsedMilliseconds, iterations, toolCalls,
                        truncated, truncationReason, succeeded: false, ErrorCodes.AiUnavailable, ct);
                    yield return new AssistantEvent.Failed(
                        ErrorCodes.AiUnavailable,
                        "The assistant is temporarily unavailable. Try again in a moment.");
                    yield break;
                }

                if (!moved) break;

                switch (turn.Current)
                {
                    case ClaudeTurnEvent.TextDelta(var text):
                        // Relay, not a callback: pulled from ClaudeSession, kept for
                        // citation-scanning, re-yielded for AssistantController to frame as
                        // SSE. See the class doc above for the full four-layer chain.
                        answer.Append(text);
                        yield return new AssistantEvent.Delta(text);
                        break;
                    case ClaudeTurnEvent.Completed(var result):
                       // ClaudeTurn 
                        completed = result;
                        break;
                }
            }

            // No turn at all means the stream ended without a completion event, which is
            // an SDK-level surprise rather than a model outcome. Stop rather than loop.
            if (completed is null)
            {
                logger.LogWarning("Assistant turn {Iteration} produced no completion event.", iterations);
                break;
            }

            if (!completed.WantsTools)
            {
                owesAnswer = false;
                break;
            }

            messages.Add(completed.AssistantMessage);

            // Every tool_result for one assistant turn must go back in a single user
            // message. Splitting them across messages is valid JSON and trains the model
            // out of ever issuing parallel calls again.
            List<ContentBase> results = [];
            foreach (var call in completed.ToolUses)
            {
                if (toolCalls >= _options.MaxToolCalls)
                {
                    truncated = true;
                    truncationReason ??= TruncationReasons.ToolCalls;
                    results.Add(Result(call.Id,
                        $"Tool-call budget exhausted ({_options.MaxToolCalls} calls). Answer from what you " +
                        "already have, and say plainly that you stopped short of checking everything.", isError: true));
                    continue;
                }

                toolCalls++;
                yield return new AssistantEvent.Status(iterations, call.Name, tools.Label(call.Name, call.Input));

                var outcome = await tools.InvokeAsync(call, context, ct);

                var text = outcome.Text;
                if (contextChars + text.Length > _options.MaxContextChars)
                {
                    truncated = true;
                    truncationReason ??= TruncationReasons.ContextChars;
                    var room = Math.Max(0, _options.MaxContextChars - contextChars);
                    text = room < 200
                        ? "Context budget exhausted; this result was not read. Answer from what you already have " +
                          "and say that you stopped short of checking everything."
                        : text[..room] + "\n[truncated: context budget reached — this result is incomplete]";
                }

                contextChars += text.Length;
                results.Add(Result(call.Id, text, outcome.IsError));

                yield return new AssistantEvent.ToolFinished(
                    iterations, call.Name, outcome.Summary, outcome.Capped, outcome.IsError);
            }

            messages.Add(new Message { Role = RoleType.User, Content = results });
            owesAnswer = true;

            if (iterations >= _options.MaxIterations)
            {
                logger.LogInformation(
                    "Assistant question {CorrelationId} hit the iteration cap.", correlationId);
                truncated = true;
                truncationReason ??= TruncationReasons.Iterations;
            }
        }

        // Exiting on a budget while the model still wanted tools leaves the user with a
        // list of progress lines and no answer, which reads as a hang. One more turn with
        // no tools offered forces it to answer from what it already gathered — and the
        // prompt tells it to say plainly that it stopped short.
        if (owesAnswer)
        {
            logger.LogInformation(
                "Assistant question {CorrelationId} forcing a final answer after a budget stop.", correlationId);

            messages.Add(new Message(RoleType.User,
                "You have run out of tool budget for this question. Answer now from what you have already " +
                "gathered, and state plainly what you were not able to check."));

            var finalEvents = session.StreamTurnAsync(template.SystemPrompt, messages, tools: null, ct);
            await using var final = finalEvents.GetAsyncEnumerator(ct);

            while (true)
            {
                bool moved;
                var failed = false;
                try
                {
                    moved = await final.MoveNextAsync();
                }
                catch (ClaudeException ex)
                {
                    logger.LogWarning(ex, "Forced final answer failed for {CorrelationId}.", correlationId);
                    failed = true;
                    moved = false;
                }

                if (failed) break;
                if (!moved) break;

                if (final.Current is ClaudeTurnEvent.TextDelta(var text))
                {
                    answer.Append(text);
                    yield return new AssistantEvent.Delta(text);
                }
            }
        }

        stopwatch.Stop();

        var cited = registry.Cited(answer.ToString());
        yield return new AssistantEvent.Citations(cited.Count > 0 ? cited : registry.All);

        logger.LogInformation(
            "Assistant question {CorrelationId} finished: {Iterations} iteration(s), {ToolCalls} tool call(s), " +
            "{Sources} source(s), truncated={Truncated} ({Reason}), {Elapsed}ms.",
            correlationId, iterations, toolCalls, registry.Count, truncated,
            truncationReason ?? "-", stopwatch.ElapsedMilliseconds);

        // The same figure the `done` event carries below, persisted rather than only
        // streamed: ai_request_log knows model time per turn, and nothing else knows how
        // long the user waited.
        await ledger.RecordQuestionAsync(
            PromptFeatures.DealAssistant, session.Model, userId, input.DealId, correlationId,
            (int)stopwatch.ElapsedMilliseconds, iterations, toolCalls,
            truncated, truncationReason, succeeded: true, error: null, ct);

        yield return new AssistantEvent.Done(
            session.Model, iterations, toolCalls, (int)stopwatch.ElapsedMilliseconds, truncated);
    }

    /// <summary>
    /// Closes out a question that was refused before any model call — no model, no turns,
    /// no tool calls. Recorded anyway so the ledger counts every question asked, not only
    /// the ones that got as far as spending money.
    /// </summary>
    private Task RecordRejectionAsync(
        string correlationId, AskInput input, string? userId, Stopwatch stopwatch, string code,
        CancellationToken ct)
    {
        stopwatch.Stop();
        return ledger.RecordQuestionAsync(
            PromptFeatures.DealAssistant, model: null, userId, input.DealId, correlationId,
            (int)stopwatch.ElapsedMilliseconds, iterations: 0, toolCalls: 0,
            truncated: false, truncationReason: null, succeeded: false, code, ct);
    }

    private static ToolResultContent Result(string toolUseId, string text, bool isError) => new()
    {
        ToolUseId = toolUseId,
        Content = [new TextContent { Text = text }],
        IsError = isError ? true : null,
    };

    /// <summary>
    /// The client sends conversation history (there is no threads table in v1), so it is
    /// untrusted input in shape as well as content: roles are normalised and the list is
    /// trimmed to the most recent turns before anything reaches the model.
    /// </summary>
    private IEnumerable<Message> History(AskInput input)
    {
        if (input.History is not { Count: > 0 } history) yield break;

        foreach (var turn in history.TakeLast(_options.MaxHistoryTurns))
        {
            var content = turn.Content?.Trim();
            if (string.IsNullOrEmpty(content)) continue;

            yield return new Message(
                turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? RoleType.Assistant
                    : RoleType.User,
                content);
        }
    }

    /// <summary>
    /// The question, prefixed with the scope the server pinned. Stating it in the turn
    /// rather than only enforcing it in the tools is what lets the model phrase its
    /// answer correctly — it should say "on this deal", not silently answer as if the
    /// whole portfolio were in view.
    /// </summary>
    private static string BuildQuestion(string question, AskInput input)
    {
        if (input.DealId is not { Length: > 0 }) return question;

        var sb = new StringBuilder();
        sb.AppendLine("<scope>");
        sb.AppendLine($"This question is about deal {input.DealId} only. Every tool call is pinned to it.");
        if (input.DocumentId is { Length: > 0 } documentId)
            sb.AppendLine($"Document searches are further restricted to document {documentId}.");
        sb.AppendLine("</scope>");
        sb.AppendLine();
        sb.Append(question);
        return sb.ToString();
    }
}
