using System.Reflection;
using System.Text.Json;
using Slowcooker.Internal;

namespace Slowcooker.Execution;

/// <summary>Turns job records into stored <see cref="Job"/> envelopes and back.</summary>
public sealed class JobSerializer(JobTypeRegistry registry)
{
    public Job Serialize(IJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var type = job.GetType();
        return new Job(registry.GetName(type), JsonSerializer.Serialize(job, type, SlowcookerJson.Options));
    }

    public IJob Deserialize(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var type = registry.Resolve(job.Name);
        try
        {
            return JsonSerializer.Deserialize(job.Payload, type, SlowcookerJson.Options) as IJob
                ?? throw new JobExecutionException($"Payload of job '{job.Name}' deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new JobExecutionException($"Payload of job '{job.Name}' could not be deserialized as {type}. Did the record's shape change since the job was created?", ex);
        }
    }

    public string DefaultQueue(Type jobType)
        => jobType.GetCustomAttribute<QueueAttribute>()?.Queue ?? JobOptions.DefaultQueue;
}
