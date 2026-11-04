using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Slowcooker.Storage.Postgres;

/// <summary>
/// PostgreSQL storage. One row per job, one per recurring job; no state-history or counter tables.
///
/// <list type="bullet">
///   <item>Fetching is a single <c>UPDATE ... FROM (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING</c>, so concurrent workers never block each other
///   and never receive the same row.</item>
///   <item>Every settle statement includes <c>lease_owner = @worker</c> in its WHERE clause; a lost lease simply updates zero rows.</item>
///   <item>New jobs raise a <c>NOTIFY</c>; workers <c>LISTEN</c> and wake up immediately instead of polling.</item>
///   <item>If you create jobs inside a <see cref="System.Transactions.TransactionScope"/>, Npgsql enlists the insert, so the job becomes
///   visible only when your transaction commits. That avoids the classic "job ran before the data was committed" bug.</item>
/// </list>
/// </summary>
public sealed class PostgresJobStorage : IJobStorage, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresStorageOptions _options;
    private readonly TimeProvider _time;
    private readonly string _jobs;
    private readonly string _recurring;
    private readonly string _channel;
    private readonly Lazy<Task> _schema;
    private readonly SemaphoreSlim _listenerLock = new(1, 1);
    private NpgsqlConnection? _listener;
    private int _pendingNotifications;

    public PostgresJobStorage(NpgsqlDataSource dataSource, PostgresStorageOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        _options = options ?? new PostgresStorageOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _jobs = $"\"{_options.Schema}\".jobs";
        _recurring = $"\"{_options.Schema}\".recurring_jobs";
        _channel = $"slowcooker_{_options.Schema}";
        _schema = new Lazy<Task>(() => _options.AutoCreateSchema ? EnsureSchemaAsync(CancellationToken.None) : Task.CompletedTask);
    }

    /// <summary>The DDL this provider needs, for teams that apply migrations themselves. Idempotent.</summary>
    public string SchemaSql => $"""
        CREATE SCHEMA IF NOT EXISTS "{_options.Schema}";

        CREATE TABLE IF NOT EXISTS {_jobs} (
            id               text PRIMARY KEY,
            queue            text NOT NULL,
            state            smallint NOT NULL,
            job_name         text NOT NULL,
            payload          text NOT NULL,
            created_at       timestamptz NOT NULL,
            run_at           timestamptz NOT NULL,
            attempt          integer NOT NULL DEFAULT 0,
            last_error       text NULL,
            lease_owner      text NULL,
            lease_expires_at timestamptz NULL,
            completed_at     timestamptz NULL,
            idempotency_key  text NULL
        );

        CREATE INDEX IF NOT EXISTS jobs_fetch_idx ON {_jobs} (queue, run_at, id) WHERE state = 0;
        CREATE INDEX IF NOT EXISTS jobs_lease_idx ON {_jobs} (lease_expires_at) WHERE state = 1;
        CREATE INDEX IF NOT EXISTS jobs_purge_idx ON {_jobs} (state, completed_at);
        CREATE INDEX IF NOT EXISTS jobs_list_idx ON {_jobs} (created_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS jobs_queue_list_idx ON {_jobs} (queue, state, created_at DESC);
        CREATE INDEX IF NOT EXISTS jobs_name_list_idx ON {_jobs} (job_name, created_at DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS jobs_idempotency_idx ON {_jobs} (idempotency_key) WHERE idempotency_key IS NOT NULL;

        CREATE TABLE IF NOT EXISTS {_recurring} (
            id               text PRIMARY KEY,
            job_name         text NOT NULL,
            payload          text NOT NULL,
            cron             text NOT NULL,
            time_zone_id     text NOT NULL,
            queue            text NOT NULL,
            next_run_at      timestamptz NULL,
            last_run_at      timestamptz NULL,
            last_job_id      text NULL,
            created_at       timestamptz NOT NULL,
            updated_at       timestamptz NOT NULL
        );

        CREATE INDEX IF NOT EXISTS recurring_due_idx ON {_recurring} (next_run_at) WHERE next_run_at IS NOT NULL;
        """;

    /// <summary>Creates the schema and tables if they do not exist. Safe to call from several instances at once.</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // Serialise concurrent CREATE IF NOT EXISTS from several starting instances (they can otherwise collide on catalog unique keys).
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@key))", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("key", $"slowcooker:{_options.Schema}");
            await lockCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var ddl = new NpgsqlCommand(SchemaSql, conn, tx))
            await ddl.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task ReadyAsync() => _schema.Value;

    private const string JobColumns = "id, queue, state, job_name, payload, created_at, run_at, attempt, last_error, lease_owner, lease_expires_at, completed_at, idempotency_key";
    private const string RecurringColumns = "id, job_name, payload, cron, time_zone_id, queue, next_run_at, last_run_at, last_job_id, created_at, updated_at";

    // ---------------------------------------------------------------- Jobs

    public async Task<string> CreateAsync(StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await ReadyAsync().ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(conn);
        var insert = InsertJobCommand(job, $"ON CONFLICT (idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING RETURNING id");
        batch.BatchCommands.Add(insert);
        batch.BatchCommands.Add(NotifyCommand());

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return reader.GetString(0);
        await reader.DisposeAsync().ConfigureAwait(false);

        // Idempotency key already taken: return the existing job's id.
        await using var lookup = new NpgsqlCommand($"SELECT id FROM {_jobs} WHERE idempotency_key = @key", conn);
        lookup.Parameters.AddWithValue("key", job.IdempotencyKey ?? throw new InvalidOperationException("Insert returned no row without an idempotency key."));
        var existing = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return existing as string ?? throw new InvalidOperationException($"Job with idempotency key '{job.IdempotencyKey}' vanished during insert.");
    }

    private NpgsqlBatchCommand InsertJobCommand(StoredJob job, string suffix)
    {
        var cmd = new NpgsqlBatchCommand($"""
            INSERT INTO {_jobs} ({JobColumns})
            VALUES (@id, @queue, 0, @name, @payload, @created, @run_at, 0, NULL, NULL, NULL, NULL, @idem)
            {suffix}
            """);
        cmd.Parameters.AddWithValue("id", job.Id);
        cmd.Parameters.AddWithValue("queue", job.Queue);
        cmd.Parameters.AddWithValue("name", job.Job.Name);
        cmd.Parameters.AddWithValue("payload", job.Job.Payload);
        cmd.Parameters.AddWithValue("created", Utc(job.CreatedAt));
        cmd.Parameters.AddWithValue("run_at", Utc(job.RunAt));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("idem", NpgsqlDbType.Text) { TypedValue = job.IdempotencyKey });
        return cmd;
    }

    private NpgsqlBatchCommand NotifyCommand()
    {
        var cmd = new NpgsqlBatchCommand("SELECT pg_notify(@channel, '')");
        cmd.Parameters.AddWithValue("channel", _channel);
        return cmd;
    }

    public async Task<StoredJob?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"SELECT {JobColumns} FROM {_jobs} WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task<IReadOnlyList<IFetchedJob>> FetchAsync(IReadOnlyList<string> queues, string workerId, TimeSpan leaseDuration, int maxCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queues);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        await ReadyAsync().ConfigureAwait(false);
        var now = _time.GetUtcNow();

        await using var cmd = _dataSource.CreateCommand($"""
            WITH candidates AS (
                SELECT id FROM {_jobs}
                WHERE queue = ANY(@queues)
                  AND ((state = 0 AND run_at <= @now) OR (state = 1 AND lease_expires_at <= @now))
                ORDER BY run_at, id
                LIMIT @max
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {_jobs} j
            SET state = 1, lease_owner = @worker, lease_expires_at = @until, attempt = j.attempt + 1
            FROM candidates c
            WHERE j.id = c.id
            RETURNING {JobColumnsOf("j")}
            """);
        cmd.Parameters.AddWithValue("queues", queues.ToArray());
        cmd.Parameters.AddWithValue("now", Utc(now));
        cmd.Parameters.AddWithValue("max", maxCount);
        cmd.Parameters.AddWithValue("worker", workerId);
        cmd.Parameters.AddWithValue("until", Utc(now + leaseDuration));

        var fetched = new List<StoredJob>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                fetched.Add(ReadJob(reader));
        }

        // RETURNING does not guarantee order.
        fetched.Sort((a, b) => a.RunAt != b.RunAt ? a.RunAt.CompareTo(b.RunAt) : string.CompareOrdinal(a.Id, b.Id));
        return fetched.Select(IFetchedJob (j) => new FetchedJob(this, j)).ToList();
    }

    private static string JobColumnsOf(string alias) => string.Join(", ", JobColumns.Split(", ").Select(c => $"{alias}.{c}"));

    /// <summary>Runs an UPDATE guarded by lease ownership. Returns true when exactly one row changed.</summary>
    private async Task<bool> SettleAsync(string id, string workerId, string setClause, Action<NpgsqlParameterCollection> addParameters, bool notify, CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(conn);
        var update = new NpgsqlBatchCommand($"UPDATE {_jobs} SET {setClause} WHERE id = @id AND state = 1 AND lease_owner = @worker");
        update.Parameters.AddWithValue("id", id);
        update.Parameters.AddWithValue("worker", workerId);
        addParameters(update.Parameters);
        batch.BatchCommands.Add(update);
        if (notify) batch.BatchCommands.Add(NotifyCommand());

        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return update.RecordsAffected == 1;
    }

    private sealed class FetchedJob(PostgresJobStorage storage, StoredJob record) : FetchedJobBase(record)
    {
        private string Worker => Record.LeaseOwner!;

        protected override Task<bool> CompleteCoreAsync(CancellationToken cancellationToken)
            => storage.SettleAsync(Record.Id, Worker, "state = 2, completed_at = @now, lease_owner = NULL, lease_expires_at = NULL",
                p => p.AddWithValue("now", Utc(storage._time.GetUtcNow())), notify: false, cancellationToken);

        protected override Task<bool> FailCoreAsync(string error, CancellationToken cancellationToken)
            => storage.SettleAsync(Record.Id, Worker, "state = 3, last_error = @error, completed_at = @now, lease_owner = NULL, lease_expires_at = NULL",
                p => { p.AddWithValue("error", error); p.AddWithValue("now", Utc(storage._time.GetUtcNow())); }, notify: false, cancellationToken);

        protected override Task<bool> RetryCoreAsync(DateTimeOffset runAt, string error, CancellationToken cancellationToken)
            => storage.SettleAsync(Record.Id, Worker, "state = 0, run_at = @run_at, last_error = @error, lease_owner = NULL, lease_expires_at = NULL",
                p => { p.AddWithValue("run_at", Utc(runAt)); p.AddWithValue("error", error); }, notify: true, cancellationToken);

        protected override Task<bool> RequeueCoreAsync(CancellationToken cancellationToken)
            => storage.SettleAsync(Record.Id, Worker, "state = 0, attempt = GREATEST(0, attempt - 1), lease_owner = NULL, lease_expires_at = NULL",
                _ => { }, notify: true, cancellationToken);

        protected override Task<bool> ExtendLeaseCoreAsync(TimeSpan leaseDuration, CancellationToken cancellationToken)
            => storage.SettleAsync(Record.Id, Worker, "lease_expires_at = @until",
                p => p.AddWithValue("until", Utc(storage._time.GetUtcNow() + leaseDuration)), notify: false, cancellationToken);
    }

    public async Task<bool> RequeueAsync(string id, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(conn);
        var update = new NpgsqlBatchCommand($"""
            UPDATE {_jobs}
            SET state = 0, run_at = @now, attempt = 0, lease_owner = NULL, lease_expires_at = NULL, completed_at = NULL
            WHERE id = @id AND state <> 1
            """);
        update.Parameters.AddWithValue("id", id);
        update.Parameters.AddWithValue("now", Utc(_time.GetUtcNow()));
        batch.BatchCommands.Add(update);
        batch.BatchCommands.Add(NotifyCommand());
        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return update.RecordsAffected == 1;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"DELETE FROM {_jobs} WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> PurgeAsync(JobState state, DateTimeOffset completedBefore, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"DELETE FROM {_jobs} WHERE state = @state AND completed_at < @before");
        cmd.Parameters.AddWithValue("state", (short)state);
        cmd.Parameters.AddWithValue("before", Utc(completedBefore));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Monitoring

    public async Task<JobStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(conn)
        {
            BatchCommands =
            {
                new NpgsqlBatchCommand($"SELECT queue, state, count(*) FROM {_jobs} GROUP BY queue, state ORDER BY queue"),
                new NpgsqlBatchCommand($"SELECT count(*) FROM {_recurring}"),
            },
        };

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var queues = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        var totals = new long[4];
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var queue = reader.GetString(0);
            var state = reader.GetInt16(1);
            var count = reader.GetInt64(2);
            if (state is < 0 or > 3) continue;
            var counts = queues.TryGetValue(queue, out var c) ? c : queues[queue] = new long[4];
            counts[state] += count;
            totals[state] += count;
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var recurring = reader.GetInt64(0);

        return new JobStats(totals[0], totals[1], totals[2], totals[3], recurring,
            queues.Select(kv => new QueueStats(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3])).ToList());
    }

    public async Task<JobPage> QueryAsync(JobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalized();
        await ReadyAsync().ConfigureAwait(false);

        var where = new List<string>();
        if (query.State is { } state) where.Add("state = @state");
        if (query.Queue is not null) where.Add("queue = @queue");
        if (query.Name is not null) where.Add("job_name = @name");
        var whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(conn);
        var count = new NpgsqlBatchCommand($"SELECT count(*) FROM {_jobs} {whereSql}");
        var page = new NpgsqlBatchCommand($"SELECT {JobColumns} FROM {_jobs} {whereSql} ORDER BY created_at DESC, id DESC OFFSET @skip LIMIT @take");
        foreach (var cmd in new[] { count, page })
        {
            if (query.State is { } s) cmd.Parameters.AddWithValue("state", (short)s);
            if (query.Queue is not null) cmd.Parameters.AddWithValue("queue", query.Queue);
            if (query.Name is not null) cmd.Parameters.AddWithValue("name", query.Name);
        }
        page.Parameters.AddWithValue("skip", query.Skip);
        page.Parameters.AddWithValue("take", query.Take);
        batch.BatchCommands.Add(count);
        batch.BatchCommands.Add(page);

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var total = reader.GetInt64(0);
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<StoredJob>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(ReadJob(reader));
        return new JobPage(items, total);
    }

    // ---------------------------------------------------------------- Recurring jobs

    public async Task UpsertRecurringAsync(RecurringJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"""
            INSERT INTO {_recurring} ({RecurringColumns})
            VALUES (@id, @name, @payload, @cron, @tz, @queue, @next, @last_run, @last_job, @created, @updated)
            ON CONFLICT (id) DO UPDATE SET
                job_name = EXCLUDED.job_name, payload = EXCLUDED.payload,
                cron = EXCLUDED.cron, time_zone_id = EXCLUDED.time_zone_id, queue = EXCLUDED.queue,
                next_run_at = EXCLUDED.next_run_at, last_run_at = EXCLUDED.last_run_at, last_job_id = EXCLUDED.last_job_id,
                created_at = EXCLUDED.created_at, updated_at = EXCLUDED.updated_at
            """);
        cmd.Parameters.AddWithValue("id", job.Id);
        cmd.Parameters.AddWithValue("name", job.Job.Name);
        cmd.Parameters.AddWithValue("payload", job.Job.Payload);
        cmd.Parameters.AddWithValue("cron", job.Cron);
        cmd.Parameters.AddWithValue("tz", job.TimeZoneId);
        cmd.Parameters.AddWithValue("queue", job.Queue);
        cmd.Parameters.Add(new NpgsqlParameter<DateTime?>("next", NpgsqlDbType.TimestampTz) { TypedValue = UtcOrNull(job.NextRunAt) });
        cmd.Parameters.Add(new NpgsqlParameter<DateTime?>("last_run", NpgsqlDbType.TimestampTz) { TypedValue = UtcOrNull(job.LastRunAt) });
        cmd.Parameters.Add(new NpgsqlParameter<string?>("last_job", NpgsqlDbType.Text) { TypedValue = job.LastJobId });
        cmd.Parameters.AddWithValue("created", Utc(job.CreatedAt));
        cmd.Parameters.AddWithValue("updated", Utc(job.UpdatedAt));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecurringJob?> GetRecurringAsync(string id, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"SELECT {RecurringColumns} FROM {_recurring} WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecurring(reader) : null;
    }

    public async Task<IReadOnlyList<RecurringJob>> ListRecurringAsync(CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"SELECT {RecurringColumns} FROM {_recurring} ORDER BY id");
        return await ReadAllRecurringAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteRecurringAsync(string id, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"DELETE FROM {_recurring} WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<RecurringJob>> GetDueRecurringAsync(int maxCount, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await using var cmd = _dataSource.CreateCommand($"SELECT {RecurringColumns} FROM {_recurring} WHERE next_run_at IS NOT NULL AND next_run_at <= @now ORDER BY next_run_at, id LIMIT @max");
        cmd.Parameters.AddWithValue("now", Utc(_time.GetUtcNow()));
        cmd.Parameters.AddWithValue("max", maxCount);
        return await ReadAllRecurringAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TriggerRecurringAsync(string id, DateTimeOffset expectedNextRunAt, DateTimeOffset? nextRunAt, StoredJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await ReadyAsync().ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        await using var cas = new NpgsqlCommand($"""
            UPDATE {_recurring}
            SET next_run_at = @next, last_run_at = @last_run, last_job_id = @job_id, updated_at = @last_run
            WHERE id = @id AND next_run_at = @expected
            """, conn, tx);
        cas.Parameters.AddWithValue("id", id);
        cas.Parameters.AddWithValue("expected", Utc(expectedNextRunAt));
        cas.Parameters.Add(new NpgsqlParameter<DateTime?>("next", NpgsqlDbType.TimestampTz) { TypedValue = UtcOrNull(nextRunAt) });
        cas.Parameters.AddWithValue("last_run", Utc(job.CreatedAt));
        cas.Parameters.AddWithValue("job_id", job.Id);

        if (await cas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using var batch = new NpgsqlBatch(conn, tx);
        batch.BatchCommands.Add(InsertJobCommand(job, ""));
        batch.BatchCommands.Add(NotifyCommand());
        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ---------------------------------------------------------------- Wake-ups

    public async Task WaitForJobAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);
        await _listenerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var listener = await GetListenerAsync(cancellationToken).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _pendingNotifications, 0) > 0)
                return; // a notification arrived since the last wait
            await listener.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _pendingNotifications, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Listener connection broke: fall back to a plain delay this round and reconnect next time.
            await DropListenerAsync().ConfigureAwait(false);
            await Task.Delay(timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _listenerLock.Release();
        }
    }

    private async Task<NpgsqlConnection> GetListenerAsync(CancellationToken cancellationToken)
    {
        if (_listener is { FullState: ConnectionState.Open } open) return open;
        await DropListenerAsync().ConfigureAwait(false);

        var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        conn.Notification += (_, _) => Interlocked.Increment(ref _pendingNotifications);
        await using (var listen = new NpgsqlCommand($"LISTEN \"{_channel}\"", conn))
            await listen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _listener = conn;
        return conn;
    }

    private async Task DropListenerAsync()
    {
        var old = Interlocked.Exchange(ref _listener, null);
        if (old is not null)
        {
            try { await old.DisposeAsync().ConfigureAwait(false); } catch { /* already broken */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DropListenerAsync().ConfigureAwait(false);
        _listenerLock.Dispose();
    }

    // ---------------------------------------------------------------- Mapping

    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;
    private static DateTime? UtcOrNull(DateTimeOffset? value) => value?.UtcDateTime;
    private static DateTimeOffset Dto(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero);

    private static StoredJob ReadJob(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Queue = r.GetString(1),
        State = (JobState)r.GetInt16(2),
        Job = new Job(r.GetString(3), r.GetString(4)),
        CreatedAt = Dto(r.GetDateTime(5)),
        RunAt = Dto(r.GetDateTime(6)),
        Attempt = r.GetInt32(7),
        LastError = r.IsDBNull(8) ? null : r.GetString(8),
        LeaseOwner = r.IsDBNull(9) ? null : r.GetString(9),
        LeaseExpiresAt = r.IsDBNull(10) ? null : Dto(r.GetDateTime(10)),
        CompletedAt = r.IsDBNull(11) ? null : Dto(r.GetDateTime(11)),
        IdempotencyKey = r.IsDBNull(12) ? null : r.GetString(12),
    };

    private static RecurringJob ReadRecurring(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Job = new Job(r.GetString(1), r.GetString(2)),
        Cron = r.GetString(3),
        TimeZoneId = r.GetString(4),
        Queue = r.GetString(5),
        NextRunAt = r.IsDBNull(6) ? null : Dto(r.GetDateTime(6)),
        LastRunAt = r.IsDBNull(7) ? null : Dto(r.GetDateTime(7)),
        LastJobId = r.IsDBNull(8) ? null : r.GetString(8),
        CreatedAt = Dto(r.GetDateTime(9)),
        UpdatedAt = Dto(r.GetDateTime(10)),
    };

    private static async Task<IReadOnlyList<RecurringJob>> ReadAllRecurringAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<RecurringJob>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(ReadRecurring(reader));
        return list;
    }
}
