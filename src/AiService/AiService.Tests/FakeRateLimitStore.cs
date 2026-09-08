using AiService.Api.Infrastructure;
using StackExchange.Redis;

namespace AiService.Tests;

/// <summary>
/// A store that answers from memory, for the tests that are about
/// <see cref="AiRateLimiter"/>'s policy rather than about Redis: which limit is checked
/// first, what happens to a lease when the window then refuses, which Retry-After is
/// advertised. Those questions have nothing to do with atomicity, and running them
/// against a container would only make them slow.
///
/// <para>Deliberately not a second implementation of the sliding window — it counts
/// calls and hands back scripted answers. The moment a fake starts reimplementing the
/// thing under test, it starts being able to agree with a bug.</para>
/// </summary>
public class FakeRateLimitStore : IAiRateLimitStore
{
    public List<string> Calls { get; } = [];
    public List<string> ReleasedLeases { get; } = [];

    public bool NextWindowAllowed { get; set; } = true;
    public bool NextLeaseAllowed { get; set; } = true;
    public TimeSpan WindowRetryAfter { get; set; } = TimeSpan.FromSeconds(7);

    public Task<WindowDecision> TryConsumeWindowAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        Calls.Add($"window:{key}");
        return Task.FromResult(NextWindowAllowed
            ? new WindowDecision(true, TimeSpan.Zero)
            : new WindowDecision(false, WindowRetryAfter));
    }

    public Task<LeaseDecision> TryAcquireLeaseAsync(
        string key, int limit, TimeSpan ttl, CancellationToken ct = default)
    {
        Calls.Add($"lease:{key}");
        return Task.FromResult(NextLeaseAllowed
            ? new LeaseDecision(true, "lease-1")
            : new LeaseDecision(false, null));
    }

    public Task ReleaseLeaseAsync(string key, string leaseId, CancellationToken ct = default)
    {
        Calls.Add($"release:{key}");
        ReleasedLeases.Add(leaseId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A store that is down, exactly the way StackExchange.Redis reports being down. Used to
/// pin the fail-closed behaviour without needing a Redis to switch off.
/// </summary>
public class UnreachableRateLimitStore : IAiRateLimitStore
{
    private static RedisConnectionException Down() =>
        new(ConnectionFailureType.UnableToConnect, "No connection is active/available.");

    public Task<WindowDecision> TryConsumeWindowAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default) => throw Down();

    public Task<LeaseDecision> TryAcquireLeaseAsync(
        string key, int limit, TimeSpan ttl, CancellationToken ct = default) => throw Down();

    public Task ReleaseLeaseAsync(string key, string leaseId, CancellationToken ct = default) =>
        throw Down();
}

/// <summary>
/// A store whose script is broken — the case that must NOT be swallowed by the failure
/// policy, or a limiter that has stopped limiting looks exactly like a healthy one.
/// </summary>
public class BrokenScriptRateLimitStore : IAiRateLimitStore
{
    public Task<WindowDecision> TryConsumeWindowAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default) =>
        throw new RedisServerException("ERR Error compiling script");

    public Task<LeaseDecision> TryAcquireLeaseAsync(
        string key, int limit, TimeSpan ttl, CancellationToken ct = default) =>
        throw new RedisServerException("ERR Error compiling script");

    public Task ReleaseLeaseAsync(string key, string leaseId, CancellationToken ct = default) =>
        throw new RedisServerException("ERR Error compiling script");
}
