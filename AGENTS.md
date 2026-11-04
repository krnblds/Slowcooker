# Working in this repository

Rules for AI agents and humans alike. Read this before changing anything; the rest of the docs are linked from here.

## What this is

Slowcooker is a background-job library for .NET: typed jobs, workers, cron schedules, three storage providers and a bundled dashboard. It ships as NuGet packages built from `src/`; the dashboard is a Vue app compiled into `Slowcooker.AspNetCore`.

| Path | Contents |
|---|---|
| `src/Slowcooker` | Core: jobs, handlers, client, worker, recurring scheduler, retry policy |
| `src/Slowcooker.Storage.{Postgres,Redis,InMemory}` | Storage providers, all implementing `IJobStorage` |
| `src/Slowcooker.AspNetCore` | Dashboard API (`SlowcookerApi.cs`), bundled UI (`SlowcookerAdminUserInterface.cs`, `EmbeddedDashboard.cs`); `ui/` is the compiled dashboard (git-ignored, embedded as `ui/<path>` resources) |
| `tests/` | Unit tests, API and dashboard-hosting tests, and the storage contract suite every provider must pass |
| `samples/Slowcooker.Samples.WebApi` | Reference app with handlers, recurring jobs, demo traffic and the dashboard |
| `dashboard/kit` | Vue 3 component kit (`@slowcooker/vue`, source only, never published to npm) |
| `dashboard/admin` | The bundled dashboard app, built on the kit |
| `docs/` | `design.md` (failure modes and how they are tested), `dashboard-api.md`, `storage.md`, `assets/` (logo, icon, README screenshots) |

## Commands

```bash
dotnet build Slowcooker.sln -p:SkipDashboardBuild=true          # SkipDashboardBuild: don't run npm; needs ui/ to exist already
dotnet test --project tests/Slowcooker.Tests -p:SkipDashboardBuild=true   # no Docker needed
dotnet test --solution Slowcooker.sln                             # Postgres/Redis suites start containers (Docker), or set
                                                                  # SLOWCOOKER_TEST_POSTGRES / SLOWCOOKER_TEST_REDIS
dotnet format                                                     # CI fails on formatting drift

cd dashboard
npm install                      # once
npm run build                    # admin app -> src/Slowcooker.AspNetCore/ui ; rebuild the .NET project afterwards to embed it
npm test                         # kit: vitest + @vue/test-utils
npm run typecheck                # both workspaces
npm run dev                      # http://localhost:5173/ with /api proxied to the sample on :5000

dotnet run --project samples/Slowcooker.Samples.WebApi            # dashboard at http://localhost:5000/slowcooker/
```

Facts that save time:

- The first build of `Slowcooker.AspNetCore` runs `npm ci && npm run build` only when `ui/index.html` is missing. After changing anything under `dashboard/`, run `npm run build` yourself and rebuild the .NET project, or the embedded UI is stale.
- `dotnet pack` refuses without the compiled UI. That is intentional.
- If the sample crashes at startup with `DirectoryNotFoundException ... wwwroot`, its static web assets manifest is stale: delete `samples/Slowcooker.Samples.WebApi/obj` and rebuild.
- `?theme=light|dark|system` on any dashboard URL overrides the stored theme. For screenshots use headless Chrome:
  `"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --hide-scrollbars --force-device-scale-factor=2 --window-size=1440,900 --virtual-time-budget=8000 --screenshot=out.png "http://localhost:5000/slowcooker/?theme=light"`

## Definition of done

A change is done when all of these hold, and you have run them, not assumed them:

1. `dotnet build` with `TreatWarningsAsErrors` passes and `dotnet format --verify-no-changes` is clean.
2. The relevant test suites pass: `tests/Slowcooker.Tests` always; the storage suites when storage changed; `npm test` and `npm run typecheck` when the dashboard changed.
3. New behaviour has a test. A fixed bug has the test that would have caught it.
4. Docs that describe the changed behaviour are updated in the same commit (README tables, `docs/*.md`, `dashboard/*/README.md`, XML doc comments on public members).
5. `CHANGELOG.md` has a line under the unreleased version for anything a user would notice.
6. For UI changes, you looked at the result (screenshot) in light and dark and at phone width.

## Code rules: .NET

