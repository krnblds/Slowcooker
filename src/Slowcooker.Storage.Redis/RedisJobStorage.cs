using System.Globalization;
using StackExchange.Redis;

namespace Slowcooker.Storage.Redis;

/// <summary>
/// Redis storage. Every operation is a single Lua script, so it is atomic and takes one round-trip.
/// Workers subscribe to a pub/sub channel and wake up the moment a job is created.
/// See <see cref="RedisScripts"/> for the key layout.
/// </summary>
public sealed class RedisJobStorage : IJobStorage, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisStorageOptions _options;
    private readonly TimeProvider _time;
    private readonly string _prefix;
    private readonly Lock _signalLock = new();
    private readonly Lazy<Task> _subscription;
    private TaskCompletionSource _signal = NewSignal();

    public RedisJobStorage(IConnectionMultiplexer redis, RedisStorageOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
        _options = options ?? new RedisStorageOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _prefix = _options.KeyPrefix;
        _subscription = new Lazy<Task>(SubscribeAsync);
    }

    private IDatabase Db => _redis.GetDatabase(_options.Database);

    private Task<RedisResult> RunAsync(string script, params RedisValue[] args)
    {
        var values = new RedisValue[args.Length + 1];
        values[0] = _prefix;
        args.CopyTo(values, 1);
        return Db.ScriptEvaluateAsync(script, keys: null, values: values);
    }

    // ---------------------------------------------------------------- Jobs

    public async Task<string> CreateAsync(StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var result = await RunAsync(RedisScripts.Create, job.Id, job.Queue, job.Job.Name, job.Job.Payload, Ms(job.CreatedAt), Ms(job.RunAt), job.IdempotencyKey ?? "").ConfigureAwait(false);
        return (string)result!;
    }

    public async Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var entries = await Db.HashGetAllAsync(JobKey(id)).ConfigureAwait(false);
        return entries.Length == 0 ? null : ReadJob(entries.ToDictionary(e => (string)e.Name!, e => (string?)e.Value));
    }

    public async Task<IReadOnlyList<IFetchedJob>> FetchAsync(IReadOnlyList<string> queues, string workerId, TimeSpan leaseDuration, int maxCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queues);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (queues.Count == 0 || maxCount <= 0) return [];

        var now = _time.GetUtcNow();
        var args = new List<RedisValue> { workerId, Ms(now), Ms(now + leaseDuration), maxCount };
        args.AddRange(queues.Select(q => (RedisValue)q));

        var result = await RunAsync(RedisScripts.Fetch, args.ToArray()).ConfigureAwait(false);
        return ReadHashes(result).Select(IFetchedJob (h) => new FetchedJob(this, ReadJob(h))).ToList();
    }

    private sealed class FetchedJob(RedisJobStorage storage, StoredJob record) : FetchedJobBase(record)
    {
        private string Worker => Record.LeaseOwner!;

        protected override async Task<bool> CompleteCoreAsync(CancellationToken cancellationToken)
            => (int)await storage.RunAsync(RedisScripts.Complete, Record.Id, Worker, Ms(storage._time.GetUtcNow())).ConfigureAwait(false) == 1;

        protected override async Task<bool> FailCoreAsync(string error, CancellationToken cancellationToken)
            => (int)await storage.RunAsync(RedisScripts.Fail, Record.Id, Worker, Ms(storage._time.GetUtcNow()), error).ConfigureAwait(false) == 1;

        protected override async Task<bool> RetryCoreAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken)
            => (int)await storage.RunAsync(RedisScripts.Retry, Record.Id, Worker, Ms(runAt), error).ConfigureAwait(false) == 1;

        protected override async Task<bool> RequeueCoreAsync(CancellationToken cancellationToken)
            => (int)await storage.RunAsync(RedisScripts.Requeue, Record.Id, Worker).ConfigureAwait(false) == 1;

        protected override async Task<bool> ExtendLeaseCoreAsync(TimeSpan leaseDuration, CancellationToken cancellationToken)
            => (int)await storage.RunAsync(RedisScripts.ExtendLease, Record.Id, Worker, Ms(storage._time.GetUtcNow() + leaseDuration)).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RequeueAsync(string id, CancellationToken cancellationToken)
        => (int)await RunAsync(RedisScripts.OperatorRequeue, id, Ms(_time.GetUtcNow())).ConfigureAwait(false) == 1;

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        => (int)await RunAsync(RedisScripts.Delete, id).ConfigureAwait(false) == 1;

    public async Task<int> PurgeAsync(JobState state, DateTimeOffset completedBefore, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = (int)await RunAsync(RedisScripts.Purge, (int)state, Ms(completedBefore), 1000).ConfigureAwait(false);
            total += n;
            if (n < 1000) return total;
        }
    }

    // ---------------------------------------------------------------- Monitoring

    public async Task<JobStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var db = Db;
        var statsTask = db.HashGetAllAsync($"{_prefix}:stats");
        var recurringTask = db.SetLengthAsync($"{_prefix}:recurring:ids");
        var stats = await statsTask.ConfigureAwait(false);
        var recurring = await recurringTask.ConfigureAwait(false);

        var queues = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        var totals = new long[4];
        foreach (var entry in stats)
        {
            var field = (string)entry.Name!;
            var bar = field.LastIndexOf('|');
            if (bar < 0 || !int.TryParse(field.AsSpan(bar + 1), out var state) || state is < 0 or > 3) continue;
            var count = (long)entry.Value;
            if (count <= 0) continue;
            var queue = field[..bar];
            var counts = queues.TryGetValue(queue, out var c) ? c : queues[queue] = new long[4];
            counts[state] += count;
            totals[state] += count;
        }

        return new JobStats(totals[0], totals[1], totals[2], totals[3], recurring,
            queues.Select(kv => new QueueStats(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3])).ToList());
    }

    public async Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalized();
        var result = await RunAsync(RedisScripts.Query,
            query.State is { } s ? ((int)s).ToString(CultureInfo.InvariantCulture) : "",
            query.Queue ?? "",
            query.Name ?? "",
            query.Skip, query.Take, Guid.NewGuid().ToString("N")).ConfigureAwait(false);

        var parts = (RedisResult[])result!;
        var total = (long)parts[0];
        var items = ReadHashes(parts[1]).Select(ReadJob).ToList();
        return new JobPage(items, total);
    }

    // ---------------------------------------------------------------- Recurring jobs

    public async Task UpsertRecurringAsync(RecurringJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await RunAsync(RedisScripts.UpsertRecurring, job.Id, job.Job.Name, job.Job.Payload, job.Cron, job.TimeZoneId, job.Queue,
            MsOrEmpty(job.NextRunAt), MsOrEmpty(job.LastRunAt), job.LastJobId ?? "", Ms(job.CreatedAt), Ms(job.UpdatedAt)).ConfigureAwait(false);
    }

    public async Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken)
    {
        var entries = await Db.HashGetAllAsync($"{_prefix}:recurring:{id}").ConfigureAwait(false);
        return entries.Length == 0 ? null : ReadRecurring(entries.ToDictionary(e => (string)e.Name!, e => (string?)e.Value));
    }

    public async Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken)
    {
        var db = Db;
        var ids = (await db.SetMembersAsync($"{_prefix}:recurring:ids").ConfigureAwait(false)).Select(v => (string)v!).Order(StringComparer.Ordinal).ToList();
        var tasks = ids.Select(id => db.HashGetAllAsync($"{_prefix}:recurring:{id}")).ToList();
        var list = new List<RecurringJob>(ids.Count);
        foreach (var task in tasks)
        {
            var entries = await task.ConfigureAwait(false);
            if (entries.Length > 0) list.Add(ReadRecurring(entries.ToDictionary(e => (string)e.Name!, e => (string?)e.Value)));
        }
        return list;
    }

    public async Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken)
        => (int)await RunAsync(RedisScripts.DeleteRecurring, id).ConfigureAwait(false) == 1;

    public async Task<IReadOnlyList<RecurringJob>> GetDueRecurringAsync(int maxCount, CancellationToken cancellationToken)
    {
        var result = await RunAsync(RedisScripts.DueRecurring, Ms(_time.GetUtcNow()), maxCount).ConfigureAwait(false);
        return ReadHashes(result).Select(ReadRecurring).ToList();
    }

    public async Task<bool> TriggerRecurringAsync(string id, DateTimeOffset expectedNextRunAt, DateTimeOffset? nextRunAt, StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var result = await RunAsync(RedisScripts.TriggerRecurring, id, Ms(expectedNextRunAt), MsOrEmpty(nextRunAt),
            job.Id, job.Queue, job.Job.Name, job.Job.Payload, Ms(job.CreatedAt), Ms(job.RunAt)).ConfigureAwait(false);
        return (int)result == 1;
    }

    // ---------------------------------------------------------------- Wake-ups

    public async Task WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _subscription.Value.ConfigureAwait(false);

        Task signal;
        lock (_signalLock) signal = _signal.Task;
        if (signal.IsCompleted) return;

        await Task.WhenAny(signal, Task.Delay(timeout, _time, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task SubscribeAsync()
    {
        var channel = RedisChannel.Literal($"{_prefix}:signal");
        await _redis.GetSubscriber().SubscribeAsync(channel, (_, _) =>
        {
            lock (_signalLock)
            {
                _signal.TrySetResult();
                _signal = NewSignal();
            }
        }).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription.IsValueCreated)
        {
            try { await _redis.GetSubscriber().UnsubscribeAsync(RedisChannel.Literal($"{_prefix}:signal")).ConfigureAwait(false); }
            catch { /* connection may already be gone */ }
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---------------------------------------------------------------- Mapping

    private string JobKey(string id) => $"{_prefix}:job:{id}";

    private static RedisValue Ms(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    private static RedisValue MsOrEmpty(DateTimeOffset? value) => value is { } v ? v.ToUnixTimeMilliseconds() : "";
    private static DateTimeOffset FromMs(string value) => DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(value, CultureInfo.InvariantCulture));
    private static DateTimeOffset? FromMsOrNull(string? value) => string.IsNullOrEmpty(value) ? null : FromMs(value);
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static IEnumerable<Dictionary<string, string?>> ReadHashes(RedisResult result)
    {
        if (result.IsNull) yield break;
        foreach (var hash in (RedisResult[])result!)
        {
            var flat = (RedisResult[])hash!;
            var dict = new Dictionary<string, string?>(flat.Length / 2, StringComparer.Ordinal);
            for (var i = 0; i + 1 < flat.Length; i += 2)
                dict[(string)flat[i]!] = (string?)flat[i + 1];
            if (dict.Count > 0) yield return dict;
        }
    }

    private static StoredJob ReadJob(Dictionary<string, string?> h) => new()
    {
        Id = h["id"]!,
        Queue = h["queue"]!,
        State = (JobState)int.Parse(h["state"]!, CultureInfo.InvariantCulture),
        Job = new Job(h["name"]!, h["payload"]!),
        CreatedAt = FromMs(h["created_at"]!),
        RunAt = FromMs(h["run_at"]!),
        Attempt = int.Parse(h["attempt"]!, CultureInfo.InvariantCulture),
        LastError = NullIfEmpty(h.GetValueOrDefault("last_error")),
        LeaseOwner = NullIfEmpty(h.GetValueOrDefault("lease_owner")),
        LeaseExpiresAt = FromMsOrNull(h.GetValueOrDefault("lease_expires_at")),
        CompletedAt = FromMsOrNull(h.GetValueOrDefault("completed_at")),
        IdempotencyKey = NullIfEmpty(h.GetValueOrDefault("idempotency_key")),
    };

    private static RecurringJob ReadRecurring(Dictionary<string, string?> h) => new()
    {
        Id = h["id"]!,
        Job = new Job(h["name"]!, h["payload"]!),
        Cron = h["cron"]!,
        TimeZoneId = h["time_zone_id"]!,
        Queue = h["queue"]!,
        NextRunAt = FromMsOrNull(h.GetValueOrDefault("next_run_at")),
        LastRunAt = FromMsOrNull(h.GetValueOrDefault("last_run_at")),
        LastJobId = NullIfEmpty(h.GetValueOrDefault("last_job_id")),
        CreatedAt = FromMs(h["created_at"]!),
        UpdatedAt = FromMs(h["updated_at"]!),
    };
}
