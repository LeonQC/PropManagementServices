using System.Security.Claims;
using System.Threading.RateLimiting;
using AiService.Business;
using Microsoft.AspNetCore.RateLimiting;

namespace AiService.Api.Infrastructure;

/// <summary>Which set of limits an endpoint is subject to.</summary>
public enum AiRateLimitGroup
{
    Assistant,
    DealQa,
}

/// <summary>
/// Marks an action as spending money, and says which budget it spends from.
///
/// <para>An attribute rather than path matching in the limiter: the two are then declared
/// where a reader of the controller will see them, and adding a third AI endpoint without
/// a limit becomes a visible omission instead of a string that quietly fails to match.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AiRateLimitAttribute(AiRateLimitGroup group) : Attribute
{
    public AiRateLimitGroup Group { get; } = group;
}

public static class RateLimiting
{
    /// <summary>
    /// Two chained limiters over the endpoints marked with <see cref="AiRateLimitAttribute"/>:
    /// a concurrency limit and a sliding window, both partitioned by the caller's "sub".
    ///
    /// <para>Chained via <see cref="PartitionedRateLimiter.CreateChained{TResource}"/> on the
    /// global limiter rather than expressed as two named policies, because a named policy
    /// resolves to exactly one partition per request — there is no way to say "this endpoint
    /// is subject to both of these" through <c>AddPolicy</c>. Endpoints without the attribute
    /// get <c>GetNoLimiter</c> from both, so health checks and Swagger are untouched.</para>
    ///
    /// <para>A rejection is a real HTTP 429 in the standard error envelope. That works for
    /// the SSE endpoint too: AssistantController only calls <c>SseWriter.Start()</c> once the
    /// first event arrives from the service, and this middleware runs well before the action,
    /// so nothing has been written and the status line is still ours to set.</para>
    /// </summary>
    public static IServiceCollection AddAiRateLimiting(
        this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Partition(context, options, "concurrency", (key, limit) =>
                        RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = limit.ConcurrentRequests,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }))),

                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Partition(context, options, "window", (key, limit) =>
                        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = limit.PermitLimit,
                            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                            SegmentsPerWindow = limit.SegmentsPerWindow,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }))));

            limiter.OnRejected = async (context, ct) =>
            {
                var retryAfter = RetryAfter(LimitFor(context.HttpContext, options));

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

                // Built by hand rather than through ApiControllerBase: middleware runs
                // before any controller exists. The shape has to match what the controllers
                // emit, or a 429 becomes the one error the client can't parse.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorEnvelope(new ErrorBody(
                        ErrorCodes.RateLimited,
                        "Too many questions in a short time. Wait a moment and ask again.",
                        [],
                        context.HttpContext.TraceIdentifier,
                        DateTime.UtcNow.ToString("O"))),
                    ct);
            };
        });
    }

    /// <summary>
    /// One partition per (limiter, endpoint group, user), or no limiter at all for
    /// everything else.
    ///
    /// <para>The prefix keeps the concurrency and window limiters from colliding on a key,
    /// and the group keeps the assistant's budget separate from Deal Q&amp;A's — a user
    /// exhausting the cheap endpoint must not lock themselves out of the expensive one, or
    /// the other way round.</para>
    /// </summary>
    private static RateLimitPartition<string> Partition(
        HttpContext context, RateLimitOptions options, string prefix,
        Func<string, EndpointLimit, RateLimitPartition<string>> build)
    {
        var group = GroupFor(context);
        if (!options.Enabled || group is null) return RateLimitPartition.GetNoLimiter("none");

        // Falls back to a single shared bucket only if an unauthenticated request somehow
        // reaches here; [Authorize] plus UseAuthorization ahead of this middleware means it
        // does not. A shared bucket is the safe direction to be wrong in.
        var user = context.User.FindFirstValue("sub") ?? "anonymous";
        return build($"{prefix}:{group}:{user}", LimitFor(group.Value, options));
    }

    private static AiRateLimitGroup? GroupFor(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AiRateLimitAttribute>()?.Group;

    private static EndpointLimit LimitFor(HttpContext context, RateLimitOptions options) =>
        LimitFor(GroupFor(context) ?? AiRateLimitGroup.Assistant, options);

    private static EndpointLimit LimitFor(AiRateLimitGroup group, RateLimitOptions options) =>
        group == AiRateLimitGroup.Assistant ? options.Assistant : options.DealQa;

    /// <summary>
    /// How long to tell the caller to wait — computed, not read from the lease.
    ///
    /// <para>Reading it from the lease is the obvious approach and does not work here.
    /// Measured against .NET 9: a <see cref="SlidingWindowRateLimiter"/> configured with
    /// <c>QueueLimit = 0</c> lists RETRY_AFTER in <c>MetadataNames</c> but returns false
    /// from <c>TryGetMetadata</c> — it only ever computes a value for a request it
    /// actually queued, and we queue nothing. So a lease-metadata lookup silently falls
    /// through to the default on every rejection, which is a bug that looks like working
    /// code.</para>
    ///
    /// <para>The chained limiter does not report which of its two stages rejected, so this
    /// takes the larger of the two waits: how long until the question ahead is likely done
    /// (the measured P95, for a concurrency rejection) and how long until a window permit
    /// frees up (one segment, for a window rejection). Deliberately an upper bound —
    /// advertising too long costs one client an extra wait, advertising too short invites
    /// a retry that earns another 429.</para>
    /// </summary>
    private static TimeSpan RetryAfter(EndpointLimit limit) =>
        TimeSpan.FromSeconds(Math.Max(
            limit.BusyRetryAfterSeconds,
            Math.Ceiling((double)limit.WindowSeconds / Math.Max(1, limit.SegmentsPerWindow))));
}
