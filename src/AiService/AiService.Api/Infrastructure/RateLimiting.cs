using System.Security.Claims;
using AiService.Business;
using StackExchange.Redis;

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

/// <summary>Why a request was let through, or why it was not.</summary>
public enum RateLimitVerdict
{
    /// <summary>Under both limits, or not subject to them at all.</summary>
    Admitted,

    /// <summary>This user has spent their permits for the window.</summary>
    WindowExceeded,

    /// <summary>This user already has as many questions in flight as they are allowed.</summary>
    ConcurrencyExceeded,

    /// <summary>Redis could not be reached, so the limiter refused rather than guessed.</summary>
    LimiterUnavailable,
}

/// <summary>
/// The result of admitting one request, and the thing that gives the concurrency slot
/// back. Disposal is the release: the slot is held for exactly as long as the request
/// runs, so the <c>finally</c> in the middleware is the only place that needs to know.
/// </summary>
public sealed class RateLimitAdmission(
    RateLimitVerdict verdict,
    TimeSpan retryAfter,
    Func<Task>? release = null) : IAsyncDisposable
{
    public static RateLimitAdmission Allowed(Func<Task>? release = null) =>
        new(RateLimitVerdict.Admitted, TimeSpan.Zero, release);

    public static RateLimitAdmission Refused(RateLimitVerdict verdict, TimeSpan retryAfter) =>
        new(verdict, retryAfter);

    public RateLimitVerdict Verdict { get; } = verdict;
    public bool IsAdmitted => Verdict == RateLimitVerdict.Admitted;

    /// <summary>What to advertise in the Retry-After header. Zero when admitted.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;

    public async ValueTask DisposeAsync()
    {
        if (release is not null) await release();
    }
}

