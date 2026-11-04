using Microsoft.Extensions.Logging;
using Slowcooker.Internal;
using Slowcooker.Storage;

namespace Slowcooker.Server;

/// <summary>
/// Turns due recurring jobs into real jobs. Safe to run on every instance: triggering is a compare-and-set on the
/// recurring job's <see cref="RecurringJob.NextRunAt"/>, so when several instances race exactly one creates the job.
///
/// Misfires: if the scheduler was down and several occurrences were missed, the job is triggered once (for the
/// most recent missed occurrence) and the next run is computed from now. Running once per missed occurrence
/// is almost never what people want after an outage.
/// </summary>
internal sealed class RecurringJobScheduler(IJobStorage storage, TimeProvider timeProvider, WorkerOptions options, ILogger logger)
{
    private const int BatchSize = 100;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.RecurringJobPollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring job scheduler tick failed; will retry");
                try { await Task.Delay(options.RecurringJobPollInterval, timeProvider, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = timeProvider.GetUtcNow();
            var due = await storage.GetDueRecurringAsync(BatchSize, cancellationToken).ConfigureAwait(false);
            if (due.Count == 0) return;

            foreach (var recurring in due)
            {
                if (recurring.NextRunAt is not { } expected) continue;
                await TriggerAsync(recurring, expected, now, cancellationToken).ConfigureAwait(false);
            }

            if (due.Count < BatchSize) return;
        }
    }

    private async Task TriggerAsync(RecurringJob recurring, DateTimeOffset expected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset? next;
        try
        {
            var timeZone = FindTimeZone(recurring.TimeZoneId);
            next = CronSchedule.Parse(recurring.Cron).GetNextOccurrence(now, timeZone);
        }
        catch (Exception ex)
        {
            // Bad cron or unknown zone (e.g. created by a newer version). Park it: NextRunAt = null means "never".
            logger.LogError(ex, "Recurring job {RecurringJobId} has an invalid schedule and has been disabled", recurring.Id);
            await storage.UpsertRecurringAsync(recurring with { NextRunAt = null, UpdatedAt = now }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = new StoredJob
        {
            Id = JobIds.New(),
            Job = recurring.Job,
            Queue = recurring.Queue,
            State = JobState.Scheduled,
            CreatedAt = now,
            RunAt = now,
        };

        var won = await storage.TriggerRecurringAsync(recurring.Id, expected, next, record, cancellationToken).ConfigureAwait(false);
        if (won)
            logger.LogInformation("Recurring job {RecurringJobId} triggered job {JobId}; next run {Next}", recurring.Id, record.Id, next);
        else
            logger.LogDebug("Recurring job {RecurringJobId} was triggered by another instance", recurring.Id);
    }

    private static TimeZoneInfo FindTimeZone(string id)
        => id is "UTC" or "Etc/UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
}
