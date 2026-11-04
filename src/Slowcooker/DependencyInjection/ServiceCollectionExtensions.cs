using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slowcooker.Execution;
using Slowcooker.Monitoring;
using Slowcooker.Storage;

namespace Slowcooker;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the job client, recurring job client and monitor. Chain a storage provider
    /// (for example <c>.UseInMemoryStorage()</c>), your handlers (<c>.AddHandler&lt;T&gt;()</c>) and, in processes
    /// that should run jobs, <c>.AddWorker()</c>.
    /// </summary>
    public static SlowcookerBuilder AddSlowcooker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRetryPolicy>(new ExponentialBackoffRetryPolicy());
        services.TryAddSingleton<JobTypeRegistry>(sp => new JobTypeRegistry(sp.GetServices<JobTypeRegistration>()));
        services.TryAddSingleton<JobSerializer>();
        services.TryAddSingleton<JobExecutor>();
        services.TryAddSingleton<IJobClient, JobClient>();
        services.TryAddSingleton<IRecurringJobClient, RecurringJobClient>();
        services.TryAddSingleton<IJobMonitor, JobMonitor>();
        services.TryAddSingleton<IJobStorage>(_ => throw new InvalidOperationException(
            "No Slowcooker storage is configured. Call UseInMemoryStorage(), UsePostgres(...) or UseRedis(...) on AddSlowcooker()."));

        return new SlowcookerBuilder(services);
    }
}
