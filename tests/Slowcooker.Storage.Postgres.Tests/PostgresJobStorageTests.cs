using Npgsql;
using Slowcooker.Storage.ContractTests;

namespace Slowcooker.Storage.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresJobStorageTests(PostgresFixture fixture) : JobStorageContractTests
{
    private NpgsqlDataSource? _dataSource;
    private string _schema = "";

    protected override async Task<IJobStorage> CreateStorageAsync(TimeProvider timeProvider)
    {
        _schema = "t_" + Guid.NewGuid().ToString("N")[..12];
        _dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var storage = new PostgresJobStorage(_dataSource, new PostgresStorageOptions { Schema = _schema }, timeProvider);
        await storage.EnsureSchemaAsync(CancellationToken.None);
        return storage;
    }

    protected override async Task DisposeStorageAsync(IJobStorage storage)
    {
        await ((PostgresJobStorage)storage).DisposeAsync();
        if (_dataSource is null) return;
        await using (var cmd = _dataSource.CreateCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE"))
            await cmd.ExecuteNonQueryAsync();
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task Schema_creation_is_idempotent_and_safe_to_race()
    {
        var storage = (PostgresJobStorage)Storage;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => storage.EnsureSchemaAsync(CancellationToken.None)));
        await storage.EnsureSchemaAsync(CancellationToken.None);
        Assert.NotNull(await Storage.GetStatsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Schema_is_created_lazily_on_first_use_when_AutoCreateSchema_is_on()
    {
        var schema = "lazy_" + Guid.NewGuid().ToString("N")[..8];
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var storage = new PostgresJobStorage(dataSource, new PostgresStorageOptions { Schema = schema }, Time);
        try
        {
            await storage.CreateAsync(NewJob(), CancellationToken.None);
            Assert.Equal(1, (await storage.GetStatsAsync(CancellationToken.None)).Scheduled);
        }
        finally
        {
            await using var cmd = dataSource.CreateCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Schema_names_are_validated()
        => Assert.Throws<InvalidOperationException>(() => new PostgresJobStorage(_dataSource!, new PostgresStorageOptions { Schema = "bad;drop" }));
}
