using Slowcooker.Storage;

namespace Slowcooker.Storage.InMemory;

/// <summary>
/// Process-local storage. Jobs live in a dictionary guarded by a single lock; nothing survives a restart.
/// Good for tests, development and single-process apps that can afford to lose queued work on crash.
/// </summary>
public sealed class InMemoryJobStorage(TimeProvider? timeProvider = null) : IJobStorage
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, StoredJob> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idempotencyKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecurringJob> _recurring = new(StringComparer.Ordinal);
    private TaskCompletionSource _signal = NewSignal();

    // ---------------------------------------------------------------- Jobs

    public Task<string> CreateAsync(StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            if (job.IdempotencyKey is { } key)
            {
                if (_idempotencyKeys.TryGetValue(key, out var existingId) && _jobs.ContainsKey(existingId))
                    return Task.FromResult(existingId);
                _idempotencyKeys[key] = job.Id;
            }

            _jobs[job.Id] = job with { State = JobState.Scheduled, LeaseOwner = null, LeaseExpiresAt = null, CompletedAt = null };
            Signal();
            return Task.FromResult(job.Id);
        }
    }

    public Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_jobs.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<IFetchedJob>> FetchAsync(IReadOnlyList<string> queues, string workerId, TimeSpan leaseDuration, int maxCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queues);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = _time.GetUtcNow();
        var queueSet = new HashSet<string>(queues, StringComparer.Ordinal);

        lock (_lock)
        {
            var eligible = _jobs.Values
                .Where(j => queueSet.Contains(j.Queue) && IsEligible(j, now))
                .OrderBy(j => j.RunAt)
                .ThenBy(j => j.Id, StringComparer.Ordinal)
                .Take(maxCount)
                .ToList();

            var fetched = new List<IFetchedJob>(eligible.Count);
            foreach (var job in eligible)
            {
                var leased = job with
                {
                    State = JobState.Processing,
                    LeaseOwner = workerId,
                    LeaseExpiresAt = now + leaseDuration,
                    Attempt = job.Attempt + 1,
                };
                _jobs[job.Id] = leased;
                fetched.Add(new FetchedJob(this, leased));
            }
            return Task.FromResult<IReadOnlyList<IFetchedJob>>(fetched);
        }
    }

    private static bool IsEligible(StoredJob job, DateTimeOffset now) => job.State switch
    {
        JobState.Scheduled => job.RunAt <= now,
        JobState.Processing => job.LeaseExpiresAt is { } expires && expires <= now,
        _ => false,
    };

    public Task<bool> RequeueAsync(string id, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State == JobState.Processing)
                return Task.FromResult(false);

            _jobs[id] = job with
            {
                State = JobState.Scheduled,
                RunAt = _time.GetUtcNow(),
                Attempt = 0,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                CompletedAt = null,
            };
            Signal();
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_jobs.Remove(id, out var job)) return Task.FromResult(false);
            if (job.IdempotencyKey is { } key) _idempotencyKeys.Remove(key);
            return Task.FromResult(true);
        }
    }

    public Task<int> PurgeAsync(JobState state, DateTimeOffset completedBefore, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var doomed = _jobs.Values.Where(j => j.State == state && j.CompletedAt is { } c && c < completedBefore).Select(j => j.Id).ToList();
            foreach (var id in doomed)
            {
                if (_jobs.Remove(id, out var job) && job.IdempotencyKey is { } key) _idempotencyKeys.Remove(key);
            }
            return Task.FromResult(doomed.Count);
        }
    }

    // ---------------------------------------------------------------- Settling (called by FetchedJob)

    private bool Settle(string id, string workerId, Func<StoredJob, StoredJob?> transition)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State != JobState.Processing || job.LeaseOwner != workerId)
                return false;

            var next = transition(job);
            if (next is null) return false;
            _jobs[id] = next;
            if (next.State == JobState.Scheduled) Signal();
            return true;
        }
    }

    private sealed class FetchedJob(InMemoryJobStorage storage, StoredJob record) : FetchedJobBase(record)
    {
        protected override Task<bool> CompleteCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(storage.Settle(Record.Id, Record.LeaseOwner!, j => j with
            {
                State = JobState.Succeeded,
                CompletedAt = storage._time.GetUtcNow(),
                LeaseOwner = null,
                LeaseExpiresAt = null,
            }));

        protected override Task<bool> FailCoreAsync(string error, CancellationToken cancellationToken)
            => Task.FromResult(storage.Settle(Record.Id, Record.LeaseOwner!, j => j with
            {
                State = JobState.Failed,
                LastError = error,
                CompletedAt = storage._time.GetUtcNow(),
                LeaseOwner = null,
                LeaseExpiresAt = null,
            }));

        protected override Task<bool> RetryCoreAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken)
            => Task.FromResult(storage.Settle(Record.Id, Record.LeaseOwner!, j => j with
            {
                State = JobState.Scheduled,
                RunAt = runAt,
                LastError = error,
                LeaseOwner = null,
                LeaseExpiresAt = null,
            }));

        protected override Task<bool> RequeueCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(storage.Settle(Record.Id, Record.LeaseOwner!, j => j with
            {
                State = JobState.Scheduled,
                Attempt = Math.Max(0, j.Attempt - 1),
                LeaseOwner = null,
                LeaseExpiresAt = null,
            }));

        protected override Task<bool> ExtendLeaseCoreAsync(TimeSpan leaseDuration, CancellationToken cancellationToken)
            => Task.FromResult(storage.Settle(Record.Id, Record.LeaseOwner!, j => j with
            {
                LeaseExpiresAt = storage._time.GetUtcNow() + leaseDuration,
            }));
    }

    // ---------------------------------------------------------------- Monitoring

    public Task<JobStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            long scheduled = 0, processing = 0, succeeded = 0, failed = 0;
            var queues = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
            foreach (var job in _jobs.Values)
            {
                var counts = queues.TryGetValue(job.Queue, out var c) ? c : queues[job.Queue] = new long[4];
                counts[(int)job.State]++;
                switch (job.State)
                {
                    case JobState.Scheduled: scheduled++; break;
                    case JobState.Processing: processing++; break;
                    case JobState.Succeeded: succeeded++; break;
                    case JobState.Failed: failed++; break;
                }
            }

            var queueStats = queues.Select(kv => new QueueStats(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3])).ToList();
            return Task.FromResult(new JobStats(scheduled, processing, succeeded, failed, _recurring.Count, queueStats));
        }
    }

    public Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalized();
        lock (_lock)
        {
            var filtered = _jobs.Values
                .Where(j => (query.State is null || j.State == query.State) && (query.Queue is null || j.Queue == query.Queue))
                .OrderByDescending(j => j.CreatedAt)
                .ThenByDescending(j => j.Id, StringComparer.Ordinal)
                .ToList();

            var items = filtered.Skip(query.Skip).Take(query.Take).ToList();
            return Task.FromResult(new JobPage(items, filtered.Count));
        }
    }

    // ---------------------------------------------------------------- Recurring jobs

    public Task UpsertRecurringAsync(RecurringJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock) _recurring[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken)
    {
        lock (_lock) return Task.FromResult(_recurring.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<RecurringJob>>(_recurring.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList());
    }

    public Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken)
    {
        lock (_lock) return Task.FromResult(_recurring.Remove(id));
    }

    public Task<IReadOnlyList<RecurringJob>> GetDueRecurringAsync(int maxCount, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var due = _recurring.Values
                .Where(r => r.NextRunAt is { } next && next <= now)
                .OrderBy(r => r.NextRunAt)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .Take(maxCount)
                .ToList();
            return Task.FromResult<IReadOnlyList<RecurringJob>>(due);
        }
    }

    public Task<bool> TriggerRecurringAsync(string id, DateTimeOffset expectedNextRunAt, DateTimeOffset? nextRunAt, StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            if (!_recurring.TryGetValue(id, out var recurring) || recurring.NextRunAt != expectedNextRunAt)
                return Task.FromResult(false);

            _recurring[id] = recurring with
            {
                NextRunAt = nextRunAt,
                LastRunAt = job.CreatedAt,
                LastJobId = job.Id,
                UpdatedAt = job.CreatedAt,
            };
            _jobs[job.Id] = job with { State = JobState.Scheduled, LeaseOwner = null, LeaseExpiresAt = null, CompletedAt = null };
            Signal();
            return Task.FromResult(true);
        }
    }

    // ---------------------------------------------------------------- Wake-ups

    public async Task WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task signal;
        lock (_lock) signal = _signal.Task;

        if (signal.IsCompleted) return;
        await Task.WhenAny(signal, Task.Delay(timeout, _time, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Wakes every waiter and arms a fresh signal for the next wait. Must be called under the lock.</summary>
    private void Signal()
    {
        _signal.TrySetResult();
        _signal = NewSignal();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
