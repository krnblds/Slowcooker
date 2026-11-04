# Changelog

## 0.1.0 (unreleased)

First cut.

* Typed jobs (`IJob` records) and handlers (`IJobHandler<T>`) with stable `[JobName]`s, `[Queue]` defaults and per-job idempotency keys.
* Async worker with batched fetching, leases and heartbeats, graceful shutdown with requeue, retention.
* Exponential backoff retry policy with dead-lettering.
* Recurring jobs on cron with time zones; lock-free compare-and-set triggering. `Cron` value type with readable presets (`Cron.Daily.At(6)`, `Cron.Every(...)`) and previews.
* Storage providers: PostgreSQL (`SKIP LOCKED`, `LISTEN/NOTIFY`), Redis (Lua), in-memory; shared contract test suite.
* `Slowcooker.AspNetCore`: dashboard JSON API with local-only default access, including a cron preview endpoint.
* Dashboard redesign: a token-based design system with light and dark themes, routed pages with shareable URLs (`/jobs?state=failed`, `/jobs/{id}`), detail drawers, confirm dialogs, toasts, an overview with recent failures and completions, a cron scratchpad with presets. Deep links and `<base href>` so it works at any mount path and behind a path base.
* Packaging: NuGet metadata, Source Link, symbol packages, XML docs, package icon; release workflow publishes to NuGet from version tags.
* `Slowcooker.AspNetCore`: bundled dashboard. `AddSlowcooker().AddAdminUserInterface()` serves the compiled UI at `/slowcooker/` and the API at `/slowcooker/api` with no further setup; `MapSlowcookerAdminUserInterface()` for explicit placement. The UI is embedded in the assembly, so the package works without Node or static files.
* Dashboard overhaul for the beta: overview with a health line, session trend lines, per-queue state mix and the most common recent errors; jobs page with state tabs, job-name filter, page sizes, bulk requeue and delete, `j`/`k` navigation and a job drawer with a history timeline and folded stack traces; recurring schedules in plain English with their recent jobs; a cron tester page; a command palette (`Ctrl K`); display settings for theme, UTC or local times and pausing live updates; an offline banner and the failed count in the tab title.
* API: `GET /jobs?name=` filters by exact job name, on every storage provider.
* API: `POST` and `DELETE` require an `X-Slowcooker-Request` header and return `403` without it, which blocks cross-site requests. Scripts that call the API must add it.
* Releases publish to NuGet with trusted publishing (OIDC); no API key is stored.
* `dashboard/kit`: Vue 3 + TypeScript component kit for building your own dashboard on the API: typed client, polling composables, tables, detail, cron preview, with a vitest suite. Source to copy, not an npm package. The bundled UI (`dashboard/admin`) is built on it.
