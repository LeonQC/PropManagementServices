using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace AiService.Tests;

/// <summary>
/// A throwaway Redis for the store tests, shared by every test in the collection.
///
/// <para>If Docker is not available the fixture records why instead of throwing, and
/// each test skips with that reason. A machine without Docker should not be told the
/// rate limiter is broken — but it also must not be told the tests passed, which is
/// what a silently-ignored suite amounts to.</para>
/// </summary>
public class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    public IConnectionMultiplexer? Redis { get; private set; }
    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder().WithImage("redis:7-alpine").Build();
            await _container.StartAsync();
            Redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        }
        catch (Exception ex)
        {
            Unavailable = $"Redis container unavailable ({ex.GetType().Name}: {ex.Message}). Is Docker running?";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Redis is not null) await Redis.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>Skips the calling test when there is no Redis to talk to.</summary>
    public IConnectionMultiplexer Require()
    {
        Assert.SkipWhen(Redis is null, Unavailable ?? "Redis unavailable.");
        return Redis!;
    }
}

[CollectionDefinition(nameof(RedisCollection))]
public class RedisCollection : ICollectionFixture<RedisFixture>;
