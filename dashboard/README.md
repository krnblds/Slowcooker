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

## The admin app

`admin/src` is a small, conventional Vue app: `router.ts` (web history on the server's `<base href>`), `App.vue` (sidebar shell, shared `/stats` poll, theme toggle), three pages under `pages/`, and `components/` for the drawer, confirm dialog, toasts, page header and icons. Styling is `kit/styles.css` for everything the kit renders plus `admin/src/app.css` for the shell. `?theme=light|dark|system` on any URL overrides the stored theme (useful for screenshots).

Ideas not done yet, roughly in order of value:

1. **Trend lines** on the overview (sample `/stats` client-side and chart the last few minutes) and a "due in the next minute" count.
2. **Bulk actions**: multi-select with requeue/delete by calling the per-job endpoints in sequence (there is no bulk endpoint on purpose).
3. **Keyboard navigation** in the job list (j/k, Enter to open, Esc already closes the drawer).
4. **A friendlier error view**: split the .NET exception string into type, message and frames, and collapse the stack by default.
5. **Auth plumbing** for hosts that use tokens rather than cookies: `createClient({ headers })`, redirect on `401/403` from `SlowcookerApiError`, and `readonly` tables for viewers.
6. **Creating schedules from the UI** needs a write endpoint in your own app that calls `IRecurringJobClient.AddOrUpdateAsync` with your job types; the dashboard API deliberately does not let a browser enqueue arbitrary jobs.
7. **Component tests for the pages**; the kit's tests show the pattern (`kit/src/**/*.test.ts`, `createClient({ fetch: mockFetch })`).

Not needed: anything server-side for reading. Every read the dashboard could want is already an endpoint, and `IJobMonitor` is there if you need a custom one.
