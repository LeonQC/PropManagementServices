using AiService.DataAccess;
using AiService.Models;
using PropTrack.Messaging;

namespace AiService.Tests;

/// <summary>
/// An in-memory fingerprint store. Records what was asked and what was written, and answers
/// from a dictionary — deliberately not a second implementation of the gate's logic, so it
/// cannot agree with a bug in the thing under test.
/// </summary>
public class FakeWorkFingerprintRepository : IAiWorkFingerprintRepository
{
    private readonly Dictionary<(string Feature, string EntityId), AiWorkFingerprint> _rows = [];

    public int Reads { get; private set; }
    public int Writes { get; private set; }

    public void Seed(string feature, string entityId, string fingerprint, double? lastOutputScore = null) =>
        _rows[(feature, entityId)] = new AiWorkFingerprint
        {
            Feature = feature,
            EntityId = entityId,
            InputFingerprint = fingerprint,
            LastOutputScore = lastOutputScore,
            ComputedAt = "2026-09-13T00:00:00.0000000Z"
        };

    public AiWorkFingerprint? Row(string feature, string entityId) =>
        _rows.TryGetValue((feature, entityId), out var row) ? row : null;

    public Task<AiWorkFingerprint?> GetAsync(string feature, string entityId, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(Row(feature, entityId));
    }

    public Task UpsertAsync(
        string feature, string entityId, string inputFingerprint, double? lastOutputScore,
        CancellationToken ct = default)
    {
        Writes++;
        Seed(feature, entityId, inputFingerprint, lastOutputScore);
        return Task.CompletedTask;
    }
}

/// <summary>A clock that does not move, so stage momentum is a fixed input rather than a
/// function of when the suite happens to run.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
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
