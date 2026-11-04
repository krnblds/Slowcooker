using Slowcooker.Execution;
using Slowcooker.Internal;
using Slowcooker.Storage;

namespace Slowcooker;

internal sealed class RecurringJobClient(IJobStorage storage, JobSerializer serializer, TimeProvider timeProvider) : IRecurringJobClient
{
    public Task AddOrUpdateAsync<TJob>(string id, TJob job, string cron, RecurringJobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(job);
        options ??= RecurringJobOptions.Default;
        return AddOrUpdateAsync(id, serializer.Serialize(job), cron, options with { Queue = options.Queue ?? serializer.DefaultQueue(job.GetType()) }, cancellationToken);
    }

    public async Task AddOrUpdateAsync(string id, Job job, string cron, RecurringJobOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(job);
        options ??= RecurringJobOptions.Default;

        var schedule = CronSchedule.Parse(cron); // throws on invalid cron, before anything is stored
        var now = timeProvider.GetUtcNow();
        var existing = await storage.GetRecurringAsync(id, cancellationToken).ConfigureAwait(false);

        await storage.UpsertRecurringAsync(new RecurringJob
        {
            Id = id,
            Job = job,
            Cron = cron,
            TimeZoneId = options.TimeZone.Id,
            Queue = options.Queue ?? JobOptions.DefaultQueue,
            NextRunAt = schedule.GetNextOccurrence(now, options.TimeZone),
            LastRunAt = existing?.LastRunAt,
            LastJobId = existing?.LastJobId,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
        => storage.DeleteRecurringAsync(id, cancellationToken);

    public async Task<string?> TriggerAsync(string id, CancellationToken cancellationToken = default)
    {
        var recurring = await storage.GetRecurringAsync(id, cancellationToken).ConfigureAwait(false);
        if (recurring is null) return null;

        var now = timeProvider.GetUtcNow();
        var record = new StoredJob
        {
            Id = JobIds.New(),
            Job = recurring.Job,
            Queue = recurring.Queue,
            State = JobState.Scheduled,
            CreatedAt = now,
            RunAt = now,
        };
        return await storage.CreateAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
