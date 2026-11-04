using StackExchange.Redis;
using Testcontainers.Redis;

namespace Slowcooker.Storage.Redis.Tests;

/// <summary>
/// One Redis container for the whole test run. Set <c>SLOWCOOKER_TEST_REDIS</c> to a StackExchange.Redis configuration
/// string to use an existing server instead of Docker. Each test uses its own key prefix.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SLOWCOOKER_TEST_REDIS");
        string configuration;
        if (!string.IsNullOrWhiteSpace(external))
        {
            configuration = external;
        }
        else
        {
            _container = new RedisBuilder("redis:7-alpine").Build();
            await _container.StartAsync();
            configuration = _container.GetConnectionString();
        }

        Connection = await ConnectionMultiplexer.ConnectAsync(configuration + ",allowAdmin=true");
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
