# Upgrade All NuGet Packages and Move to .NET 10

Linear: [424-143 Upgrade all NuGet packages to the latest versions and move every project to .NET 10](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)

This spec lives in `docs/specs/` because the change covers the whole `src/` tree (all areas, not only the Platform). Its reviews go to `docs/specs/reviews/`, one per phase.

## Problem

The repository is several months behind on its dependencies:

- `Directory.Packages.props` has 170 entries. About 110 of them have a newer version on nuget.org, nine of them a newer major (Aspire's own line is behind by five minors, Orleans by four).
- Restore reports NuGet audit warnings (`NU1902`, `NU1903`) for vulnerable transitive packages: `MessagePack` 2.5.192, `System.Security.Cryptography.Pkcs` 8.0.1 and 10.0.10, `Microsoft.AspNetCore.Http` 2.1.0, `Newtonsoft.Json` 11.0.2 and `SSH.NET` 2025.1.0.
- Two test projects still target `net6.0`. `Microsoft.NET.Test.Sdk` 18.x refuses them, so `dotnet build src` has failed for weeks.
- `global.json` pins SDK 10.0.201 with `rollForward: latestMajor` and `allowPrerelease: true`, so a machine with a .NET 11 preview SDK builds with it. `mrepo.json` and the `mrepo` scaffolding still default to .NET 9, and the `Repo.Generator` template to `net6.0`.
- PR [akobr/mono.me#60](https://github.com/akobr/mono.me/pull/60) (security patches, .NET 10 servicing and minor bumps, 2026-10-08) is still open and out of date with `main`.

Backward compatibility of our own APIs is not a concern (nothing is in production or used by external systems). Breaking package changes are fixed in our code.

## Current State

### Baseline

- The branch `feat/424-143` starts from `origin/main` (`87892691`) with `chore/claude/deps-2026-10-08` (PR #60) merged into it (`caeb70a4`). The conflicts in `Directory.Packages.props` were resolved per package: the higher version wins, entries `main` added stay, entries `main` removed (`JsonSubTypes`, the six `Microsoft.Kiota.*`, `RestSharp`) stay removed. PR #60's `sdk.typescript/src/package-lock.json` of the old SDK path stays deleted. PR #60 also brought the `MessagePack` 2.5.307 pin into both AppHosts and a refreshed `docs/42for.net/pnpm-lock.yaml`.
- The merged baseline was built with SDK 10.0.403 (the newest .NET 10 SDK, runtime 10.0.14, compiler Roslyn 5.9.0):
  - `dotnet build src`: 3 errors. `Microsoft.NET.Test.Sdk doesn't support net6.0` in `src/Monorepo/Repo.Generator/test` and `src/Platform/Supervisor/Abstraction/test`, and on a clean tree `CS0246 IChainedCrumbsExecutor` in `src/Crumble/Playground/Examples` (an incremental build passes, see "Crumble").
  - Audit (the `logs` of every `obj/project.assets.json`): `MessagePack` 2.5.192 (13 advisories) in `Storyteller/Aspire.Hosting` src and test, which have no pin; `System.Security.Cryptography.Pkcs` 10.0.10 in `Storyteller/Api.Web` (through `Microsoft.Identity.Web.TokenCache` 4.16.0) and 8.0.1 in `c0ded0c/Cli` (through `NuGet.Packaging`); `Microsoft.AspNetCore.Http` 2.1.0 and `Newtonsoft.Json` 11.0.2 in `Scheduler/Api.Functions` and `Supervisor/Api.Functions`; `SSH.NET` 2025.1.0 in `KeyValues.Azure.KeyVault/testint` (through `Testcontainers` 4.11.0).
  - `NU1510` ("will not be pruned") for in-box packages referenced by `net10.0` projects: `System.Text.Json` (`Abstractions.Access`, `Abstractions.Annotations`, `c0ded0c/Core`), `System.Collections.Immutable` (`c0ded0c/Core`, `c0ded0c/MsBuild`), `System.Diagnostics.DiagnosticSource` (`Crumble/Runtime.Core`), `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` (`Api.Web`), `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Logging.Console` (`Crumble/Playground/Orleans.Silo`), `Microsoft.CSharp` (`Repo.Generator`). `NU1701` for `Microsoft.IO.Redist` and `Microsoft.VisualStudio.SolutionPersistence` in `Libraries/Roslyn/Compose` (`netstandard2.0`), because `Microsoft.CodeAnalysis.Workspaces.MSBuild` has no `netstandard2.0` asset.
  - `dotnet test src --no-build` with `STORYTELLER_TESTS_USE_CONTAINER=true` and Docker: 27 test assemblies ran. All pass except `Backend.CosmosDb.UnitTests` (442 of 642 failed), `Access.Certificates.IntegrationTests` (73 of 73), `KeyValues.Azure.KeyVault.IntegrationTests` (1 of 13: the end-to-end test against the vault) — every failure "The SSL connection could not be established" against the Cosmos emulator container or Lowkey Vault, i.e. the local container environment — and one timing test in `Binding.Object.UnitTests` (`AttackCorpusTests.Vector_StopsOnLimit`, 3.06 s against a 2 s limit under the parallel load). The two `net6.0` projects did not build.

### SDK and repository settings

- `global.json`: `sdk.version` 10.0.201, `rollForward: latestMajor`, `allowPrerelease: true`; `msbuild-sdks` `Microsoft.Build.Traversal` 4.1.0 (the newest is 4.1.82).
- `.github/workflows/storyteller-conformance.yml` and `storyteller-sdk-release.yml` install the SDK with `actions/setup-dotnet@v4` and `global-json-file: global.json`.
- `.github/workflows/ui-admin-automation.yml` builds inside `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`, whose SDK must satisfy `global.json`. That image (built 2026-09-22) ships SDK **10.0.401** (`DOTNET_SDK_VERSION` of the image config). It cannot move to a newer Playwright image (see "Packages held back").
- `mrepo.json:42` `custom.dotnetVersion` is 9 (`CLAUDE.md:360` calls it informational). `src/Monorepo/Cli/src/Configuration/BasicOptionsExtensions.cs:39-40` defaults to 9. `GlobalJsonT4.tt` writes SDK 9.0.300. `RootDirectoryPackagesPropsT4.tt` writes old test package versions. `src/Monorepo/Repo.Generator/src/Templates/ProjectCsprojT4.tt:10` hardcodes `net6.0`. The `.cs` files next to the `.tt` files are committed preprocessor output.

### Target frameworks

`grep -r "<TargetFramework" --include=*.csproj src`: 101 projects on `net10.0`, and

- `net6.0`: `src/Monorepo/Repo.Generator/test/Repo.Generator.Tests.csproj`, `src/Platform/Supervisor/Abstraction/test/Abstraction.Tests.csproj`;
- `netstandard2.0`: `Crumble/SourceGenerator.Orleans` (a Roslyn source generator), `Libraries/Functional/Monads`, `Monads.Trees`, `Libraries/Roslyn/Compose`;
- `netstandard2.1`: `Texo/Core.Markdown`;
- `net10.0;netstandard2.0`: `Crumble/Abstractions`, `Crumble/Volume.Abstractions`; `net10.0;netstandard2.1`: `Libraries/Utils/Async`.

The owner's comment on the issue: projects that target `netstandard` stay on `netstandard`, and Crumble (source generators) is fragile, so it needs care.

### Crumble and its source generator

- `SourceGenerator.Orleans` (`netstandard2.0`, `IIncrementalGenerator`) references `Microsoft.CodeAnalysis.CSharp` and `.Analyzers` 5.3.0 (central). A generator compiled against Roslyn X loads only in compilers ≥ X (otherwise `CS9057`, the generator does not run and `Examples` loses `IChainedCrumbsExecutor` and `AddCrumbs()`). SDK 10.0.2xx ships Roslyn 5.3, SDK 10.0.403 ships 5.9. The other Roslyn consumers (`c0ded0c/MsBuild`, `c0ded0c/PlantUml`, `Roslyn/Compose`, `Repo.Generator`) use Roslyn as a library.
- The generator's only consumers are `Playground/Examples.Crumble` and `Playground/Crumble` (`Examples.Full`). `Playground/Examples/src/Examples.csproj:29` compiles the generated files of `Examples.Crumble` with a static glob (`..\..\Examples.Crumble\src\obj\Generated\42.Crumble.SourceGenerator*\**\*.cs`). The glob is evaluated before `Examples.Crumble` builds, so a clean build fails with `CS0246`.
- Orleans is 10.0.1 (latest 10.4.2). `Playground/Orleans.Silo` uses the third-party `OrleansDashboard` 8.2.0 (`UseDashboard(HostSelf = false)`, `app.Map("/dashboard", x => x.UseOrleansDashboard())`). That package is deprecated (merged into dotnet/orleans as `Microsoft.Orleans.Dashboard`) and fails at silo start on Orleans 10 (`MissingMethodException ITargetHolder.GetTarget`, OrleansContrib issue 450).
- `Microsoft.Orleans.Runtime.Abstractions` 3.8.0 is an orphaned Orleans 3 entry.

### Platform

- Aspire 13.1.2 (latest 13.6.4): nine `Aspire.*` entries and the `<Sdk Name="Aspire.AppHost.Sdk" Version="13.1.2" />` of `src/Platform/Aspire.Host/src/Aspire.Host.Platform.csproj:3` and `src/Crumble/Playground/Aspire.Host/src/Aspire.Host.Crumble.csproj:3`. Both AppHosts reference `MessagePack` directly to pin it (comment: "remove after Aspire upgrade to 13.6+"). `src/Platform/Aspire.Host/src/AppHost.cs:19-35` uses `RunAsPreviewEmulator` under `#pragma warning disable ASPIRECOSMOSDB001`. `docs/Platform/Storyteller/aspire.md:17-18` and `docs/Platform/Storyteller/Aspire.Hosting/package-readme.md:32` name 13.1.2 and the MessagePack audit warnings.
- Functions apps (`Storyteller`, `Scheduler`, `Supervisor` `Api.Functions`): `Microsoft.Azure.Functions.Worker` 2.52.0, `.Sdk` 2.1.0 and `.ApplicationInsights` 2.51.0 are the newest. `Microsoft.ApplicationInsights.WorkerService` is 2.22.0 (2.23.0 is the newest 2.x, 3.2.0 the newest major). The OpenAPI extension is a custom build, `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` and `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` `2.0.0-preview-prop-naming` (fork commit `f49d9782`, upstream PR Azure/azure-functions-openapi-extension#646, still open), restored from a local package folder that `nuget.config` does not list. Only Storyteller references `OpenApi.Core` directly; Scheduler and Supervisor resolve the public `OpenApi.Core` 1.0.0 under the custom worker build, and with it ASP.NET Core 2.1 packages and `Newtonsoft.Json` 11.0.2.
- `Storyteller/Api.Web` (legacy, maintained to compile): `Asp.Versioning.Http` 8.1.1 (latest 10.2.3), `NSwag.AspNetCore` 14.7.1 (latest).
- Dockerfiles: `src/Platform/Storyteller/Api.Functions/src/Dockerfile` uses `azure-functions/dotnet-isolated:4-dotnet-isolated8.0` and `dotnet/sdk:8.0`, `src/Platform/Storyteller/Api.Web/src/Dockerfile` `dotnet/aspnet:8.0` and `dotnet/sdk:8.0`, for `net10.0` projects. Their `COPY` paths point at projects that no longer exist.
- Testcontainers: `Testcontainers.CosmosDb` 4.15.0 and `Testcontainers.LowkeyVault` 4.11.0 (latest 4.16.0). `Backend.CosmosDb/test/Startup.cs:44` and `Access.Certificates/testint/CosmosFixture.cs:29` call the obsolete parameterless `new CosmosDbBuilder()` (`CS0618`).

### Tooling and libraries

- `Microsoft.Build`/`.Framework` 18.4.0. The newest, 18.10.1 and 18.11.0, ship only `net11.0` and `net472` (the .NET 11 SDK's MSBuild); 18.9.6 is the newest for `net10.0` and matches the MSBuild of SDK 10.0.403.
- `NuGet.Frameworks`/`Packaging`/`ProjectModel`/`Versioning` 7.3.1 are referenced only by `src/c0ded0c/Cli/src/Cli.csproj:25-28` (`ExcludeAssets=runtime`), and no c0ded0c code uses NuGet APIs. `NuGet.Packaging` brings the vulnerable `System.Security.Cryptography.Pkcs` 8.0.1, also in 7.9.0.
- `TestableIO.System.IO.Abstractions*` 22.1.0 → 22.3.0 adds abstract members (`IFileSystem.RandomAccess`, `IFile.OpenHandle`, the `IRandomAccess` interface), so the four implementations in `src/Libraries/Testing/System.IO.Abstractions.Trace/src` stop compiling.
- `YamlDotNet` 16.3.0 → 18.1.0 and `Markdig` 0.26.0 → 1.5.1 (`src/Texo/Directory.Packages.props`): no API we use changed. Markdig 1.5.1 still ships `netstandard2.1`.
- `StyleCop.Analyzers` 1.2.0-beta.556: the 1.3 prerelease line (1.3.151-beta, 2026-10-21, Sam Harwell) adds rules (`SA1143`, `SA1315`) and widens others (`SA1017`, `SA1407`). The repository has no `TreatWarningsAsErrors`.
- `System.Linq.Async` 7.0.1 is referenced only by `Libraries/Utils/Async/test`, which uses no async LINQ; its authors deprecated it for the in-box `System.Linq.AsyncEnumerable`.

### Tests

- 31 test projects on xUnit 2.9.3 (the last v2) with `Microsoft.NET.Test.Sdk` 18.10.1 and `xunit.runner.visualstudio` 3.1.5 under VSTest, plus the TUnit project `UI.Admin.AutomationTests` (built, not run by `dotnet test`). Eight test projects use `FluentAssertions` `[7.2.0]`; the others use Shouldly.
- Linear 424-151 ("Migrate all test projects to xUnit v3 and the Microsoft.Testing.Platform runner") owns the xUnit v3 migration and the switch of `global.json` to MTP.
- `TUnit`/`TUnit.Core` 1.76.45; the comment at `Directory.Packages.props:171` asks to recheck `RetryPolicy` of `UI.Automation.Runtime.TUnit` on an upgrade.

### Unused central entries

No project or props file references: `coverlet.collector`, `Microsoft.AspNetCore.App` 2.2.8, `Microsoft.Azure.Functions.Worker.Extensions.Abstractions`, `.DurableTask`, `.Http`, `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Identity.Web.MicrosoftGraph`, `Microsoft.Orleans.Reminders.Redis`, `Microsoft.Orleans.Runtime.Abstractions` 3.8.0, `Microsoft.Orleans.Serialization.Abstractions`, `NBuilder`, `Nerdbank.GitVersioning`, `Serilog.Sinks.ApplicationInsights`, `Serilog.Sinks.Console`, `Swashbuckle.AspNetCore`, `System.ComponentModel.Annotations` 5.0.0, `Testcontainers`, `Testcontainers.Azurite`, `Testcontainers.PostgreSql`. Transitive pinning is off, so they have no effect. Several are part of the documented toolbox (`CLAUDE.md`: Testcontainers Azurite/PostgreSql, NBuilder).

### The coding plan on the issue

CodeRabbit attached a coding plan, and the owner replied with its file. It was used as a second opinion. This spec follows it on the merge of PR #60, the phase order, the root `docs/specs/` location, the nested `Directory.Packages.props` for the generator, the dashboard replacement and the conditional SDK regeneration. It differs where the owner or the evidence says otherwise:

- **netstandard stays.** The plan moved `Monads`, `Monads.Trees`, `Compose`, `Async` and `Core.Markdown` to `net10.0` only; the owner's comment keeps every `netstandard` target as it is.
- **xUnit stays on v2 here, and FluentAssertions stays.** The plan migrated all tests to xUnit v3 and FluentAssertions to Shouldly. The v3 migration belongs to 424-151, which also switches to MTP; doing it here on VSTest needs the transitional `xunit.v3.mtp-off` packages that 424-151 would replace at once. FluentAssertions keeps its Apache-2.0 7.x pin (8.x needs a paid commercial license), moved to 7.2.2; 7.1+ supports xUnit v3, so 424-151 is not blocked.
- **SDK 10.0.401, not 10.0.403**, as the minimum: the UI automation job's Playwright image ships 10.0.401.
- **Unused entries.** The plan removed all 21 unused entries; this spec removes only the obsolete or superseded ones and upgrades the rest (several are the documented toolbox).

## Proposed Changes

The work is delivered in five phases, one or more commits each, with one review per phase. Each phase ends with the **phase gate**: `dotnet build src` without errors, the tests of the touched area, and the audit (no `NU1901`–`NU1904` in any `project.assets.json`). The last phase runs the **final gate**: a clean `dotnet build src`, `dotnet test src` with Docker and `STORYTELLER_TESTS_USE_CONTAINER=true` compared with the baseline, and the target-framework check.

Rules for every phase:

- Every `PackageVersion` goes to its latest stable version (a prerelease only where one is used today). A package that stays behind gets its reason in the phase review and in the final table.
- `Directory.Packages.props` stays alphabetical, without grouping; no version in a `.csproj`. A comment is allowed on an entry that is held back on purpose.
- Breaking changes are fixed in code. SDKs (NSwag, Orval) are regenerated only if the exported OpenAPI document changes.
- The JavaScript dependencies are out of scope (Linear 424-144).

### 1. Phase 1: SDK, shared packages and target frameworks

1. **SDK.** `global.json`: `"version": "10.0.401"`, `"rollForward": "latestFeature"`, `"allowPrerelease": false`; `Microsoft.Build.Traversal` 4.1.82. 10.0.401 is the newest SDK that every build environment of the repository has (the Playwright 1.63 image); `latestFeature` takes the newest installed 10.0 SDK (10.0.403 locally and through `setup-dotnet`) and never a .NET 11 preview.
2. **mrepo.** `mrepo.json` `custom.dotnetVersion` 10; the `CLAUDE.md` note follows.
3. **Target frameworks.** `Repo.Generator.Tests.csproj` and `Supervisor/Abstraction/test/Abstraction.Tests.csproj` → `net10.0`. No other target framework changes: the `netstandard` and multi-targeted projects keep their targets, and the documented exceptions in `CLAUDE.md` are the generator (`netstandard2.0`, a Roslyn requirement) and the libraries the owner keeps on `netstandard`.
4. **Shared packages** to their latest stable versions: all `Microsoft.Extensions.*` 10.0.x, `Microsoft.AspNetCore.*`, `System.*` and `Microsoft.Bcl.AsyncInterfaces` to 10.0.14; `Microsoft.Extensions.Http.Resilience`, `.ServiceDiscovery` and `.TimeProvider.Testing` to 10.11.0 (they need `Microsoft.Extensions.*` ≥ 10.0.13, so they move in the same commit); `OpenTelemetry.*` to 1.20.1 (all six together: the instrumentation packages pin `OpenTelemetry.Api` to `[1.20.1, 2.0)`); `Azure.Security.KeyVault.Secrets` 4.12.0; `Microsoft.Azure.Cosmos` 3.63.3; `Microsoft.Identity.Client`(`.Extensions.Msal`) 4.92.0; `Microsoft.Identity.Web`, `.UI`, `.MicrosoftGraph` 4.16.1; `Microsoft.Graph` 6.9.0; `Polly` 8.9.0; `PostHog` 2.18.0; `Sentry`/`Sentry.Serilog` 6.12.1; `Serilog.Sinks.ApplicationInsights` 5.0.1; `Swashbuckle.AspNetCore` 10.3.1; `Nerdbank.GitVersioning` 3.10.94. The others of these families are already the newest.
5. **Roslyn.** All `Microsoft.CodeAnalysis.*` to 5.9.0. The generator keeps 5.3.0 through a new `src/Crumble/SourceGenerator.Orleans/Directory.Packages.props` that imports the root file (the `src/Texo/Directory.Packages.props` pattern) and sets `<PackageVersion Update="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />` and the same for `.Analyzers`, with a comment: the generator must load in every compiler from SDK 10.0.2xx / VS 18.3 on, and it uses only Roslyn 4.x APIs. Compose drops its `Microsoft.CodeAnalysis.Workspaces.MSBuild` reference (no `netstandard2.0` asset, the source of `NU1701`; Compose uses only C# syntax APIs).
6. **StyleCop.Analyzers** 1.3.151-beta. New warnings in code are fixed when they are few and mechanical; a rule is suppressed in `src/.stylecop/GlobalStylecopSuppressions.cs` only if it contradicts the repository style, with the reason in the review.
7. **Obsolete entries removed:** `Microsoft.AspNetCore.App` 2.2.8 (the ASP.NET Core 2.2 metapackage; every project uses the shared framework), `Microsoft.Orleans.Runtime.Abstractions` 3.8.0 (Orleans 3), `System.ComponentModel.Annotations` 5.0.0 (in-box since .NET Core 3.0). The other unused entries are upgraded and kept.
8. **In-box references.** The `PackageReference`s that restore reports with `NU1510` are removed from those `net10.0` projects (the central entries stay where other consumers need them).

### 2. Phase 2: Platform (Aspire, Functions, Storyteller)

1. **Aspire 13.6.4.** All nine `Aspire.*` entries and both `Aspire.AppHost.Sdk` versions move together (mixed versions fail at runtime). `AppHost.cs`: `RunAsPreviewEmulator` → `RunAsEmulator` (the vNext emulator; the old name is obsolete) and the retired `ASPIRECOSMOSDB001` pragmas go; the image override stays as it is. Both AppHosts set `<NoWarn>$(NoWarn);ASPIRE010</NoWarn>` (the new "CLI bundle disabled" warning; the DCP and Dashboard packages keep coming from NuGet, as today).
2. **MessagePack pin removed.** Aspire.Hosting 13.6.4 depends on `MessagePack` 2.5.307 and `StreamJsonRpc` 2.25.29 itself, which have no advisories. The `MessagePack` `PackageReference`s of both AppHosts and the central entry go. `docs/Platform/Storyteller/aspire.md` and `Aspire.Hosting/package-readme.md` name 13.6.4 and drop the audit-warning note.
3. **Functions.** `Microsoft.ApplicationInsights.WorkerService` 2.23.0. `Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues` 5.5.5, `.DurableTask` 1.21.0. Scheduler and Supervisor reference `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` directly, like Storyteller, so they resolve the same custom build instead of the public 1.0.0 with its ASP.NET Core 2.1 and `Newtonsoft.Json` 11 dependencies.
4. **Api.Web.** `Asp.Versioning.Http` 10.2.3. The versioning behaviour stays as it is (no `Asp.Versioning.Mvc`). If `System.Security.Cryptography.Pkcs` from `Microsoft.Identity.Web.TokenCache` is still vulnerable after 4.16.1, a central `System.Security.Cryptography.Pkcs` 10.0.14 entry and a direct reference in `Api.Web` with a comment pin it.
5. **Testcontainers** 4.16.0 (all five entries). The two `CosmosDbBuilder` call sites pass the image to the constructor instead of `WithImage(...)`.
6. **Dockerfiles.** The base images move to .NET 10: `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0`, `mcr.microsoft.com/dotnet/sdk:10.0`. Their stale `COPY` paths are a known, older defect and are not fixed here (recorded in the review).
7. **OpenAPI.** The exported document (`STORYTELLER_OPENAPI_EXPORT` with the `OpenApiExportTests` filter) is compared before and after this phase; the SDKs are regenerated only if it differs.

### 3. Phase 3: Crumble (Orleans)

1. **Orleans 10.4.2** for every `Microsoft.Orleans.*` entry. Fix the new nullability warning of `IReminderRegistry.GetReminder` (now `IGrainReminder?`) in `MessageActionPoolingGrain` and `VolumeActionPoolingGrain` with a null check; the generator and its emitted code stay unchanged (nothing it emits changed in 10.4).
2. **Dashboard.** `OrleansDashboard` 8.2.0 → `Microsoft.Orleans.Dashboard` 10.4.2 (new central entry, alphabetical). `Orleans.Silo/src/Program.cs`: `siloBuilder.AddDashboard()` and `app.MapOrleansDashboard(routePrefix: "/dashboard")`; the Aspire "Open Dashboard" command keeps its `/dashboard` URL.
3. **Clean builds of `Examples`.** The static glob of `Examples.csproj:29` becomes a target that adds the generated files after `Examples.Crumble` has built (before `CoreCompile`), so `dotnet build src` passes on a clean clone. The generator and `Examples.Crumble` are not touched.
4. **Cronos** 0.13.0 (`Actions.Time.Orleans`).
5. **Crumble AppHost.** The members Aspire 13.6 made obsolete in `Playground/Aspire.Host` (`ExecuteCommandResult.ErrorMessage`, the old `WithCommand` overload, `ResourceCommandAnnotation.Parameter`) move to their replacements, and `ExplicitStartupExtensions` compares with `KnownResourceCommands.StartCommand` (13.2 renamed the start command).

### 4. Phase 4: Monorepo, c0ded0c, Texo, Libraries and Games

1. **Build and NuGet.** `Microsoft.Build`/`.Framework` 18.9.6 (held below 18.10, which is `net11.0`-only, with a comment). The unused `NuGet.*` references of `c0ded0c/Cli` and their four central entries go, which also removes the vulnerable `System.Security.Cryptography.Pkcs` 8.0.1.
2. **Console and tools.** `LibGit2Sharp` 0.32.0, `Sharprompt` 3.2.0, `Spectre.Console.Json` 0.57.2, `YamlDotNet` 18.1.0, `Markdig` 1.5.1 (`src/Texo/Directory.Packages.props`). No code changes are expected.
3. **TestableIO 22.3.0.** `DiagnosticFileSystem` and `ReadonlyFileSystem` get `IRandomAccess RandomAccess`, `DiagnosticFile` and `ReadonlyFile` get `OpenHandle(...)`: the diagnostic variants trace and delegate (a new `DiagnosticRandomAccess`), the read-only ones delegate reads and refuse writes, as the rest of `ReadonlyFile` does.
4. **Scaffolding.** `BasicOptionsExtensions` defaults to .NET 10; `GlobalJsonT4` writes SDK 10.0.401; `RootDirectoryPackagesPropsT4` writes the new versions of the packages it lists; `Repo.Generator`'s `ProjectCsprojT4` writes `net10.0`. Each `.tt` and its committed `.cs` change together. `docs/Monorepo/mrepo-json.md` shows `"dotnetVersion": 10`.

### 5. Phase 5: Test frameworks and final validation

1. `Microsoft.NET.Test.Sdk` 18.11.0, `xunit.runner.visualstudio` 4.0.1 (it still runs xUnit v1/v2/v3 under VSTest), `coverlet.collector` 10.1.0, `FluentAssertions` `[7.2.2]`. `xunit` stays 2.9.3 (the last v2; `xunit.v3` is 424-151).
2. `TUnit`/`TUnit.Core` 1.78.0. TUnit's retry decision (`RetryHelper`) is byte-identical between 1.76.45 and 1.78.0, so `RetryPolicy` keeps its logic; the comments that name 1.76 (`Directory.Packages.props`, `RetryPolicy.cs`, `UiAutomationSession.cs`), the comment in `UiTest.cs` about the late end receiver (TUnit 1.78 now reports a failed After(Test) hook to it as Failed), `docs/Libraries/Testing/UI.Automation/readme.md` and `CLAUDE.md` follow.
3. `System.Linq.Async` leaves `Async.UnitTests` and the catalog.
4. **Final gate** as described above, and `grep -r "<TargetFramework" --include=*.csproj src` shows only the documented exceptions.
5. **Documentation.** `CLAUDE.md` (Traversal version, the SDK pin, `mrepo.json` note, TUnit version, FluentAssertions pin), and the troubleshooting row about the `net6.0` project in `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` goes.
6. **PR #60.** The merge commit puts its head into this branch, so GitHub marks PR #60 as merged when this branch reaches `main`.

### 6. Packages held back

| Package | Stays at | Newest | Reason |
|---|---|---|---|
| `xunit` | 2.9.3 | (`xunit.v3` 4.0.2) | The v3 migration with the MTP switch is Linear 424-151. |
| `Microsoft.Playwright` | 1.63.0 | 1.64.1 | BrowserStack runs only up to 1.63, and a Playwright server refuses another minor; the CI image follows the pin. |
| `Microsoft.ApplicationInsights.WorkerService` | 2.23.0 | 3.2.0 | `Microsoft.Azure.Functions.Worker.ApplicationInsights` (latest 2.51.0) is built on AI 2.x and will not support 3.x (azure-functions-dotnet-worker#3322: `TypeLoadException` at start). Moving on means replacing it with OpenTelemetry, a separate change. |
| `FluentAssertions` | `[7.2.2]` | 8.11.0 | 8.x is under the Xceed Community License (paid for commercial use); 7.x is Apache-2.0. |
| OpenAPI extension (two packages) | `2.0.0-preview-prop-naming` | public `2.0.0-preview2`, 1.6.0 | Custom build of the unmerged naming-strategy PR #646; no public build contains it. NuGet sorts the public preview2 above it, which is a false positive. |
| `Microsoft.Build`, `.Framework` | 18.9.6 | 18.11.0 | 18.10+ ship only `net11.0`/`net472`. |
| `Microsoft.CodeAnalysis.CSharp`, `.Analyzers` (generator only) | 5.3.0 | 5.9.0 | The generator must load in compilers from SDK 10.0.2xx on. |
| `Sentry`, `Sentry.Serilog` | 6.12.1 | 7.0.0-rc.3 | 7.x is a release candidate only. |
