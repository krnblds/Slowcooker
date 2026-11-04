using Slowcooker.Storage.ContractTests;
using StackExchange.Redis;

namespace Slowcooker.Storage.Redis.Tests;

[Collection(RedisCollection.Name)]
public sealed class RedisJobStorageTests(RedisFixture fixture) : JobStorageContractTests
{
    private string _prefix = "";

    protected override Task<IJobStorage> CreateStorageAsync(TimeProvider timeProvider)
    {
        _prefix = "t_" + Guid.NewGuid().ToString("N")[..12];
        return Task.FromResult<IJobStorage>(new RedisJobStorage(fixture.Connection, new RedisStorageOptions { KeyPrefix = _prefix }, timeProvider));
    }

    protected override async Task DisposeStorageAsync(IJobStorage storage)
    {
        await ((RedisJobStorage)storage).DisposeAsync();
        foreach (var endpoint in fixture.Connection.GetEndPoints())
        {
            var server = fixture.Connection.GetServer(endpoint);
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: $"{_prefix}:*")) keys.Add(key);
            if (keys.Count > 0) await fixture.Connection.GetDatabase().KeyDeleteAsync(keys.ToArray());
        }
    }

    [Fact]
    public async Task Leaves_no_stray_keys_behind_after_a_full_job_lifecycle()
    {
        var id = await Storage.CreateAsync(NewJob(idempotencyKey: "k"), CancellationToken.None);
        await (await FetchOneAsync()).CompleteAsync();
        Assert.True(await Storage.DeleteAsync(id, CancellationToken.None));

        var db = fixture.Connection.GetDatabase();
        Assert.False(await db.KeyExistsAsync($"{_prefix}:job:{id}"));
        Assert.False(await db.KeyExistsAsync($"{_prefix}:idem:k"));
        Assert.Equal(0, await db.SortedSetLengthAsync($"{_prefix}:idx:all"));
        Assert.Equal(0, await db.SortedSetLengthAsync($"{_prefix}:idx:completed:2"));
        Assert.Equal(0, await db.SortedSetLengthAsync($"{_prefix}:processing"));
        Assert.Empty((await Storage.GetStatsAsync(CancellationToken.None)).Queues);
    }

    [Fact]
    public void Key_prefixes_are_validated()
        => Assert.Throws<InvalidOperationException>(() => new RedisJobStorage(fixture.Connection, new RedisStorageOptions { KeyPrefix = "a:b" }));
}
