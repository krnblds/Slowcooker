using Microsoft.Extensions.Time.Testing;
using Slowcooker.Storage;

namespace Slowcooker.Storage.ContractTests;

/// <summary>
/// The behavioural contract every <see cref="IJobStorage"/> must satisfy. Provider test projects derive from this
/// and only supply <see cref="CreateStorageAsync"/>. Time is controlled through a <see cref="FakeTimeProvider"/>
/// so lease expiry, scheduling and retention are tested deterministically.
/// </summary>
public abstract class JobStorageContractTests : IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    protected FakeTimeProvider Time { get; } = new(Start);
    protected IJobStorage Storage { get; private set; } = null!;
    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract Task<IJobStorage> CreateStorageAsync(TimeProvider timeProvider);
    protected virtual Task DisposeStorageAsync(IJobStorage storage) => Task.CompletedTask;

    public async ValueTask InitializeAsync() => Storage = await CreateStorageAsync(Time);
    public async ValueTask DisposeAsync() => await DisposeStorageAsync(Storage);

    private static int _counter;

    protected StoredJob NewJob(string queue = "default", DateTimeOffset? runAt = null, string? idempotencyKey = null, DateTimeOffset? createdAt = null)
    {
        var n = Interlocked.Increment(ref _counter);
        return new StoredJob
        {
            Id = $"{n:D10}{Guid.NewGuid():N}"[..32], // creation-ordered like the real ids
            Job = new Job("send-email", $"{{\"to\":\"to-{n}@example.com\",\"n\":{n}}}"),
            Queue = queue,
            State = JobState.Scheduled,
            CreatedAt = createdAt ?? Time.GetUtcNow(),
            RunAt = runAt ?? Time.GetUtcNow(),
            IdempotencyKey = idempotencyKey,
        };
    }

    protected Task<IReadOnlyList<IFetchedJob>> FetchAsync(string worker = "w1", int max = 10, TimeSpan? lease = null, params string[] queues)
        => Storage.FetchAsync(queues.Length == 0 ? ["default"] : queues, worker, lease ?? TimeSpan.FromMinutes(5), max, Ct);

    protected async Task<IFetchedJob> FetchOneAsync(string worker = "w1", TimeSpan? lease = null)
    {
        var fetched = await FetchAsync(worker, 1, lease);
        Assert.Single(fetched);
        return fetched[0];
    }

    // ================================================================ Create / Get

    [Fact]
    public async Task Create_then_Get_roundtrips_every_field()
    {
        var job = NewJob(queue: "mail", runAt: Start.AddMinutes(5), idempotencyKey: "key-1");
        var id = await Storage.CreateAsync(job, Ct);
        Assert.Equal(job.Id, id);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(job.Id, stored.Id);
        Assert.Equal(job.Job, stored.Job);
        Assert.Equal("mail", stored.Queue);
        Assert.Equal(JobState.Scheduled, stored.State);
        Assert.Equal(job.CreatedAt, stored.CreatedAt);
        Assert.Equal(Start.AddMinutes(5), stored.RunAt);
        Assert.Equal(0, stored.Attempt);
        Assert.Null(stored.LastError);
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.LeaseExpiresAt);
        Assert.Null(stored.CompletedAt);
        Assert.Equal("key-1", stored.IdempotencyKey);
    }

    [Fact]
    public async Task Get_unknown_id_returns_null()
        => Assert.Null(await Storage.GetAsync("does-not-exist", Ct));

    [Fact]
    public async Task Create_with_existing_idempotency_key_returns_existing_id_and_creates_nothing()
    {
        var first = await Storage.CreateAsync(NewJob(idempotencyKey: "order-42"), Ct);
        var second = await Storage.CreateAsync(NewJob(idempotencyKey: "order-42"), Ct);

        Assert.Equal(first, second);
        var stats = await Storage.GetStatsAsync(Ct);
        Assert.Equal(1, stats.Scheduled);
    }

    [Fact]
    public async Task Deleting_a_job_frees_its_idempotency_key()
    {
        var first = await Storage.CreateAsync(NewJob(idempotencyKey: "k"), Ct);
        Assert.True(await Storage.DeleteAsync(first, Ct));
        var second = await Storage.CreateAsync(NewJob(idempotencyKey: "k"), Ct);
        Assert.NotEqual(first, second);
    }

    // ================================================================ Fetch

    [Fact]
    public async Task Fetch_returns_due_jobs_in_RunAt_then_id_order()
    {
        var later = await Storage.CreateAsync(NewJob(runAt: Start.AddSeconds(-10)), Ct);
        var earliest = await Storage.CreateAsync(NewJob(runAt: Start.AddSeconds(-30)), Ct);
        var sameTimeA = await Storage.CreateAsync(NewJob(runAt: Start.AddSeconds(-20)), Ct);
        var sameTimeB = await Storage.CreateAsync(NewJob(runAt: Start.AddSeconds(-20)), Ct);

        var fetched = await FetchAsync(max: 10);

        Assert.Equal([earliest, sameTimeA, sameTimeB, later], fetched.Select(f => f.Record.Id));
    }

    [Fact]
    public async Task Fetch_skips_jobs_scheduled_in_the_future_until_their_time_comes()
    {
        var id = await Storage.CreateAsync(NewJob(runAt: Start.AddMinutes(10)), Ct);

        Assert.Empty(await FetchAsync());

        Time.Advance(TimeSpan.FromMinutes(10));
        var fetched = await FetchAsync();
        Assert.Equal(id, Assert.Single(fetched).Record.Id);
    }

    [Fact]
    public async Task Fetch_only_returns_jobs_from_the_requested_queues()
    {
        await Storage.CreateAsync(NewJob(queue: "a"), Ct);
        var b = await Storage.CreateAsync(NewJob(queue: "b"), Ct);
        var c = await Storage.CreateAsync(NewJob(queue: "c"), Ct);

        var fetched = await FetchAsync(queues: ["b", "c"]);

        Assert.Equal(new[] { b, c }.Order(), fetched.Select(f => f.Record.Id).Order());
    }

    [Fact]
    public async Task Fetch_moves_job_to_Processing_with_lease_and_increments_attempt()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);

        var fetched = await FetchOneAsync(worker: "worker-7", lease: TimeSpan.FromSeconds(90));

        Assert.Equal(JobState.Processing, fetched.Record.State);
        Assert.Equal("worker-7", fetched.Record.LeaseOwner);
        Assert.Equal(Start.AddSeconds(90), fetched.Record.LeaseExpiresAt);
        Assert.Equal(1, fetched.Record.Attempt);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Processing, stored.State);
        Assert.Equal("worker-7", stored.LeaseOwner);
        Assert.Equal(Start.AddSeconds(90), stored.LeaseExpiresAt);
        Assert.Equal(1, stored.Attempt);
    }

    [Fact]
    public async Task Fetch_respects_maxCount()
    {
        for (var i = 0; i < 5; i++) await Storage.CreateAsync(NewJob(), Ct);

        Assert.Equal(2, (await FetchAsync(max: 2)).Count);
        Assert.Equal(3, (await FetchAsync(max: 10)).Count);
        Assert.Empty(await FetchAsync(max: 10));
    }

    [Fact]
    public async Task A_leased_job_is_not_fetched_again_while_the_lease_holds()
    {
        await Storage.CreateAsync(NewJob(), Ct);
        await FetchOneAsync(worker: "w1", lease: TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(4));
        Assert.Empty(await FetchAsync(worker: "w2"));
    }

    [Fact]
    public async Task A_job_whose_lease_expired_is_fetched_again_with_attempt_incremented()
    {
        // Crash recovery: worker 1 dies mid-job and never settles.
        var id = await Storage.CreateAsync(NewJob(), Ct);
        await FetchOneAsync(worker: "w1", lease: TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(5));
        var again = await FetchOneAsync(worker: "w2");

        Assert.Equal(id, again.Record.Id);
        Assert.Equal("w2", again.Record.LeaseOwner);
        Assert.Equal(2, again.Record.Attempt);
    }

    [Fact]
    public async Task ExtendLease_pushes_the_expiry_forward()
    {
        await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync(worker: "w1", lease: TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(4));
        await fetched.ExtendLeaseAsync(TimeSpan.FromMinutes(5), Ct);

        Time.Advance(TimeSpan.FromMinutes(4)); // 8 minutes after fetch, 4 after extend
        Assert.Empty(await FetchAsync(worker: "w2"));

        var stored = await Storage.GetAsync(fetched.Record.Id, Ct);
        Assert.Equal(Start.AddMinutes(9), stored!.LeaseExpiresAt);
    }

    // ================================================================ Settling

    [Fact]
    public async Task Complete_marks_the_job_Succeeded_and_clears_the_lease()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();

        Time.Advance(TimeSpan.FromSeconds(3));
        await fetched.CompleteAsync(Ct);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Succeeded, stored.State);
        Assert.Equal(Start.AddSeconds(3), stored.CompletedAt);
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.LeaseExpiresAt);
        Assert.Equal(1, stored.Attempt);
        Assert.Empty(await FetchAsync());
    }

    [Fact]
    public async Task Fail_marks_the_job_Failed_with_the_error()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();

        await fetched.FailAsync("boom", Ct);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Failed, stored.State);
        Assert.Equal("boom", stored.LastError);
        Assert.Equal(Start, stored.CompletedAt);
        Assert.Null(stored.LeaseOwner);
        Assert.Empty(await FetchAsync());
    }

    [Fact]
    public async Task Retry_schedules_the_job_again_at_runAt_and_keeps_the_attempt_count()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();

        await fetched.RetryAsync(Start.AddMinutes(1), "transient", Ct);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Scheduled, stored.State);
        Assert.Equal(Start.AddMinutes(1), stored.RunAt);
        Assert.Equal("transient", stored.LastError);
        Assert.Equal(1, stored.Attempt);
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.CompletedAt);

        Assert.Empty(await FetchAsync());
        Time.Advance(TimeSpan.FromMinutes(1));
        var again = await FetchOneAsync();
        Assert.Equal(id, again.Record.Id);
        Assert.Equal(2, again.Record.Attempt);
    }

    [Fact]
    public async Task Requeue_from_the_handle_gives_the_attempt_back_and_keeps_RunAt()
    {
        var id = await Storage.CreateAsync(NewJob(runAt: Start.AddSeconds(-5)), Ct);
        var fetched = await FetchOneAsync();

        await fetched.RequeueAsync(Ct);

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Scheduled, stored.State);
        Assert.Equal(0, stored.Attempt);
        Assert.Equal(Start.AddSeconds(-5), stored.RunAt);
        Assert.Null(stored.LeaseOwner);

        var again = await FetchOneAsync();
        Assert.Equal(1, again.Record.Attempt);
    }

    [Fact]
    public async Task Disposing_an_unsettled_handle_requeues_the_job()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();

        await fetched.DisposeAsync();

        var stored = await Storage.GetAsync(id, Ct);
        Assert.Equal(JobState.Scheduled, stored!.State);
        Assert.Equal(0, stored.Attempt);
    }

    [Fact]
    public async Task Disposing_a_settled_handle_changes_nothing()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();
        await fetched.CompleteAsync(Ct);

        await fetched.DisposeAsync();

        Assert.Equal(JobState.Succeeded, (await Storage.GetAsync(id, Ct))!.State);
    }

    [Fact]
    public async Task Settling_twice_throws()
    {
        await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();
        await fetched.CompleteAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetched.FailAsync("x", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fetched.ExtendLeaseAsync(TimeSpan.FromMinutes(1), Ct));
    }

    [Fact]
    public async Task A_worker_whose_lease_was_lost_cannot_touch_the_job()
    {
        // The slow original worker must not clobber the new owner's state.
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var slow = await FetchOneAsync(worker: "slow", lease: TimeSpan.FromMinutes(1));

        Time.Advance(TimeSpan.FromMinutes(1));
        var fast = await FetchOneAsync(worker: "fast", lease: TimeSpan.FromMinutes(5));
        Assert.Equal(id, fast.Record.Id);

        await Assert.ThrowsAsync<LeaseLostException>(() => slow.ExtendLeaseAsync(TimeSpan.FromMinutes(5), Ct));
        await Assert.ThrowsAsync<LeaseLostException>(() => slow.CompleteAsync(Ct));

        var stored = await Storage.GetAsync(id, Ct);
        Assert.Equal(JobState.Processing, stored!.State);
        Assert.Equal("fast", stored.LeaseOwner);
        Assert.Equal(2, stored.Attempt);

        await fast.CompleteAsync(Ct);
        Assert.Equal(JobState.Succeeded, (await Storage.GetAsync(id, Ct))!.State);
    }

    [Fact]
    public async Task Each_settle_kind_throws_LeaseLost_for_a_lost_lease()
    {
        var handles = new List<IFetchedJob>();
        for (var i = 0; i < 4; i++)
        {
            await Storage.CreateAsync(NewJob(), Ct);
            handles.Add(await FetchOneAsync(worker: "slow", lease: TimeSpan.FromMinutes(1)));
        }
        Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(4, (await FetchAsync(worker: "fast", max: 10)).Count);

        await Assert.ThrowsAsync<LeaseLostException>(() => handles[0].CompleteAsync(Ct));
        await Assert.ThrowsAsync<LeaseLostException>(() => handles[1].FailAsync("x", Ct));
        await Assert.ThrowsAsync<LeaseLostException>(() => handles[2].RetryAsync(Start.AddMinutes(5), "x", Ct));
        await Assert.ThrowsAsync<LeaseLostException>(() => handles[3].RequeueAsync(Ct));

        var stats = await Storage.GetStatsAsync(Ct);
        Assert.Equal(4, stats.Processing);
    }

    [Fact]
    public async Task Deleting_a_processing_job_makes_its_handle_lose_the_lease()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();

        Assert.True(await Storage.DeleteAsync(id, Ct));

        await Assert.ThrowsAsync<LeaseLostException>(() => fetched.CompleteAsync(Ct));
        Assert.Null(await Storage.GetAsync(id, Ct));
    }

    // ================================================================ Operator actions

    [Fact]
    public async Task Requeue_runs_a_failed_job_again_now_with_attempt_reset()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        var fetched = await FetchOneAsync();
        await fetched.FailAsync("boom", Ct);

        Time.Advance(TimeSpan.FromHours(1));
        Assert.True(await Storage.RequeueAsync(id, Ct));

        var stored = await Storage.GetAsync(id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(JobState.Scheduled, stored.State);
        Assert.Equal(Start.AddHours(1), stored.RunAt);
        Assert.Equal(0, stored.Attempt);
        Assert.Null(stored.CompletedAt);

        Assert.Equal(id, (await FetchOneAsync()).Record.Id);
    }

    [Fact]
    public async Task Requeue_also_works_for_scheduled_and_succeeded_jobs()
    {
        var scheduled = await Storage.CreateAsync(NewJob(runAt: Start.AddDays(1)), Ct);
        var succeeded = await Storage.CreateAsync(NewJob(), Ct);
        await (await FetchOneAsync()).CompleteAsync(Ct);

        Assert.True(await Storage.RequeueAsync(scheduled, Ct));
        Assert.True(await Storage.RequeueAsync(succeeded, Ct));

        Assert.Equal(2, (await FetchAsync()).Count);
    }

    [Fact]
    public async Task Requeue_refuses_processing_and_unknown_jobs()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);
        await FetchOneAsync();

        Assert.False(await Storage.RequeueAsync(id, Ct));
        Assert.False(await Storage.RequeueAsync("nope", Ct));
    }

    [Fact]
    public async Task Delete_removes_the_job()
    {
        var id = await Storage.CreateAsync(NewJob(), Ct);

        Assert.True(await Storage.DeleteAsync(id, Ct));
        Assert.Null(await Storage.GetAsync(id, Ct));
        Assert.False(await Storage.DeleteAsync(id, Ct));
    }

    [Fact]
    public async Task Purge_deletes_only_jobs_of_the_given_state_completed_before_the_threshold()
    {
        var oldSucceeded = await Storage.CreateAsync(NewJob(), Ct);
        await (await FetchOneAsync()).CompleteAsync(Ct);
        var oldFailed = await Storage.CreateAsync(NewJob(), Ct);
        await (await FetchOneAsync()).FailAsync("x", Ct);

        Time.Advance(TimeSpan.FromDays(2));
        var newSucceeded = await Storage.CreateAsync(NewJob(), Ct);
        await (await FetchOneAsync()).CompleteAsync(Ct);
        var pending = await Storage.CreateAsync(NewJob(createdAt: Start.AddDays(-30), runAt: Start.AddDays(30)), Ct);

        var purged = await Storage.PurgeAsync(JobState.Succeeded, Start.AddDays(1), Ct);

        Assert.Equal(1, purged);
        Assert.Null(await Storage.GetAsync(oldSucceeded, Ct));
        Assert.NotNull(await Storage.GetAsync(oldFailed, Ct));
        Assert.NotNull(await Storage.GetAsync(newSucceeded, Ct));
        Assert.NotNull(await Storage.GetAsync(pending, Ct));
    }

    // ================================================================ Monitoring

    [Fact]
    public async Task Stats_count_jobs_by_state_overall_and_per_queue()
    {
        await Storage.CreateAsync(NewJob(queue: "a"), Ct);
        await Storage.CreateAsync(NewJob(queue: "a", runAt: Start.AddDays(1)), Ct);
        await Storage.CreateAsync(NewJob(queue: "b"), Ct);
        await Storage.CreateAsync(NewJob(queue: "b"), Ct);
        await Storage.CreateAsync(NewJob(queue: "b"), Ct);

        var fetched = await FetchAsync(max: 10, queues: ["b"]);
        await fetched[0].CompleteAsync(Ct);
        await fetched[1].FailAsync("x", Ct);
        await Storage.UpsertRecurringAsync(NewRecurring("r1"), Ct);

        var stats = await Storage.GetStatsAsync(Ct);

        Assert.Equal(2, stats.Scheduled);
        Assert.Equal(1, stats.Processing);
        Assert.Equal(1, stats.Succeeded);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(1, stats.Recurring);

        var a = Assert.Single(stats.Queues, q => q.Name == "a");
        Assert.Equal(new QueueStats("a", 2, 0, 0, 0), a);
        var b = Assert.Single(stats.Queues, q => q.Name == "b");
        Assert.Equal(new QueueStats("b", 0, 1, 1, 1), b);
    }

    [Fact]
    public async Task Stats_are_empty_for_an_empty_storage()
    {
        var stats = await Storage.GetStatsAsync(Ct);
        Assert.Equal(JobStats.Empty with { Queues = stats.Queues }, stats);
        Assert.Empty(stats.Queues);
    }

    [Fact]
    public async Task Query_filters_by_state_and_queue_and_pages_newest_first()
    {
        var ids = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            ids.Add(await Storage.CreateAsync(NewJob(queue: i % 2 == 0 ? "even" : "odd"), Ct));
        }
        var fetched = await FetchAsync(max: 10, queues: ["even"]);
        foreach (var f in fetched) await f.CompleteAsync(Ct);

        var all = await Storage.QueryAsync(new JobQuery { Take = 4 }, Ct);
        Assert.Equal(6, all.Total);
        Assert.Equal(ids.AsEnumerable().Reverse().Take(4), all.Items.Select(j => j.Id));

        var secondPage = await Storage.QueryAsync(new JobQuery { Skip = 4, Take = 4 }, Ct);
        Assert.Equal(6, secondPage.Total);
        Assert.Equal(ids.AsEnumerable().Reverse().Skip(4), secondPage.Items.Select(j => j.Id));

        var succeeded = await Storage.QueryAsync(new JobQuery { State = JobState.Succeeded }, Ct);
        Assert.Equal(3, succeeded.Total);
        Assert.All(succeeded.Items, j => Assert.Equal(JobState.Succeeded, j.State));

        var oddScheduled = await Storage.QueryAsync(new JobQuery { State = JobState.Scheduled, Queue = "odd" }, Ct);
        Assert.Equal(3, oddScheduled.Total);
        Assert.All(oddScheduled.Items, j => Assert.Equal("odd", j.Queue));

        var none = await Storage.QueryAsync(new JobQuery { State = JobState.Failed, Queue = "odd" }, Ct);
        Assert.Equal(0, none.Total);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Query_clamps_paging_arguments()
    {
        await Storage.CreateAsync(NewJob(), Ct);
        var page = await Storage.QueryAsync(new JobQuery { Skip = -5, Take = 100_000 }, Ct);
        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
    }

    // ================================================================ Recurring jobs

    protected RecurringJob NewRecurring(string id, DateTimeOffset? next = null) => new()
    {
        Id = id,
        Job = NewJob().Job,
        Cron = "*/5 * * * *",
        TimeZoneId = "UTC",
        Queue = "default",
        NextRunAt = next ?? Start.AddMinutes(5),
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    [Fact]
    public async Task Recurring_jobs_can_be_upserted_read_listed_and_deleted()
    {
        var r = NewRecurring("nightly") with { TimeZoneId = "Europe/London", Queue = "reports", LastRunAt = Start.AddDays(-1), LastJobId = "job-1" };
        await Storage.UpsertRecurringAsync(r, Ct);
        await Storage.UpsertRecurringAsync(NewRecurring("hourly"), Ct);

        var stored = await Storage.GetRecurringAsync("nightly", Ct);
        Assert.Equal(r, stored);

        var updated = r with { Cron = "0 3 * * *", NextRunAt = Start.AddDays(1), UpdatedAt = Start.AddSeconds(1) };
        await Storage.UpsertRecurringAsync(updated, Ct);
        Assert.Equal(updated, await Storage.GetRecurringAsync("nightly", Ct));

        var list = await Storage.ListRecurringAsync(Ct);
        Assert.Equal(["hourly", "nightly"], list.Select(x => x.Id));

        Assert.True(await Storage.DeleteRecurringAsync("nightly", Ct));
        Assert.False(await Storage.DeleteRecurringAsync("nightly", Ct));
        Assert.Null(await Storage.GetRecurringAsync("nightly", Ct));
    }

    [Fact]
    public async Task GetDueRecurring_returns_due_schedules_soonest_first_and_skips_parked_ones()
    {
        await Storage.UpsertRecurringAsync(NewRecurring("later", Start.AddMinutes(2)), Ct);
        await Storage.UpsertRecurringAsync(NewRecurring("sooner", Start.AddMinutes(1)), Ct);
        await Storage.UpsertRecurringAsync(NewRecurring("future", Start.AddMinutes(30)), Ct);
        await Storage.UpsertRecurringAsync(NewRecurring("parked") with { NextRunAt = null }, Ct);

        Assert.Empty(await Storage.GetDueRecurringAsync(10, Ct));

        Time.Advance(TimeSpan.FromMinutes(2));
        var due = await Storage.GetDueRecurringAsync(10, Ct);
        Assert.Equal(["sooner", "later"], due.Select(r => r.Id));

        Assert.Single(await Storage.GetDueRecurringAsync(1, Ct));
    }

    [Fact]
    public async Task TriggerRecurring_is_a_compare_and_set_that_exactly_one_caller_wins()
    {
        await Storage.UpsertRecurringAsync(NewRecurring("r", Start), Ct);
        var jobA = NewJob(queue: "default") with { CreatedAt = Start.AddSeconds(1), RunAt = Start.AddSeconds(1) };
        var jobB = NewJob(queue: "default") with { CreatedAt = Start.AddSeconds(1), RunAt = Start.AddSeconds(1) };

        var won = await Storage.TriggerRecurringAsync("r", Start, Start.AddMinutes(5), jobA, Ct);
        var lost = await Storage.TriggerRecurringAsync("r", Start, Start.AddMinutes(5), jobB, Ct);

        Assert.True(won);
        Assert.False(lost);
        Assert.NotNull(await Storage.GetAsync(jobA.Id, Ct));
        Assert.Null(await Storage.GetAsync(jobB.Id, Ct));

        var recurring = await Storage.GetRecurringAsync("r", Ct);
        Assert.NotNull(recurring);
        Assert.Equal(Start.AddMinutes(5), recurring.NextRunAt);
        Assert.Equal(Start.AddSeconds(1), recurring.LastRunAt);
        Assert.Equal(jobA.Id, recurring.LastJobId);

        Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(jobA.Id, (await FetchOneAsync()).Record.Id);
    }

    [Fact]
    public async Task TriggerRecurring_can_park_a_schedule_with_no_next_occurrence()
    {
        await Storage.UpsertRecurringAsync(NewRecurring("once", Start), Ct);
        Assert.True(await Storage.TriggerRecurringAsync("once", Start, null, NewJob(), Ct));
        Assert.Null((await Storage.GetRecurringAsync("once", Ct))!.NextRunAt);
        Assert.Empty(await Storage.GetDueRecurringAsync(10, Ct));
    }

    [Fact]
    public async Task TriggerRecurring_returns_false_for_unknown_ids()
        => Assert.False(await Storage.TriggerRecurringAsync("ghost", Start, Start.AddMinutes(1), NewJob(), Ct));

    // ================================================================ Wake-ups

    [Fact]
    public async Task WaitForJob_returns_promptly_when_a_job_is_created()
    {
        var wait = Storage.WaitForJobAsync(TimeSpan.FromSeconds(30), Ct);
        await Task.Delay(50, Ct); // let the provider subscribe

        await Storage.CreateAsync(NewJob(), Ct);

        await wait.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task WaitForJob_returns_when_the_timeout_elapses()
    {
        var wait = Storage.WaitForJobAsync(TimeSpan.FromMilliseconds(200), Ct);
        // Providers that wait on the TimeProvider need the clock advanced (after they have set up their wait); the others use real time.
        for (var i = 0; i < 100 && !wait.IsCompleted; i++)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50, Ct);
        }
        await wait.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task WaitForJob_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var wait = Storage.WaitForJobAsync(TimeSpan.FromMinutes(5), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    // ================================================================ Concurrency

    [Fact]
    public async Task Concurrent_fetchers_never_receive_the_same_job()
    {
        const int jobCount = 60;
        for (var i = 0; i < jobCount; i++) await Storage.CreateAsync(NewJob(), Ct);

        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        var workers = Enumerable.Range(0, 8).Select(async w =>
        {
            while (true)
            {
                var fetched = await Storage.FetchAsync(["default"], $"w{w}", TimeSpan.FromMinutes(5), 4, Ct);
                if (fetched.Count == 0) return;
                foreach (var f in fetched)
                {
                    seen.Add(f.Record.Id);
                    await f.CompleteAsync(Ct);
                }
            }
        });
        await Task.WhenAll(workers);

        Assert.Equal(jobCount, seen.Count);
        Assert.Equal(jobCount, seen.Distinct().Count());
        Assert.Equal(jobCount, (await Storage.GetStatsAsync(Ct)).Succeeded);
    }
}
