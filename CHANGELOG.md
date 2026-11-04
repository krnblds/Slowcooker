# Changelog

## 0.1.0 (unreleased)

First cut.

* Typed jobs (`IJob` records) and handlers (`IJobHandler<T>`) with stable `[JobName]`s, `[Queue]` defaults and per-job idempotency keys.
* Async worker with batched fetching, leases and heartbeats, graceful shutdown with requeue, retention.
* Exponential backoff retry policy with dead-lettering.
* Recurring jobs on cron with time zones; lock-free compare-and-set triggering. `Cron` value type with readable presets (`Cron.Daily.At(6)`, `Cron.Every(...)`) and previews.
* Storage providers: PostgreSQL (`SKIP LOCKED`, `LISTEN/NOTIFY`), Redis (Lua), in-memory; shared contract test suite.
* `Slowcooker.AspNetCore`: dashboard JSON API with local-only default access, including a cron preview endpoint.
* `Slowcooker.AspNetCore`: bundled dashboard. `AddSlowcooker().AddAdminUserInterface()` serves the compiled UI at `/slowcooker/` and the API at `/slowcooker/api` with no further setup; `MapSlowcookerAdminUserInterface()` for explicit placement. The UI is embedded in the assembly, so the package works without Node or static files.
* `dashboard/kit`: Vue 3 + TypeScript component kit for building your own dashboard on the API: typed client, polling composables, tables, detail, cron preview, with a vitest suite. Source to copy, not an npm package. The bundled UI (`dashboard/admin`) is built on it.
