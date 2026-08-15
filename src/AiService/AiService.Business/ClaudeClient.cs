using System.Diagnostics;
using AiService.DataAccess;
using AiService.Models;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiService.Business;

/// <summary>What a completed model call produced, for the caller to shape into a DTO.</summary>
public record ClaudeCompletion(string Text, int InputTokens, int OutputTokens, int LatencyMs, string? StopReason);

/// <summary>Raised when the model call fails; the controller turns this into a 503.</summary>
public class ClaudeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The single place that calls Claude, and the single place that writes to
/// ai_request_log. Pairing them is deliberate: a caller cannot spend tokens without
/// the spend being recorded, because the only way to make the call is through here.
/// Failures are logged too — an outage should show up in the ledger, not just the
/// application log.
/// </summary>
public class ClaudeClient(
    IOptions<AnthropicOptions> options,
    IAiRequestLogRepository requestLog,
    ILogger<ClaudeClient> logger)
{
    private readonly AnthropicOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string Model => _options.Model;

    public async Task<ClaudeCompletion> CompleteAsync(
        string systemPrompt,
        string userMessage,
        string feature,
        string? userId,
        string? entityId,
        int chunkCount,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ClaudeException("No Anthropic API key is configured for ai-service.");

        var parameters = new MessageParameters
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            Stream = false,
            System = [new SystemMessage(systemPrompt)],
            Messages = [new Message(RoleType.User, userMessage)],
        };

        // Only set when configured — the SDK serializes the property whenever it has a
        // value, and models that have deprecated temperature reject the request rather
        // than ignoring the field.
        if (_options.Temperature is { } temperature)
            parameters.Temperature = temperature;

        // Owned per call rather than injected: AnthropicClient is IDisposable and
        // wraps its own HttpClient, and this endpoint is user-initiated at human
        // pace — one Claude call per question — so pooling buys nothing here.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds) };
        using var client = new AnthropicClient(new APIAuthentication(_options.ApiKey), http)
        {
            // The SDK opts every request into nine betas by default (prompt-caching,
            // computer-use, skills, code-execution, files-api, …). Deal Q&A uses none
            // of them, and opting in is not free: the API rejects the request outright
            // with "Skills beta requires the code_execution tool to be included" —
            // a beta we never asked for failing a call that doesn't use it. Cleared so
            // this sends a plain, unversioned Messages request.
            AnthropicBetaVersion = "",
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
            stopwatch.Stop();

            var text = response.Message?.ToString() ?? response.FirstMessage?.Text ?? "";
            var usage = response.Usage;

            await LogAsync(feature, userId, entityId, chunkCount,
                usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0,
                (int)stopwatch.ElapsedMilliseconds, succeeded: true, error: null, ct);

            return new ClaudeCompletion(
                text, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0,
                (int)stopwatch.ElapsedMilliseconds, response.StopReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Claude call failed for feature {Feature}.", feature);

            // Type and message only — never response content, which may echo the
            // retrieved document text this table deliberately doesn't store.
            await LogAsync(feature, userId, entityId, chunkCount, 0, 0,
                (int)stopwatch.ElapsedMilliseconds, succeeded: false,
                error: $"{ex.GetType().Name}: {ex.Message}", ct);

            throw new ClaudeException("The model call failed.", ex);
        }
    }

    /// <summary>
    /// Records a decision not to call the model at all — the relevance floor rejected
    /// everything, so the question was answered for free. Logged so the ledger shows
    /// the whole question volume, not just the billable part.
    /// </summary>
    public Task LogSkippedCallAsync(string feature, string? userId, string? entityId, CancellationToken ct = default) =>
        LogAsync(feature, userId, entityId, chunkCount: 0, 0, 0, 0, succeeded: true,
                 error: null, ct);

    private async Task LogAsync(
        string feature, string? userId, string? entityId, int chunkCount,
        int inputTokens, int outputTokens, int latencyMs, bool succeeded, string? error,
        CancellationToken ct)
    {
        try
        {
            await requestLog.AddAsync(new AiRequestLog
            {
                Id = "",
                Feature = feature,
                Model = _options.Model,
                UserId = userId,
                EntityId = entityId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                LatencyMs = latencyMs,
                CostUsd = EstimateCost(inputTokens, outputTokens),
                ChunkCount = chunkCount,
                Succeeded = succeeded,
                Error = error,
                CreatedAt = DateTime.UtcNow.ToString("O"),
            }, ct);
        }
        catch (Exception ex)
        {
            // A failed ledger write must not turn a good answer into an error for the
            // user. Loud in the log, invisible in the response.
            logger.LogError(ex, "Failed to write ai_request_log row for feature {Feature}.", feature);
        }
    }

    private double EstimateCost(int inputTokens, int outputTokens) =>
        inputTokens / 1_000_000.0 * _options.InputCostPerMillionTokens +
        outputTokens / 1_000_000.0 * _options.OutputCostPerMillionTokens;
}
