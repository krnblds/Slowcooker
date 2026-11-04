# Slowcooker dashboard

Two npm workspaces:

```
dashboard/
  kit/      @slowcooker/vue — the component kit: typed client, polling composables, tables, detail views. See kit/README.md
  admin/    the admin app bundled with Slowcooker.AspNetCore, built on the kit (App.vue + three views)
```

`AddAdminUserInterface()` serves the compiled admin app, which ships inside the `Slowcooker.AspNetCore` NuGet package. The kit is source for building your own dashboard, in your own design, on the same API; it is not published to npm.

## Run it

```bash
# terminal 1: the demo server (in-memory storage, generates demo traffic, serves the embedded dashboard at /slowcooker/)
dotnet run --project samples/Slowcooker.Samples.WebApi
# terminal 2: the admin app with hot reload, proxying /api to the server's /slowcooker/api
cd dashboard && npm install && npm run dev        # http://localhost:5173/
```

Against real storage: `docker compose up -d` then `dotnet run --project samples/Slowcooker.Samples.WebApi -- --Slowcooker:Storage=Postgres` (or `Redis`).

## Build and test

| Command (in `dashboard/`) | What it does |
|---|---|
| `npm run build` | Builds the admin app into `src/Slowcooker.AspNetCore/ui/`, which that project embeds in its assembly as `ui/...` resources. The first `dotnet build` of `Slowcooker.AspNetCore` runs this for you when Node is installed; after changing anything here, run it again and rebuild |
| `npm test` | The kit's vitest suite |
| `npm run typecheck` | `vue-tsc` over both workspaces |

The admin app imports the kit's source through the workspace link (hot reload, no build step), builds with a relative `base` and finds its API at `./api`, so the server can mount it at any path.

## What is left to build in the admin app

The kit gives data access and functional building blocks; the admin app wires them together. For a dashboard people enjoy using, these are the pieces to design and build:

1. **Layout and navigation.** The app uses tabs in `App.vue`. You probably want `vue-router` with routes like `/`, `/jobs`, `/jobs/:id`, `/queues/:name`, `/recurring`, so links are shareable and the browser back button works.
2. **Visual design.** Replace or extend `kit/styles.css`: typography, spacing, a real colour system for the four states, hover/selection states, dark mode polish, responsive tables (cards on narrow screens).
3. **Overview page.** The counters and queue table are there; add trend lines (store the last N `/stats` samples client-side and chart them), a "due in the next minute" count (`runAt <= serverTime + 60s` among scheduled jobs), and a prominent failed-jobs call-out.
4. **Jobs page UX.** Search by id, column sorting, a payload viewer that collapses large objects, keyboard navigation, multi-select with bulk requeue/delete (call the per-job endpoints in sequence; there is no bulk endpoint), a "live" toggle that enables polling.
5. **Job detail.** Timeline rendering (created → runs at → lease → completed), a friendlier error view (split the .NET exception string into type, message and stack), a "copy id" button, and a link back to the recurring job if it created this one (`lastJobId` on recurring jobs; the reverse link is not in the API, so match by name and time if you want it).
6. **Recurring page.** The table and cron preview are there. If you want to *create or edit* schedules from the UI you will need a small write endpoint on your server that calls `IRecurringJobClient.AddOrUpdateAsync` with your own job types (the dashboard API deliberately does not let a browser enqueue arbitrary job types). A cron builder UI on top of `CronPreview` (presets for daily/weekly/monthly that emit expressions, matching the `Cron` helpers in C#) would be a nice touch.
7. **Auth plumbing.** Pass a token in `createClient({ headers })`, handle `401/403` from `SlowcookerApiError` by redirecting to login, and hide the destructive buttons (`readonly`) for users who should only look.
8. **Confirmations and feedback.** The app uses `confirm()`; use proper dialogs, toasts for requeue/delete results, and optimistic updates where it helps.
9. **Empty, loading and error states.** `loading`, `error` and the `empty` slot are all exposed; design them.
10. **Component tests for the admin views.** The kit's own tests show the pattern (`kit/src/**/*.test.ts`, `createClient({ fetch: mockFetch })`).

Not needed: anything server-side for reading. Every read the dashboard could want is already an endpoint, and `IJobMonitor` is there if you need a custom one.
