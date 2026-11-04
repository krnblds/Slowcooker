namespace Slowcooker.Storage;

/// <summary>
/// The persistence contract. Every provider (in-memory, Postgres, Redis, ...) implements this and is verified by the
/// shared contract test suite in <c>tests/Slowcooker.Storage.ContractTests</c>.
///
/// Rules every provider must follow:
/// <list type="bullet">
///   <item><see cref="FetchAsync"/> is the only way a job enters <see cref="JobState.Processing"/>, and it must be atomic:
///   two workers can never fetch the same job at the same time.</item>
///   <item>A fetched job is leased. Settling it through <see cref="IFetchedJob"/> must be rejected (return false) when the
///   lease has been lost. This is what makes crash recovery safe: a job re-fetched after a lease expiry cannot be clobbered
///   by the original, slow worker.</item>
///   <item><see cref="StoredJob.Attempt"/> is incremented at fetch time, not completion time.</item>
///   <item>Providers get "now" from the <see cref="TimeProvider"/> they are constructed with, never from the wall clock directly,
///   so time-dependent behaviour is testable.</item>
/// </list>
/// </summary>
public interface IJobStorage
{
    // ---------------------------------------------------------------- Jobs

    /// <summary>Persists a new job in <see cref="JobState.Scheduled"/>.</summary>
    /// <returns>The job id. When <see cref="StoredJob.IdempotencyKey"/> is set and a job with that key already exists, the existing job's id.</returns>
    Task<string> CreateAsync(StoredJob job, CancellationToken cancellationToken);

    Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically leases up to <paramref name="maxCount"/> jobs that are eligible to run: in <see cref="JobState.Scheduled"/> with
    /// <see cref="StoredJob.RunAt"/> ≤ now, or in <see cref="JobState.Processing"/> with an expired lease (crash recovery).
    /// Fetched jobs move to <see cref="JobState.Processing"/>, get <paramref name="workerId"/> as lease owner, a lease until
    /// now + <paramref name="leaseDuration"/>, and <see cref="StoredJob.Attempt"/> + 1.
    /// Within a queue, jobs are fetched in <see cref="StoredJob.RunAt"/> order, then by id (ids are time-ordered).
    /// </summary>
    Task<IReadOnlyList<IFetchedJob>> FetchAsync(IReadOnlyList<string> queues, string workerId, TimeSpan leaseDuration, int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Operator action: runs a <see cref="JobState.Scheduled"/>, <see cref="JobState.Succeeded"/> or <see cref="JobState.Failed"/> job (again),
    /// now, with <see cref="StoredJob.Attempt"/> reset to 0. Returns false if the job does not exist or is <see cref="JobState.Processing"/>.
    /// </summary>
    Task<bool> RequeueAsync(string id, CancellationToken cancellationToken);

    /// <summary>Deletes a job in any state. Deleting a <see cref="JobState.Processing"/> job does not stop the running worker; its settle calls will return false.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Deletes jobs in <paramref name="state"/> that completed before <paramref name="completedBefore"/>. Returns how many were deleted.</summary>
    Task<int> PurgeAsync(JobState state, DateTimeOffset completedBefore, CancellationToken cancellationToken);

    // ---------------------------------------------------------------- Monitoring

    Task<JobStats> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>Lists jobs newest first, optionally filtered by state and queue.</summary>
    Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken);

    // ---------------------------------------------------------------- Recurring jobs

    Task UpsertRecurringAsync(RecurringJob job, CancellationToken cancellationToken);

    Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken);

    Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken);

    /// <summary>Recurring jobs whose <see cref="RecurringJob.NextRunAt"/> ≤ now, soonest first.</summary>
    Task<IReadOnlyList<RecurringJob>> GetDueRecurringAsync(int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic compare-and-set trigger. If the recurring job still has <see cref="RecurringJob.NextRunAt"/> == <paramref name="expectedNextRunAt"/>:
    /// set it to <paramref name="nextRunAt"/>, record <paramref name="job"/>'s id and creation time as the last run, and create <paramref name="job"/>,
    /// all in one atomic step. Returns false (and creates nothing) if another scheduler already triggered this occurrence.
    /// This replaces distributed locks: every scheduler instance may race, exactly one wins.
    /// </summary>
    Task<bool> TriggerRecurringAsync(string id, DateTimeOffset expectedNextRunAt, DateTimeOffset? nextRunAt, StoredJob job, CancellationToken cancellationToken);

    // ---------------------------------------------------------------- Wake-ups

    /// <summary>
    /// Completes when a job may have become runnable, or after <paramref name="timeout"/>. The default implementation simply waits
    /// for the timeout; providers override it with push notifications (Postgres LISTEN/NOTIFY, Redis pub/sub) so workers pick up
    /// new jobs within milliseconds without hot polling.
    /// </summary>
    Task WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.Delay(timeout, cancellationToken);
}
