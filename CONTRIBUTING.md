# Contributing

Thanks for helping. A few things that keep the project healthy:

* **Run the tests with Docker running.** `dotnet test --solution Slowcooker.sln` starts Postgres and Redis containers through Testcontainers. Without Docker, point the suites at servers with `SLOWCOOKER_TEST_POSTGRES` / `SLOWCOOKER_TEST_REDIS`.
* **Storage changes go through the contract.** Anything that changes `IJobStorage` semantics needs a contract test in `tests/Slowcooker.Storage.ContractTests`, and all three providers must pass it.
* **Failure modes get a test.** If you fix a race, a crash-recovery hole or a scheduling bug, add the test that would have caught it and mention it in `docs/design.md`.
* **Keep the public surface small.** New options need a default that is right for most people and a line in the README table.
* **Style** is enforced by `.editorconfig` and `TreatWarningsAsErrors`. File-scoped namespaces, `sealed` by default, no unexplained `catch {}`.

Open an issue before a large change so the design can be discussed first.

## Releasing

Packages are published by `.github/workflows/release.yml`. Versions are SemVer 2; a suffix makes it a prerelease.

```bash
# update CHANGELOG.md, then one of:
git tag v0.2.0-beta.1 && git push origin v0.2.0-beta.1    # beta / preview / rc: NuGet prerelease, GitHub pre-release
git tag v0.2.0 && git push origin v0.2.0                  # stable
```

A preview can also be published from any branch without tagging first: Actions > Release > Run workflow, enter a version such as `0.2.0-preview.1`, and the workflow tags that commit. Either way it runs the tests, packs every project under `src/` with that version, pushes to NuGet with trusted publishing (no stored key: the run's OIDC token is exchanged for a one-hour key; needs a trusted publishing policy on nuget.org for `release.yml` and the `nuget` environment, and a `NUGET_USER` repository variable holding the nuget.org profile name; reviewers on the `nuget` environment can gate it) and creates a GitHub release with the packages attached and generated notes.

Between releases `Directory.Build.props` holds the next version as `VersionPrefix`; local builds are `x.y.z-dev` and CI builds `x.y.z-ci.N` (downloadable from each CI run), so only the workflow produces release-looking versions. Prerelease packages are installed with `dotnet add package Slowcooker --prerelease`.

CI also checks formatting (`dotnet format --verify-no-changes`) and that every package packs; run `dotnet format` before pushing.
