namespace Slowcooker;

/// <summary>Manages jobs that run on a cron schedule. The job record is stored as a template and enqueued on every occurrence.</summary>
public interface IRecurringJobClient
{
    /// <summary>Creates or replaces a recurring job. Changing the schedule recomputes the next run; history (last run) is kept.</summary>
    Task AddOrUpdateAsync<TJob>(string id, TJob job, string cron, RecurringJobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob;

    /// <summary>Low-level: creates or replaces a recurring job from an already serialized template.</summary>
    Task AddOrUpdateAsync(string id, Job job, string cron, RecurringJobOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a recurring job from a typed <see cref="Cron"/> schedule.</summary>
    Task AddOrUpdateAsync<TJob>(string id, TJob job, Cron cron, RecurringJobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob
        => AddOrUpdateAsync(id, job, cron.Expression, options, cancellationToken);

    /// <summary>Removes a recurring job. Jobs it already created are not affected.</summary>
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Enqueues the template right now, without changing its schedule.</summary>
    /// <returns>The id of the created job, or null if the recurring job does not exist.</returns>
    Task<string?> TriggerAsync(string id, CancellationToken cancellationToken = default);
}
