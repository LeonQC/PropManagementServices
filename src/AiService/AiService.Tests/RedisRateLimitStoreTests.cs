using AiService.Api.Infrastructure;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The Lua, against a real Redis. These are the tests the previous in-process limiter
/// could not have passed: several of them run more than one limiter instance against
/// one store, which is exactly the arrangement that made two replicas hand a user twice
/// their budget.
/// </summary>
[Collection(nameof(RedisCollection))]
public class RedisRateLimitStoreTests(RedisFixture fixture)
{
    private RedisRateLimitStore Store() => new(fixture.Require());

    private static string Key() => $"test:{Guid.NewGuid():N}";

    // -- sliding window ------------------------------------------------------

    [Fact]
    public async Task Window_admits_exactly_the_permit_limit()
    {
        var store = Store();
        var key = Key();

        for (var i = 0; i < 5; i++)
            Assert.True((await store.TryConsumeWindowAsync(key, 5, TimeSpan.FromMinutes(5))).Allowed,
                $"request {i + 1} of 5 should have been inside the budget");

        Assert.False((await store.TryConsumeWindowAsync(key, 5, TimeSpan.FromMinutes(5))).Allowed);
    }

    /// <summary>
    /// The regression test for the bug this whole change exists for. Two stores are two
    /// replicas: independent objects, no shared memory, one Redis. Ten simultaneous
    /// requests split between them must yield five permits, not ten.
    /// </summary>
    [Fact]
    public async Task Window_is_shared_across_instances_under_concurrency()
    {
        var redis = fixture.Require();
        var replicaA = new RedisRateLimitStore(redis);
        var replicaB = new RedisRateLimitStore(redis);
        var key = Key();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            (i % 2 == 0 ? replicaA : replicaB)
                .TryConsumeWindowAsync(key, 5, TimeSpan.FromMinutes(5))));

        Assert.Equal(5, results.Count(r => r.Allowed));
    }

    [Fact]
    public async Task Window_returns_permits_once_the_window_has_passed()
    {
        var store = Store();
        var key = Key();
        var window = TimeSpan.FromMilliseconds(600);

        Assert.True((await store.TryConsumeWindowAsync(key, 1, window)).Allowed);
        Assert.False((await store.TryConsumeWindowAsync(key, 1, window)).Allowed);

        await Task.Delay(window + TimeSpan.FromMilliseconds(250));

        Assert.True((await store.TryConsumeWindowAsync(key, 1, window)).Allowed);
    }

    /// <summary>
    /// A log, not a ring of counters: the wait is measured from the individual request
    /// that has to age out, so it lands close to the true answer rather than to a
    /// segment boundary. This is what makes the Retry-After on a 429 worth trusting.
    /// </summary>
    [Fact]
    public async Task Window_rejection_reports_when_the_oldest_request_ages_out()
    {
        var store = Store();
        var key = Key();
        var window = TimeSpan.FromSeconds(10);

        Assert.True((await store.TryConsumeWindowAsync(key, 1, window)).Allowed);
        await Task.Delay(1_000);

        var refused = await store.TryConsumeWindowAsync(key, 1, window);

        Assert.False(refused.Allowed);
        // ~9s left of the first request's 10s life. Generous bounds: this asserts the
        // number is derived from the log, not that the test host has a real-time clock.
        Assert.InRange(refused.RetryAfter.TotalSeconds, 8.0, 9.6);
    }

    [Fact]
    public async Task Window_keys_do_not_leak_between_users_or_groups()
    {
        var store = Store();
        var (a, b) = (Key(), Key());

        Assert.True((await store.TryConsumeWindowAsync(a, 1, TimeSpan.FromMinutes(5))).Allowed);
        Assert.False((await store.TryConsumeWindowAsync(a, 1, TimeSpan.FromMinutes(5))).Allowed);
        Assert.True((await store.TryConsumeWindowAsync(b, 1, TimeSpan.FromMinutes(5))).Allowed);
    }

    /// <summary>
    /// Two requests inside the same millisecond are two entries. Scores collide; sorted
    /// set members must not, or the second request would silently overwrite the first
    /// and cost nothing.
    /// </summary>
    [Fact]
    public async Task Window_counts_requests_that_land_in_the_same_millisecond()
    {
        var store = Store();
        var key = Key();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            store.TryConsumeWindowAsync(key, 2, TimeSpan.FromMinutes(5))));

        Assert.Equal(2, results.Count(r => r.Allowed));
    }

    [Fact]
    public async Task Window_key_expires_so_an_idle_user_leaves_nothing_behind()
    {
        var redis = fixture.Require();
        var store = new RedisRateLimitStore(redis);
        var key = Key();

        await store.TryConsumeWindowAsync(key, 5, TimeSpan.FromSeconds(30));

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 1, 30);
    }

    // -- concurrency leases --------------------------------------------------

    [Fact]
    public async Task Leases_admit_exactly_the_concurrency_limit()
    {
        var store = Store();
        var key = Key();
        var ttl = TimeSpan.FromSeconds(30);

        Assert.True((await store.TryAcquireLeaseAsync(key, 2, ttl)).Allowed);
        Assert.True((await store.TryAcquireLeaseAsync(key, 2, ttl)).Allowed);
        Assert.False((await store.TryAcquireLeaseAsync(key, 2, ttl)).Allowed);
    }

    [Fact]
    public async Task Releasing_a_lease_frees_the_slot()
    {
        var store = Store();
        var key = Key();
        var ttl = TimeSpan.FromSeconds(30);

        var held = await store.TryAcquireLeaseAsync(key, 1, ttl);
        Assert.True(held.Allowed);
        Assert.False((await store.TryAcquireLeaseAsync(key, 1, ttl)).Allowed);

        await store.ReleaseLeaseAsync(key, held.LeaseId!);

        Assert.True((await store.TryAcquireLeaseAsync(key, 1, ttl)).Allowed);
    }

    /// <summary>
    /// The crashed-instance case, and the reason a lease carries a TTL at all: a holder
    /// that never releases must not cost that user a permit forever.
    /// </summary>
    [Fact]
    public async Task An_abandoned_lease_expires_instead_of_holding_the_slot_forever()
    {
        var store = Store();
        var key = Key();
        var ttl = TimeSpan.FromMilliseconds(600);

        Assert.True((await store.TryAcquireLeaseAsync(key, 1, ttl)).Allowed);
        Assert.False((await store.TryAcquireLeaseAsync(key, 1, ttl)).Allowed);

        // The holder is gone. Nothing releases; the score does the work.
        await Task.Delay(ttl + TimeSpan.FromMilliseconds(250));

        Assert.True((await store.TryAcquireLeaseAsync(key, 1, ttl)).Allowed);
    }

    [Fact]
    public async Task Leases_are_shared_across_instances_under_concurrency()
    {
        var redis = fixture.Require();
        var replicaA = new RedisRateLimitStore(redis);
        var replicaB = new RedisRateLimitStore(redis);
        var key = Key();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            (i % 2 == 0 ? replicaA : replicaB)
                .TryAcquireLeaseAsync(key, 2, TimeSpan.FromSeconds(30))));

        Assert.Equal(2, results.Count(r => r.Allowed));
    }

    /// <summary>
    /// The same expiry check as the window's, and it caught the same bug: a PEXPIRE
    /// issued before the first ZADD does nothing, because the key it names does not
    /// exist yet.
    /// </summary>
    [Fact]
    public async Task Lease_key_expires_so_a_finished_user_leaves_nothing_behind()
    {
        var redis = fixture.Require();
        var store = new RedisRateLimitStore(redis);
        var key = Key();

        await store.TryAcquireLeaseAsync(key, 2, TimeSpan.FromSeconds(30));

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 1, 30);
    }

    [Fact]
    public async Task Releasing_an_expired_lease_is_harmless()
    {
        var store = Store();
        var key = Key();

        var held = await store.TryAcquireLeaseAsync(key, 1, TimeSpan.FromMilliseconds(300));
        await Task.Delay(600);

        // A slow request finishing after its own lease expired: releasing must not throw,
        // and must not remove a slot someone else has since taken.
        var other = await store.TryAcquireLeaseAsync(key, 1, TimeSpan.FromSeconds(30));
        await store.ReleaseLeaseAsync(key, held.LeaseId!);

        Assert.True(other.Allowed);
        Assert.False((await store.TryAcquireLeaseAsync(key, 1, TimeSpan.FromSeconds(30))).Allowed);
    }
}
