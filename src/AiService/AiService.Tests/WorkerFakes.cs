using AiService.DataAccess;
using AiService.Models;
using PropTrack.Messaging;

namespace AiService.Tests;

/// <summary>
/// An in-memory record store. Records what was asked and what was written, and answers from a
/// dictionary — deliberately not a second implementation of the gate, so it cannot agree with
/// a bug in the thing under test.
/// </summary>
public class FakeWorkRecordRepository : IAiWorkRecordRepository
{
    private readonly Dictionary<(string Feature, string EntityId), AiWorkRecord> _rows = [];

    public int Reads { get; private set; }
    public int Writes { get; private set; }

    public void Seed(string feature, string entityId, double? lastOutputScore) =>
        _rows[(feature, entityId)] = new AiWorkRecord
        {
            Feature = feature,
            EntityId = entityId,
            LastOutputScore = lastOutputScore,
            ComputedAt = "2026-09-16T00:00:00.0000000Z"
        };

    public AiWorkRecord? Row(string feature, string entityId) =>
        _rows.TryGetValue((feature, entityId), out var row) ? row : null;

    public Task<AiWorkRecord?> GetAsync(string feature, string entityId, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(Row(feature, entityId));
    }

    public Task UpsertAsync(string feature, string entityId, double? lastOutputScore, CancellationToken ct = default)
    {
        Writes++;
        Seed(feature, entityId, lastOutputScore);
        return Task.CompletedTask;
    }
}

/// <summary>Captures published events instead of reaching a broker.</summary>
public class FakeEventPublisher : IEventPublisher
{
    public List<(string Topic, string Key, object Payload)> Published { get; } = [];

    public Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
    {
        Published.Add((topic, key, payload!));
        return Task.CompletedTask;
    }
}

/// <summary>A clock that does not move, so anything time-dependent is a fixed input rather
/// than a function of when the suite happens to run.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
