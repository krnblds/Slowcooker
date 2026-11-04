# Changelog

## 0.1.0 (unreleased)

First cut.

* Typed jobs (`IJob` records) and handlers (`IJobHandler<T>`) with stable `[JobName]`s, `[Queue]` defaults and per-job idempotency keys.
* Async worker with batched fetching, leases and heartbeats, graceful shutdown with requeue, retention.
* Exponential backoff retry policy with dead-lettering.
* Recurring jobs on cron with time zones; lock-free compare-and-set triggering. `Cron` value type with readable presets (`Cron.Daily.At(6)`, `Cron.Every(...)`) and previews.
* Storage providers: PostgreSQL (`SKIP LOCKED`, `LISTEN/NOTIFY`), Redis (Lua), in-memory; shared contract test suite.
* `Slowcooker.AspNetCore`: dashboard JSON API with local-only default access, including a cron preview endpoint.
* `dashboard/`: Vue 3 + TypeScript component kit (typed client, polling composables, tables, detail, cron preview) and a demo admin app served by the sample.
