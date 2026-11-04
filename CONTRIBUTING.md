# Contributing

Thanks for helping. A few things that keep the project healthy:

* **Run the tests with Docker running.** `dotnet test --solution Slowcooker.sln` starts Postgres and Redis containers through Testcontainers. Without Docker, point the suites at servers with `SLOWCOOKER_TEST_POSTGRES` / `SLOWCOOKER_TEST_REDIS`.
* **Storage changes go through the contract.** Anything that changes `IJobStorage` semantics needs a contract test in `tests/Slowcooker.Storage.ContractTests`, and all three providers must pass it.
* **Failure modes get a test.** If you fix a race, a crash-recovery hole or a scheduling bug, add the test that would have caught it and mention it in `docs/design.md`.
* **Keep the public surface small.** New options need a default that is right for most people and a line in the README table.
* **Style** is enforced by `.editorconfig` and `TreatWarningsAsErrors`. File-scoped namespaces, `sealed` by default, no unexplained `catch {}`.

Open an issue before a large change so the design can be discussed first.
