using Slowcooker.Execution;
using Slowcooker.Internal;
using Slowcooker.Storage;

namespace Slowcooker;

internal sealed class JobClient(IJobStorage storage, JobSerializer serializer, TimeProvider timeProvider) : IJobClient
{
    public Task<string> EnqueueAsync<TJob>(TJob job, JobOptions? options = null, CancellationToken cancellationToken = default) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(job);
        options ??= JobOptions.Default;

        var envelope = serializer.Serialize(job);
        var effective = options with
        {
            Queue = options.Queue ?? serializer.DefaultQueue(job.GetType()),
            IdempotencyKey = options.IdempotencyKey ?? job.IdempotencyKey,
        };
        return CreateAsync(envelope, effective, cancellationToken);
    }

    public async Task<string> CreateAsync(Job job, JobOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);
        var queue = options.Queue ?? JobOptions.DefaultQueue;
        if (string.IsNullOrWhiteSpace(queue))
            throw new ArgumentException("Queue name must not be empty.", nameof(options));

        var now = timeProvider.GetUtcNow();
        var record = new StoredJob
        {
            Id = JobIds.New(),
            Job = job,
            Queue = queue,
            State = JobState.Scheduled,
            CreatedAt = now,
            RunAt = options.RunAt ?? (options.Delay is { } delay ? now + delay : now),
            IdempotencyKey = options.IdempotencyKey,
        };

        return await storage.CreateAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
