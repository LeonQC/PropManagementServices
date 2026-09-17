using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AiService.Business.Workers;

/// <summary>
/// Bounded retry around a model call.
///
/// <para>The shared Kafka consumer auto-commits offsets and swallows handler exceptions, so a
/// message that fails is dropped rather than redelivered. Retrying here is what stops a single
/// blip from silently costing a deal its rationale until someone republishes.</para>
///
/// <para>Deliberately small. The handler blocks the consume loop while it waits, so three
/// attempts against a 30-second client timeout is already a minute and a half of one partition
/// standing still. Raising the attempt count means lowering the timeout.</para>
/// </summary>
public class ModelCallPolicy(ILogger<ModelCallPolicy> logger, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const int MaxAttempts = 3;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

    // Injectable so the tests assert the backoff shape without sleeping through it.
    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((d, token) => Task.Delay(d, token));

    /// <summary>Runs <paramref name="call"/>, retrying transient failures. Returns null when
    /// every attempt failed, so the caller can decline to record the work as done.</summary>
    public async Task<T?> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> call, string context, CancellationToken ct) where T : class
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await call(ct);
            }
            catch (OperationCanceledException)
            {
                // Shutdown, not failure. Never retried, never swallowed.
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogError(ex, "{Context}: giving up after {Attempts} attempts", context, MaxAttempts);
                    return null;
                }

                var wait = BackoffFor(attempt);
                logger.LogWarning(ex, "{Context}: attempt {Attempt} failed, retrying in {Delay}",
                    context, attempt, wait);
                await _delay(wait, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Context}: permanent failure, not retrying", context);
                return null;
            }
        }

        return null;
    }

    /// <summary>Exponential with full jitter, capped. Jitter matters on a cold start, where a
    /// provider blip would otherwise line every retry up on the same instant.</summary>
    private static TimeSpan BackoffFor(int attempt)
    {
        var scaled = BaseDelay * Math.Pow(2, attempt - 1);
        var capped = scaled > MaxDelay ? MaxDelay : scaled;
        return capped * Random.Shared.NextDouble();
    }

    /// <summary>
    /// Transient faults are worth another attempt; everything else is not. Unknown failures
    /// count as permanent on purpose: retrying a genuinely broken request costs three ledger
    /// rows and a blocked consumer per message, times every deal in the pipeline.
    /// </summary>
    private static bool IsRetryable(Exception ex, CancellationToken ct)
    {
        // The client wraps its cause, so classification has to look through it.
        var cause = ex is RationaleException ? ex.InnerException ?? ex : ex;

        if (cause is HttpRequestException or SocketException or IOException) return true;

        // An HttpClient timeout surfaces as a cancellation that our own token did not request.
        if (cause is TaskCanceledException && !ct.IsCancellationRequested) return true;

        var message = ex.Message;
        return message.Contains("429") || message.Contains("500") || message.Contains("502")
            || message.Contains("503") || message.Contains("504")
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
}
