using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slowcooker.Execution;
using Slowcooker.Server;
using Slowcooker.Storage;

namespace Slowcooker;

/// <summary>Fluent configuration returned by <see cref="ServiceCollectionExtensions.AddSlowcooker"/>.</summary>
public sealed class SlowcookerBuilder
{
    internal SlowcookerBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a handler (scoped) for every <see cref="IJobHandler{TJob}"/> it implements, and registers those job types
    /// by name so workers can resolve them.
    /// </summary>
    public SlowcookerBuilder AddHandler<THandler>() where THandler : class
        => AddHandler(typeof(THandler));

    public SlowcookerBuilder AddHandler(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var jobTypes = HandledJobTypes(handlerType).ToList();
        if (jobTypes.Count == 0)
            throw new ArgumentException($"{handlerType} does not implement IJobHandler<TJob>.", nameof(handlerType));

        Services.TryAddScoped(handlerType);
        foreach (var jobType in jobTypes)
        {
            var serviceType = typeof(IJobHandler<>).MakeGenericType(jobType);
            Services.Replace(ServiceDescriptor.Scoped(serviceType, sp => sp.GetRequiredService(handlerType)));
            Services.AddSingleton(new JobTypeRegistration(jobType));
        }
        return this;
    }

    /// <summary>Registers every concrete <see cref="IJobHandler{TJob}"/> in the given assemblies.</summary>
    public SlowcookerBuilder AddHandlersFrom(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } && HandledJobTypes(type).Any())
                    AddHandler(type);
            }
        }
        return this;
    }

    /// <summary>Registers a job type by name without a handler, for processes that only enqueue and want name validation.</summary>
    public SlowcookerBuilder AddJob<TJob>() where TJob : IJob
    {
        Services.AddSingleton(new JobTypeRegistration(typeof(TJob)));
        return this;
    }

    /// <summary>Registers a storage provider. Storage packages expose friendlier overloads such as <c>UseInMemoryStorage()</c>.</summary>
    public SlowcookerBuilder UseStorage<TStorage>() where TStorage : class, IJobStorage
    {
        Services.Replace(ServiceDescriptor.Singleton<IJobStorage, TStorage>());
        return this;
    }

    /// <summary>Registers a storage provider instance or factory.</summary>
    public SlowcookerBuilder UseStorage(Func<IServiceProvider, IJobStorage> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.Replace(ServiceDescriptor.Singleton(factory));
        return this;
    }

    /// <summary>Replaces the default <see cref="ExponentialBackoffRetryPolicy"/>.</summary>
    public SlowcookerBuilder UseRetryPolicy(IRetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Services.Replace(ServiceDescriptor.Singleton(policy));
        return this;
    }

    /// <summary>
    /// Runs a worker in this process: fetches and executes jobs and (by default) runs the recurring job scheduler and retention.
    /// Without this call the process can only create jobs.
    /// </summary>
    public SlowcookerBuilder AddWorker(Action<WorkerOptions>? configure = null)
    {
        if (configure is not null) Services.Configure(configure);
        Services.AddHostedService<JobWorker>();
        return this;
    }

    private static IEnumerable<Type> HandledJobTypes(Type handlerType)
        => handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobHandler<>))
            .Select(i => i.GetGenericArguments()[0]);
}
