using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Slowcooker.Execution;

/// <summary>
/// Runs a stored job: deserializes the record, resolves its <see cref="IJobHandler{TJob}"/> from the job's DI scope and
/// invokes it. The handler call is compiled once per job type into a delegate, so there is no reflection on the hot path.
/// </summary>
internal sealed class JobExecutor(JobSerializer serializer)
{
    private readonly ConcurrentDictionary<Type, Invoker> _invokers = new();

    public async Task ExecuteAsync(StoredJob stored, IServiceProvider scope, IJobClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(scope);

        var job = serializer.Deserialize(stored.Job);
        var invoker = _invokers.GetOrAdd(job.GetType(), Compile);

        var handler = scope.GetService(invoker.HandlerType)
            ?? throw new JobExecutionException($"No handler is registered for job '{stored.Job.Name}'. Add one with AddSlowcooker().AddHandler<{job.GetType().Name}Handler>().");

        var context = new JobContext(stored, client, cancellationToken);
        await invoker.Invoke(handler, job, context).ConfigureAwait(false);
    }

    private static Invoker Compile(Type jobType)
    {
        var handlerType = typeof(IJobHandler<>).MakeGenericType(jobType);
        var method = handlerType.GetMethod(nameof(IJobHandler<IJob>.HandleAsync))!;

        var handler = Expression.Parameter(typeof(object), "handler");
        var job = Expression.Parameter(typeof(object), "job");
        var context = Expression.Parameter(typeof(JobContext), "context");
        var call = Expression.Call(Expression.Convert(handler, handlerType), method, Expression.Convert(job, jobType), context);
        var invoke = Expression.Lambda<Func<object, object, JobContext, Task>>(call, handler, job, context).Compile();

        return new Invoker(handlerType, invoke);
    }

    private sealed record Invoker(Type HandlerType, Func<object, object, JobContext, Task> Invoke);
}
