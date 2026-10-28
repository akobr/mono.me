# Phase 4 of Shared Conformance Vectors for Configuration Providers and SDKs

Linear: [424-105](https://linear.app/424fun/issue/424-105/shared-conformance-vectors-for-configuration-providers-and-sdks-c22).

## Overview

Phase 4 covers sections 7 to 9 of [Shared Conformance Vectors for Configuration Providers and SDKs](../2026-10-28%20Shared%20Conformance%20Vectors%20for%20Configuration%20Providers%20and%20SDKs.md).

- **CI.** A reusable workflow runs the suite: both .NET test projects on Linux and Windows, and the TypeScript SDK.
- **Release.** A hand-run release workflow calls it as its gate, packs the provider's NuGet closure and the npm package once, and publishes those files from `main`. It is a dry run unless `publish` is set.
- **Docs.** The suite has its page, and the formats, provider, SDK, folder and `CLAUDE.md` docs point to it.

The review found that the Linux leg of the .NET job would have failed every run, because the provider's mock file system could not create a directory with a Unix mode. That is fixed. **The first Linux run is still pending:** the new workflows can only run once they reach `main`, or from a pull request into `main`.

| Commit | Content |
|---|---|
| `3b646551` | Both workflows, `docs/Platform/Storyteller/conformance.md`, `conformance/README.md`, the SDK README, the provider and formats docs, `CLAUDE.md`, the spec's U+2028 and U+2029 escapes |
| `5c49e8bf` | Review fix: `UnixModeMockFileSystem` for the cache on Linux |
| `bd98cbd7` | Review fix: the docs say how the rules order errors, how keys fold their case, and which documents the flattening files share; the spec's `#` escape |

## What Was Done

### 7. The conformance workflow

`.github/workflows/storyteller-conformance.yml`, "Storyteller conformance", follows the spec.

- **Triggers.**
  - `push` and `pull_request` to `main`, filtered to the suite, the five projects the runners build, `System.IO.Abstractions.Trace`, the SDK, the build settings and the workflow file;
  - `workflow_dispatch`;
  - `workflow_call`.
- **Settings.**
  - `concurrency` is `storyteller-conformance-${{ github.workflow }}-${{ github.ref }}`, with `cancel-in-progress` only for pull requests.
  - `permissions` are `contents: read`.
  - The default shell is bash.
- **`dotnet`**, ".NET reference", is a matrix of `ubuntu-latest` and `windows-latest` with `fail-fast: false`.
  - It checks out with `fetch-depth: 0` (NBGV) and `persist-credentials: false`.
  - It runs `setup-dotnet@v4` from `global.json`.
  - It runs `dotnet test` for `ConfigurationFormats.UnitTests.csproj` and `Configuration.UnitTests.csproj`.
- **`typescript`**, "TypeScript SDK", runs `setup-node@v4` from the SDK's `.nvmrc` with the npm cache on its lockfile, then npm 11, then `npm ci`, `npm run typecheck` and `npm test`.

**Review fix (`5c49e8bf`).** On Linux and macOS, `LastKnownGoodCache.CreateDirectory` calls `Directory.CreateDirectory(path, UnixFileMode)`. TestableIO's `MockDirectory` throws `NotImplementedException` for that overload, which a probe of 22.1.0 confirmed. The other Unix calls of the cache work under the mock:

- `FileStreamOptions.UnixCreateMode` is ignored by `MockFileStreamFactory`;
- `DirectoryInfo.UnixFileMode` returns `None`.

So every cache and engine test with a cache directory would have failed on Linux, and with it the gate. `Configuration/test/UnixModeMockFileSystem.cs` is a `MockFileSystem` whose `Directory` implements the overload by creating the directory. It does not keep the mode, which the `[UnixFact]` tests check on the real file system. `EngineFixture`, `LastKnownGoodCacheTests` and `TestOptions` use it, and `UnixModeMockFileSystemTests` exercises the overload on every OS. The production code is unchanged.

**Not verified.**

- **Linux.** The Linux leg has not run. The machine has no .NET or Node in WSL and no running Docker. The workflows are not on `main`, so they cannot be dispatched, and this branch's pull request targets `feat/424-104`. The `[UnixFact]` tests and the other non-Windows paths of the provider will run for the first time when the stack reaches `main`, or in a pull request into `main`.
- **actionlint** was not available, and nothing was downloaded to get it. Both files parse with the `yaml` package, and their semantics were reviewed against the GitHub documentation.

### 8. The release workflow and its gate

`.github/workflows/storyteller-sdk-release.yml` follows the spec.

- **Inputs.** `packages` (`all`, `nuget` or `npm`) and `publish` (default `false`). `run-name` names the packages and the mode.
- **Settings.** `concurrency: storyteller-sdk-release` without cancel, and `contents: read`.
- **`conformance`** is the called workflow.
- **`pack`** (`needs: conformance`):
  - it refuses to publish from any ref other than `main`;
  - it checks out the full history;
  - it packs `Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Sdk.NSwag`, `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault` into `out/nuget`, with `-c Release -p:ContinuousIntegrationBuild=true`;
  - it runs `npm ci` and checks that the version is not on npm yet (when publishing), then runs `npm run build` and `npm pack` into `out/npm`;
  - it uploads `out/` as `storyteller-sdk-packages`.
- **`publish-nuget`** and **`publish-npm`** need `pack` and run only with `publish` and the matching `packages`. Both use the environment `storyteller-sdk`, which holds `NUGET_API_KEY` and `NPM_TOKEN`, and push the downloaded files:
  - `dotnet nuget push … --skip-duplicate`;
  - `npm publish out/npm/*.tgz --access public` with `NODE_AUTH_TOKEN`.

The six packages packed locally on Windows with that command, as `0.10.x-alpha-g<sha>` off `main`. The workflow has not run.

**Deviations.**

- The workflow is named "Storyteller SDK release", like "Storyteller action release", not "Release the Storyteller SDKs".
- `publish-npm` checks out the repository, only to read the SDK's `.nvmrc`, and installs npm 11.
- Both publish jobs fail first with an error that names the secret and the environment when the secret is empty.
- `publish-nuget` installs `10.0.x` instead of reading `global.json`, because it has no checkout.
- `pack` sets up .NET or Node only for the packages it packs.

**Still the owner's:** the environment `storyteller-sdk`, its required reviewers and its two secrets, the first release, and npm provenance.

### 9. Documentation

- **`docs/Platform/Storyteller/conformance.md`** (new) covers the layout, the manifest, identity, versions, each kind with an example, the response model, portability and numbers, the runners and what each does not run, CI and the gate, and adopting the suite in another language.
  - **Review fix (`bd98cbd7`).** It says that errors are found rule group by rule group, each in document order (as the vectors pin), not in plain document order. It says that `properties.json` and `env.json` hold every document of `flat.json` and more. It adds the ascending order of array-index names, and how keys fold their case. The `error` description of `flattening.schema.json` changed the same way.
- **`conformance/README.md`** (new) has the layout, the commands and a link to the page.
- **`configuration-formats.md`**:
  - "Test vectors" names the new files, their targets and the TypeScript runner;
  - **review fix:** the `flat` section named the removed `flattening.json`.
- **`configuration-provider.md`** links the suite from "Flattening", from the failure classes and from "Refresh".
- **`sdk.typescript/README.md`** has the section "Configuration formats and errors" with the helpers, an example and the number limits, and links `isTransientError` from the conditional reads.
- **`CLAUDE.md`** has a Platform row for `Storyteller/conformance/` and both workflows under CI/CD.
- **The spec** lost two escapes to the editor that wrote it: `\u2028` and `\u2029` in section 6 (restored in `3b646551`), and `\u0023` in section 6 (restored in `bd98cbd7`). These restore the text as it was planned; nothing else in the spec changed after implementation began.

### The review of the branch

Four reviewers looked at the whole range: the .NET runners, the TypeScript port against the .NET library (a differential fuzz of several thousand documents), the workflows, and docs consistency. A skeptic per dimension then tried to refute each finding.

**Confirmed and fixed:**

- the Linux mock file system (above);
- the missing kind check of the .NET reference (phase 1);
- the case fold and the canonical document of the TypeScript port (phase 3);
- the order of the flat keys in the .NET runner (phase 1);
- the doc wording about error order, shared documents and the order rule, and the `#` comment (phases 1, 3 and 4);
- the stale `flattening.json` reference;
- the weaker checks of `AtRefresh` (phase 2).

**Refuted:**

- That the one-request assertion contradicts the docs. The docs say the runners run without retries. It was hardened anyway (phase 2).
- That `1e400` breaks portability. It differs only from `DocumentFlattener.Flatten`, never from the formatter, and no vector holds it.

The spec deviations the reviewers listed are recorded on these pages.
