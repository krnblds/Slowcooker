namespace Slowcooker.Storage;

/// <summary>
/// A job leased to one worker. Exactly one of Complete, Fail, Retry or Requeue should be called.
/// Each one changes the job's state and releases the lease in a single atomic step,
/// so providers don't need a general-purpose transaction API.
/// </summary>
public interface IFetchedJob : IAsyncDisposable
{
    /// <summary>The job, already in the Processing state with Attempt incremented.</summary>
    StoredJob Record { get; }

    /// <summary>Marks the job Succeeded.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks the job Failed for good.</summary>
    Task FailAsync(string error, CancellationToken cancellationToken = default);

    /// <summary>Moves the job back to Scheduled so it runs again at <paramref name="runAt"/>.</summary>
    Task RetryAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken = default);

    /// <summary>Puts the job straight back in its queue without counting a failure, e.g. on shutdown.</summary>
    Task RequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the lease expiry forward. Long-running jobs call this periodically (a heartbeat)
    /// so they aren't mistaken for crashed and picked up by another worker.
    /// </summary>
    Task ExtendLeaseAsync(TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    // DisposeAsync: if none of the above was called, requeue the job.
}