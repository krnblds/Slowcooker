# Storage

## The contract

`IJobStorage` (in `src/Slowcooker/Storage/IJobStorage.cs`) is the whole persistence surface. A provider implements:

| Member | Purpose |
|---|---|
| `CreateAsync` | Insert a `Scheduled` job, honouring the idempotency key |
| `GetAsync` | Read one job |
| `FetchAsync` | Atomically lease up to *n* due jobs (or jobs whose lease expired) for a worker; returns `IFetchedJob` handles |
| `IFetchedJob.CompleteAsync / FailAsync / RetryAsync / RequeueAsync / ExtendLeaseAsync` | Settle or renew a leased job; must throw `LeaseLostException` if the lease is no longer the caller's (`FetchedJobBase` does this for you) |
| `RequeueAsync`, `DeleteAsync`, `PurgeAsync` | Operator actions and retention |
| `GetStatsAsync`, `QueryAsync` | Dashboard reads |
| `UpsertRecurringAsync`, `GetRecurringAsync`, `ListRecurringAsync`, `DeleteRecurringAsync`, `GetDueRecurringAsync`, `TriggerRecurringAsync` | Recurring jobs; `TriggerRecurringAsync` is a compare-and-set on `NextRunAt` that also inserts the job, atomically |
| `WaitForJobAsync` | Optional push wake-up; the default just waits for the timeout |

Rules: fetch is the only way into `Processing`; settles are conditional on lease ownership; `Attempt` is incremented at fetch; "now" comes from the `TimeProvider` the provider was constructed with.

To write a provider, implement the interface, derive a test class from `JobStorageContractTests` (`tests/Slowcooker.Storage.ContractTests`) and supply `CreateStorageAsync`. The suite's 40 tests cover ordering, leases, lease loss, idempotency, paging, recurring CAS, wake-ups and concurrent fetching.

## Postgres

Package `Slowcooker.Storage.Postgres`, built on `NpgsqlDataSource`.

```csharp
.UsePostgres("Host=…;Database=…", o => { o.Schema = "slowcooker"; o.AutoCreateSchema = true; })
.UsePostgres()   // uses the NpgsqlDataSource registered in DI
```

**Schema.** Created on first use under an advisory lock (safe when many instances start at once). To manage it yourself, set `AutoCreateSchema = false` and apply `PostgresJobStorage.SchemaSql`:

```sql
CREATE SCHEMA IF NOT EXISTS "slowcooker";

CREATE TABLE IF NOT EXISTS "slowcooker".jobs (
    id               text PRIMARY KEY,
    queue            text NOT NULL,
    state            smallint NOT NULL,          -- 0 scheduled, 1 processing, 2 succeeded, 3 failed
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
CREATE INDEX IF NOT EXISTS jobs_fetch_idx       ON "slowcooker".jobs (queue, run_at, id) WHERE state = 0;
CREATE INDEX IF NOT EXISTS jobs_lease_idx       ON "slowcooker".jobs (lease_expires_at) WHERE state = 1;
CREATE INDEX IF NOT EXISTS jobs_purge_idx       ON "slowcooker".jobs (state, completed_at);
CREATE INDEX IF NOT EXISTS jobs_list_idx        ON "slowcooker".jobs (created_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS jobs_queue_list_idx  ON "slowcooker".jobs (queue, state, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS jobs_idempotency_idx ON "slowcooker".jobs (idempotency_key) WHERE idempotency_key IS NOT NULL;

CREATE TABLE IF NOT EXISTS "slowcooker".recurring_jobs (
    id            text PRIMARY KEY,
    job_name      text NOT NULL,
    payload       text NOT NULL,
    cron          text NOT NULL,
    time_zone_id  text NOT NULL,
    queue         text NOT NULL,
    next_run_at   timestamptz NULL,
    last_run_at   timestamptz NULL,
    last_job_id   text NULL,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS recurring_due_idx ON "slowcooker".recurring_jobs (next_run_at) WHERE next_run_at IS NOT NULL;
```

**Fetch.**

```sql
WITH candidates AS (
    SELECT id FROM "slowcooker".jobs
    WHERE queue = ANY($1)
      AND ((state = 0 AND run_at <= $2) OR (state = 1 AND lease_expires_at <= $2))
    ORDER BY run_at, id
    LIMIT $3
    FOR UPDATE SKIP LOCKED
)
UPDATE "slowcooker".jobs j
SET state = 1, lease_owner = $4, lease_expires_at = $5, attempt = j.attempt + 1
FROM candidates c WHERE j.id = c.id
RETURNING j.*;
```

**Settles** are `UPDATE … WHERE id = $1 AND state = 1 AND lease_owner = $2`; zero rows affected means the lease was lost.

**Wake-ups.** Inserts, retries and requeues run `pg_notify('slowcooker_<schema>', '')`. The worker holds one `LISTEN` connection per storage instance and uses `NpgsqlConnection.WaitAsync(timeout)`. If that connection breaks the worker falls back to a one-second delay and reconnects.

**Transactions.** Npgsql enlists in an ambient `System.Transactions.TransactionScope`. Creating a job inside one makes it visible only when the scope commits.

**Sizing.** Everything the worker does is index-backed point operations. Retention keeps the table bounded; with the defaults it holds one day of succeeded jobs and a week of failures.

## Redis

Package `Slowcooker.Storage.Redis`, built on StackExchange.Redis.

```csharp
.UseRedis("localhost:6379", o => { o.KeyPrefix = "slowcooker"; o.Database = -1; })
.UseRedis()   // uses the IConnectionMultiplexer registered in DI
```

Every operation is a single Lua script, so it is atomic and one round-trip. Key layout (`p` = prefix):

| Key | Type | Holds |
|---|---|---|
| `p:job:{id}` | hash | the job |
| `p:queue:{queue}` | zset | scheduled jobs, score = `run_at` ms |
| `p:processing` | zset | leased jobs, score = `lease_expires_at` ms |
| `p:idx:all`, `p:idx:state:{n}`, `p:idx:queue:{q}` | zset | listing indexes, score = `created_at` ms |
| `p:idx:completed:{n}` | zset | succeeded / failed jobs by `completed_at`, for retention |
| `p:stats` | hash | `"{queue}|{state}"` → count |
| `p:idem:{key}` | string | owner of an idempotency key |
| `p:recurring:{id}`, `p:recurring:ids`, `p:recurring:due` | hash, set, zset | recurring jobs |
| `p:signal` | channel | wake-ups |

Ids are time-ordered strings, so a zset score tie (same `run_at`) still yields FIFO. Timestamps are stored as Unix milliseconds. On Redis Cluster use a hash-tag prefix such as `{slowcooker}` so all keys share a slot; the scripts touch several keys per call.

## In-memory

Package `Slowcooker.Storage.InMemory`. A dictionary under a lock, with a `TaskCompletionSource` for wake-ups. Fast, deterministic with a `FakeTimeProvider`, and the right choice for unit tests of your handlers:

```csharp
services.AddSingleton<TimeProvider>(fakeTime);
services.AddSlowcooker().UseInMemoryStorage().AddHandler<MyHandler>().AddWorker();
```
