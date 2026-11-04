# Dashboard API

`Slowcooker.AspNetCore` exposes the monitoring and operator surface of Slowcooker as a small JSON API. It is the backend for whatever dashboard you build: a React page, a Blazor component, a CLI, a Grafana panel. The core library has no opinion about the UI.

```csharp
using Slowcooker.AspNetCore;

var api = app.MapSlowcookerApi("/slowcooker");   // returns a RouteGroupBuilder
api.RequireAuthorization("ops");                  // strongly recommended
```

## Access control

The API can requeue and delete jobs, so it refuses remote callers unless you opt in. The rule, evaluated per request:

1. If `SlowcookerApiOptions.AllowUnauthenticatedRemoteAccess` is `true`: allowed.
2. If the endpoint carries authorization metadata (`RequireAuthorization(...)` on the group) and no `AllowAnonymous`: allowed, and ASP.NET Core's authorization middleware decides.
3. If the request comes from the same machine (loopback or the server's own address): allowed.
4. Otherwise `403` with an explanatory JSON error.

So in development it just works, and in production you attach your real policy. For a browser dashboard on another origin add CORS yourself (`app.UseCors(...)` plus `api.RequireCors(...)`).

## Conventions

* All responses are JSON, camelCase. Enums are camelCase strings (`"scheduled"`, `"processing"`, `"succeeded"`, `"failed"`).
* Timestamps are ISO 8601 with offset, always UTC (`2026-11-04T09:15:00+00:00`). `serverTime` is included where useful so a dashboard can show "due in 3 min" without trusting the browser clock.
* `payload` is the job's JSON exactly as stored (an object), so a dashboard can render it directly.
* Errors are `{ "error": "message" }` with an appropriate status code.
* The API uses its own serializer settings; your application's `JsonOptions` are untouched.

## Endpoints

Base path below is whatever you passed to `MapSlowcookerApi` (default `/slowcooker`).

### `GET /info`

```json
{ "version": "0.1.0", "storage": "PostgresJobStorage", "serverTime": "2026-11-04T09:15:00+00:00" }
```

### `GET /stats`

Counts by state, overall and per queue, plus the number of recurring jobs. Poll this every few seconds for a header bar.

```json
{
  "scheduled": 12, "processing": 3, "succeeded": 4810, "failed": 2, "recurring": 4,
  "queues": [
    { "name": "default",  "scheduled": 10, "processing": 1, "succeeded": 4000, "failed": 0 },
    { "name": "payments", "scheduled": 2,  "processing": 2, "succeeded": 810,  "failed": 2 }
  ],
  "serverTime": "2026-11-04T09:15:00+00:00"
}
```

`scheduled` includes jobs that are due now and jobs due in the future (including retries waiting for their backoff). Compare `runAt` with `serverTime` to tell them apart.

### `GET /queues`

The `queues` array from `/stats` on its own.

### `GET /jobs?state=&queue=&skip=&take=`

Jobs newest first. `state` is one of the four state names (case-insensitive); `queue` is an exact name; `take` is clamped to 1..500 (default 50).

```json
{
  "items": [
    {
      "id": "019a3c1f2b7a7000a3c1e9f0d2b4c6e8",
      "name": "charge-card",
      "displayName": "charge-card",
      "payload": { "OrderId": "o-42", "Amount": 99.0 },
      "queue": "payments",
      "state": "scheduled",
      "createdAt": "2026-11-04T09:10:00+00:00",
      "runAt": "2026-11-04T09:12:10+00:00",
      "attempt": 1,
      "lastError": "System.Net.Http.HttpRequestException: gateway timeout\n   at ...",
      "leaseOwner": null,
      "leaseExpiresAt": null,
      "completedAt": null,
      "idempotencyKey": "charge:o-42"
    }
  ],
  "total": 12,
  "skip": 0,
  "take": 50
}
```

Field notes:

| Field | Meaning |
|---|---|
| `attempt` | How many times a worker has fetched the job. `0` = never run. After a failed attempt that is waiting to retry, `attempt` is the number of attempts so far |
| `lastError` | `exception.ToString()` of the most recent failure, or `null` |
| `leaseOwner`, `leaseExpiresAt` | Set while `processing`. If `leaseExpiresAt` is in the past the worker probably died and the job will be re-fetched |
| `completedAt` | Set for `succeeded` and `failed` |
| `idempotencyKey` | The job's deduplication key, if any |

`400` if `state` is not a known state.

### `GET /jobs/{id}`

One job in the shape above, or `404`.

### `POST /jobs/{id}/requeue`

Runs the job again now: state `scheduled`, `runAt` = now, `attempt` = 0, lease and completion cleared. Works for scheduled, succeeded and failed jobs. Returns the updated job.

* `404` unknown id.
* `409` the job is `processing`; wait for it to finish or for its lease to expire.

Use this for "retry now" on failed jobs and "run now" on jobs scheduled for later.

### `DELETE /jobs/{id}`

Removes the job. `204`, or `404`. Deleting a `processing` job does not stop the worker that is running it; the worker's settle call fails harmlessly and the job is gone. Deleting a scheduled job cancels it.

### `GET /recurring`

```json
[
  {
    "id": "daily-report",
    "name": "generate-daily-report",
    "displayName": "generate-daily-report",
    "payload": { "Region": "eu" },
    "cron": "0 6 * * *",
    "timeZoneId": "Europe/London",
    "queue": "default",
    "nextRunAt": "2026-11-05T06:00:00+00:00",
    "lastRunAt": "2026-11-04T06:00:00+00:00",
    "lastJobId": "019a3b...",
    "createdAt": "2026-10-01T12:00:00+00:00",
    "updatedAt": "2026-10-01T12:00:00+00:00"
  }
]
```

`nextRunAt` is `null` when the schedule is parked: its cron expression has no future occurrence, or its time zone could not be resolved on the scheduler. `lastJobId` links to `/jobs/{id}` while that job is still retained.

### `GET /recurring/{id}`

One recurring job, or `404`.

### `POST /recurring/{id}/trigger`

Creates a job from the template right now without changing the schedule. `201` with `{ "jobId": "..." }`, or `404`.

### `DELETE /recurring/{id}`

Removes the schedule. Jobs it already created are unaffected. `204`, or `404`.

### `GET /cron/preview?expression=&timeZone=&count=`

Validates a cron expression and lists its next occurrences, for a "new schedule" form. `timeZone` is an IANA or Windows id (default UTC); `count` is clamped to 0..100 (default 5).

```json
{ "expression": "0 6 * * *", "valid": true, "error": null, "timeZoneId": "Europe/London",
  "upcoming": ["2026-11-05T06:00:00+00:00", "2026-11-06T06:00:00+00:00", "2026-11-07T06:00:00+00:00"] }
```

An invalid expression still returns `200` with `valid: false` and an `error` message, so the form can show it inline. An unknown time zone is `400`.

## Building a dashboard on it

A minimal page needs three calls: `/stats` on a timer, `/jobs?state=failed` for the thing people actually come to look at, and `POST /jobs/{id}/requeue` for the button next to each failure. Everything is paged and cheap: stats are one aggregate query, listings are one indexed query plus one count.

```ts
const base = "/slowcooker";
const stats = await fetch(`${base}/stats`).then(r => r.json());
const failed = await fetch(`${base}/jobs?state=failed&take=100`).then(r => r.json());
await fetch(`${base}/jobs/${failed.items[0].id}/requeue`, { method: "POST" });
```

There is no push channel for the dashboard itself; poll `/stats` every 2 to 5 seconds and the job list when the counts change.

## Programmatic access

Everything the API does is available in-process through `IJobMonitor` (`GetStatsAsync`, `QueryAsync`, `GetAsync`, `RequeueAsync`, `DeleteAsync`, `ListRecurringAsync`, `TriggerRecurringAsync`, `DeleteRecurringAsync`) for custom tooling, health checks or tests.
