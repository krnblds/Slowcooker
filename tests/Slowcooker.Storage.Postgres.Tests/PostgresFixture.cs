using Testcontainers.PostgreSql;

namespace Slowcooker.Storage.Postgres.Tests;

/// <summary>
/// One PostgreSQL container for the whole test run. Set <c>SLOWCOOKER_TEST_POSTGRES</c> to a connection string to use an
/// existing server instead of Docker. Each test class gets its own schema, so tests never see each other's rows.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SLOWCOOKER_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
