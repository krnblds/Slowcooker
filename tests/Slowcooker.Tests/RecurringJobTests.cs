using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Slowcooker.Execution;
using Slowcooker.Server;
using Slowcooker.Storage.InMemory;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

public class RecurringJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 10, 30, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryJobStorage _storage;
    private readonly RecurringJobClient _client;

    public RecurringJobTests()
    {
        _storage = new InMemoryJobStorage(_time);
        _client = new RecurringJobClient(_storage, new JobSerializer(new JobTypeRegistry()), _time);
    }

    private RecurringJobScheduler NewScheduler() => new(_storage, _time, new WorkerOptions(), NullLogger.Instance);
    private async Task<RecurringJob> Get(string id) => (await _storage.GetRecurringAsync(id, CancellationToken.None))!;
    private async Task<long> ScheduledCount() => (await _storage.GetStatsAsync(CancellationToken.None)).Scheduled;

    [Fact]
    public async Task AddOrUpdate_stores_the_template_and_computes_the_next_run_in_the_given_time_zone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); // UTC+1 in March, before DST
        await _client.AddOrUpdateAsync("report", new Note("r"), "0 12 * * *", new RecurringJobOptions { TimeZone = zone, Queue = "reports" });

        var stored = await Get("report");
        Assert.Equal(new Job("record", "{\"Value\":\"r\"}"), stored.Job);
        Assert.Equal("0 12 * * *", stored.Cron);
        Assert.Equal("Europe/Berlin", stored.TimeZoneId);
        Assert.Equal("reports", stored.Queue);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 11, 0, 0, TimeSpan.Zero), stored.NextRunAt); // 12:00 Berlin = 11:00 UTC
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task Queue_attribute_applies_to_recurring_jobs_too()
    {
        await _client.AddOrUpdateAsync("charge", new ChargeCard("o", 1), "0 * * * *");
        Assert.Equal("payments", (await Get("charge")).Queue);
    }

    [Fact]
    public async Task AddOrUpdate_keeps_history_but_recomputes_the_schedule()
    {
        await _client.AddOrUpdateAsync("r", new Note("r"), "*/5 * * * *");
        await _storage.UpsertRecurringAsync((await Get("r")) with { LastRunAt = Now.AddHours(-1), LastJobId = "old" }, CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _client.AddOrUpdateAsync("r", new Note("r2"), "0 * * * *");

        var stored = await Get("r");
        Assert.Equal("0 * * * *", stored.Cron);
        Assert.Contains("r2", stored.Job.Payload);
        Assert.Equal(Now.AddMinutes(30), stored.NextRunAt);
        Assert.Equal(Now.AddHours(-1), stored.LastRunAt);
        Assert.Equal("old", stored.LastJobId);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(Now.AddMinutes(1), stored.UpdatedAt);
    }

    [Fact]
    public async Task Invalid_cron_is_rejected_before_anything_is_stored()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddOrUpdateAsync("bad", new Note("r"), "every tuesday"));
        Assert.Null(await _storage.GetRecurringAsync("bad", CancellationToken.None));
    }

    [Fact]
    public async Task Six_field_expressions_give_second_precision()
    {
        await _client.AddOrUpdateAsync("fast", new Note("r"), "*/10 * * * * *");
        Assert.Equal(Now.AddSeconds(10), (await Get("fast")).NextRunAt);
    }

    [Fact]
    public async Task Trigger_creates_a_job_now_without_touching_the_schedule()
    {
        await _client.AddOrUpdateAsync("r", new Note("r"), "0 0 1 1 *", new RecurringJobOptions { Queue = "q" });
        var before = await Get("r");

        var jobId = await _client.TriggerAsync("r");

        Assert.NotNull(jobId);
        var job = (await _storage.GetAsync(jobId, CancellationToken.None))!;
        Assert.Equal("q", job.Queue);
        Assert.Equal(Now, job.RunAt);
        Assert.Equal(before.Job, job.Job);
        Assert.Equal(before, await Get("r"));
        Assert.Null(await _client.TriggerAsync("missing"));
    }

    [Fact]
    public async Task Remove_deletes_the_schedule()
    {
        await _client.AddOrUpdateAsync("r", new Note("r"), "* * * * *");
        Assert.True(await _client.RemoveAsync("r"));
        Assert.False(await _client.RemoveAsync("r"));
    }

    [Fact]
    public async Task Scheduler_triggers_due_schedules_once_and_advances_them()
    {
        await _client.AddOrUpdateAsync("every-minute", new Note("tick"), "* * * * *");
        var scheduler = NewScheduler();

        await scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(0, await ScheduledCount());

        _time.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None); // same minute again: nothing new

        Assert.Equal(1, await ScheduledCount());
        var recurring = await Get("every-minute");
        Assert.Equal(Now.AddMinutes(2), recurring.NextRunAt);
        Assert.Equal(Now.AddMinutes(1), recurring.LastRunAt);
        Assert.NotNull(recurring.LastJobId);
        Assert.Equal(recurring.Job, (await _storage.GetAsync(recurring.LastJobId, CancellationToken.None))!.Job);
    }

    [Fact]
    public async Task Missed_occurrences_after_an_outage_collapse_into_one_run()
    {
        await _client.AddOrUpdateAsync("r", new Note("tick"), "* * * * *");

        _time.Advance(TimeSpan.FromHours(3));
        await NewScheduler().TickAsync(CancellationToken.None);

        Assert.Equal(1, await ScheduledCount());
        Assert.Equal(Now.AddHours(3).AddMinutes(1), (await Get("r")).NextRunAt);
    }

    [Fact]
    public async Task Competing_schedulers_trigger_each_occurrence_exactly_once()
    {
        for (var i = 0; i < 20; i++)
            await _client.AddOrUpdateAsync($"r{i}", new Note("tick"), "* * * * *");
        _time.Advance(TimeSpan.FromMinutes(1));

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => NewScheduler().TickAsync(CancellationToken.None)));

        Assert.Equal(20, await ScheduledCount());
    }

    [Fact]
    public async Task A_schedule_with_a_broken_time_zone_is_parked_instead_of_crashing_the_scheduler()
    {
        await _client.AddOrUpdateAsync("ok", new Note("tick"), "* * * * *");
        await _storage.UpsertRecurringAsync((await Get("ok")) with { Id = "broken", TimeZoneId = "Mars/Olympus" }, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));

        await NewScheduler().TickAsync(CancellationToken.None);

        Assert.Null((await Get("broken")).NextRunAt);
        Assert.Equal(1, await ScheduledCount());
    }

    [Fact]
    public void Cron_handles_daylight_saving_transitions_without_skipping_or_doubling()
    {
        // London: clocks go forward 29 March 2026 at 01:00 UTC (01:00 -> 02:00 local). 01:30 local does not exist that day.
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var schedule = Internal.CronSchedule.Parse("30 1 * * *");
        var before = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        var first = schedule.GetNextOccurrence(before, london)!.Value;   // 29 March: the skipped 01:30 runs at 02:00 local instead
        var second = schedule.GetNextOccurrence(first, london)!.Value;   // 30 March 01:30 BST

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), first);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 30, 0, TimeSpan.Zero), second);
    }
}
