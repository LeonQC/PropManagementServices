using System.Net.Sockets;
using AiService.Business.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// The retry policy exists because the shared Kafka consumer auto-commits offsets and swallows
/// handler exceptions: a message that fails is gone, not redelivered. These tests pin what is
/// worth another attempt and what is not, and assert the backoff shape without sleeping through
/// it.
/// </summary>
public class ModelCallPolicyTests
{
    private sealed class Recorder
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task Delay(TimeSpan d, CancellationToken ct)
        {
            Delays.Add(d);
            return Task.CompletedTask;
        }
    }

    private static (ModelCallPolicy Policy, Recorder Delays) Build()
    {
        var recorder = new Recorder();
        return (new ModelCallPolicy(NullLogger<ModelCallPolicy>.Instance, recorder.Delay), recorder);
    }

    [Fact]
    public async Task A_call_that_succeeds_runs_once_and_never_waits()
    {
        var (policy, delays) = Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromResult("ok");
        }, "test", CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
        Assert.Empty(delays.Delays);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_can_succeed()
    {
        var (policy, delays) = Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            attempts++;
            if (attempts < 3) throw new RationaleException("boom", new HttpRequestException("connection reset"));
            return Task.FromResult("ok");
        }, "test", CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Delays.Count);
    }

    /// <summary>Exponential with full jitter, so the cap is what can be asserted rather than an
    /// exact value. Without jitter a provider blip lines every retry in a backfill up on the
    /// same instant.</summary>
    [Fact]
    public async Task Backoff_stays_within_its_bounds()
    {
        var (policy, delays) = Build();

        await policy.ExecuteAsync<string>(
            _ => throw new RationaleException("boom", new SocketException()),
            "test", CancellationToken.None);

        Assert.All(delays.Delays, d =>
        {
            Assert.True(d >= TimeSpan.Zero);
            Assert.True(d <= TimeSpan.FromSeconds(15));
        });
    }

    [Fact]
    public async Task Exhaustion_returns_null_after_the_attempt_limit()
    {
        var (policy, delays) = Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new RationaleException("boom", new HttpRequestException("still down"));
        }, "test", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(ModelCallPolicy.MaxAttempts, attempts);
        Assert.Equal(ModelCallPolicy.MaxAttempts - 1, delays.Delays.Count);
    }

    /// <summary>
    /// A rate limit or an overloaded upstream is worth waiting for. These arrive as a status
    /// code in the message rather than as a typed exception, because the client reports what
    /// LiteLLM returned.
    /// </summary>
    [Theory]
    [InlineData("LiteLLM returned 429: rate limited")]
    [InlineData("LiteLLM returned 503: upstream overloaded")]
    [InlineData("LiteLLM returned 500: internal error")]
    public async Task Server_side_failures_are_retried(string message)
    {
        var (policy, _) = Build();
        var attempts = 0;

        await policy.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new RationaleException(message);
        }, "test", CancellationToken.None);

        Assert.Equal(ModelCallPolicy.MaxAttempts, attempts);
    }

    /// <summary>
    /// A malformed request will be malformed every time. Retrying it costs three ledger rows
    /// and a blocked consumer per message, times every deal in the pipeline — so anything not
    /// recognised as transient is treated as permanent on purpose.
    /// </summary>
    [Theory]
    [InlineData("LiteLLM returned 400: invalid model")]
    [InlineData("LiteLLM returned 401: bad api key")]
    [InlineData("something nobody anticipated")]
    public async Task Client_side_and_unknown_failures_are_not_retried(string message)
    {
        var (policy, delays) = Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new RationaleException(message);
        }, "test", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, attempts);
        Assert.Empty(delays.Delays);
    }

    /// <summary>Shutdown is not a failure. It propagates rather than being retried or
    /// swallowed, so a stopping host stops.</summary>
    [Fact]
    public async Task Cancellation_propagates_without_retrying()
    {
        var (policy, _) = Build();
        var attempts = 0;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => policy.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw new OperationCanceledException();
        }, "test", cts.Token));

        Assert.Equal(1, attempts);
    }
}
