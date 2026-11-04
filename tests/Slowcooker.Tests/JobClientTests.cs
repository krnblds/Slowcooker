using Microsoft.Extensions.Time.Testing;
using Slowcooker.Execution;
using Slowcooker.Storage.InMemory;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public class JobClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryJobStorage _storage;
    private readonly JobClient _client;

    public JobClientTests()
    {
        _storage = new InMemoryJobStorage(_time);
        _client = new JobClient(_storage, new JobSerializer(new JobTypeRegistry()), _time);
    }

    private async Task<StoredJob> Get(string id) => (await _storage.GetAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task Enqueue_creates_a_scheduled_job_due_now_on_the_default_queue()
    {
        var id = await _client.EnqueueAsync(new Note("x"));

        var job = await Get(id);
        Assert.Equal(JobState.Scheduled, job.State);
        Assert.Equal("default", job.Queue);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Equal(Now, job.RunAt);
        Assert.Equal(0, job.Attempt);
        Assert.Equal(new Job("record", "{\"Value\":\"x\"}"), job.Job);
        Assert.Null(job.IdempotencyKey);
    }

    [Fact]
    public async Task Queue_attribute_sets_the_default_queue_and_options_override_it()
    {
        var byAttribute = await _client.EnqueueAsync(new ChargeCard("o1", 10));
        var byOptions = await _client.EnqueueAsync(new ChargeCard("o2", 10), new JobOptions { Queue = "urgent" });

        Assert.Equal("payments", (await Get(byAttribute)).Queue);
        Assert.Equal("urgent", (await Get(byOptions)).Queue);
    }

    [Fact]
    public async Task Jobs_declare_their_own_idempotency_key()
    {
        var first = await _client.EnqueueAsync(new ChargeCard("order-9", 10));
        var second = await _client.EnqueueAsync(new ChargeCard("order-9", 10));
        var other = await _client.EnqueueAsync(new ChargeCard("order-10", 10));

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal("charge:order-9", (await Get(first)).IdempotencyKey);
        Assert.Equal(2, (await _storage.GetStatsAsync(CancellationToken.None)).Scheduled);
    }

    [Fact]
    public async Task Options_can_override_the_idempotency_key()
    {
        var id = await _client.EnqueueAsync(new ChargeCard("o", 1), new JobOptions { IdempotencyKey = "custom" });
        Assert.Equal("custom", (await Get(id)).IdempotencyKey);
    }

    [Fact]
    public async Task Schedule_with_a_delay_uses_the_time_provider()
    {
        var id = await _client.ScheduleAsync(new Note("x"), TimeSpan.FromMinutes(10));
        Assert.Equal(Now.AddMinutes(10), (await Get(id)).RunAt);
    }

    [Fact]
    public async Task Schedule_at_a_point_in_time()
    {
        var at = Now.AddDays(1);
        var id = await _client.ScheduleAsync(new Note("x"), at, new JobOptions { Queue = "later" });
        var job = await Get(id);
        Assert.Equal(at, job.RunAt);
        Assert.Equal("later", job.Queue);
    }

    [Fact]
    public async Task Ids_are_time_ordered()
    {
        var ids = new List<string>();
        for (var i = 0; i < 20; i++) ids.Add(await _client.EnqueueAsync(new Note("x")));
        Assert.Equal(ids, ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Empty_queue_names_are_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => _client.EnqueueAsync(new Note("x"), new JobOptions { Queue = " " }));
}