/// <summary>
/// Both limits for one user on one endpoint group: the concurrency lease first, then the
/// sliding window.
///
/// <para><b>That order is deliberate.</b> Reversed, a user who fires six questions at once
/// under a concurrency limit of two would spend six window permits to run two questions —
/// punished four times over for one impatient click. Taking the lease first means a
/// request refused for being too parallel costs nothing from the budget that bounds spend
/// over time. It does mean the window check can fail after a lease has been taken, which
/// is why that path hands the lease straight back.</para>
/// </summary>
public class AiRateLimiter(
    IAiRateLimitStore store, RateLimitOptions options, ILogger<AiRateLimiter> logger)
{
    public async Task<RateLimitAdmission> AcquireAsync(
        AiRateLimitGroup group, string user, CancellationToken ct = default)
    {
        var limit = LimitFor(group, options);
        var leaseKey = $"{options.KeyPrefix}:concurrency:{group}:{user}";
        var windowKey = $"{options.KeyPrefix}:window:{group}:{user}";

        try
        {
            var lease = await store.TryAcquireLeaseAsync(
                leaseKey, limit.ConcurrentRequests, TimeSpan.FromSeconds(limit.LeaseTtlSeconds), ct);

            if (!lease.Allowed)
                return RateLimitAdmission.Refused(
                    RateLimitVerdict.ConcurrencyExceeded,
                    TimeSpan.FromSeconds(limit.BusyRetryAfterSeconds));

            var window = await store.TryConsumeWindowAsync(
                windowKey, limit.PermitLimit, TimeSpan.FromSeconds(limit.WindowSeconds), ct);

            if (!window.Allowed)
            {
                // Not released on a best-effort basis: leaving it would cost this user a
                // concurrency slot for the whole lease TTL on a request that never ran.
                await store.ReleaseLeaseAsync(leaseKey, lease.LeaseId!, CancellationToken.None);
                return RateLimitAdmission.Refused(RateLimitVerdict.WindowExceeded, window.RetryAfter);
            }

            // CancellationToken.None on release: by the time this runs the request is
            // over, and its token is cancelled on exactly the disconnect case where
            // giving the slot back matters most.
            return RateLimitAdmission.Allowed(
                () => store.ReleaseLeaseAsync(leaseKey, lease.LeaseId!, CancellationToken.None));
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // Refused, not admitted. Nothing here can tell whether this user is over
            // budget, and the two ways of being wrong are not comparable: guessing "under"
            // turns a Redis outage into an unbounded Claude bill for as long as it lasts,
            // while guessing "over" costs two endpoints their availability — bounded, and
            // loud enough that someone fixes Redis. See RateLimitOptions.
            logger.LogError(ex,
                "Rate limit store unavailable; refusing {Group} request for {User}.", group, user);
            return RateLimitAdmission.Refused(
                RateLimitVerdict.LimiterUnavailable,
                TimeSpan.FromSeconds(limit.BusyRetryAfterSeconds));
        }
    }

    /// <summary>
    /// Redis being unreachable, slow, or mid-failover — the cases the fail-closed
    /// policy above is about.
    ///
    /// <para>Caught by type rather than with a bare <c>catch</c>, so that a bug in the
    /// Lua (<see cref="RedisServerException"/> — a syntax error, a wrong argument count)
    /// surfaces as a 500 during development instead of hiding behind whichever failure
    /// policy happens to be configured. A limiter that silently stops limiting because
    /// its script no longer parses is the failure mode worth being loud about.</para>
    /// </summary>
    private static bool IsStoreFailure(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or TimeoutException
            or ObjectDisposedException;

    private static EndpointLimit LimitFor(AiRateLimitGroup group, RateLimitOptions options) =>
        group == AiRateLimitGroup.Assistant ? options.Assistant : options.DealQa;
}

/// <summary>
/// Applies <see cref="AiRateLimiter"/> to endpoints marked with
/// <see cref="AiRateLimitAttribute"/>, and turns a refusal into the standard error
/// envelope.
///
/// <para><b>Hand-rolled, rather than <c>Microsoft.AspNetCore.RateLimiting</c> over a
/// Redis <c>RateLimiter</c>.</b> The framework's abstraction is shaped around a
/// synchronous <c>AttemptAcquire</c> plus an async queue, and a Redis round trip is
/// neither: every Redis implementation of it ends up making the synchronous path lie.
/// Two things this service actually needs also fall outside it — releasing the
/// concurrency slot in a <c>finally</c> tied to the request's own lifetime, and a
/// Retry-After computed from the store's state. The previous version documented, at
/// length, that lease metadata does not carry a usable RETRY_AFTER when nothing is
/// queued; a <c>try</c>/<c>finally</c> around <c>next</c> and a number read straight off
/// the sorted set are both plainer than working around that.</para>
///
/// <para><b>Nor an off-the-shelf package.</b> RedisRateLimiting and friends exist and
/// are reasonable; they solve the same problem behind the same framework abstraction,
/// which means inheriting its shape and still not getting the exact Retry-After. Against
/// roughly eighty lines of Lua whose atomicity is the entire point of the exercise, a
/// dependency that hides that Lua is a poor trade.</para>
///
/// <para><b>Where it sits.</b> After authentication (the partition is the "sub" claim)
/// and after routing (the group comes from endpoint metadata), and before MVC — so a
/// refusal happens before any action runs. That is what keeps the SSE endpoint honest:
/// AssistantController only calls <c>SseWriter.Start()</c> once the first event arrives,
/// and this runs long before that, so nothing has been written and the status line is
/// still ours to set.</para>
/// </summary>
public class AiRateLimitMiddleware(RequestDelegate next, RateLimitOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var group = context.GetEndpoint()?.Metadata.GetMetadata<AiRateLimitAttribute>()?.Group;
        if (!options.Enabled || group is null)
        {
            await next(context);
            return;
        }

        // Resolved here rather than injected, because when RateLimit:Enabled is false
        // there is no AiRateLimiter registered at all — AddAiRateLimiting returns before
        // it opens a Redis connection, so an eval run does not need the container. Taking
        // it as a parameter of this method asks DI for it on every request including the
        // ones that just returned above, and turns the off switch into a 500.
        var limiter = context.RequestServices.GetRequiredService<AiRateLimiter>();

        // Falls back to a single shared bucket only if an unauthenticated request somehow
        // reaches here; [Authorize] plus UseAuthorization ahead of this middleware means it
        // does not. A shared bucket is the safe direction to be wrong in.
        var user = context.User.FindFirstValue("sub") ?? "anonymous";

        await using var admission = await limiter.AcquireAsync(group.Value, user, context.RequestAborted);
        if (!admission.IsAdmitted)
        {
            await RefuseAsync(context, admission);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// The refusal, in the same envelope the controllers emit.
    ///
    /// <para>Built by hand rather than through ApiControllerBase: middleware runs before
    /// any controller exists. The shape has to match what the controllers emit, or a 429
    /// becomes the one error the client cannot parse.</para>
    ///
    /// <para>One status for every refusal, including a Redis outage. A 503 would be more
    /// literally accurate there — the caller has exceeded nothing — but it buys the client
    /// nothing it can act on, since the advice is "wait and retry" either way, and it
    /// costs a second refusal path that only executes when Redis is already down. The
    /// server log carries the distinction, which is where it is actually needed.</para>
    /// </summary>
    private static Task RefuseAsync(HttpContext context, RateLimitAdmission admission)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        // Rounded up, never to zero: a Retry-After of 0 invites an immediate retry that
        // earns the same refusal.
        context.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(admission.RetryAfter.TotalSeconds)).ToString();
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(
            new ErrorEnvelope(new ErrorBody(
                ErrorCodes.RateLimited,
                "Too many questions in a short time. Wait a moment and ask again.",
                [], context.TraceIdentifier, DateTime.UtcNow.ToString("O"))),
            context.RequestAborted);
    }
}

public static class RateLimiting
{
    /// <summary>
    /// Registers the limiter and, unless it is switched off, the Redis connection behind it.
    ///
    /// <para>Nothing is connected when <c>RateLimit:Enabled</c> is false: an eval run
    /// bypassing the limits should not also require the container to be up.</para>
    /// </summary>
    public static IServiceCollection AddAiRateLimiting(
        this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddSingleton(options);

        if (!options.Enabled) return services;

        var connection = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Redis is required when RateLimit:Enabled is true. "
                + "Set RateLimit:Enabled=false to run without per-user limits.");

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisConfig = ConfigurationOptions.Parse(connection);

            // The service must start even if Redis is not up yet — compose ordering,
            // a restart, a failover — and reconnect on its own when it comes back.
            // Commands then throw while it is down, which is what the fail-closed
            // policy in AiRateLimiter acts on; aborting here would instead turn a Redis
            // blip into a service that will not boot.
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ClientName = "ai-service-ratelimit";

            return ConnectionMultiplexer.Connect(redisConfig);
        });

        services.AddSingleton<IAiRateLimitStore, RedisRateLimitStore>();
        services.AddSingleton<AiRateLimiter>();

        return services;
    }

    public static IApplicationBuilder UseAiRateLimiter(this IApplicationBuilder app) =>
        app.UseMiddleware<AiRateLimitMiddleware>();
}
