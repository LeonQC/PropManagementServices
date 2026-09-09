namespace AiService.Api.Infrastructure;

/// <summary>
/// Per-user limits on the two endpoints that spend money, from the "RateLimit" config
/// section.
///
/// <para><b>Redis-backed, because the counter has to be shared.</b> This was in-process
/// (<c>Microsoft.AspNetCore.RateLimiting</c>) for as long as ai-service ran as a single
/// container, and the trigger for replacing it was named in advance: the moment a second
/// replica exists, each one holds its own counter and every limit multiplies by the
/// replica count. That is measured, not assumed: two replicas behind a round-robin
/// balancer let one user spend 60 permits against a limit of 30, and run 4 questions at
/// once against a concurrency limit of 2 — exactly double, both times.</para>
///
/// <para><b>Fails closed.</b> If Redis cannot be reached the request is refused rather
/// than admitted. The usual advice is the opposite — a broken safety rail should not take
/// the road with it — but that assumes the limited thing is cheap. Every admitted request
/// here is a Claude call at up to $0.097, bounding that spend is the limiter's entire
/// job, and an outage is exactly when nobody is watching the ledger. Going dark on two
/// endpoints is bounded and immediately visible; an unbounded bill is neither.</para>
///
/// <para><b>Partitioned by user, never by IP.</b> Everyone here shares an office egress
/// address, so an IP partition would have the first analyst to ask three questions throttle
/// the whole floor. The key is the "sub" claim, which is also what
/// <see cref="ApiControllerBase.ActorId"/> and the ai_request_log ledger attribute cost to
/// — so a limit and a bill can be reconciled against the same identity.</para>
///
/// <para><b>Not a response cache.</b> A tempting alternative to limiting the assistant is
/// caching its answers by semantic similarity. That would be wrong here: answers are
/// grounded in live portfolio data, so a cached "how many deals are in underwriting?" is
/// confidently wrong the moment someone advances a deal — the exact failure the feature's
/// coverage-honesty design exists to prevent. And embedding similarity is not equivalence:
/// "occupancy below 70%" and "below 60%" are neighbours in embedding space with entirely
/// different answers. This is a different thing from the prompt caching in ClaudeSession,
/// which is exact prefix matching at the provider and cannot serve stale data.</para>
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// Off switch. scripts/eval_ragas.py drives bulk sequential questions under one
    /// account and is the first thing any per-user limit breaks; an eval run should not
    /// require a code change to get through. Off also means no Redis connection is
    /// opened at all, so an eval run does not need the container either.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Prefix for every key this service writes, so a shared Redis stays legible and
    /// <c>SCAN MATCH ai:rl:*</c> finds exactly this feature's keys and nothing else.
    /// </summary>
    public string KeyPrefix { get; set; } = "ai:rl";

    /// <summary>
    /// The assistant. Measured over 15 questions on 2026-09-07: P50 6.5s, P95 57.4s, and
    /// the cross-corpus shape alone P50 55.0s / P95 60.7s at $0.086 average, $0.097 worst.
    ///
    /// <para>Concurrency 1 is the limit that does the real work here. A cross-corpus
    /// question occupies a connection for the better part of a minute, so one-at-a-time
    /// already caps a user at roughly five expensive questions per five minutes — the
    /// window exists to bound the <em>cheap</em> shapes, which return in 2.8s and would
    /// otherwise permit a hundred per window.</para>
    ///
    /// <para>Ten per five minutes is about twice what active exploration looks like, and
    /// holds a single user's worst case near $6/hour — which the ledger will show.</para>
    /// </summary>
    public EndpointLimit Assistant { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 300,
        ConcurrentRequests = 1,
        BusyRetryAfterSeconds = 60,
        // Assistant:WallClockSeconds is 90; see EndpointLimit.LeaseTtlSeconds for why
        // this is comfortably above it rather than equal to it.
        LeaseTtlSeconds = 120,
    };

    /// <summary>
    /// Deal Q&amp;A. Measured P50 8.8s, P95 11.8s, $0.0137 average — a sixth of the
    /// assistant's cost per question, and five times faster.
    ///
    /// <para>Which is exactly why the window is not proportionally looser. Cheap and fast
    /// compounds: at concurrency 2 and ~9s a question, a per-minute limit would allow more
    /// spend per hour than the expensive endpoint does. Thirty per five minutes puts the
    /// two features at the same worst case — near $6/hour per user — rather than at the
    /// same request count.</para>
    /// </summary>
    public EndpointLimit DealQa { get; set; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 300,
        ConcurrentRequests = 2,
        BusyRetryAfterSeconds = 12,
        // One Claude call behind a 60s Anthropic timeout plus retrieval, so a question
        // cannot outlive this even in the worst case.
        LeaseTtlSeconds = 90,
    };
}