- Style is `.editorconfig` plus the analyzers: file-scoped namespaces, `sealed` by default, nullable enabled, no unexplained empty `catch`. Public members get XML doc comments when the name alone does not explain them.
- Keep the public surface small. A new option needs a default that is right for most people, a row in the README options table and a sentence on when to change it.
- Everything time-related goes through `TimeProvider` so tests can use `FakeTimeProvider`. Never call `DateTimeOffset.UtcNow` directly in library code.
- Storage semantics are defined by `tests/Slowcooker.Storage.ContractTests`. Any change to `IJobStorage` behaviour needs a contract test, and all three providers must pass it. Do not special-case one provider in the core.
- Failure modes are the product. If you touch leases, retries, shutdown, recurring triggering or fetching, add or update a test and a paragraph in `docs/design.md` that names the test.
- Jobs are messages. Nothing may serialize a delegate, a method name or a CLR type name as a contract; `[JobName]` is the contract.
- The dashboard API is read plus operator actions (requeue, delete, trigger). It must never let a browser enqueue arbitrary job types or payloads. Keep the local-only default and the `RequireAuthorization`/`AllowUnauthenticatedRemoteAccess` rule intact; do not add a weaker path.
- `AddAdminUserInterface()` mounts after the application's own pipeline on purpose, so forwarded headers and authentication have run. Do not move it earlier. The branch removes the host's global route builder key before `UseRouting()`; keep that comment and behaviour.
- The embedded UI must work at any mount path and behind a path base: assets are relative, the page gets `<base href>`, the API is found at `./api`. Nothing in the server or the app may hard-code `/slowcooker`.

## Code rules: dashboard

- `dashboard/kit` is a component kit for other people's dashboards. It is unopinionated about looks: semantic markup, `sc-*` classes, no inline styles, all colour, type, radius and motion through the tokens in `kit/styles.css`. Components must render correctly with the stylesheet replaced.
- Changing a kit component's markup or props is a public change: keep existing class names and props working, document in `kit/README.md`, and add or update a test in `kit/src/**/*.test.ts`.
- The kit depends on `vue` only. No UI frameworks, no icon libraries, no CSS frameworks. The admin app may depend on `vue-router` and nothing else at runtime.
- The dashboard is self-contained and works offline: no CDN scripts, no web fonts, no analytics, no external requests other than its own API.
- The admin app's state that a user would want to share lives in the URL (filters, page, selected id). Drawers close on Escape; destructive actions go through the confirm dialog; results and errors surface as toasts.
- The API client is created once in `admin/src/client.ts` from the document's `<base href>`; pages get it through `provideSlowcooker`. Shared `/stats` polling is provided once in `App.vue`; do not add a second poll for the same data.
- Light and dark must both look finished. Add a colour as a token with both values, never as a literal in a component.
- Node is a build-time dependency only. Consumers of the NuGet package never need it, and the kit is never published to npm.

## Commits

- One concern per commit, each leaving the tree building and tests passing. Title in the imperative, under 72 characters, then a body that says what changed and why, with bullets for a multi-part change. Look at `git log` for the house style.
- Keep refactors, behaviour changes and docs in separate commits unless the docs describe the change.
- Never commit `src/Slowcooker.AspNetCore/ui/`, `dashboard/node_modules`, `kit/dist` or packages; `.gitignore` covers them.
- Screenshots in `docs/assets/` are taken from the sample at 2x scale and must not contain local file paths, usernames or real email addresses. The demo traffic uses `example.com`-style addresses; stack traces include local paths, so do not screenshot an open error.
- Do not push, tag or publish unless the user asked for that in this conversation. A tag triggers a NuGet publish.

## Versions and releases

- `Directory.Build.props` holds the next version as `VersionPrefix`. Local builds are `x.y.z-dev`, CI builds `x.y.z-ci.N`; only `.github/workflows/release.yml` produces a release-looking version, from a `vX.Y.Z` or `vX.Y.Z-beta.N` tag or a manual run. Never hand-edit a version to look released.
- A prerelease suffix (`-beta.1`, `-preview.2`, `-rc.1`) publishes a NuGet prerelease and a GitHub pre-release. Stable tags have no suffix.
- The changelog is updated before tagging, with the version and date replacing "unreleased".

## Writing

- Documentation describes behaviour and the reasons for it; it does not advertise. No superlatives, no feature claims that are not implemented and tested.
- The README compares Slowcooker with Hangfire's open-source design because the design was shaped by it. Do not add comparisons with commercial editions or paid features of other libraries anywhere in the repository.
- Keep `README.md` as the user guide, `docs/design.md` as the rationale, `docs/dashboard-api.md` as the HTTP reference and `dashboard/kit/README.md` as the kit reference. Do not duplicate; link.
