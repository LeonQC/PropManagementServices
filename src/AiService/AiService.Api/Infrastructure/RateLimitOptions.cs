namespace AiService.Api.Infrastructure;

/// <summary>
/// Per-user limits on the two endpoints that spend money, from the "RateLimit" config
/// section.
///
/// <para><b>In-process, not Redis.</b> Architecture §3.5 specifies a Redis-backed limiter,
/// and this is deliberately not that. ai-service runs as a single container, so a counter
/// in memory is the same counter every request sees, and
/// <c>Microsoft.AspNetCore.RateLimiting</c> ships in the shared framework — no new
/// dependency, no new thing to be down. Redis earns its place at exactly one trigger:
/// <b>the moment ai-service runs more than one instance</b>. Two replicas holding
/// independent in-process counters give every user double the intended limit, silently,
/// and the limit stops meaning anything. Scale this service out and this class must be
/// replaced before the replicas ship, not after.</para>
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
    /// require a code change to get through.
    /// </summary>
    public bool Enabled { get; set; } = true;

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

    /// <summary>Length of the window in seconds.</summary>
    public int WindowSeconds { get; set; }

    /// <summary>
    /// Segments the sliding window is divided into. Sliding rather than fixed because a
    /// fixed window lets a user spend a full window on each side of a boundary — twice the
    /// intended budget in a few seconds, which at up to $0.09 a question is real money.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>
    /// Questions this user may have in flight at once. Queue depth is deliberately zero
    /// everywhere: a queued question holds a connection for as long as the one ahead of it
    /// takes — up to the 90s wall clock — before its own work even begins, which reads as a
    /// hang. A prompt 429 the client can act on is the more honest answer.
    /// </summary>
    public int ConcurrentRequests { get; set; }

    /// <summary>
    /// How long to advertise in Retry-After when the <em>concurrency</em> limiter rejects.
    /// Nothing can know when the question ahead will finish, so this is an estimate: set it
    /// from the endpoint's measured P95, which is the honest answer to "when should I come
    /// back". The window's own wait is derived from <see cref="WindowSeconds"/> and
    /// <see cref="SegmentsPerWindow"/> rather than configured — see RateLimiting.RetryAfter
    /// for why neither is read from the lease.
    /// </summary>
    public int BusyRetryAfterSeconds { get; set; } = 30;
}
