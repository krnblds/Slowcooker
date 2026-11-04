using Slowcooker.Storage;

namespace Slowcooker;

/// <summary>
/// Read model and operator actions for dashboards. <c>Slowcooker.AspNetCore</c> exposes this over HTTP;
/// use it directly for custom tooling.
/// </summary>
public interface IJobMonitor
{
    Task<JobStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken = default);

    Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Runs a scheduled, succeeded or failed job again, now, with its attempt counter reset. Not allowed while processing.</summary>
    Task<bool> RequeueAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken = default);

    Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates a job from the recurring template now, without touching its schedule.</summary>
    Task<string?> TriggerRecurringAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken = default);
}
