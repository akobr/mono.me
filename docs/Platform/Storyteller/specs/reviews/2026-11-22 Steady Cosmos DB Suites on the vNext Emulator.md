# Steady Cosmos DB Suites on the vNext Emulator

## Overview

This review covers the whole of [the spec](../2026-11-22%20Steady%20Cosmos%20DB%20Suites%20on%20the%20vNext%20Emulator.md) (Linear [424-310](https://linear.app/424fun/issue/424-310/flaky-cosmos-db-suites-on-the-vnext-emulator-random-500s-from-its)), delivered in one phase. The eight failed attempts of the vNext job since 2026-11-11 had three causes, none of them load and none in the platform's code: Lowkey Vault answering before it served (our test setup), the vNext emulator replacing its own PostgreSQL and then a moved image tag (the emulator), and parallel build nodes corrupting `$GITHUB_ENV` (our versioning settings). Each cause was reproduced locally and removed at its source; the suites retry nothing new.

## What Was Done

### 1. The emulator images pinned by digest

`Backend.CosmosDb/test/CosmosTestEmulator.cs`:

- `VNextImage`: `…azure-cosmos-emulator:vnext-EN20261019@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406` (build 20261019.1). The tag alone now names the hotfix build 20261120.1 (`sha256:09dcd04e…`), checked on MCR on 2026-11-22.
- `ClassicImage`: `…:2.14.28@sha256:9a4f69121f20c1d6502db84625c71ebb441ab61b01880a7b2d3d8031c6dac429`, the digest of the tag today and of the image the classic job has run.
- The comment says why a tag is not enough here (the moved tag and #391) and what moving the pin needs.

The hotfix's regression was reproduced with the suite before the change: with `VNextImage` pointed at `sha256:09dcd04e…` (a local edit, not committed), `CosmosViewServiceTests` failed `CreateView_Twice_ThrowsViewExists` in 3 runs of 3 with `500` "cannot drop portal … while it is being set up"; the other 8 tests passed.

### 2. The vNext emulator keeps its packages

`BuildVNext` adds `.WithExtraHost("packages.microsoft.com", "127.0.0.1")` and `.WithEnvironment("ENABLE_PACKAGE_UPDATE", "false")`, with a comment that names #390, the symptoms and why no `500` is retried.

Reproduced on 2026-11-22 with two throwaway containers of the pinned digest, started together:

| | after 80 s | `DO $$ BEGIN PERFORM 1; END $$` in its PostgreSQL |
| --- | --- | --- |
| default | `postgresql-16.16-1.azl3` installed under the running 16.15 (`/logs/package-updates.log`: `Installing/Updating: postgresql-16.16…`) | `could not load library "/usr/lib/postgresql/plpgsql.so": … undefined symbol: heap_datum_update_type` |
| `--add-host packages.microsoft.com:127.0.0.1` | `postgresql-16.15-1.azl3`, the update "completed" with nothing to do | `DO` |

The test containers of the local runs below kept `postgresql-16.15-1.azl3` for the whole run.

### 3. One `CosmosTestEmulator`

`Access.Certificates/testint/CosmosTestEmulator.cs` is gone; `Access.Certificates.IntegrationTests.csproj` links the file of `Backend.CosmosDb/test` as `DbCreator/testint` does, and `CosmosFixture.cs` imports `_42.Platform.Storyteller.Backend.CosmosDb.UnitTests`. The copy differed only in its namespace and the place of two comments.

### 4. Lowkey Vault started once and ready when it serves

- New `Backend.CosmosDb/test/LowkeyVaultTestContainer.cs` (static): `Image` (`nagyesta/lowkey-vault:7.3.112`, before written in three places), `ReuseLabel`, and `Build(suite)`: the module's builder with reuse and the suite's label, plus `UntilHttpRequestIsSucceeded` on `GET /ping` of port 8443 over TLS with a handler that accepts the vault's certificate. Linked into `DbCreator/testint` and `KeyValues.Azure.KeyVault/testint`.
- New `Backend.CosmosDb/test/KeyValueEndToEndVault.cs`: the class fixture of `KeyValueEndToEndTests` (`LOWKEY_VAULT_URL`, or the container with the label `backend-cosmosdb-unittests`; disposing it stops the reused container after the class). `KeyValueEndToEndTests` takes it through `IClassFixture<KeyValueEndToEndVault>` next to `Startup`, and keeps `_vaultUri` as a field read from the fixture, so its two tests are unchanged; its own `IAsyncLifetime` is gone.
- `DbCreatorFixture` (`LowkeyVaultImage` removed) and `LowkeyVaultFixture` (`Image` removed) build with `LowkeyVaultTestContainer.Build` and their labels as before.

The cause was reproduced outside the repository with a small program on Testcontainers.LowkeyVault 4.16 that starts a reused vault three times, disposing (stopping) it after each start:

| Wait | start 1 | start 2 | start 3 |
| --- | --- | --- | --- |
| the module's (log line) | ready after 4.8 s, `/ping`: `pong` | ready after 0.3 s, `/ping`: "The SSL connection could not be established" | the same |
| plus `/ping` | 4.8 s, `pong` | 4.4 s, `pong` | 4.4 s, `pong` |

### 5. No retries of Cosmos DB errors

As planned: no test retries a `500` of the emulator. The fixtures' delete of the database at their start keeps its Polly retry of any error (`Startup`, `CosmosFixture`, `DbCreatorFixture`, unchanged); the comment of `BuildVNext` and CLAUDE.md say so (the review of the branch found the first wording, "the suites retry no 500", too broad).

### 6. No cloud build variables of the versioning

Root `version.json`: `"setVersionVariables": false`, with a comment. Checked with `dotnet msbuild -t:GetBuildVersion` and `GITHUB_ACTIONS=true` pointing `GITHUB_ENV` at a file: with the setting committed as `true` the three variables are written, with `false` nothing. A temporary commit of the change gave the same versions (`Backend.Core` `0.10.57-alpha`, `CLI/Toolkit` `1.0.3`, root projects `0.1.0` with the height one up for the extra commit), so heights and releases are unaffected. (An uncommitted change of the setting still writes the variables: the versioning reads it from the committed `version.json`.)

### 7. Ten runs of one commit at once

`Platform.Storyteller.CosmosDb.yml`: inputs `vnext_runs` (choice `1`/`5`/`10`) and `classic` (boolean) on `workflow_dispatch`, and the same on `workflow_call` (`vnext_runs` as a string there). The vNext job has a matrix `run` from `fromJSON(...)` (`[1]` without the input, so on a pull request), `fail-fast: false`, the name `vNext emulator` for one job and `vNext emulator (n)` for more, and the artifact suffix `-n` only for more. The classic job runs when `inputs.classic` is true or there are no inputs (`!inputs.vnext_runs`: only a pull request has none). The plan's `github.event_name != 'workflow_dispatch' || inputs.classic`, and the first version of the branch with `github.event_name == 'pull_request'`, would have ignored `classic: false` in a call from a workflow that a pull request started, since a called workflow sees its caller's event (found in the review of the branch). `actionlint` reports nothing new (only the existing SC2086 note on the deliberate word splitting of `$TRAIT_FILTER`).

The first run by hand showed the names: ten jobs `vNext emulator (1)` … `(10)` and `Classic emulator` skipped.

### 8. Documentation

- CLAUDE.md: the testing note of the Cosmos DB suites (digests, the blocked update, no retries, `LowkeyVaultTestContainer`) and the CI/CD entry of the workflow (digests, the inputs).
- `docs/github/workflows.md`: the entry of the workflow, with the command of a ten-run check.
- `docs/Platform/development.md`, troubleshooting: tests failing with `E58P01` or "cannot drop portal", and how to remove the old reused containers.

### 9. Verification

Locally (Docker Desktop, 8 CPUs, 11.7 GB; `STORYTELLER_TESTS_USE_CONTAINER=true`, vNext), on the final code:

| Suite | Result |
| --- | --- |
| `Backend.CosmosDb/test`, without `Emulator=Classic` | 977 of 977 passed (6 min 30 s) |
| `Access.Certificates/testint` | 101 of 101 passed |
| `DbCreator/testint` (Lowkey Vault for the `Standard` pairs, restarting the suite's stopped vault of an earlier run) | 35 of 35 passed (7 min 23 s) |
| `KeyValues.Azure.KeyVault/testint`, twice (the second run restarts the stopped vault) | 13 of 13, both runs |
| `KeyValueEndToEndTests`, twice | 2 of 2, both runs |

After the run each of the three emulator containers still had `postgresql-16.15-1.azl3`, with "Nothing to do. Package updates completed" in its update log.

In CI, on 2026-11-22:

| Run | Commit | Result |
| --- | --- | --- |
| [44970544370](https://github.com/akobr/mono.me/actions/runs/44970544370), by hand, `vnext_runs=10`, `classic=false` | `cbace0cb7`, the last commit of the code | **10 of 10 vNext jobs green**, run at once on ten runners, 18–27 min each; `Classic emulator` skipped |
| [44970706932](https://github.com/akobr/mono.me/actions/runs/44970706932), the pull request ([#211](https://github.com/akobr/mono.me/pull/211)) | `f105607aa` (this review's first version, same code) | `vNext emulator` green (25 min), `Classic emulator` green; the check keeps its name `vNext emulator` |

So the acceptance criterion of ten green runs of the vNext job on an unchanged commit holds, as ten runs at once rather than one after another (each on its own runner, as a run of the job always is). None of the 11 jobs failed a step on `$GITHUB_ENV`.

A review of the branch by three independent reviewers (the workflow, the test code, the versioning and the docs), each finding checked by a skeptic, confirmed two defects of the branch, both fixed: the `if` of the classic job in a call (section 7) and the wording of the no-retry claim (section 5). Two findings were outside this change and are follow-ups below.

### Follow-ups

- The Aspire host's `cosmos` container (`Aspire.Host/src/AppHost.cs`, `RunAsEmulator`, persistent) has cause B too: on this machine it ran `postgresql-16.16` under 16.15 after its start. It needs the same block (`WithContainerRuntimeArgs("--add-host", …)`, `ENABLE_PACKAGE_UPDATE=false`) and a pinned image, or the emulator release announced for 2026-11-23; a separate issue.
- When the emulator team releases a fixed vNext image, move `VNextImage` on purpose (change 1) and check that its package update stays off.
- The classic emulator's readiness (`BuildClassic`) is a log wait too, so it has cause A's blind spot: a stopped reused classic container (e.g. after a restart of Docker) is reported ready at once, and the fixtures' first requests fail with the SSL error while it starts. Only local runs meet it (CI starts fresh containers, and the fixtures never stop the emulator); remove the stopped container. Not changed here: a sound check needs a marker of the classic image's own start, which wants a run of that image to settle.
- `mrepo`'s template of a root `version.json` (`src/Monorepo/Cli/src/Templates/RootVersionJsonT4.tt`) and `docs/Monorepo/versioning.md` still set `setVersionVariables: true`, so a new repository scaffolded by `mrepo` can meet cause C. A decision for `mrepo`.
