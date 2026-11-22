# Steady Cosmos DB Suites on the vNext Emulator

Linear: [424-310](https://linear.app/424fun/issue/424-310/flaky-cosmos-db-suites-on-the-vnext-emulator-random-500s-from-its).

Line numbers refer to the tree this plan was written against (`20ef5b5a2`). No application code is changed by this document.

## Problem

The vNext job of `Platform.Storyteller.CosmosDb.yml` failed on every run of [#210](https://github.com/akobr/mono.me/pull/210) (424-287), which changes no code the suites build or test. Each run failed in other tests with other errors, and none of them was a failed assertion on the platform's code. A red job means nothing while that lasts.

The issue names four failures. Its history has more: of the 56 runs of the workflow since 2026-11-11, eight attempts failed, all in the vNext job, for three unrelated causes. None of them is load, and only one is the emulator itself.

| Run (attempt) | Date | Failed | Error | Cause |
| --- | --- | --- | --- | --- |
| 43455632886 (1) | 11-13 | `KeyValueEndToEndTests.Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos` | "The SSL connection could not be established", after 4 tries | A (Lowkey Vault) |
| 43722544756 (1) | 11-15 | no test (all passed) | `Unable to process file command 'env' successfully. Invalid format '.46'` | C (`$GITHUB_ENV`) |
| 44172923243 (1) | 11-17 | the same `Secret_Written…` | the same SSL error | A |
| 44206962726 (1) | 11-18 | no test (all passed) | `Invalid format 'bbbf'` | C |
| 44657121158 (1) | 11-20 | `DbCreator…AccessAuditTests.Build_LeavesCoreWithTimeToLiveOn_AndAnExistingContainerGetsIt` | `DELETE` of a container: `500`, `PostgresError(SqlState(E58P01))` | B (emulator) |
| 44830968139 (1) | 11-20 | `OrganizationsContainerStructureTests.LegacyOrganizationContainer_IsReportedAndRefusedByDbCreator` (and two tests after it) | the same `DELETE` → `500` `E58P01` | B |
| 44830968139 (2) | 11-21 | `CosmosViewServiceTests.CreateView_Twice_ThrowsViewExists`, `Secret_Written…` | `500` "cannot drop portal … while it is being set up"; the SSL error | B, A |
| 44956657238 (1) | 11-21 | `CreateView_Twice_ThrowsViewExists` | `500` "cannot drop portal" | B |

## Current State

### A. Lowkey Vault answers before it serves (4 of 8)

- `Backend.CosmosDb/test/KeyValueEndToEndTests.cs:31-58`: the test class itself is `IAsyncLifetime`, so xUnit starts the vault **for each of its two tests** and disposes it after each. The container is reused (`WithReuse(true)`, its own label), and disposing a reused container **stops** it (Testcontainers 4.16 `DockerContainer.DisposeAsyncCore`: a reused container has no session id, so it is stopped, not removed).
- The order of the two tests is fixed: `FirstStandardSecret_RecordsTheVaultKey` creates the container, `Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos` starts it again. The second test failed in all four runs.
- `LowkeyVaultBuilder` (Testcontainers.LowkeyVault 4.16) waits for the log line `Started LowkeyVaultApp`. `UntilMessageIsLogged` reads the logs since `max(StoppedTime, CreatedTime)` of **the instance**. The second test builds a new instance, which found the stopped container by its reuse hash: its `StoppedTime` is unset and its `CreatedTime` is when the container was first created. So it reads the logs of the first start, finds the line at once and returns while the vault is still starting.
- The test then lives on the Azure SDK's retries (4 tries in about 6 s). The trx files of the runs show it: when the second test passed, it took 6.4–7.1 s (the vault came up during the retries); when it failed, 6.0–6.2 s (the retries ran out first, "unexpected EOF" and "connection reset by peer" from the TLS handshake).
- The same builder is in `DbCreator/testint/DbCreatorFixture.cs:153-156` and `KeyValues.Azure.KeyVault/testint/LowkeyVaultFixture.cs:31-34`, each starting once per run: they meet the bug only when a later local run starts the container that an earlier run stopped. The image `nagyesta/lowkey-vault:7.3.112` is written in all three places.
- Lowkey Vault serves `GET /ping` (`pong`) on both of its ports (`PingController`, `PortSeparationFilter` in 7.3.112).

### B. The vNext emulator replaces its own PostgreSQL (3 of 8)

- `Backend.CosmosDb/test/CosmosTestEmulator.cs:41`: `VNextImage = "…azure-cosmos-emulator:vnext-EN20261019"`, a tag. `Access.Certificates/testint/CosmosTestEmulator.cs` is a copy of the file (another namespace); `DbCreator/testint` links the original.
- The image's entrypoint (`/home/cosmosdev/docker_entrypoint.sh`, line 518) runs `sudo tdnf distro-sync -y` **in the background on every start**. On 2026-11-20 at 01:22 UTC, `postgresql` and `postgresql-libs` 16.16 were published to the Azure Linux 3.0 repository. From then on every new emulator container upgrades 16.15 to 16.16 about a minute after its start, under the running server: a backend that loads `plpgsql.so` after the swap fails (`undefined symbol: heap_datum_update_type`), which the gateway answers as `500 PostgresError(SqlState(E58P01))` (`undefined_file`), and writes fail with "cannot drop portal". Connections that loaded the library before the swap keep working, so the failures hit a few requests at random. Reported as [Azure/azure-cosmos-db-emulator-docker#390](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/390).
- Reproduced on 2026-11-22 with the image the suites ran green on (`sha256:db53613d…`): after 80 s a default container runs PostgreSQL 16.15 with the 16.16 packages installed, and `DO $$ … $$` fails with `could not load library "/usr/lib/postgresql/plpgsql.so"`; a container started with `--add-host packages.microsoft.com:127.0.0.1` keeps 16.15 (its update "completes" with nothing to do) and `plpgsql` works.
- On 2026-11-21 at 02:20 UTC the emulator team published a hotfix (build 20261120.1, `sha256:09dcd04e…`) that turns the update off by default (`ENABLE_PACKAGE_UPDATE`, `--enable-package-update`), to `vnext-latest` and, as MCR shows today, **also to the dated tag `vnext-EN20261019`**: the pin is a tag that moved. The hotfix has its own regression: creating an item that exists answers `500` "cannot drop portal" instead of `409` ([#391](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/391), confirmed by the team, a fix announced for 2026-11-23). That is the failure of `CreateView_Twice_ThrowsViewExists` in both runs after 02:20 UTC (`CosmosViewService.CreateViewAsync` maps a `409` to `ViewExists`); on the hotfix image it fails locally in 3 runs of 3, the other 8 tests of the class pass.
- The image the suites ran green on is still on MCR by its digest (`vnext-EN20261019@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406`, a manifest list for amd64 and arm64). The classic tag `2.14.28` still has the digest of the local image (`sha256:9a4f69121f20…`). Newer vNext tags are previews only (`vnext-EN20261029pre`, `vnext-EN20261117pre`).
- Testcontainers 4.16 parses `name:tag@digest` (`MatchImage`) and pulls by it.
- The classic job passed on the same commits, but on a pull request it runs only the `Emulator=Classic` tests of `Backend.CosmosDb`, so it says little about the two integration suites.

### C. Concurrent writes to `$GITHUB_ENV` (2 of 8)

- Root `version.json:8-17`: `cloudBuild.setVersionVariables: true`. Every project that 42.Monorepo.GitVersioning (the repository's Nerdbank.GitVersioning) versions runs `SetCloudBuildVersionVars` (`build/42.Monorepo.GitVersioning.targets:90-101`), which on GitHub Actions appends `GitBuildVersion`, `GitBuildVersionSimple` and `GitAssemblyInformationalVersion` to the file of `$GITHUB_ENV`.
- Each `dotnet test --project` step builds 20–40 projects on parallel MSBuild nodes, which are separate processes appending to the same file. .NET appends on Unix by writing at the end it saw when it opened the file, not with `O_APPEND`, so two nodes can write over each other. The runner then cannot parse the file and fails the step after its tests passed: `Invalid format 'bbbf'` is the end of `0.10.9-alpha+2f925fbbbf`, `.46` the end of a version.
- No workflow, script or project reads the three variables (searched `.github/`, `*.ps1`, `*.sh`, `*.props`, `*.targets`, `*.cs`).

### The workflow

- `.github/workflows/Platform.Storyteller.CosmosDb.yml:50-89`: the vNext job runs the three suites one per step (one emulator at a time), then DbCreator's unit tests. A run by hand (`workflow_dispatch`, line 26) has no inputs and also runs the classic job over the whole suites (up to 90 minutes). Runs of one ref queue behind each other (`concurrency`, lines 31-33), so ten runs of one commit take ten times as long as one.

## Proposed Changes

### 1. Pin the emulator images by digest

In `CosmosTestEmulator`:

- `VNextImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20261019@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406"`: build 20261019.1, the image the suites ran green on until 2026-11-19, before the hotfix moved the tag.
- `ClassicImage = "…:2.14.28@sha256:9a4f69121f20c1d6502db84625c71ebb441ab61b01880a7b2d3d8031c6dac429"`: the image the classic job runs today.
- The comment says why: a tag of this registry can move, the digest cannot; the tag stays for the reader. Moving to a newer image stays a deliberate change: both values, a run of the suites, and for vNext a check that the new image keeps the package update off.

### 2. Keep the vNext emulator's packages

In `BuildVNext`:

- `.WithExtraHost("packages.microsoft.com", "127.0.0.1")`: the update of the pinned image cannot reach the package repository, fails fast, and PostgreSQL stays the one the image was built and tested with. It is the workaround the reporters of #390 confirmed, and it does not depend on the text of the entrypoint.
- `.WithEnvironment("ENABLE_PACKAGE_UPDATE", "false")`: no effect on the pinned image, but the switch of the hotfix and later images, so the update stays off when the pin moves.
- The comment names #390 and #391 and the symptoms (`E58P01`, "cannot drop portal"), so that the next person who meets them finds the cause.

Both change the container's reuse hash, so a developer's reused container of the old configuration (whose PostgreSQL may already be replaced, and a restart does not restore it) is not picked again; the docs say how to remove it.

### 3. One `CosmosTestEmulator`

`Access.Certificates/testint` links `Backend.CosmosDb/test/CosmosTestEmulator.cs` as `DbCreator/testint` does (`<Compile Include=… Link=…>`), and its copy goes, so the pins live in one file. `CosmosFixture.cs` imports the namespace `_42.Platform.Storyteller.Backend.CosmosDb.UnitTests`. (The review of 2026-11-10 kept two copies because the projects shared no code; DbCreator has linked the file since 2026-11-12, and the pins of this change must not drift.)

### 4. Lowkey Vault: started once, and ready when it serves

- New `Backend.CosmosDb/test/LowkeyVaultTestContainer.cs` (static, namespace `_42.Platform.Storyteller.Backend.CosmosDb.UnitTests`), linked into `DbCreator/testint` and `KeyValues.Azure.KeyVault/testint`:
  - `public const string Image = "nagyesta/lowkey-vault:7.3.112"` (one pin instead of three);
  - `public static LowkeyVaultContainer Build(string suite)`: `LowkeyVaultBuilder(Image)`, `WithReuse(true)`, `WithLabel("net.42for.tests.lowkey-vault", suite)`, and a second wait, `UntilHttpRequestIsSucceeded` on `GET /ping` of port 8443 over TLS (a handler that accepts the vault's self-signed certificate). Wait strategies add up in Testcontainers, so the module's log wait stays and the ping wait follows it; the ping answers only once the vault serves, after a first start and after a restart alike.
- `KeyValueEndToEndTests`: the vault moves to a class fixture, `KeyValueEndToEndVault` (own file, `IAsyncLifetime`: `LOWKEY_VAULT_URL` or `LowkeyVaultTestContainer.Build("backend-cosmosdb-unittests")`, `VaultUri`), taken by `IClassFixture<KeyValueEndToEndVault>` next to the `Startup` of the collection. The vault starts once for the two tests and stops after the class; the per-test `InitializeAsync`/`DisposeAsync` go.
- `DbCreatorFixture.StartVaultAsync` and `LowkeyVaultFixture.InitializeAsync` build with `LowkeyVaultTestContainer.Build(<their labels>)`; `DbCreatorFixture.LowkeyVaultImage` and `LowkeyVaultFixture.Image` go (no other reader).

### 5. No retries of Cosmos DB errors

The issue proposed retrying transient `500`s around container deletes and creates. Both kinds of `500` have a known, non-transient cause (the replaced PostgreSQL, and the hotfix's answer to a duplicate create), and changes 1 and 2 remove it. A retry would only hide the next regression of the emulator, which is what this issue wants a red job to show. So the suites retry nothing new; a `500` of the emulator fails the test as before.

### 6. No cloud build variables of the versioning

Root `version.json`: `"setVersionVariables": false`. Versions are computed as before (the build number of the cloud build and the version itself do not depend on it); only the three unread variables are no longer exported to later steps, and no build process writes to `$GITHUB_ENV` any more. This holds for every workflow that builds .NET projects, not only this one.

### 7. Ten runs of one commit at once

`Platform.Storyteller.CosmosDb.yml`:

- `workflow_dispatch.inputs`: `vnext_runs` (choice `1`, `5`, `10`; default `1`): how many vNext jobs run in parallel on the commit; `classic` (boolean, default `true`): whether the classic job runs.
- The vNext job gets `strategy: { fail-fast: false, matrix: { run: … } }` with the list `[1]`, `[1..5]` or `[1..10]` from `vnext_runs` (`[1]` on a pull request and when called). Its name stays `vNext emulator` for one run and becomes `vNext emulator (n)` for more; the artifact gets the suffix `-n` only then.
- The classic job runs unless a run by hand turned it off (`github.event_name != 'workflow_dispatch' || inputs.classic`).

So "ten runs in a row on an unchanged commit" is one run by hand, about as long as one vNext job.

### 8. Documentation

- `CosmosTestEmulator`: the comments of changes 1 and 2.
- CLAUDE.md: the testing note of the Cosmos DB suites (images pinned by digest, the package update of the vNext emulator blocked, no retries of emulator errors) and the CI/CD entry of the workflow (digests, the two inputs).
- `docs/github/workflows.md`: the entry of the workflow (the inputs).
- `docs/Platform/development.md`, troubleshooting: tests on vNext failing with `E58P01` or "cannot drop portal" on a developer's machine, and how to remove the reused test containers.

### 9. Verification

- Locally (`STORYTELLER_TESTS_USE_CONTAINER=true`, vNext): `Backend.CosmosDb/test` without the `Emulator=Classic` trait, `Access.Certificates/testint`, `DbCreator/testint`, `KeyValues.Azure.KeyVault/testint`; `KeyValueEndToEndTests` twice in a row (the second run restarts the stopped vault).
- The pull request's own run of the workflow, then one run by hand with `vnext_runs=10` and `classic=false` on the branch's last commit: all ten green.

### Out of scope

- The Aspire host's emulator (`Aspire.Host/src/AppHost.cs:23-32`, `RunAsEmulator`, a persistent container) has cause B too: its PostgreSQL is replaced after each start. A follow-up issue.
- Moving to the emulator release the team announced for 2026-11-23: a later, deliberate move of the pin (change 1).
