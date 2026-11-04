# Contributing

Thanks for helping. A few things that keep the project healthy:

* **Run the tests with Docker running.** `dotnet test --solution Slowcooker.sln` starts Postgres and Redis containers through Testcontainers. Without Docker, point the suites at servers with `SLOWCOOKER_TEST_POSTGRES` / `SLOWCOOKER_TEST_REDIS`.
* **Storage changes go through the contract.** Anything that changes `IJobStorage` semantics needs a contract test in `tests/Slowcooker.Storage.ContractTests`, and all three providers must pass it.
* **Failure modes get a test.** If you fix a race, a crash-recovery hole or a scheduling bug, add the test that would have caught it and mention it in `docs/design.md`.
* **Keep the public surface small.** New options need a default that is right for most people and a line in the README table.
* **Style** is enforced by `.editorconfig` and `TreatWarningsAsErrors`. File-scoped namespaces, `sealed` by default, no unexplained `catch {}`.

Open an issue before a large change so the design can be discussed first.

## Releasing

Packages are published by `.github/workflows/release.yml` when a version tag is pushed:

```bash
# update CHANGELOG.md, then
git tag v0.2.0 && git push origin v0.2.0
```

The workflow runs the tests, packs every project under `src/` with that version, pushes to NuGet (`NUGET_API_KEY` secret) and creates a GitHub release with the packages attached. Between releases `Directory.Build.props` holds the next version as `VersionPrefix`.

CI also checks formatting (`dotnet format --verify-no-changes`) and that every package packs; run `dotnet format` before pushing.
