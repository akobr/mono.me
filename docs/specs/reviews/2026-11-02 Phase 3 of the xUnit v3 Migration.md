# Phase 3 of the xUnit v3 Migration: Linux and the Final Gate

## Overview

Phase 3, the last, of [Migrate All Test Projects to xUnit v3 and Microsoft.Testing.Platform](../2026-11-02%20Migrate%20All%20Test%20Projects%20to%20xUnit%20v3%20and%20Microsoft.Testing.Platform.md) (Linear [424-151](https://linear.app/424fun/issue/424-151/migrate-all-test-projects-to-xunit-v3-and-the-microsofttestingplatform)): `dotnet build src` works on Linux again, the final gate ran on Windows and Linux, and a final review went over the whole branch. One `dotnet test --solution 42.mono.slnx` runs the 29 xUnit assemblies and the TUnit UI suite, 12,643 tests, with the baseline's number of tests in every assembly on both systems; on Windows, one module at a time, all of them pass. `main` was merged in afterwards (section 5). Commits: `c296fe2f` (the `.esproj` target), the fixes of the final review (section 4), the reviews, the merge of `main` and the fixes of the pull request's review (section 5).

## What Was Done

### 1. `obj\Debug` on Linux

- `src/Platform/ui.admin/src/ui.admin.esproj` and `src/Platform/Storyteller/sdk.typescript/src/sdk.typescript.esproj` have a target `_NormalizePackagePropsPath` (`BeforeTargets="GeneratePackageJsonProps"`) that sets `PackagePropsPath` to `$([MSBuild]::NormalizePath('$(MSBuildProjectDirectory)', '$(IntermediateOutputPath)', 'package.g.props'))`, with a comment on why. On Windows the file stays where it was (`obj/Debug/package.g.props`); in WSL the restore no longer creates `obj\Debug`.
- A checkout whose restore ran before this change keeps the folder (it is not under `obj/`, and `dotnet clean` does not remove it), and its traversal builds keep failing until it is deleted once, with its `obj\Release` sibling of a Release build: `rm -rf 'obj\Debug' 'obj\Release'` in `src/Platform/ui.admin/src` and `src/Platform/Storyteller/sdk.typescript/src` (found by the final review, `obj\Release` by CodeRabbit's review of the pull request; the comment of both targets says so). CI runners start from a clean checkout.

### 2. Final gate

- **Build.** Windows: every `bin` and `obj` under `src` deleted (235 folders), then `dotnet build src`: 0 errors; compared by file, line and code with a full rebuild of the baseline clone, the same 1,054 warnings. Linux (WSL Ubuntu 22.04, a fresh clone of `c296fe2f`): `dotnet build src` 0 errors, no `obj\` folder afterwards. The baseline could not build `src` on Linux at all.
- **Tests.** `dotnet test --solution 42.mono.slnx --report-trx --results-directory <dir>` under the baseline conditions (Windows with Docker and `STORYTELLER_TESTS_USE_CONTAINER=true`; Linux without it, so the Cosmos DB fixtures fail fast against an empty `localhost:8081`). Results / failed per assembly (`42.` and `.dll` left out):

  | Assembly | Windows baseline | Windows solution | Windows solution, one module at a time | Linux baseline | Linux solution |
  |---|---|---|---|---|---|
  | `platform.cli.unittests` | 1062 / 0 | 1062 / 0 | 1062 / 0 | 1062 / 0 | 1062 / 0 |
  | `platform.storyteller.access.authkit.integrationtests` | 1 / 0 | 1 / 0 | 1 / 0 | 1 / 0 | 1 / 0 |
  | `platform.storyteller.access.authkit.unittests` | 180 / 0 | 180 / 0 | 180 / 0 | 180 / 0 | 180 / 0 |
  | `platform.storyteller.access.azuread.unittests` | 140 / 0 | 140 / 0 | 140 / 0 | 140 / 0 | 140 / 0 |
  | `platform.storyteller.access.certificates.integrationtests` | 73 / 73 | 73 / 73 | 73 / 0 | 73 / 73 | 73 / 73 |
  | `platform.storyteller.access.certificates.unittests` | 114 / 0 | 114 / 0 | 114 / 0 | 114 / 1 | 114 / 1 |
  | `platform.storyteller.access.federation.unittests` | 472 / 0 | 472 / 0 | 472 / 0 | 472 / 0 | 472 / 0 |
  | `platform.storyteller.access.keycloak.unittests` | 138 / 0 | 138 / 0 | 138 / 0 | 138 / 0 | 138 / 0 |
  | `platform.storyteller.api.functions.integrationtests` | 4 / 0 | 4 / 0 | 4 / 0 | 4 / 0 | 4 / 0 |
  | `platform.storyteller.api.functions.unittests` | 2743 / 0 | 2743 / 0 | 2743 / 0 | 2743 / 0 | 2743 / 0 |
  | `platform.storyteller.aspire.hosting.unittests` | 164 / 0 | 164 / 0 | 164 / 0 | 164 / 0 | 164 / 0 |
  | `platform.storyteller.backend.core.unittests` | 1221 / 0 | 1221 / 0 | 1221 / 0 | 1221 / 0 | 1221 / 0 |
  | `platform.storyteller.backend.cosmosdb.unittests` | 642 / 196 | 642 / 442 | 642 / 0 | 642 / 442 | 642 / 442 |
  | `platform.storyteller.binding.jsone.unittests` | 2429 / 0 | 2429 / 0 | 2429 / 0 | 2429 / 0 | 2429 / 0 |
  | `platform.storyteller.binding.language.unittests` | 192 / 0 | 192 / 0 | 192 / 0 | 192 / 0 | 192 / 0 |
  | `platform.storyteller.binding.object.unittests` | 239 / 2 | 239 / 0 | 239 / 0 | 239 / 0 | 239 / 0 |
  | `platform.storyteller.configuration.keyvault.unittests` | 87 / 0 | 87 / 0 | 87 / 0 | 87 / 0 | 87 / 0 |
  | `platform.storyteller.configuration.unittests` | 641 / 0, 2 skipped | 641 / 0, 2 skipped | 641 / 0, 2 skipped | 641 / 0 | 641 / 0 |
  | `platform.storyteller.configurationformats.unittests` | 416 / 0 | 416 / 0 | 416 / 0 | 416 / 0 | 416 / 0 |
  | `platform.storyteller.hosting.unittests` | 218 / 0 | 218 / 0 | 218 / 0 | 218 / 0 | 218 / 0 |
  | `platform.storyteller.keyvalues.azure.keyvault.integrationtests` | 13 / 2 | 13 / 5 | 13 / 0 | 13 / 2 | 13 / 2 |
  | `platform.storyteller.mcp.integrationtests` | 2 / 0 | 2 / 0 | 2 / 0 | 2 / 0 | 2 / 0 |
  | `platform.storyteller.mcp.unittests` | 728 / 0 | 728 / 0 | 728 / 0 | 728 / 0 | 728 / 0 |
  | `platform.storyteller.sdk.nswag.unittests` | 338 / 0 | 338 / 0 | 338 / 0 | 338 / 0 | 338 / 0 |
  | `platform.ui.admin.automationtests` (TUnit) | — | 6 / 0, 6 skipped | 6 / 0, 6 skipped | — | 6 / 0, 6 skipped |
  | `testing.system.io.abstractions.trace.unittests` | 9 / 0 | 9 / 0 | 9 / 0 | 9 / 0 | 9 / 0 |
  | `testing.ui.automation.reporting.testiny.unittests` | 111 / 0 | 111 / 0 | 111 / 0 | 111 / 0 | 111 / 0 |
  | `testing.ui.automation.runtime.tunit.unittests` | 28 / 0 | 28 / 0 | 28 / 0 | 28 / 0 | 28 / 0 |
  | `testing.ui.automation.runtime.unittests` | 227 / 0 | 227 / 0 | 227 / 0 | 227 / 0 | 227 / 0 |
  | `utils.async.unittests` | 5 / 0 | 5 / 0 | 5 / 0 | 5 / 0 | 5 / 0 |
  | **Total** | **12,637 / 273, 2 skipped** | **12,643 / 520, 8 skipped** | **12,643 / 0, 8 skipped** | **12,637 / 518** | **12,643 / 518, 6 skipped** |

- Every assembly has the baseline's number of tests on both systems; the 6 new tests are the UI suite, skipped without a target, and its module exits 0. Linux has exactly the baseline's results. Exit codes: 2 for the runs with failures, 0 for the Windows run one module at a time (5 m 28 s).
- **Windows, all modules at once** (1 m 17 s; the baseline's `dotnet test src` took about 35 minutes, most of it in the Cosmos module): `Backend.CosmosDb.UnitTests` failed 442 instead of 196, all with "Collection fixture type `Startup` threw in InitializeAsync" (the reused emulator container had restarted and did not serve yet; 424-143 recorded the same 442), and `KeyValues.Azure.KeyVault.IntegrationTests` 5 instead of 2 (Lowkey Vault retries; that container is shared with `KeyValueEndToEndTests` of the Cosmos module, and the module that ends first stops it). MTP starts every module at once, as the traversal's parallel VSTest run did; the infrastructure suites get less time before the others are done. As the spec planned, the run was repeated with `--max-parallel-test-modules 1`: **all 12,643 tests pass** (8 skipped), the first run on this machine in which the Cosmos-backed suites pass in a full run. `CLAUDE.md` names `--max-parallel-test-modules 1` for the Cosmos DB emulator. The cleanup logic of the fixtures is unchanged.
- Not run: the CI workflows themselves (GitHub runs `storyteller-conformance.yml` for the pull request); locally both of its projects pass with `dotnet test --project` (Phase 2).

### 3. Reviews

- One review per phase in `docs/specs/reviews/`: `2026-11-02 Phase 1 of the xUnit v3 Migration.md`, `… Phase 2 …`, `… Phase 3 …`.

### 4. Fixes from the final review

Four read-only reviewers (MSBuild and packages, the C# changes, documentation and completeness against the spec, CI and runtime risk) went over the whole branch, and a skeptic per finding checked each one. Four of four findings held; none was a defect of the build or the tests:

- **The conformance comment.** In `Configuration/test/Configuration.UnitTests.csproj` the script had put `Microsoft.Testing.Extensions.TrxReport` between the comment "Only to validate the conformance files against their schemas." and `NJsonSchema`; it now stands before the comment.
- **The UI suite's terminal lines under `dotnet test`.** `dotnet test` starts a module with `--server dotnettestcli`, keeps its output and prints it only when the module fails, so a passing run shows none of the `ui-automation:` lines (the run id, the seed, the run URLs, the path of `results.jsonl`; checked: none under `dotnet test --project`, also with `--output Detailed`). `automation-tests.md` and the library readme say so and point to `dotnet run` or `results.jsonl`.
- **The `obj\Debug` leftover** of an older Linux restore (section 1).
- **`mrepo build -t` in an area.** The spec named only the repository level. `mrepo` falls back to `dotnet test Directory.Build.proj` in every item folder without a `test` script that has a traversal (`src/Platform`, `src/Libraries` and its subfolders, `src/Platform/Examples`, `src/Texo`, `src/c0ded0c`, `src/Monorepo`, `src/Games`), and MTP mode refuses a traversal ("No test projects were found", exit 1). The repository level already failed before this change (the script runs in the root, the traversal is in `src/`). Project items keep working through their type's `"test": "dotnet test --project test"`. Not changed here: an area script in `mrepo.json` would be inherited by the items below it in `mrepo`'s script tree; see "Follow-ups".

Not taken: nothing; the reviewers' other checks (the four `ValueTask` fixtures, the `TheoryData` rows, `UnixFact`, the catalog, `global.json` on the 10.0.401 image of `ui-admin-automation.yml`, packing of the test projects, which stay `IsPackable=false`) found no defect.

### 5. Merge of `main` and the review of the pull request

- **`main` moved on** while the branch was open (the self-preparing admin UI of the Aspire host, the staging seed, and the UI test pool), and the pull request conflicted in `CLAUDE.md` and `automation-tests.md`; `main` is merged in (`dd09755a`), keeping both sides.
- **The committed UI target.** `main` now defaults the UI suite to the local admin UI, `Target:BaseUrl` `http://localhost:5173`, with a committed pool of 8 users (passwords in the user secrets). So the solution's `dotnet test` runs the UI suite against that target: with nothing on port 5173, the target check fails all 6 UI tests (`net::ERR_CONNECTION_REFUSED at http://localhost:5173/login`) and with them the run. A blank target skips them, exit 0: `UITESTS_Target__BaseUrl= dotnet test --solution 42.mono.slnx` in bash (checked in Git Bash on Windows), `$env:UITESTS_Target__BaseUrl = ' '` in PowerShell, which removes an empty variable (checked; `UiAutomationSettings.IsConfigured` treats a blank `BaseUrl` as none). `CLAUDE.md`, `automation-tests.md` and the suite's README say so. **Deviation:** the spec (and the issue) expected the plain solution run to skip the suite without configuration; the owner's later default makes that an explicit blank target.
- **After the merge** (Windows, Docker, `STORYTELLER_TESTS_USE_CONTAINER=true`, blank UI target, one module at a time): 12,646 tests, the 3 more being `main`'s new `UI.Automation.Runtime.UnitTests` (230, as `main` under VSTest); every assembly without infrastructure passed and the UI suite skipped its 6. The Cosmos DB emulator container crashed during that run and twice in the reruns of the two Cosmos-backed projects (core dumps in the container's log, as recorded in 424-143), so `Backend.CosmosDb.UnitTests` (206 failed, 203 in the rerun) and `Access.Certificates.IntegrationTests` (73) failed only with connection errors ("No connection could be made because the target machine actively refused it", "Collection fixture … threw in InitializeAsync"); no assertion failed. They passed in full before the merge (section 2).
- **GitHub checks** of the pull request: `storyteller-conformance.yml` on `ubuntu-latest` and `windows-latest` ran both projects with `dotnet test --project` on MTP (416 and 641 passed on Linux), the TypeScript SDK and the admin UI jobs passed.
- **CodeRabbit's review** of the pull request, taken: the solution command in `CLAUDE.md` runs one module at a time (`--max-parallel-test-modules 1`), with the faster parallel run as an alternative, because the Cosmos DB suites share one emulator and delete its databases and two suites share Lowkey Vault; the cleanup in the `.esproj` comments names `obj\Release` too. Not taken: isolating the Cosmos DB databases and the Lowkey Vault container per module, which the spec leaves out of scope (the fixtures keep their behaviour); it stays a follow-up.

## Follow-ups

- `mrepo build -t` in an area: a `dotnet build Directory.Build.proj -t:Test -p:SkipNonexistentTargets=true` script (MTP's own `Test` target, probed in Phase 1 of the spec), a solution filter per area, or traversal support in `mrepo`'s `BuildCommand` until .NET 11 lets `dotnet test` take traversals.
- The `mrepo` scaffolding (`ProjectTestCsprojT4`, `RootDirectoryPackagesPropsT4`, `GlobalJsonT4`, `MrepoJsonT4`, the `mrepo.schema.json` example) still generates xUnit v2 on VSTest; a `tests-xunit` project made with `mrepo new project` in this repository would not restore now.
- Pass `TestContext.Current.CancellationToken` in the tests and raise `xUnit1051` back to a warning.
- Give the Cosmos DB and Lowkey Vault fixtures their own databases and containers per module, so the infrastructure suites pass with all modules at once.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` ("CI") and the comment in `ui-admin-automation.yml` say that `dotnet run --no-build` runs no build targets in the step with the secrets; `dotnet run` still runs `ComputeRunArguments` and the targets hooked to it. The wording predates this change.
- The race of `Crumble/SourceGenerator.Orleans` in a parallel build (Phase 1, section 8).
- On .NET 11: `dotnet test` of the traversals (`IsTraversal`), and a run that does not fail for one module without tests.
