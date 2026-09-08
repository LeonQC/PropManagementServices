using StackExchange.Redis;

namespace AiService.Api.Infrastructure;

/// <summary>
/// The two limits, in Redis, as two Lua scripts.
///
/// <para><b>Why a script and not two commands.</b> Both limits are check-then-act: read
/// how much is in use, compare against the ceiling, and record a claim if there is room.
/// Split across two round trips that sequence has a hole in the middle. Two instances
/// both <c>ZCARD</c> a window holding 29 of 30, both conclude there is room, and both
/// <c>ZADD</c> — 31 requests admitted against a limit of 30. Nothing is corrupt and no
/// command failed; the read simply stopped being true before the write landed. Widening
/// the race to two instances is the whole bug this class replaces, so re-introducing it
/// one layer down would be a poor joke. A Lua script is Redis's atomic unit: the server
/// is single-threaded and runs the script to completion with no other client's commands
/// interleaved, so the decision and the claim are one indivisible step.</para>
///
/// <para><b>Why not INCR.</b> <c>INCR</c> is often offered as the atomic answer, and it
/// is — for a <em>fixed</em> window, because it returns the post-increment value, so the
/// decision can be made from the reply rather than from a prior read. It does not
/// survive contact with the requirements here: a fixed window lets a user spend a full
/// budget on each side of a boundary, which is the reason this service chose a sliding
/// window in the first place. And the usual <c>INCR</c> + <c>EXPIRE</c> pairing has a
/// race of its own — a process that dies between the two leaves a key with no TTL, and
/// that user is limited forever.</para>
///
/// <para><b>Time comes from Redis, not from the caller.</b> Every script reads
/// <c>TIME</c> rather than accepting a timestamp argument. Instances do not share a
/// clock, and a few seconds of skew on a 300s window is a few seconds of budget invented
/// or destroyed depending on which replica you happened to reach. One clock, at the one
/// place that sees every request, removes the question. (This makes the scripts
/// non-deterministic, which was a replication problem in Redis 3; since 5.0 scripts
/// replicate by their effects, so it is not one now.)</para>
///
/// <para><b>EVAL, not EVALSHA.</b> These scripts ship whole on every call — about half a
/// kilobyte on a service whose entire budget is thirty requests per five minutes per
/// user. Caching by hash would save bytes that do not matter and add a NOSCRIPT recovery
/// path that would be exercised only after a Redis restart, which is to say almost never,
/// which is to say it would be the least-tested code here.</para>
/// </summary>
public class RedisRateLimitStore(IConnectionMultiplexer redis) : IAiRateLimitStore
{
    /// <summary>
    /// Sliding window as a log: one sorted-set member per request, scored by the instant
    /// it happened. Trim everything older than the window, count what is left, and add
    /// only if that count is under the limit.
    ///
    /// <para>Returns <c>{allowed, retryAfterMs}</c>. On refusal the wait is exact rather
    /// than estimated: the oldest surviving entry's score plus the window is precisely
    /// when a permit next frees up.</para>
    /// </summary>
    private const string WindowScript = """
        local limit  = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local member = ARGV[3]

        local t = redis.call('TIME')
        local now = (tonumber(t[1]) * 1000) + math.floor(tonumber(t[2]) / 1000)

        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now - window)

        -- PEXPIRE goes AFTER the ZADD, on every path. Before it, the very first request
        -- from a user sets an expiry on a key that does not exist yet: PEXPIRE returns 0,
        -- does nothing, and the ZADD then creates the key with no TTL at all. That is the
        -- immortal-key bug reached by a different route -- the key outlives the window,
        -- its entries never age out of anything that trims them, and the user's budget
        -- comes back only by hand. Caught by a test, not by review.
        if redis.call('ZCARD', KEYS[1]) < limit then
          redis.call('ZADD', KEYS[1], now, member)
          redis.call('PEXPIRE', KEYS[1], window)
          return {1, 0}
        end

        local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        -- Refused, so the key certainly exists, and its TTL is pushed out to one window
        -- past now: the entries still in it have that long left to live.
        redis.call('PEXPIRE', KEYS[1], window)
        return {0, (tonumber(oldest[2]) + window) - now}
        """;

    /// <summary>
    /// Concurrency as leases with an expiry: one sorted-set member per in-flight
    /// request, scored by the instant that lease goes stale.
    ///
    /// <para>The score being an expiry rather than a start time is what makes a crashed
    /// instance harmless. A held slot is only held until its score passes; the next
    /// caller trims it away before counting, so a process that dies mid-question — SIGKILL,
    /// OOM, a container replaced under it — cannot keep a permit forever. The cost of
    /// getting that TTL wrong is one-sided and worth stating: too long and a user waits
    /// out the remainder of a lease nobody holds, too short and a still-running request
    /// loses its slot and a second one is admitted alongside it.</para>
    ///
    /// <para>Returns <c>1</c> if a slot was taken, <c>0</c> if all of them were in use.</para>
    /// </summary>
    private const string LeaseScript = """
        local limit = tonumber(ARGV[1])
        local ttl   = tonumber(ARGV[2])
        local id    = ARGV[3]

        local t = redis.call('TIME')
        local now = (tonumber(t[1]) * 1000) + math.floor(tonumber(t[2]) / 1000)

        -- Scores are expiry instants, so anything at or before now is a lease whose
        -- holder went away without giving it back.
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)

        -- After the ZADD, for the same reason as the window script: a PEXPIRE ahead of
        -- the first write is a no-op on a key that does not exist yet, and leaves the key
        -- with no expiry once it does.
        if redis.call('ZCARD', KEYS[1]) < limit then
          redis.call('ZADD', KEYS[1], now + ttl, id)
          redis.call('PEXPIRE', KEYS[1], ttl)
          return 1
        end
        redis.call('PEXPIRE', KEYS[1], ttl)
        return 0
        """;

    public async Task<WindowDecision> TryConsumeWindowAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var windowMs = (long)window.TotalMilliseconds;

        var reply = (RedisValue[])(await redis.GetDatabase().ScriptEvaluateAsync(
            WindowScript,
            [key],
            // A unique member per request: two requests landing in the same millisecond
            // are two entries, not one overwritten by the other. Scores collide freely;
            // members must not.
            [limit, windowMs, Guid.NewGuid().ToString("N")]))!;

        return (long)reply[0] == 1
            ? new WindowDecision(true, TimeSpan.Zero)
            : new WindowDecision(false, TimeSpan.FromMilliseconds((long)reply[1]));
    }

    public async Task<LeaseDecision> TryAcquireLeaseAsync(
        string key, int limit, TimeSpan ttl, CancellationToken ct = default)
    {
        var leaseId = Guid.NewGuid().ToString("N");

        var acquired = (long)(await redis.GetDatabase().ScriptEvaluateAsync(
            LeaseScript, [key], [limit, (long)ttl.TotalMilliseconds, leaseId]));

        return acquired == 1 ? new LeaseDecision(true, leaseId) : new LeaseDecision(false, null);
    }

    /// <summary>
    /// One command, no script: removing a member by name is already atomic, and there is
    /// no decision to make. A lease that expired first is simply not there, and ZREM
    /// reporting zero removals is the correct outcome, not an error.
    /// </summary>
    public Task ReleaseLeaseAsync(string key, string leaseId, CancellationToken ct = default) =>
        redis.GetDatabase().SortedSetRemoveAsync(key, leaseId);
}
