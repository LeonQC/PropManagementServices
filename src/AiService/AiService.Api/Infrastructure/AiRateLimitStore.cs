namespace AiService.Api.Infrastructure;

/// <summary>Outcome of asking for one window permit.</summary>
/// <param name="Allowed">Whether the permit was granted.</param>
/// <param name="RetryAfter">When refused, exactly how long until the oldest recorded
/// request falls out of the window. Zero when allowed.</param>
public readonly record struct WindowDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>Outcome of asking for one concurrency lease.</summary>
/// <param name="Allowed">Whether a slot was free.</param>
/// <param name="LeaseId">The token to hand back to <see cref="IAiRateLimitStore.ReleaseLeaseAsync"/>.
/// Null when refused.</param>
public readonly record struct LeaseDecision(bool Allowed, string? LeaseId);

/// <summary>
/// The two primitives the limiter needs from shared storage. Narrow on purpose: an
/// interface the size of the actual requirement is one a fake can implement honestly,
/// and everything about key naming, failure policy and HTTP lives above it.
/// </summary>
public interface IAiRateLimitStore
{
    /// <summary>
    /// Records one request against <paramref name="key"/> if fewer than
    /// <paramref name="limit"/> have been recorded in the trailing
    /// <paramref name="window"/>, and says so.
    /// </summary>
    Task<WindowDecision> TryConsumeWindowAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Takes one of <paramref name="limit"/> slots under <paramref name="key"/>, held
    /// until released or until <paramref name="ttl"/> elapses — whichever comes first.
    /// </summary>
    Task<LeaseDecision> TryAcquireLeaseAsync(
        string key, int limit, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Gives a slot back. Releasing an already-expired lease is a no-op.</summary>
    Task ReleaseLeaseAsync(string key, string leaseId, CancellationToken ct = default);
}
