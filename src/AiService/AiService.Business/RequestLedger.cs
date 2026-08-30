using AiService.DataAccess;
using AiService.Models;
using Microsoft.Extensions.Logging;

namespace AiService.Business;

/// <summary>
/// The single writer of ai_request_log. Both the one-shot Deal Q&amp;A call and every
/// turn of the assistant's tool-use loop land here, so the ledger stays a complete
/// record of what was spent rather than a record of whichever caller remembered.
///
/// <para>Rates are passed in rather than read from options: the two features run on
/// different models at different prices, and a single price pair baked in here would
/// quietly misreport whichever feature didn't own the numbers. The row is priced at
/// call time so a later price change doesn't rewrite history.</para>
/// </summary>
public class RequestLedger(
    IAiRequestLogRepository requestLog,
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
}