/// <summary>
/// One endpoint's limits. Two of them, because they bound different things: the window
/// bounds spend over time, the concurrency bounds how much work one user can have in
/// flight at once. Neither implies the other — ten questions per five minutes still allows
/// ten simultaneous ones.
/// </summary>
public class EndpointLimit
{
    /// <summary>Requests allowed per <see cref="WindowSeconds"/>, per user.</summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Length of the window in seconds. Sliding rather than fixed because a fixed window
    /// lets a user spend a full window on each side of a boundary — twice the intended
    /// budget in a few seconds, which at up to $0.09 a question is real money.
    ///
    /// <para>There is no <c>SegmentsPerWindow</c> any more. It existed because
    /// <c>SlidingWindowRateLimiter</c> approximates the window with a ring of counters
    /// and needed to be told how many; six meant budget came back in 50-second lumps
    /// rather than continuously. Redis holds a log of request timestamps instead, which
    /// is the same idea with the segment count taken to infinity: a permit returns at the
    /// exact instant the request that spent it turns 300 seconds old. Thirty timestamps
    /// per user is nothing to store, and it is what makes the Retry-After on a window
    /// rejection an exact figure rather than a guess.</para>
    /// </summary>
    public int WindowSeconds { get; set; }

    /// <summary>
    /// Questions this user may have in flight at once. Queue depth is deliberately zero
    /// everywhere: a queued question holds a connection for as long as the one ahead of it
    /// takes — up to the 90s wall clock — before its own work even begins, which reads as a
    /// hang. A prompt 429 the client can act on is the more honest answer.
    /// </summary>
    public int ConcurrentRequests { get; set; }

    /// <summary>
    /// How long a concurrency slot stays claimed if nobody gives it back.
    ///
    /// <para>The normal path releases in a <c>finally</c>, so this only matters when the
    /// instance holding the slot stops existing — SIGKILL, an OOM, a container replaced
    /// mid-question. Without it that user's permit is gone until someone flushes Redis by
    /// hand, and the failure looks like "the assistant stopped working for one person",
    /// which is a miserable thing to debug.</para>
    ///
    /// <para>Set it above the endpoint's own wall-clock ceiling, not equal to it: the two
    /// ways of being wrong are not symmetric. Too long, and a user waits out the tail of
    /// a lease nobody holds. Too short, and a request that is still legitimately running
    /// has its slot taken from under it and a second one joins it — the limit quietly
    /// stops holding while everything appears to work.</para>
    /// </summary>
    public int LeaseTtlSeconds { get; set; } = 120;

    /// <summary>
    /// What to advertise in Retry-After when the <em>concurrency</em> limiter rejects.
    /// Nothing can know when the question ahead will finish, so this is an estimate: set it
    /// from the endpoint's measured P95, which is the honest answer to "when should I come
    /// back". A window rejection does not use this — Redis can say exactly when the oldest
    /// request falls out of the window, so that number is computed rather than configured.
    /// </summary>
    public int BusyRetryAfterSeconds { get; set; } = 30;
}
