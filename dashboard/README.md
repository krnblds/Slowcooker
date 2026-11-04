# Slowcooker dashboard kit

A Vue 3 + TypeScript kit for building a dashboard on the [Slowcooker HTTP API](../docs/dashboard-api.md), plus a small demo admin app built with it. The kit is deliberately **unopinionated about looks**: semantic markup, `sc-` CSS classes, a handful of CSS variables. Bring your own design system, or start from `src/styles/kit.css`.

```
dashboard/
  src/kit/              ← the kit (copy this folder into your app, or import it from here)
    api/                  typed client + wire types for every endpoint
    composables/          polling, jobs, job, recurring, cron preview, provide/inject
    components/           StatsBar, JobTable, JobDetail, RecurringTable, CronPreview, …
    format.ts             relative time / JSON helpers
  src/demo/             ← the demo admin app (App.vue + three views)
  src/styles/kit.css    ← default styling via CSS variables (light + dark)
```

## Run it

```bash
# terminal 1: the demo server (in-memory storage, generates demo traffic, serves the built dashboard)
dotnet run --project samples/Slowcooker.Samples.WebApi
# terminal 2: the dashboard with hot reload, proxying /slowcooker to the server
cd dashboard && npm install && npm run dev        # http://localhost:5173/dashboard/
```

`npm run build` type-checks (`vue-tsc`) and writes the production build to `samples/Slowcooker.Samples.WebApi/wwwroot/dashboard`, where the sample serves it at `http://localhost:5000/dashboard/`. The sample's first `dotnet build` runs that for you when Node is installed.

Against real storage: `docker compose up -d` then `dotnet run --project samples/Slowcooker.Samples.WebApi -- --Slowcooker:Storage=Postgres` (or `Redis`).

## Use the kit in your own app

Copy `src/kit` into your project (it only depends on `vue`), then:

```ts
// main.ts / App.vue
import { createClient, provideSlowcooker } from "@/kit";
const api = createClient({
  baseUrl: "/slowcooker",                                   // where MapSlowcookerApi() is mounted
  headers: () => ({ Authorization: `Bearer ${getToken()}` }) // whatever your RequireAuthorization() policy needs
});
provideSlowcooker(api);   // in a root component's setup()
```

Every composable and component below finds the client through `provideSlowcooker`, or accepts a `client` option explicitly.

## API client

`createClient(options)` returns an object with one method per endpoint. All methods return promises of the types in `api/types.ts`; failures throw `SlowcookerApiError { status, message, url }`.

| Method | Endpoint | Returns |
|---|---|---|
| `info()` | `GET /info` | `Info` (version, storage, serverTime) |
| `stats()` | `GET /stats` | `Stats` (counts, per-queue counts, serverTime) |
| `queues()` | `GET /queues` | `QueueStats[]` |
| `jobs({ state, queue, skip, take })` | `GET /jobs` | `Page<Job>` newest first |
| `job(id)` | `GET /jobs/{id}` | `Job` |
| `requeue(id)` | `POST /jobs/{id}/requeue` | `Job` (409 while processing) |
| `deleteJob(id)` | `DELETE /jobs/{id}` | `void` |
| `recurring()` | `GET /recurring` | `RecurringJob[]` |
| `recurringJob(id)` | `GET /recurring/{id}` | `RecurringJob` |
| `trigger(id)` | `POST /recurring/{id}/trigger` | `{ jobId }` |
| `deleteRecurring(id)` | `DELETE /recurring/{id}` | `void` |
| `cronPreview(expression, timeZone?, count?)` | `GET /cron/preview` | `CronPreviewResult` (`valid`, `error`, `upcoming[]`) |

Wire types: `Job`, `JobState` (`"scheduled" | "processing" | "succeeded" | "failed"`), `RecurringJob`, `QueueStats`, `Stats`, `Page<T>`, `Info`, `CronPreviewResult`. Field meanings are documented in [`docs/dashboard-api.md`](../docs/dashboard-api.md).

## Composables

All composables return Vue refs and are safe to use in `<script setup>`. Polling stops when the component unmounts and pauses while the tab is hidden.

### `usePolling(fetcher, { intervalMs, immediate, pauseWhenHidden })`
The primitive the others build on. Returns `{ data, error, loading, refresh, pause, resume, paused }`. Keeps the last good `data` while refreshing; coalesces concurrent refreshes.

### `useStats({ intervalMs = 3000 })`
`/stats` on a timer. `stats.data.value?.serverTime` is the server clock; pass it as `now` to time-displaying components.

### `useJobs({ state, queue, pageSize = 50, intervalMs = 0 })`
A filtered, paged list. Returns everything from `usePolling` plus reactive `state`, `queue`, `pageSize`, `page`, and computed `items`, `total`, `pageCount`, with `next()`, `prev()`, `requeue(id)`, `remove(id)`. Changing a filter resets to page 0 and refetches.

```ts
const jobs = useJobs({ state: "failed", pageSize: 25, intervalMs: 5000 });
jobs.queue.value = "payments";        // refetches
await jobs.requeue(jobs.items.value[0].id);
```

### `useJob(idRef)`
Follows a selected id: `{ job, error, loading, refresh, requeue, remove }`.

### `useRecurring({ intervalMs = 10000 })`
`{ data, …, trigger(id) → jobId, remove(id) }`.

