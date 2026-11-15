using Confluent.Kafka;
using DealsService.DataAccess;
using Microsoft.EntityFrameworkCore;
using PropTrack.Messaging;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace DealsService.Tests.Reliability;

/// <summary>
/// A throwaway Kafka broker and a migrated deals database, shared by every reliability test.
///
/// <para>Same contract as AiService.Tests.RedisFixture: if Docker is not available the fixture
/// records why instead of throwing, and each test skips with that reason — a machine without
/// Docker is told neither that delivery is broken nor that it was verified.</para>
/// </summary>
public class KafkaPostgresFixture : IAsyncLifetime
{
    private KafkaContainer? _kafka;
    private PostgreSqlContainer? _postgres;

    public string? Unavailable { get; private set; }
    public KafkaSettings Kafka { get; private set; } = new();
    private string _connectionString = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Pinned: the Testcontainers default tag has no arm64 build.
            _kafka = new KafkaBuilder().WithImage("confluentinc/cp-kafka:7.6.1").Build();
            _postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
            await Task.WhenAll(_kafka.StartAsync(), _postgres.StartAsync());

            Kafka = new KafkaSettings { BootstrapServers = _kafka.GetBootstrapAddress() };
            _connectionString = _postgres.GetConnectionString();

            await using var db = NewDbContext();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Unavailable = $"Kafka/Postgres containers unavailable ({ex.GetType().Name}: {ex.Message}). Is Docker running?";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_kafka is not null) await _kafka.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    /// <summary>Skips the calling test when there are no containers to talk to.</summary>
    public void Require() => Assert.SkipWhen(Unavailable is not null, Unavailable ?? "");

    /// <summary>Configured the same way as DataAccess.ServiceCollectionExtensions.</summary>
    public DealsDbContext NewDbContext() => new(
        new DbContextOptionsBuilder<DealsDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>
    /// Reads <paramref name="topic"/> from the beginning with a throwaway group and returns the
    /// values of every message with <paramref name="key"/> seen before <paramref name="wait"/>
    /// runs out. Returns as soon as one arrives.
    /// </summary>
    public List<string> ReadByKey(string topic, string key, TimeSpan wait)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Kafka.BootstrapServers,
            GroupId = $"probe-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true,
        }).Build();
        consumer.Subscribe(topic);

        var found = new List<string>();
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline && found.Count == 0)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result?.Message?.Key == key) found.Add(result.Message.Value);
            }
            catch (ConsumeException)
            {
                // The topic may not exist yet; keep polling until the deadline.
            }
        }

        consumer.Close();
        return found;
    }
}

[CollectionDefinition(nameof(KafkaPostgresCollection))]
public class KafkaPostgresCollection : ICollectionFixture<KafkaPostgresFixture>;
