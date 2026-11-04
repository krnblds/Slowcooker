# @slowcooker/vue

A Vue 3 + TypeScript component kit for building a dashboard on the [Slowcooker HTTP API](../../docs/dashboard-api.md): a typed client, polling composables and functional components. It is what the admin app bundled with `Slowcooker.AspNetCore` is built from, and it is deliberately **unopinionated about looks**: semantic markup, `sc-` CSS classes, a handful of CSS variables. Bring your own design system, or start from `styles.css`.

It is source, not a published npm package: Slowcooker ships on NuGet, with the finished dashboard inside `Slowcooker.AspNetCore`. To build your own UI, copy `kit/src` and `kit/styles.css` into your app (the only dependency is `vue` ^3.4), or reference this folder from a workspace the way `dashboard/admin` does.

```ts
// main.ts / App.vue
import { createClient, provideSlowcooker } from "@slowcooker/vue";
import "@slowcooker/vue/styles.css";                           // optional default styling

const api = createClient({
  baseUrl: "/slowcooker/api",                                   // {Path}/api of AddAdminUserInterface(), or where MapSlowcookerApi() is mounted
  headers: () => ({ Authorization: `Bearer ${getToken()}` }),   // whatever your RequireAuthorization() policy needs
});
provideSlowcooker(api);   // in a root component's setup()
```

Every composable and component below finds the client through `provideSlowcooker`, or accepts a `client` option explicitly.

```
src/
  api/            typed client + wire types for every endpoint
  composables/    polling, jobs, job, recurring, cron preview, provide/inject
  components/     StatsBar, JobTable, JobDetail, RecurringTable, CronPreview, …
  format.ts       relative time / JSON helpers
styles.css        default styling via CSS variables (light + dark)
```

## API client

`createClient(options)` returns an object with one method per endpoint. All methods return promises of the types in `api/types.ts`; failures throw `SlowcookerApiError { status, message, url }`. Options: `baseUrl` (default `/slowcooker/api`), `headers` (object or function, sync or async), `fetch` (for tests and SSR). Every request carries the `X-Slowcooker-Request` header the API requires on `POST` and `DELETE`.

| Method | Endpoint | Returns |
|---|---|---|
| `info()` | `GET /info` | `Info` (version, storage, serverTime) |
| `stats()` | `GET /stats` | `Stats` (counts, per-queue counts, serverTime) |
| `queues()` | `GET /queues` | `QueueStats[]` |
| `jobs({ state, queue, name, skip, take })` | `GET /jobs` | `Page<Job>` newest first; `name` is an exact job name |
| `job(id)` | `GET /jobs/{id}` | `Job` |
| `requeue(id)` | `POST /jobs/{id}/requeue` | `Job` (409 while processing) |
| `deleteJob(id)` | `DELETE /jobs/{id}` | `void` |
| `recurring()` | `GET /recurring` | `RecurringJob[]` |
| `recurringJob(id)` | `GET /recurring/{id}` | `RecurringJob` |
| `trigger(id)` | `POST /recurring/{id}/trigger` | `{ jobId }` |
| `deleteRecurring(id)` | `DELETE /recurring/{id}` | `void` |
| `cronPreview(expression, timeZone?, count?)` | `GET /cron/preview` | `CronPreviewResult` (`valid`, `error`, `upcoming[]`) |

Wire types: `Job`, `JobState` (`"scheduled" | "processing" | "succeeded" | "failed"`), `RecurringJob`, `QueueStats`, `Stats`, `Page<T>`, `Info`, `CronPreviewResult`. Field meanings are documented in [`docs/dashboard-api.md`](../../docs/dashboard-api.md).

## Composables

All composables return Vue refs and are safe to use in `<script setup>`. Polling stops when the component unmounts and pauses while the tab is hidden.

