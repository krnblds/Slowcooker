using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slowcooker.Storage;
using Slowcooker.Tests.Support;

namespace Slowcooker.Tests;

/// <summary>End-to-end worker behaviour against the in-memory storage, on real time with short intervals.</summary>
public sealed class JobWorkerTests : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly IHostedService _worker;
    private readonly Recorder _recorder;
    private readonly IJobClient _client;
    private readonly IJobStorage _storage;

    public JobWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<Recorder>();
        services.AddSlowcooker()
            .UseInMemoryStorage()
            .AddHandler<TestHandlers>()
            .UseRetryPolicy(new FastRetryPolicy(maxAttempts: 3))
            .AddWorker(o =>
            {
                o.Queues = ["default", "payments"];
                o.PollInterval = TimeSpan.FromMilliseconds(50);
                o.LeaseDuration = TimeSpan.FromSeconds(1);
                o.ShutdownTimeout = TimeSpan.FromMilliseconds(500);
                o.RecurringJobPollInterval = TimeSpan.FromMilliseconds(100);
                o.MaxConcurrency = 4;
            });

        _services = services.BuildServiceProvider();
        _worker = _services.GetRequiredService<IHostedService>();
        _recorder = _services.GetRequiredService<Recorder>();
        _client = _services.GetRequiredService<IJobClient>();
        _storage = _services.GetRequiredService<IJobStorage>();
    }

    private sealed class FastRetryPolicy(int maxAttempts) : IRetryPolicy
    {
        public TimeSpan? GetRetryDelay(int attempt, Exception exception) => attempt >= maxAttempts ? null : TimeSpan.FromMilliseconds(10);
    }

    private async Task<StoredJob> JobAsync(string id) => (await _storage.GetAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task Runs_enqueued_jobs_and_marks_them_succeeded()
    {
        await _worker.StartAsync(CancellationToken.None);

        var id = await _client.EnqueueAsync(new Note("hello"));

        await Wait.UntilAsync(() => _recorder.Calls.Contains("hello"));
        await Wait.UntilAsync(async () => (await JobAsync(id)).State == JobState.Succeeded);
        var job = await JobAsync(id);
        Assert.Equal(1, job.Attempt);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task Picks_up_new_jobs_within_milliseconds_thanks_to_the_wake_up_signal()
    {
        await _worker.StartAsync(CancellationToken.None);
        await Task.Delay(100); // worker is now idle, waiting for a signal

        var started = DateTime.UtcNow;
        await _client.EnqueueAsync(new Note("fast"));
        await Wait.UntilAsync(() => _recorder.Calls.Contains("fast"));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Jobs_on_queues_this_worker_does_not_serve_are_left_alone()
    {
        await _worker.StartAsync(CancellationToken.None);
        var served = await _client.EnqueueAsync(new ChargeCard("o", 1));                           // payments: served
        var ignored = await _client.EnqueueAsync(new Note("x"), new JobOptions { Queue = "other" }); // other: not served

        await Wait.UntilAsync(async () => (await JobAsync(served)).State == JobState.Succeeded);
        await Task.Delay(200);
        Assert.Equal(JobState.Scheduled, (await JobAsync(ignored)).State);
    }

    [Fact]
    public async Task Retries_failed_jobs_until_they_succeed()
    {
        await _worker.StartAsync(CancellationToken.None);

        var id = await _client.EnqueueAsync(new FailTimes("flaky", 2));

        await Wait.UntilAsync(() => _recorder.Calls.Contains("flaky:ok"));
        await Wait.UntilAsync(async () => (await JobAsync(id)).State == JobState.Succeeded);
        var job = await JobAsync(id);
        Assert.Equal(3, job.Attempt);
        Assert.Contains("transient failure 2", job.LastError);
    }

    [Fact]
    public async Task Moves_jobs_to_Failed_when_the_retry_policy_gives_up()
    {
        await _worker.StartAsync(CancellationToken.None);

        var id = await _client.EnqueueAsync(new AlwaysFail("hopeless"));

        await Wait.UntilAsync(async () => (await JobAsync(id)).State == JobState.Failed);
        var job = await JobAsync(id);
        Assert.Equal(3, job.Attempt);
        Assert.Equal(3, _recorder.Counters["hopeless"]);
        Assert.Contains("hopeless", job.LastError);
        Assert.NotNull(job.CompletedAt);

        await Task.Delay(200);
        Assert.Equal(3, _recorder.Counters["hopeless"]); // dead letter: no more runs
    }

    [Fact]
    public async Task Unknown_job_types_are_retried_like_any_failure_and_then_dead_lettered()
    {
        // A rolling deploy can hand an old worker a job type it does not know yet; the retry gives a new worker a chance.
        await _worker.StartAsync(CancellationToken.None);

        var id = await _client.CreateAsync(new Job("no-such-job", "{}"), JobOptions.Default);

        await Wait.UntilAsync(async () => (await JobAsync(id)).State == JobState.Failed);
        var job = await JobAsync(id);
        Assert.Equal(3, job.Attempt);
        Assert.Contains("no-such-job", job.LastError);
    }

    [Fact]
    public async Task Runs_jobs_concurrently_up_to_MaxConcurrency()
    {
        await _worker.StartAsync(CancellationToken.None);

        for (var i = 0; i < 6; i++)
            await _client.EnqueueAsync(new WaitForGate($"g{i}"));

        await Wait.UntilAsync(() => _recorder.Started.Count == 4);
        await Task.Delay(200);
        Assert.Equal(4, _recorder.Started.Count); // the 5th and 6th wait for a slot
        Assert.Equal(4, (await _storage.GetStatsAsync(CancellationToken.None)).Processing);

        foreach (var key in _recorder.Started.Keys.ToList()) _recorder.Gate(key).TrySetResult();
        await Wait.UntilAsync(() => _recorder.Started.Count == 6);
        foreach (var key in _recorder.Started.Keys.ToList()) _recorder.Gate(key).TrySetResult();

        await Wait.UntilAsync(async () => (await _storage.GetStatsAsync(CancellationToken.None)).Succeeded == 6);
    }

    [Fact]
    public async Task Graceful_shutdown_cancels_running_jobs_and_requeues_them_without_burning_an_attempt()
    {
        await _worker.StartAsync(CancellationToken.None);
        var id = await _client.EnqueueAsync(new WaitForGate("slow"));
        await _recorder.StartedSignal("slow").Task.WaitAsync(TimeSpan.FromSeconds(5));

        await _worker.StopAsync(CancellationToken.None);

        Assert.Contains("slow:cancelled", _recorder.Calls);
        var job = await JobAsync(id);
        Assert.Equal(JobState.Scheduled, job.State);
        Assert.Equal(0, job.Attempt);
        Assert.Null(job.LeaseOwner);
    }

    [Fact]
    public async Task Shutdown_waits_for_jobs_that_finish_in_time()
    {
        await _worker.StartAsync(CancellationToken.None);
        var id = await _client.EnqueueAsync(new WaitForGate("quick"));
        await _recorder.StartedSignal("quick").Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = _worker.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        _recorder.Gate("quick").TrySetResult();
        await stopping;

        Assert.Contains("quick:released", _recorder.Calls);
        Assert.Equal(JobState.Succeeded, (await JobAsync(id)).State);
    }

    [Fact]
    public async Task Heartbeats_keep_long_jobs_leased_beyond_the_lease_duration()
    {
        await _worker.StartAsync(CancellationToken.None);
        var id = await _client.EnqueueAsync(new WaitForGate("long"));
        await _recorder.StartedSignal("long").Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Delay(TimeSpan.FromSeconds(2.5)); // lease is 1s; without heartbeats the worker would re-fetch it

        var job = await JobAsync(id);
        Assert.Equal(JobState.Processing, job.State);
        Assert.Equal(1, job.Attempt);
        Assert.True(job.LeaseExpiresAt > DateTimeOffset.UtcNow);

        _recorder.Gate("long").TrySetResult();
        await Wait.UntilAsync(async () => (await JobAsync(id)).State == JobState.Succeeded);
        Assert.Equal(1, (await JobAsync(id)).Attempt);
    }

    [Fact]
    public async Task Losing_the_lease_cancels_the_running_job()
    {
        await _worker.StartAsync(CancellationToken.None);
        var id = await _client.EnqueueAsync(new WaitForGate("stolen"));
        await _recorder.StartedSignal("stolen").Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await _storage.DeleteAsync(id, CancellationToken.None)); // the next heartbeat finds no lease

        await Wait.UntilAsync(() => _recorder.Calls.Contains("stolen:cancelled"));
    }

    [Fact]
    public async Task Handlers_can_chain_jobs_through_the_context()
    {
        await _worker.StartAsync(CancellationToken.None);
        await _client.EnqueueAsync(new EnqueueFollowUp("chain"));
        await Wait.UntilAsync(() => _recorder.Calls.Contains("follow-up:chain"));
    }

    [Fact]
    public async Task Recurring_jobs_are_triggered_and_executed()
    {
        await _worker.StartAsync(CancellationToken.None);
        var recurring = _services.GetRequiredService<IRecurringJobClient>();

        await recurring.AddOrUpdateAsync("every-second", new Note("tick"), "* * * * * *");

        await Wait.UntilAsync(() => _recorder.Calls.Count(c => c == "tick") >= 2, TimeSpan.FromSeconds(15));
        var stored = (await _storage.GetRecurringAsync("every-second", CancellationToken.None))!;
        Assert.NotNull(stored.LastRunAt);
        Assert.NotNull(stored.LastJobId);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }
}
