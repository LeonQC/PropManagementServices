using AiService.DataAccess;
using AiService.Models;
using Microsoft.Extensions.Logging;

namespace AiService.Business;

/// <summary>
/// The single writer of the two ledger tables. Both the one-shot Deal Q&amp;A call and
/// every turn of the assistant's tool-use loop land here, so the ledger stays a complete
/// record of what was spent rather than a record of whichever caller remembered.
///
/// <para><see cref="RecordAsync"/> writes ai_request_log, one row per model turn.
/// <see cref="RecordQuestionAsync"/> writes ai_question_log, one row per user question —
/// the wall clock and the loop outcome, which no single turn knows.</para>
///
/// <para>Rates are passed in rather than read from options: the two features run on
/// different models at different prices, and a single price pair baked in here would
/// quietly misreport whichever feature didn't own the numbers. The row is priced at
/// call time so a later price change doesn't rewrite history.</para>
/// </summary>
public class RequestLedger(
    IAiRequestLogRepository requestLog,
    IAiQuestionLogRepository questionLog,
    ILogger<RequestLedger> logger)
{
    /// <summary>Cached input is billed at a tenth of the base rate on read, and at a
    /// quarter above it on write. Without these multipliers, switching prompt caching on
    /// would silently make every cost in the ledger wrong.</summary>
    private const double CacheReadMultiplier = 0.1;
    private const double CacheWriteMultiplier = 1.25;

    public async Task RecordAsync(
        string feature, string model, string? userId, string? entityId, string? correlationId,
        int chunkCount, int inputTokens, int outputTokens, int latencyMs,
        double inputRatePerMillion, double outputRatePerMillion,
        bool succeeded, string? error, CancellationToken ct = default,
        int cacheReadTokens = 0, int cacheWriteTokens = 0)
    {
        try
        {
            // InputTokens stays the honest total of everything the model read, cached or
            // not — a cache hit is a discount, not fewer tokens. Only the price differs,
            // which is why the three bands are summed separately below.
            var billedInput =
                inputTokens / 1_000_000.0 * inputRatePerMillion
                + cacheReadTokens / 1_000_000.0 * inputRatePerMillion * CacheReadMultiplier
                + cacheWriteTokens / 1_000_000.0 * inputRatePerMillion * CacheWriteMultiplier;

            await requestLog.AddAsync(new AiRequestLog
            {
                Id = "",
                Feature = feature,
                Model = model,
                UserId = userId,
                EntityId = entityId,
                CorrelationId = correlationId,
                InputTokens = inputTokens + cacheReadTokens + cacheWriteTokens,
                OutputTokens = outputTokens,
                LatencyMs = latencyMs,
                CostUsd = billedInput + outputTokens / 1_000_000.0 * outputRatePerMillion,
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

    /// <summary>
    /// Writes the one ai_question_log row that closes out a question: the wall clock the
    /// user actually waited, and how the loop ended.
    ///
    /// <para>Called once, after the last turn, because none of these values exist before
    /// then. That is also why it is a second table rather than a column on ai_request_log:
    /// a column would have to be back-filled onto a turn row chosen arbitrarily, turning
    /// an append-only ledger into one with an update path.</para>
    /// </summary>
    public async Task RecordQuestionAsync(
        string feature, string? model, string? userId, string? entityId, string correlationId,
        int totalLatencyMs, int iterations, int toolCalls,
        bool truncated, string? truncationReason,
        bool succeeded, string? error, CancellationToken ct = default)
    {
        try
        {
            await questionLog.AddAsync(new AiQuestionLog
            {
                Id = "",
                CorrelationId = correlationId,
                Feature = feature,
                Model = model,
                UserId = userId,
                EntityId = entityId,
                TotalLatencyMs = totalLatencyMs,
                Iterations = iterations,
                ToolCalls = toolCalls,
                Truncated = truncated,
                TruncationReason = truncationReason,
                Succeeded = succeeded,
                Error = error,
                CreatedAt = DateTime.UtcNow.ToString("O"),
            }, ct);
        }
        catch (Exception ex)
        {
            // Same rule as RecordAsync: a failed ledger write must not turn a good answer
            // into an error for the user. Loud in the log, invisible in the response.
            logger.LogError(ex, "Failed to write ai_question_log row for feature {Feature}.", feature);
        }
    }
}
