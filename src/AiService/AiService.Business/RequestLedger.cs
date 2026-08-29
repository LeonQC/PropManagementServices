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
    public async Task RecordAsync(
        string feature, string model, string? userId, string? entityId, string? correlationId,
        int chunkCount, int inputTokens, int outputTokens, int latencyMs,
        double inputRatePerMillion, double outputRatePerMillion,
        bool succeeded, string? error, CancellationToken ct = default)
    {
        try
        {
            await requestLog.AddAsync(new AiRequestLog
            {
                Id = "",
                Feature = feature,
                Model = model,
                UserId = userId,
                EntityId = entityId,
                CorrelationId = correlationId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                LatencyMs = latencyMs,
                CostUsd = inputTokens / 1_000_000.0 * inputRatePerMillion
                          + outputTokens / 1_000_000.0 * outputRatePerMillion,
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
