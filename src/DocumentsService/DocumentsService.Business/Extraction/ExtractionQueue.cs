using System.Threading.Channels;

namespace DocumentsService.Business.Extraction;

/// <summary>
/// In-process work queue between the Kafka consumer (producer side) and the
/// extraction worker (consumer side). Unbounded is fine at dev scale; a real
/// deployment would swap this for a durable queue (e.g. Hangfire) without
/// touching either endpoint of the abstraction.
/// </summary>
public interface IExtractionQueue
{
    ValueTask EnqueueAsync(string documentId, CancellationToken ct = default);
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct);
}

public sealed class ExtractionQueue : IExtractionQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ValueTask EnqueueAsync(string documentId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(documentId, ct);

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
