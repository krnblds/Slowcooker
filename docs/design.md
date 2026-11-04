# Design notes

Background job processing is easy on the happy path and hard everywhere else. This document goes through the failure modes one by one, says how Slowcooker handles each, and names the test that proves it. It closes with what was taken from Hangfire and what was deliberately done differently.

## The failure modes

### Two workers run the same job

The only way a job enters `Processing` is `IJobStorage.FetchAsync`, and every provider makes that atomic:

* Postgres: one statement, `UPDATE … FROM (SELECT … FOR UPDATE SKIP LOCKED) RETURNING`. Concurrent workers skip each other's rows instead of blocking.
* Redis: one Lua script. Redis runs scripts serially.
* In-memory: a lock.

Tests: `Concurrent_fetchers_never_receive_the_same_job` (8 workers, 60 jobs, every id seen exactly once, run against all three providers), `A_leased_job_is_not_fetched_again_while_the_lease_holds`.

### A worker dies mid-job

A fetched job carries a lease (`LeaseExpiresAt`). The worker extends it every third of `LeaseDuration` while the job runs. If the process disappears the lease expires and the next fetch takes the job over with `Attempt + 1`.

The subtle part is the *slow* worker rather than the dead one: a worker that stalled for six minutes and then wakes up must not overwrite what the new owner did. Every settle operation (`Complete`, `Fail`, `Retry`, `Requeue`, `ExtendLease`) is conditional on `lease_owner = me` and throws `LeaseLostException` otherwise, and the worker cancels the job's token as soon as a heartbeat reports the lease lost.

Tests: `A_job_whose_lease_expired_is_fetched_again_with_attempt_incremented`, `A_worker_whose_lease_was_lost_cannot_touch_the_job`, `Each_settle_kind_throws_LeaseLost_for_a_lost_lease`, `Heartbeats_keep_long_jobs_leased_beyond_the_lease_duration`, `Losing_the_lease_cancels_the_running_job`.

### At-least-once and idempotency

Everything above means a job can run twice. That is stated plainly on `IJobHandler<T>` and in the README; handlers must tolerate it. Two things make it easier:

* `IJob.IdempotencyKey` prevents duplicate *creation* (double-submitted forms, retried HTTP calls, replayed messages) at the storage level: Postgres uses a partial unique index, Redis a key, in-memory a dictionary.
* With Postgres, creating a job inside a `TransactionScope` enlists the insert, so the job only becomes visible when your own transaction commits. The "job ran before the data was committed" bug cannot happen.

Tests: `Create_with_existing_idempotency_key_returns_existing_id_and_creates_nothing`, `Deleting_a_job_frees_its_idempotency_key`, `Jobs_declare_their_own_idempotency_key`.

### Poison jobs and retries

`Attempt` is incremented when a job is *fetched*, not when it finishes. A job that kills the process still burns an attempt every time, so it cannot loop forever: after `MaxAttempts` the retry policy gives up and the job is `Failed`. Failed is a terminal, retained state (a dead-letter queue by another name) that the dashboard API can list and requeue.

The default backoff is Hangfire's, because it is tuned by a decade of production use: `(attempt-1)^4 + 15 + random(0..30)·attempt` seconds, ten attempts, roughly two hours in total. `DoNotRetry` short-circuits exceptions that will never succeed.

Tests: `Retries_failed_jobs_until_they_succeed`, `Moves_jobs_to_Failed_when_the_retry_policy_gives_up`, `Default_policy_retries_up_to_ten_attempts_with_growing_delays`.

### Scheduling, time zones and DST

Cron expressions are evaluated by Cronos in the recurring job's `TimeZoneInfo`. A 01:30 schedule on the night clocks jump from 01:00 to 02:00 runs once at 02:00, not zero or two times; a schedule in the repeated hour in autumn runs once. Test: `Cron_handles_daylight_saving_transitions_without_skipping_or_doubling`.

Across servers the scheduler never needs a lock. Triggering is a compare-and-set: "if `NextRunAt` is still the value I read, set it to the next occurrence and create the job, atomically". Any number of instances can race; exactly one wins and the rest see `false`. Tests: `TriggerRecurring_is_a_compare_and_set_that_exactly_one_caller_wins`, `Competing_schedulers_trigger_each_occurrence_exactly_once`.

Missed occurrences after an outage collapse into a single run (`Missed_occurrences_after_an_outage_collapse_into_one_run`). Running "once per missed minute" after a three-hour outage is almost never what anyone wants.

Clock skew between app servers is tolerated by the lease length (minutes) and by cron precision (seconds at best). Run NTP; all timestamps are UTC.

