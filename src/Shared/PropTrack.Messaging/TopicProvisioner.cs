using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace PropTrack.Messaging;

/// <summary>
/// Creates Kafka topics that need non-default configuration, at startup, by the service that
/// owns them.
///
/// <para>The broker runs with KAFKA_AUTO_CREATE_TOPICS_ENABLE=true, which is fine for ordinary
/// event topics but silently wrong for compacted ones: a topic auto-created on first publish
/// gets the default <c>cleanup.policy=delete</c> and its history is eventually discarded. For a
/// snapshot topic that is the rebuild source for a derived store, that quietly destroys the
/// guarantee the design depends on — and it fails invisibly, months later, when someone tries
/// to reindex. So the owning service declares such topics explicitly before publishing.</para>
/// </summary>
public sealed class TopicProvisioner(KafkaSettings settings, ILogger<TopicProvisioner> logger)
{
    /// <summary>
    /// Idempotently ensures <paramref name="topic"/> exists as a compacted topic. Safe to call
    /// on every startup: an already-existing topic is left untouched (its config is NOT
    /// rewritten, so a deliberate operator change isn't clobbered).
    /// </summary>
    public async Task EnsureCompactedTopicAsync(
        string topic, int partitions = 1, short replicationFactor = 1, CancellationToken ct = default)
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = settings.BootstrapServers }).Build();

        try
        {
            ct.ThrowIfCancellationRequested();

            // List ALL topics rather than asking about this one. GetMetadata(topic, ...) makes
            // the broker auto-create that topic when auto.create.topics.enable=true, so the
            // existence check would itself create it with the default delete policy — the very
            // failure this class exists to prevent.
            var meta = admin.GetMetadata(TimeSpan.FromSeconds(10));
            var existing = meta.Topics.FirstOrDefault(
                t => t.Topic == topic && t.Error.Code == ErrorCode.NoError);

            if (existing is not null)
            {
                await EnsureCompactionEnabledAsync(admin, topic);
                return;
            }

            await admin.CreateTopicsAsync([
                new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = partitions,
                    ReplicationFactor = replicationFactor,
                    Configs = new Dictionary<string, string>
                    {
                        // Retain exactly one current message per key, forever.
                        ["cleanup.policy"] = "compact",
                        // Compact promptly in dev so a reindex doesn't replay long runs of
                        // superseded versions; production would tune these up.
                        ["min.cleanable.dirty.ratio"] = "0.1",
                        ["segment.ms"] = "60000",
                        // Never drop a key's last value just because it's old.
                        ["delete.retention.ms"] = "86400000",
                    },
                },
            ], new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(15) });

            logger.LogInformation("Created compacted Kafka topic {Topic}.", topic);
        }
        catch (CreateTopicsException ex) when (
            ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Two instances racing at startup — whoever lost is fine.
            logger.LogInformation("Kafka topic {Topic} was created concurrently.", topic);
        }
        catch (Exception ex)
        {
            // Don't take the API down over this: publishing still works (the broker will
            // auto-create), but the topic would lack compaction, so make the risk loud.
            logger.LogError(ex,
                "Could not ensure compacted topic {Topic}. Publishing will still work, but the " +
                "topic may have the default delete policy, which breaks index rebuilds.", topic);
        }
    }

    /// <summary>
    /// Repairs an existing topic that isn't compacted. This happens whenever the broker
    /// auto-created it on first publish (or on a metadata request), which yields
    /// cleanup.policy=delete — silently discarding the history a rebuild depends on.
    /// </summary>
    private async Task EnsureCompactionEnabledAsync(IAdminClient admin, string topic)
    {
        var resource = new ConfigResource { Type = ResourceType.Topic, Name = topic };
        var described = await admin.DescribeConfigsAsync([resource]);
        var policy = described
            .SelectMany(r => r.Entries)
            .FirstOrDefault(e => e.Key == "cleanup.policy").Value?.Value;

        if (policy is not null && policy.Contains("compact", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Kafka topic {Topic} already exists and is compacted.", topic);
            return;
        }

        logger.LogWarning(
            "Kafka topic {Topic} exists with cleanup.policy={Policy} — expected compact. " +
            "Correcting it; it was most likely auto-created by the broker.", topic, policy ?? "(unset)");

        await admin.IncrementalAlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
        {
            [resource] =
            [
                new ConfigEntry
                {
                    Name = "cleanup.policy",
                    Value = "compact",
                    IncrementalOperation = AlterConfigOpType.Set,
                },
            ],
        });

        logger.LogInformation("Set cleanup.policy=compact on {Topic}.", topic);
    }
}
