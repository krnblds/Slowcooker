namespace Slowcooker;

/// <summary>Configuration for the background worker added with <c>AddSlowcooker(...).AddWorker(...)</c>.</summary>
public sealed class WorkerOptions
{
    /// <summary>Queues this worker serves. Default: just "default".</summary>
    public IList<string> Queues { get; set; } = [JobOptions.DefaultQueue];

    /// <summary>Maximum number of jobs executing at once in this process. Default: processor count.</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum jobs fetched per storage round-trip. The worker fetches min(free slots, FetchBatchSize). Default 10.
    /// </summary>
    public int FetchBatchSize { get; set; } = 10;

    /// <summary>
    /// Upper bound on how long the worker waits before asking storage again when it found nothing.
    /// Storages with push notifications wake the worker earlier. Default 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a lease lasts before another worker may assume this one crashed and take the job over.
    /// The worker renews the lease every third of this interval while the job runs. Default 5 minutes.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait for running jobs on shutdown before cancelling their <see cref="CancellationToken"/> and releasing them.
    /// Keep this below the host's shutdown timeout (<c>HostOptions.ShutdownTimeout</c>). Default 20 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Whether this worker also runs the recurring job scheduler. Default true. Safe to enable on every instance.</summary>
    public bool RunRecurringJobScheduler { get; set; } = true;

    /// <summary>How often the recurring job scheduler checks for due schedules. Default 15 seconds.</summary>
    public TimeSpan RecurringJobPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long succeeded jobs are kept before being purged. Null keeps them forever. Default 1 day.</summary>
    public TimeSpan? SucceededJobRetention { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How long failed jobs are kept before being purged. Null keeps them forever. Default 7 days.</summary>
    public TimeSpan? FailedJobRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often retention is applied. Default 5 minutes.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Identifies this worker in storage. Default: machine name, process id and a random suffix.</summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..8]}";

    internal void Validate()
    {
        if (Queues.Count == 0) throw new InvalidOperationException("WorkerOptions.Queues must contain at least one queue.");
        if (MaxConcurrency < 1) throw new InvalidOperationException("WorkerOptions.MaxConcurrency must be at least 1.");
        if (FetchBatchSize < 1) throw new InvalidOperationException("WorkerOptions.FetchBatchSize must be at least 1.");
        if (PollInterval <= TimeSpan.Zero) throw new InvalidOperationException("WorkerOptions.PollInterval must be positive.");
        if (LeaseDuration < TimeSpan.FromSeconds(1)) throw new InvalidOperationException("WorkerOptions.LeaseDuration must be at least 1 second.");
        if (string.IsNullOrWhiteSpace(WorkerId)) throw new InvalidOperationException("WorkerOptions.WorkerId must not be empty.");
    }
}