### `usePolling(fetcher, { intervalMs, immediate, pauseWhenHidden })`
The primitive the others build on. Returns `{ data, error, loading, refresh, pause, resume, paused }`. Keeps the last good `data` while refreshing; coalesces concurrent refreshes. `intervalMs: 0` means manual `refresh()` only.

### `useStats({ intervalMs = 3000 })`
`/stats` on a timer. `stats.data.value?.serverTime` is the server clock; pass it as `now` to time-displaying components.

### `useJobs({ state, queue, name, pageSize = 50, intervalMs = 0 })`
A filtered, paged list. Returns everything from `usePolling` plus reactive `state`, `queue`, `name` (exact job name), `pageSize`, `page`, and computed `items`, `total`, `pageCount`, with `next()`, `prev()`, `requeue(id)`, `remove(id)`. Changing a filter resets to page 0 and refetches.

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
| `JobTable` | `jobs`, `now?`, `selectedId?`, `readonly?`, `selectable?` | `@select(job)`, `@requeue(job)`, `@delete(job)`, `v-model:selection` (ids); slots `actions`, `empty` | The list: name, queue, badge, attempt, relative times, first line of the last error. Scheduled jobs are marked `due` or `retry`. `selectable` adds a checkbox column with select-all for bulk actions |
| `StateTabs` | `counts?`, `total?` | `v-model:state` (`null` = all) | Segmented state filter with per-state counts, e.g. from `/stats` |
| `JobDetail` | `job`, `now?`, `readonly?`, `closable?` | `@requeue`, `@delete`, `@close`; slots `actions`, default | Everything about one job: properties, history, error and payload |
| `JobTimeline` | `job`, `now?` | | The job's history as far as storage records it: enqueued, scheduled, attempts, lease, outcome |
| `ErrorView` | `error` | | An exception string as a headline, message lines and a collapsed stack trace |
| `Pagination` | `page`, `pageCount`, `total`, `pageSize` | `@change(page)` | Prev/next + "1–25 of 400" |
| `RecurringTable` | `items`, `now?`, `readonly?` | `@select`, `@trigger`, `@delete`; slot `actions` | Recurring jobs with next/last run and a plain-English schedule; parked schedules get a badge |
| `CronPreview` | `expression`, `timeZone?`, `count?` | | Live validation, a plain-English summary and upcoming runs (uses `useCronPreview`) |
| `StateBadge` | `state` | | Coloured pill |
| `RelativeTime` | `value`, `now?` | | "in 3 minutes" / "2 hours ago" with the absolute time as tooltip. Pass the server clock as `now` |
| `PayloadView` | `value` | | Pretty-printed JSON |
| `ErrorBanner` | `error` | | Renders an `Error`'s message, or nothing |

Helpers in `format.ts`: `formatRelative`, `formatDateTime`, `formatDuration`, `formatJson`, `errorHeadline`, `splitError` (headline, message lines, stack frames), `describeCron` ("Weekdays at 09:00" for common five-field patterns, `null` otherwise) and `setDisplayTimeZone("UTC" | ianaZone | undefined)`, which switches every absolute time the kit renders to that zone, reactively.

## Theming

Everything is styled through `sc-*` classes and these variables (defined for light and `prefers-color-scheme: dark` in `styles.css`):

```css
--sc-bg --sc-fg --sc-muted --sc-border --sc-surface --sc-accent
--sc-scheduled --sc-processing --sc-succeeded --sc-failed
--sc-radius --sc-font --sc-mono
--sc-brand-2 --sc-toast-bg --sc-toast-fg     /* used by the admin shell */
```

Each theme also sets `color-scheme`, so native checkboxes, selects and scrollbars follow light and dark.

Override them in your own stylesheet, or skip `styles.css` entirely and style the classes yourself. Components contain no inline styles.

## Developing the kit

```bash
cd dashboard
npm install
npm test -w kit                # vitest + @vue/test-utils on happy-dom
npm run typecheck -w kit       # vue-tsc
npm run dev                    # the admin app on top of the kit, with hot reload
```
