using Slowcooker.Storage;

namespace Slowcooker.Monitoring;

internal sealed class JobMonitor(IJobStorage storage, IRecurringJobClient recurringJobs) : IJobMonitor
{
    public Task<JobStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => storage.GetStatsAsync(cancellationToken);

    public Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
        => storage.QueryAsync((query ?? new JobQuery()).Normalized(), cancellationToken);

    public Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken = default)
        => storage.GetAsync(id, cancellationToken);

    public Task<bool> RequeueAsync(string id, CancellationToken cancellationToken = default)
        => storage.RequeueAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => storage.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken = default)
        => storage.ListRecurringAsync(cancellationToken);

    public Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken = default)
        => storage.GetRecurringAsync(id, cancellationToken);

    public Task<string?> TriggerRecurringAsync(string id, CancellationToken cancellationToken = default)
        => recurringJobs.TriggerAsync(id, cancellationToken);

    public Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken = default)
        => storage.DeleteRecurringAsync(id, cancellationToken);
}
