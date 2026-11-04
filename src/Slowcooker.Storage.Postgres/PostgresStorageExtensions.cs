using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Slowcooker.Storage.Postgres;

namespace Slowcooker;

public static class PostgresStorageExtensions
{
    /// <summary>
    /// Stores jobs in PostgreSQL. Creates the <c>slowcooker</c> schema on first use unless
    /// <see cref="PostgresStorageOptions.AutoCreateSchema"/> is turned off.
    /// </summary>
    public static SlowcookerBuilder UsePostgres(this SlowcookerBuilder builder, string connectionString, Action<PostgresStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new PostgresStorageOptions();
        configure?.Invoke(options);
        options.Validate();

        builder.Services.TryAddSingleton(options);
        return builder.UseStorage(sp =>
        {
            var dataSource = NpgsqlDataSource.Create(connectionString);
            return new PostgresJobStorage(dataSource, options, sp.GetService<TimeProvider>());
        });
    }

    /// <summary>Stores jobs in PostgreSQL using an <see cref="NpgsqlDataSource"/> you registered in dependency injection.</summary>
    public static SlowcookerBuilder UsePostgres(this SlowcookerBuilder builder, Action<PostgresStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new PostgresStorageOptions();
        configure?.Invoke(options);
        options.Validate();

        builder.Services.TryAddSingleton(options);
        return builder.UseStorage(sp => new PostgresJobStorage(sp.GetRequiredService<NpgsqlDataSource>(), options, sp.GetService<TimeProvider>()));
    }
}
