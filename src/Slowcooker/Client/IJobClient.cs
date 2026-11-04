namespace Slowcooker;

/// <summary>Creates background jobs. Inject it anywhere; it is a singleton.</summary>
public interface IJobClient
{
    /// <summary>Stores <paramref name="job"/> so a worker runs it as soon as possible (or per <paramref name="options"/>).</summary>
    /// <returns>The id of the created job, or of the existing job when an idempotency key matched one.</returns>
    Task<string> EnqueueAsync<TJob>(TJob job, JobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob;

    /// <summary>Low-level: stores an already serialized job. Used by the recurring scheduler and by tooling.</summary>
    Task<string> CreateAsync(Job job, JobOptions options, CancellationToken cancellationToken = default);
}

public static class JobClientExtensions
{
    /// <summary>Runs the job after <paramref name="delay"/>.</summary>
    public static Task<string> ScheduleAsync<TJob>(this IJobClient client, TJob job, TimeSpan delay, JobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob
        => client.EnqueueAsync(job, (options ?? JobOptions.Default) with { Delay = delay, RunAt = null }, cancellationToken);

    /// <summary>Runs the job at <paramref name="runAt"/>.</summary>
    public static Task<string> ScheduleAsync<TJob>(this IJobClient client, TJob job, DateTimeOffset runAt, JobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob
        => client.EnqueueAsync(job, (options ?? JobOptions.Default) with { RunAt = runAt, Delay = null }, cancellationToken);
}
