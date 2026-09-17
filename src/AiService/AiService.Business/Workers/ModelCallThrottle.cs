using Microsoft.Extensions.Options;

namespace AiService.Business.Workers;

/// <summary>
/// Caps how fast the worker is allowed to spend: a concurrency limit plus a minimum gap
/// between calls.
///
/// <para>The gap is the part that matters. A backfill republishes every deal at once, and
/// without spacing the worker would fire a model call per deal as fast as the broker can
/// deliver them. Dripping instead turns a burst into a few minutes of steady work, which is
/// both cheaper to abort if something looks wrong and kinder to a rate limit.</para>
///
/// <para>Registered as a singleton, so the limit is per process rather than per message.</para>
/// </summary>
public sealed class ModelCallThrottle : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _spacingLock = new(1, 1);
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _clock;
    private DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public ModelCallThrottle(IOptions<WorkerOptions> options, TimeProvider? timeProvider = null)
    {
        var o = options.Value;
        _slots = new SemaphoreSlim(Math.Max(1, o.MaxConcurrentModelCalls));
        _minInterval = TimeSpan.FromMilliseconds(Math.Max(0, o.MinCallIntervalMs));
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Waits for a slot and for the spacing interval. Dispose the returned handle to
    /// release the slot.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _slots.WaitAsync(ct);

        try
        {
            await _spacingLock.WaitAsync(ct);
            try
            {
                var since = _clock.GetUtcNow() - _lastCall;
                if (since < _minInterval) await Task.Delay(_minInterval - since, _clock, ct);
                _lastCall = _clock.GetUtcNow();
            }
            finally
            {
                _spacingLock.Release();
            }
        }
        catch
        {
            // Never hold a slot we are not going to use.
            _slots.Release();
            throw;
        }

        return new Slot(_slots);
    }

    public void Dispose()
    {
        _slots.Dispose();
        _spacingLock.Dispose();
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
