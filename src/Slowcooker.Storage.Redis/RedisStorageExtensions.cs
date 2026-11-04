using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slowcooker.Storage.Redis;
using StackExchange.Redis;

namespace Slowcooker;

public static class RedisStorageExtensions
{
    /// <summary>Stores jobs in Redis, connecting with the given StackExchange.Redis configuration string (for example "localhost:6379").</summary>
    public static SlowcookerBuilder UseRedis(this SlowcookerBuilder builder, string configuration, Action<RedisStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        var options = new RedisStorageOptions();
        configure?.Invoke(options);
        options.Validate();

        builder.Services.TryAddSingleton(options);
        return builder.UseStorage(sp => new RedisJobStorage(ConnectionMultiplexer.Connect(configuration), options, sp.GetService<TimeProvider>()));
    }

    /// <summary>Stores jobs in Redis using an <see cref="IConnectionMultiplexer"/> you registered in dependency injection.</summary>
    public static SlowcookerBuilder UseRedis(this SlowcookerBuilder builder, Action<RedisStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new RedisStorageOptions();
        configure?.Invoke(options);
        options.Validate();

        builder.Services.TryAddSingleton(options);
        return builder.UseStorage(sp => new RedisJobStorage(sp.GetRequiredService<IConnectionMultiplexer>(), options, sp.GetService<TimeProvider>()));
    }
}
