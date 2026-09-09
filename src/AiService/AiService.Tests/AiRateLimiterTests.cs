using AiService.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The policy above the store: which limit is consulted first, what a refusal costs, and
/// what happens when the store cannot answer at all.
/// </summary>
public class AiRateLimiterTests
{
    private static RateLimitOptions Options() => new()
    {
        Enabled = true,
        KeyPrefix = "test",
        DealQa = new EndpointLimit
        {
            PermitLimit = 30,
            WindowSeconds = 300,
            ConcurrentRequests = 2,
            LeaseTtlSeconds = 90,
            BusyRetryAfterSeconds = 12,
        },
    };

    private static AiRateLimiter Limiter(IAiRateLimitStore store, RateLimitOptions? options = null) =>
        new(store, options ?? Options(), NullLogger<AiRateLimiter>.Instance);

    [Fact]
    public async Task Admits_when_both_limits_have_room()
    {
        var admission = await Limiter(new FakeRateLimitStore())
            .AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.True(admission.IsAdmitted);
    }

    /// <summary>
    /// Concurrency is checked first so that firing six questions at once under a limit
    /// of two costs two window permits, not six. Being refused for impatience should not
    /// also spend the budget that bounds spend over an hour.
    /// </summary>
    [Fact]
    public async Task A_concurrency_refusal_does_not_spend_a_window_permit()
    {
        var store = new FakeRateLimitStore { NextLeaseAllowed = false };

        var admission = await Limiter(store).AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.Equal(RateLimitVerdict.ConcurrencyExceeded, admission.Verdict);
        Assert.DoesNotContain(store.Calls, c => c.StartsWith("window:"));
    }

    /// <summary>
    /// The other side of that ordering: a lease taken for a request the window then
    /// refuses has to go straight back, or the user loses a concurrency slot for the
    /// whole lease TTL over a request that never ran.
    /// </summary>
    [Fact]
    public async Task A_window_refusal_hands_the_lease_back_immediately()
    {
        var store = new FakeRateLimitStore { NextWindowAllowed = false };

        var admission = await Limiter(store).AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.Equal(RateLimitVerdict.WindowExceeded, admission.Verdict);
        Assert.Equal(["lease-1"], store.ReleasedLeases);
    }

    [Fact]
    public async Task An_admitted_request_holds_its_lease_until_disposal()
    {
        var store = new FakeRateLimitStore();
        var admission = await Limiter(store).AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.Empty(store.ReleasedLeases);

        await admission.DisposeAsync();

        Assert.Equal(["lease-1"], store.ReleasedLeases);
    }

    [Fact]
    public async Task Window_refusals_advertise_the_stores_measured_wait()
    {
        var store = new FakeRateLimitStore
        {
            NextWindowAllowed = false,
            WindowRetryAfter = TimeSpan.FromSeconds(231),
        };

        var admission = await Limiter(store).AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        // Not one segment's worth, and not the configured BusyRetryAfterSeconds: the
        // exact remaining life of the oldest request in the window.
        Assert.Equal(TimeSpan.FromSeconds(231), admission.RetryAfter);
    }

    /// <summary>
    /// The dead-config bug the old <c>Math.Max</c> hid: with a single computed number
    /// serving both limiters, DealQa's measured 12s could never win against the window's
    /// 50s. Knowing which limiter refused is what makes the value usable.
    /// </summary>
    [Fact]
    public async Task Concurrency_refusals_advertise_the_endpoints_measured_p95()
    {
        var store = new FakeRateLimitStore { NextLeaseAllowed = false };

        var admission = await Limiter(store).AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.Equal(TimeSpan.FromSeconds(12), admission.RetryAfter);
    }

    [Fact]
    public async Task Groups_and_users_get_separate_keys()
    {
        var store = new FakeRateLimitStore();
        var limiter = Limiter(store);

        await limiter.AcquireAsync(AiRateLimitGroup.DealQa, "ava");
        await limiter.AcquireAsync(AiRateLimitGroup.Assistant, "ava");
        await limiter.AcquireAsync(AiRateLimitGroup.DealQa, "marcus");

        Assert.Contains("window:test:window:DealQa:ava", store.Calls);
        Assert.Contains("window:test:window:Assistant:ava", store.Calls);
        Assert.Contains("window:test:window:DealQa:marcus", store.Calls);
        Assert.Contains("lease:test:concurrency:DealQa:ava", store.Calls);
    }

    // -- when Redis is not there ---------------------------------------------

    /// <summary>
    /// Fails closed: an outage must not become an unbounded Claude bill. See
    /// RateLimitOptions for why that trade goes this way here and not the usual one.
    /// </summary>
    [Fact]
    public async Task Refuses_when_the_store_is_unreachable()
    {
        var admission = await Limiter(new UnreachableRateLimitStore())
            .AcquireAsync(AiRateLimitGroup.DealQa, "user-1");

        Assert.Equal(RateLimitVerdict.LimiterUnavailable, admission.Verdict);
        Assert.False(admission.IsAdmitted);
    }

    /// <summary>
    /// A broken script is a bug, not an outage, and must surface as a 500 during
    /// development rather than being folded into the failure policy — a limiter that has
    /// stopped limiting because its Lua no longer parses should not look like a healthy
    /// one having a bad minute.
    /// </summary>
    [Fact]
    public async Task A_script_error_is_not_treated_as_an_outage()
    {
        await Assert.ThrowsAsync<RedisServerException>(() =>
            Limiter(new BrokenScriptRateLimitStore())
                .AcquireAsync(AiRateLimitGroup.DealQa, "user-1"));
    }
}
