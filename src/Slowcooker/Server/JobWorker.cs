using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slowcooker.Execution;
using Slowcooker.Storage;

namespace Slowcooker.Server;

/// <summary>
/// The background worker. One instance per process; concurrency is handled with async slots, not threads.
///
/// Failure handling:
/// <list type="bullet">
///   <item>Double execution: jobs are fetched atomically by storage; a fetch is the only path into Processing.</item>
///   <item>Crash recovery: every fetch carries a lease. Heartbeats extend it while the job runs. If the process dies,
///   the lease expires and another worker fetches the job. If a heartbeat reports the lease lost, the job's token is cancelled
///   and the job is not settled, since it belongs to someone else now.</item>
///   <item>Poison jobs: attempts are counted at fetch time, so a job that crashes the process still exhausts its retries.</item>
///   <item>Retries: delegated to <see cref="IRetryPolicy"/>; exhausting it moves the job to Failed (dead letter), never deletes it.</item>
///   <item>Graceful shutdown: stop fetching, wait up to <see cref="WorkerOptions.ShutdownTimeout"/>, then cancel the job tokens and
///   requeue unfinished jobs so another instance picks them up immediately without burning an attempt.</item>
/// </list>
/// </summary>
internal sealed class JobWorker(
    IJobStorage storage,
    JobExecutor executor,
    IJobClient client,
    IServiceScopeFactory scopeFactory,
    IRetryPolicy retryPolicy,
    TimeProvider timeProvider,
    IOptions<WorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly CancellationTokenSource _jobCts = new();
    private readonly ConcurrentDictionary<string, Task> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        var queues = _options.Queues.ToArray();
        logger.LogInformation("Slowcooker worker {WorkerId} starting: queues [{Queues}], concurrency {Concurrency}", _options.WorkerId, string.Join(", ", queues), _options.MaxConcurrency);

        // Let the host finish starting before we hit storage.
        await Task.Yield();

        var background = new List<Task>();
        if (_options.RunRecurringJobScheduler)
            background.Add(new RecurringJobScheduler(storage, timeProvider, _options, logger).RunAsync(stoppingToken));
        if (_options.SucceededJobRetention is not null || _options.FailedJobRetention is not null)
            background.Add(RunPurgeLoopAsync(stoppingToken));

        using var slots = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        var consecutiveErrors = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await slots.WaitAsync(stoppingToken).ConfigureAwait(false); // we now hold one slot
                var batch = Math.Min(1 + slots.CurrentCount, _options.FetchBatchSize);

                var fetched = await storage.FetchAsync(queues, _options.WorkerId, _options.LeaseDuration, batch, stoppingToken).ConfigureAwait(false);
                consecutiveErrors = 0;

                if (fetched.Count == 0)
                {
                    slots.Release();
                    await storage.WaitForJobAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                for (var i = 0; i < fetched.Count; i++)
                {
                    if (i > 0 && !slots.Wait(0))
                        throw new InvalidOperationException("Fetched more jobs than free slots. This is a bug in the worker.");

                    var job = fetched[i];
                    _inFlight[job.Record.Id] = Task.Run(() => ProcessAsync(job, slots), CancellationToken.None);
                }

                // When the batch came back full, storage probably has more: go straight back without waiting.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, consecutiveErrors)));
                logger.LogError(ex, "Slowcooker worker failed to fetch jobs (attempt {Attempt}); retrying in {Backoff}", consecutiveErrors, backoff);
                try { await Task.Delay(backoff, timeProvider, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        await ShutdownAsync().ConfigureAwait(false);
        await Task.WhenAll(background).ConfigureAwait(false);
    }

    private async Task ProcessAsync(IFetchedJob fetched, SemaphoreSlim slots)
    {
        var record = fetched.Record;
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_jobCts.Token);
        using var heartbeatCts = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(fetched, jobCts, heartbeatCts.Token);

        try
        {
            await using (fetched.ConfigureAwait(false)) // requeues if we somehow fail to settle below
            {
                logger.LogDebug("Job {JobId} ({Job}) started, attempt {Attempt}", record.Id, record.Job.DisplayName, record.Attempt);

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await executor.ExecuteAsync(record, scope.ServiceProvider, client, jobCts.Token).ConfigureAwait(false);

                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                    await fetched.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                    logger.LogInformation("Job {JobId} ({Job}) succeeded", record.Id, record.Job.DisplayName);
                }
                catch (LeaseLostException)
                {
                    logger.LogWarning("Job {JobId} ({Job}) finished but its lease had been lost; another worker may re-run it", record.Id, record.Job.DisplayName);
                }
                catch (OperationCanceledException) when (_jobCts.IsCancellationRequested)
                {
                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await fetched.RequeueAsync(CancellationToken.None).ConfigureAwait(false);
                        logger.LogWarning("Job {JobId} ({Job}) interrupted by shutdown and requeued", record.Id, record.Job.DisplayName);
                    }
                    catch (LeaseLostException)
                    {
                        logger.LogWarning("Job {JobId} ({Job}) interrupted by shutdown; its lease was already lost", record.Id, record.Job.DisplayName);
                    }
                }
                catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
                {
                    // Lease lost: somebody else owns the job now. Touch nothing; disposing the handle is a no-op in storage.
                    logger.LogWarning("Job {JobId} ({Job}) was cancelled because its lease was lost", record.Id, record.Job.DisplayName);
                }
                catch (Exception ex)
                {
                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                    await HandleFailureAsync(fetched, ex).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while finishing job {JobId}", record.Id);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat.ConfigureAwait(false); } catch { /* logged inside */ }
            _inFlight.TryRemove(record.Id, out _);
            slots.Release();
        }
    }

    private async Task HandleFailureAsync(IFetchedJob fetched, Exception exception)
    {
        var record = fetched.Record;
        var error = exception.ToString();
        TimeSpan? delay;
        try
        {
            delay = retryPolicy.GetRetryDelay(record.Attempt, exception);
        }
        catch (Exception policyError)
        {
            logger.LogError(policyError, "Retry policy threw for job {JobId}; failing the job", record.Id);
            delay = null;
        }

        try
        {
            if (delay is { } d)
            {
                var runAt = timeProvider.GetUtcNow() + d;
                await fetched.RetryAsync(runAt, error, CancellationToken.None).ConfigureAwait(false);
                logger.LogWarning(exception, "Job {JobId} ({Job}) failed on attempt {Attempt}; retry at {RunAt}", record.Id, record.Job.DisplayName, record.Attempt, runAt);
            }
            else
            {
                await fetched.FailAsync(error, CancellationToken.None).ConfigureAwait(false);
                logger.LogError(exception, "Job {JobId} ({Job}) failed permanently after {Attempt} attempt(s)", record.Id, record.Job.DisplayName, record.Attempt);
            }
        }
        catch (LeaseLostException)
        {
            logger.LogWarning(exception, "Job {JobId} ({Job}) failed on attempt {Attempt} but its lease had been lost; another worker owns it now", record.Id, record.Job.DisplayName, record.Attempt);
        }
    }

    /// <summary>Extends the lease every LeaseDuration/3 while the job runs. Losing the lease cancels the job.</summary>
    private async Task HeartbeatAsync(IFetchedJob fetched, CancellationTokenSource jobCts, CancellationToken stop)
    {
        var id = fetched.Record.Id;
        var interval = TimeSpan.FromTicks(_options.LeaseDuration.Ticks / 3);
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, timeProvider, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            try
            {
                await fetched.ExtendLeaseAsync(_options.LeaseDuration, stop).ConfigureAwait(false);
                failures = 0;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (LeaseLostException)
            {
                logger.LogWarning("Lost the lease on job {JobId}; cancelling it", id);
                await jobCts.CancelAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                // Transient storage error: one more try before the lease would actually expire.
                failures++;
                logger.LogWarning(ex, "Heartbeat for job {JobId} failed ({Failures})", id, failures);
                if (failures >= 2)
                {
                    logger.LogWarning("Heartbeats for job {JobId} keep failing; cancelling it before the lease expires", id);
                    await jobCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private async Task ShutdownAsync()
    {
        var running = _inFlight.Values.ToArray();
        if (running.Length == 0) return;

        logger.LogInformation("Slowcooker worker stopping; waiting up to {Timeout} for {Count} running job(s)", _options.ShutdownTimeout, running.Length);
        try
        {
            await Task.WhenAll(running).WaitAsync(_options.ShutdownTimeout, timeProvider).ConfigureAwait(false);
            return;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Shutdown timeout elapsed; cancelling {Count} job(s) and requeueing them", _inFlight.Count);
        }
        catch (Exception)
        {
            // Individual job failures are handled inside ProcessAsync; nothing to do here.
        }

        await _jobCts.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(_inFlight.Values.ToArray()).ConfigureAwait(false); } catch { /* logged in ProcessAsync */ }
    }

    private async Task RunPurgeLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PurgeInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                var now = timeProvider.GetUtcNow();
                if (_options.SucceededJobRetention is { } s)
                {
                    var n = await storage.PurgeAsync(JobState.Succeeded, now - s, stoppingToken).ConfigureAwait(false);
                    if (n > 0) logger.LogDebug("Purged {Count} succeeded job(s)", n);
                }
                if (_options.FailedJobRetention is { } f)
                {
                    var n = await storage.PurgeAsync(JobState.Failed, now - f, stoppingToken).ConfigureAwait(false);
                    if (n > 0) logger.LogDebug("Purged {Count} failed job(s)", n);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purging old jobs failed; will retry next interval");
            }
        }
    }

    public override void Dispose()
    {
        _jobCts.Dispose();
        base.Dispose();
    }
}