### Serialization and versioning

A job is stored as `(name, payload)`. The name comes from `[JobName("send-email")]`, so the record can be renamed, moved or have properties added without breaking queued jobs. Payloads are `System.Text.Json` with enums as strings, so reordering an enum is safe too.

If a worker cannot resolve a name or deserialize a payload it throws `JobExecutionException`. That is retried like any other failure, deliberately: during a rolling deploy an old worker can fetch a job type that only the new build knows, and the next attempt will land on a new worker. If the type is really gone the job ends up `Failed` with a clear message. Tests: `Unregistered_CLR_names_still_resolve_when_the_type_is_loadable`, `Unknown_names_are_a_JobExecutionException`, `Unknown_job_types_are_retried_like_any_failure_and_then_dead_lettered`.

### Storage pressure

Hangfire's SQL Server storage polls, and every job touches several tables (queue, job, state history, parameters, counters). Slowcooker:

* fetches in batches: `min(free slots, FetchBatchSize)` jobs per round-trip;
* waits for push notifications (`LISTEN/NOTIFY`, pub/sub) and only polls as a fallback;
* keeps one row per job with partial indexes for the fetch path (`WHERE state = 0`) and the lease-expiry path (`WHERE state = 1`);
* settles with a single conditional `UPDATE`.

Test: `Picks_up_new_jobs_within_milliseconds_thanks_to_the_wake_up_signal`, `WaitForJob_returns_promptly_when_a_job_is_created` (all providers).

### Graceful shutdown

On stop the worker stops fetching, waits up to `ShutdownTimeout` for running jobs, then cancels their `JobContext.CancellationToken` and *requeues* them. Requeue is distinct from retry: the job goes back to `Scheduled` at its original `RunAt` with `Attempt - 1`, so a deploy does not count against the retry budget. If a worker is killed before it can requeue, the lease expiry covers it.

Tests: `Graceful_shutdown_cancels_running_jobs_and_requeues_them_without_burning_an_attempt`, `Shutdown_waits_for_jobs_that_finish_in_time`, `Disposing_an_unsettled_handle_requeues_the_job`.

### The dashboard as an attack surface

The API can delete and requeue jobs. By default it only answers local requests; it opens up only when the endpoints carry authorization metadata or when you set `AllowUnauthenticatedRemoteAccess` explicitly. The bundled UI sits behind the same rule. It is mounted *after* the application's pipeline so that forwarded-headers and authentication middleware have run before the local-or-authorized check. Tests: `Remote_requests_are_refused_by_default`, `Remote_requests_are_allowed_when_the_endpoints_require_authorization`, and the same pair in `SlowcookerAdminUserInterfaceTests`.

## What was kept from Hangfire

Reading Hangfire's source (`Worker.cs`, `AutomaticRetryAttribute.cs`, `RecurringJobScheduler.cs`, `Hangfire.PostgreSql`'s `PostgreSqlJobQueue.cs`, `InvocationData.cs`) shaped several decisions:

* the fetch statement for Postgres (`FOR UPDATE SKIP LOCKED` inside an `UPDATE`) and the `LISTEN/NOTIFY` wake-up;
* the invisibility timeout idea, generalized into leases with heartbeats;
* the retry curve and the default of ten attempts;
* `CancellationToken` as a first-class input to a job;
* the "local requests only" default for the dashboard.

## What was done differently, and why

* **Messages, not method calls.** Hangfire stores `TypeName`, `MethodName`, `ParameterTypes` and argument JSON, and resolves the overload at run time. Every refactor is a risk. A record with a stable name is a contract you can see.
* **Handlers resolved from DI with a scope per job.** No `JobActivator` abstraction; constructor injection just works.
* **One state machine with four states.** No `Enqueued` vs `Scheduled` distinction (a scheduled job is just one whose `RunAt` is in the future), no `Deleted` or `Awaiting` states, no state history table. The job row *is* the history you need: attempt, last error, timestamps.
* **No distributed locks anywhere.** Fetching is atomic per row; recurring triggers are compare-and-set.
* **Async everywhere.** Handlers return `Task`; the worker never blocks a thread.
* **Time is injectable.** Every provider and the worker take a `TimeProvider`, which is why the storage contract suite can test lease expiry and retention deterministically with `FakeTimeProvider`.
* **No bundled UI.** A JSON API with clear semantics beats a UI you cannot change.

## Things it does not do (yet)

Batches with dependencies, continuations other than "enqueue from the handler", per-type retry attributes, job priorities within a queue, OpenTelemetry. All are possible on top of the current storage contract without changing it.
