using System.Security.Claims;
using System.Text.Json;
using AiService.Api.Infrastructure;
using AiService.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The HTTP half: the attribute opt-in, the off switch, and the shape of a refusal.
///
/// <para>Driven through <see cref="DefaultHttpContext"/> rather than a
/// <c>WebApplicationFactory</c> on purpose — booting the real app would need Postgres for
/// the migration step and auth-service for JWKS, which would make a test about a
/// Retry-After header depend on two containers being up.</para>
/// </summary>
public class AiRateLimitMiddlewareTests
{
    private static RateLimitOptions Options(bool enabled = true) => new()
    {
        Enabled = enabled,
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

    /// <summary>Runs one request through the middleware and returns the context it wrote to.</summary>
    private static async Task<HttpContext> RunAsync(
        IAiRateLimitStore store,
        RateLimitOptions options,
        AiRateLimitGroup? group,
        Action<HttpContext>? onNext = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "ava")], "test")),
        };
        context.Response.Body = new MemoryStream();

        if (group is not null)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AiRateLimitAttribute(group.Value)),
                "test-endpoint"));
        }

        // Through RequestServices, exactly as the middleware resolves it — so a test
        // that switches the limiter off is also asserting it never asks for one.
        var services = new ServiceCollection();
        services.AddSingleton(new AiRateLimiter(store, options, NullLogger<AiRateLimiter>.Instance));
        await using var provider = services.BuildServiceProvider();
        context.RequestServices = provider;

        var middleware = new AiRateLimitMiddleware(
            ctx => { onNext?.Invoke(ctx); return Task.CompletedTask; }, options);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return context;
    }

    private static async Task<JsonElement> BodyAsync(HttpContext context) =>
        (await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body));

    // -- opt-in --------------------------------------------------------------

    /// <summary>
    /// The reason the opt-in is an attribute: an endpoint that forgot to declare a
    /// budget is visibly unmarked in its controller, rather than quietly failing to
    /// match a path pattern somewhere else.
    /// </summary>
    [Fact]
    public async Task An_unmarked_endpoint_is_not_limited_at_all()
    {
        var store = new FakeRateLimitStore { NextLeaseAllowed = false };
        var reached = false;

        var context = await RunAsync(store, Options(), group: null, onNext: _ => reached = true);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_marked_endpoint_consults_both_limits()
    {
        var store = new FakeRateLimitStore();

        await RunAsync(store, Options(), AiRateLimitGroup.DealQa);

        Assert.Contains("lease:test:concurrency:DealQa:ava", store.Calls);
        Assert.Contains("window:test:window:DealQa:ava", store.Calls);
    }

    /// <summary>
    /// scripts/eval_ragas.py fires bulk sequential questions under one account. The off
    /// switch has to be a genuine bypass — no store call, so no Redis needed either.
    /// </summary>
    [Fact]
    public async Task The_off_switch_bypasses_the_limiter_entirely()
    {
        var store = new FakeRateLimitStore { NextWindowAllowed = false, NextLeaseAllowed = false };
        var reached = false;

        var context = await RunAsync(
            store, Options(enabled: false), AiRateLimitGroup.DealQa, onNext: _ => reached = true);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(store.Calls);
    }

    /// <summary>
    /// And the off switch has to survive there being no limiter to switch off. With
    /// RateLimit:Enabled false, AddAiRateLimiting registers no AiRateLimiter at all —
    /// so anything that asks DI for one before checking the flag turns every request
    /// into a 500. Which is exactly what happened: the test above passes a limiter in,
    /// so it went on passing while the real eval path was broken.
    /// </summary>
    [Fact]
    public async Task The_off_switch_works_when_no_limiter_is_registered_at_all()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AiRateLimitAttribute(AiRateLimitGroup.DealQa)),
            "test-endpoint"));

        await using var empty = new ServiceCollection().BuildServiceProvider();
        context.RequestServices = empty;

        var reached = false;
        var middleware = new AiRateLimitMiddleware(
            _ => { reached = true; return Task.CompletedTask; }, Options(enabled: false));

        await middleware.InvokeAsync(context);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    // -- refusals ------------------------------------------------------------

    [Fact]
    public async Task A_window_refusal_is_a_429_in_the_standard_envelope()
    {
        var store = new FakeRateLimitStore
        {
            NextWindowAllowed = false,
            WindowRetryAfter = TimeSpan.FromSeconds(231),
        };
        var reached = false;

        var context = await RunAsync(
            store, Options(), AiRateLimitGroup.DealQa, onNext: _ => reached = true);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("231", context.Response.Headers.RetryAfter);
        Assert.StartsWith("application/json", context.Response.ContentType);

        var error = (await BodyAsync(context)).GetProperty("error");
        Assert.Equal(ErrorCodes.RateLimited, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.Equal(JsonValueKind.Array, error.GetProperty("details").ValueKind);
        Assert.Equal(context.TraceIdentifier, error.GetProperty("requestId").GetString());
        Assert.True(DateTime.TryParse(error.GetProperty("timestamp").GetString(), out _));
    }

    [Fact]
    public async Task A_concurrency_refusal_is_a_429_carrying_the_endpoints_own_wait()
    {
        var store = new FakeRateLimitStore { NextLeaseAllowed = false };

        var context = await RunAsync(store, Options(), AiRateLimitGroup.DealQa);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("12", context.Response.Headers.RetryAfter);
    }

    /// <summary>
    /// The SSE constraint. AssistantController writes nothing until the first event
    /// arrives, and this middleware runs before the action, so a refusal still owns the
    /// status line — there is no half-open 200 stream carrying bad news.
    /// </summary>
    [Fact]
    public async Task A_refusal_happens_before_anything_is_written_to_the_body()
    {
        var store = new FakeRateLimitStore { NextWindowAllowed = false };

        var context = await RunAsync(store, Options(), AiRateLimitGroup.DealQa);

        Assert.False(context.Features.Get<IHttpResponseFeature>()!.HasStarted);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    /// <summary>
    /// A limiter outage refuses too, in the same envelope as any other refusal. The
    /// distinction between "over budget" and "cannot tell" is real, but it is a server-log
    /// distinction — the client's move is identical either way.
    /// </summary>
    [Fact]
    public async Task An_unreachable_store_refuses_rather_than_admitting()
    {
        var reached = false;

        var context = await RunAsync(
            new UnreachableRateLimitStore(), Options(), AiRateLimitGroup.DealQa,
            onNext: _ => reached = true);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);

        var error = (await BodyAsync(context)).GetProperty("error");
        Assert.Equal(ErrorCodes.RateLimited, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Retry_after_is_never_zero()
    {
        var store = new FakeRateLimitStore
        {
            NextWindowAllowed = false,
            WindowRetryAfter = TimeSpan.FromMilliseconds(40),
        };

        var context = await RunAsync(store, Options(), AiRateLimitGroup.DealQa);

        Assert.Equal("1", context.Response.Headers.RetryAfter);
    }

    // -- lease lifetime ------------------------------------------------------

    /// <summary>
    /// The slot is held for exactly as long as the request runs — including a streamed
    /// response, which does not return from the pipeline until the stream is finished.
    /// </summary>
    [Fact]
    public async Task The_lease_is_released_when_the_request_finishes()
    {
        var store = new FakeRateLimitStore();

        await RunAsync(store, Options(), AiRateLimitGroup.DealQa,
            onNext: _ => Assert.Empty(store.ReleasedLeases));

        Assert.Equal(["lease-1"], store.ReleasedLeases);
    }

    /// <summary>
    /// A handler that throws still gives the slot back. Otherwise one 500 would cost the
    /// user a permit for the whole lease TTL.
    /// </summary>
    [Fact]
    public async Task The_lease_is_released_when_the_request_throws()
    {
        var store = new FakeRateLimitStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(
            store, Options(), AiRateLimitGroup.DealQa,
            onNext: _ => throw new InvalidOperationException("boom")));

        Assert.Equal(["lease-1"], store.ReleasedLeases);
    }
}