### `useCronPreview(expressionRef, timeZoneRef, { count = 5, debounceMs = 300 })`
Debounced validation + upcoming runs for a schedule editor: `{ preview, error, loading, refresh }`. An invalid expression is `preview.valid === false` with `preview.error`, not a thrown error.

### `provideSlowcooker(client)` / `useSlowcooker(client?)`
Dependency injection for the client.

## Components

Props are typed; events are listed as `@event(payload)`. Every table has an `actions` slot (`{ job }`) to replace the default buttons, and `readonly` hides them.

| Component | Props | Events / slots | Purpose |
|---|---|---|---|
| `StatsBar` | `stats`, `loading` | `@select(state)`; default slot | The four state counters + recurring count. Click a counter to filter the job list |
| `QueueTable` | `queues` | `@select(queue)` | Per-queue counts |
| `JobFilters` | `queues?` | `v-model:state`, `v-model:queue`; default slot | State select + queue select (or free text) |
| `JobTable` | `jobs`, `now?`, `selectedId?`, `readonly?` | `@select(job)`, `@requeue(job)`, `@delete(job)`; slots `actions`, `empty` | The list: name, queue, badge, attempt, relative times, first line of the last error |
| `JobDetail` | `job`, `now?`, `readonly?` | `@requeue`, `@delete`, `@close`; slots `actions`, default | Everything about one job, payload and full error |
| `Pagination` | `page`, `pageCount`, `total`, `pageSize` | `@change(page)` | Prev/next + "1–25 of 400" |
| `RecurringTable` | `items`, `now?`, `readonly?` | `@select`, `@trigger`, `@delete`; slot `actions` | Recurring jobs with next/last run; parked schedules get a badge |
| `CronPreview` | `expression`, `timeZone?`, `count?` | | Live validation + upcoming runs (uses `useCronPreview`) |
| `StateBadge` | `state` | | Coloured pill |
| `RelativeTime` | `value`, `now?` | | "in 3 minutes" / "2 hours ago" with the absolute time as tooltip. Pass the server clock as `now` |
| `PayloadView` | `value` | | Pretty-printed JSON |
| `ErrorBanner` | `error` | | Renders an `Error`'s message, or nothing |

Helpers in `format.ts`: `formatRelative`, `formatDateTime`, `formatJson`, `errorHeadline`.

## Theming

Everything is styled through `sc-*` classes and these variables (defined for light and `prefers-color-scheme: dark`):

```css
--sc-bg --sc-fg --sc-muted --sc-border --sc-surface --sc-accent
--sc-scheduled --sc-processing --sc-succeeded --sc-failed
--sc-radius --sc-font --sc-mono
```

Override them in your own stylesheet, or drop `kit.css` entirely and style the classes yourself. Components contain no inline styles.

## What is left for you to build

The kit gives you data access and functional building blocks; the demo shows them wired together. For a dashboard people enjoy using, these are the pieces to design and build:

1. **Layout and navigation.** The demo uses tabs in `App.vue`. You probably want `vue-router` with routes like `/`, `/jobs`, `/jobs/:id`, `/queues/:name`, `/recurring`, so links are shareable and the browser back button works.
2. **Visual design.** Replace or extend `kit.css`: typography, spacing, a real colour system for the four states, hover/selection states, dark mode polish, responsive tables (cards on narrow screens).
3. **Overview page.** The counters and queue table are there; add trend lines (store the last N `/stats` samples client-side and chart them), a "due in the next minute" count (`runAt <= serverTime + 60s` among scheduled jobs), and a prominent failed-jobs call-out.
4. **Jobs page UX.** Search by id, column sorting, a payload viewer that collapses large objects, keyboard navigation, multi-select with bulk requeue/delete (call the per-job endpoints in sequence; there is no bulk endpoint), a "live" toggle that enables polling.
5. **Job detail.** Timeline rendering (created → runs at → lease → completed), a friendlier error view (split the .NET exception string into type, message and stack), a "copy id" button, and a link back to the recurring job if it created this one (`lastJobId` on recurring jobs; the reverse link is not in the API, so match by name and time if you want it).
6. **Recurring page.** The table and cron preview are there. If you want to *create or edit* schedules from the UI you will need a small write endpoint on your server that calls `IRecurringJobClient.AddOrUpdateAsync` with your own job types (the dashboard API deliberately does not let a browser enqueue arbitrary job types). A cron builder UI on top of `CronPreview` (presets for daily/weekly/monthly that emit expressions, matching the `Cron` helpers in C#) would be a nice touch.
7. **Auth plumbing.** Pass a token in `createClient({ headers })`, handle `401/403` from `SlowcookerApiError` by redirecting to login, and hide the destructive buttons (`readonly`) for users who should only look.
8. **Confirmations and feedback.** The demo uses `confirm()`; use proper dialogs, toasts for requeue/delete results, and optimistic updates where it helps.
9. **Empty, loading and error states.** `loading`, `error` and the `empty` slot are all exposed; design them.
10. **Tests.** Component tests with Vitest + Vue Test Utils against `createClient({ fetch: mockFetch })`; the client takes a custom `fetch` for exactly this reason.

Not needed: anything server-side for reading. Every read the dashboard could want is already an endpoint, and `IJobMonitor` is there if you need a custom one.
