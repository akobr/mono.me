# Phase 5 of the NuGet Upgrade: Test Frameworks and Final Validation

## Overview

Phase 5, the last, of [Upgrade All NuGet Packages and Move to .NET 10](../2026-11-01%20Upgrade%20All%20NuGet%20Packages%20and%20Move%20to%20.NET%2010.md) (Linear [424-143](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)): the test packages and TUnit 1.78, the documentation, the final gate over the whole branch, and the fixes of a final review. A clean `dotnet build src` (every `bin` and `obj` deleted first) has no errors, the audit reports no vulnerable package, every tracked project targets `net10.0` except the `netstandard` libraries the owner keeps, and `dotnet test src` runs the same 12,628 tests in 28 assemblies as the baseline with the same results; the only differences are timing tests that pass when run alone. The packages that stay behind, with their reasons, are in section 6.

## What Was Done

### 1. Test packages

- `Microsoft.NET.Test.Sdk` 18.10.1 → 18.11.0, `xunit.runner.visualstudio` 3.1.5 → 4.0.1 (its nuspec: "Capable of running xUnit.net v1, v2, and v3 tests"; all 28 test assemblies, xUnit v2, ran under it), `coverlet.collector` 8.0.0 → 10.1.0 (unused, kept for the scaffolding), `FluentAssertions` `[7.2.0]` → `[7.2.2]` (bug fixes, Apache-2.0).
- `xunit` stays 2.9.3, the last v2 (Linear 424-151 moves to `xunit.v3` and MTP). `Shouldly` 4.3.0, `Moq` 4.21.0, `Bogus` 35.6.5 and `NBuilder` 6.1.0 were already the newest.
- The held-back entries carry a short comment in `Directory.Packages.props` (`FluentAssertions`, `Microsoft.ApplicationInsights.WorkerService`, the custom OpenAPI extension, `Microsoft.Build`, `Microsoft.Playwright`, `xunit`), so the next upgrade does not take them by mistake. The comments name no other package id: `mrepo`'s catalog editor (`PackagesDefinitionManager`) edits the first line that contains an id, and a first wording that named `Microsoft.Azure.Functions.Worker.ApplicationInsights`, `Microsoft.NET.StringTools` and `xunit.v3` would have sent `mrepo update package` to the wrong line (found by the final review; a scan of every id now finds its own entry first). The comment does not disturb the "Playwright version matches the image" step of `ui-admin-automation.yml`, which reads the line up to its `Version`.

### 2. TUnit 1.78.0

- `TUnit` and `TUnit.Core` 1.76.45 → 1.78.0. `src/TUnit.Engine/Services/TestExecution/RetryHelper.cs` is not in the GitHub diff between the tags `v1.76.45` and `v1.78.0`, so `RetryPolicy.WillRetryAsync` keeps its logic; its comment, the one in `Directory.Packages.props`, the version examples in `UiAutomationSession.cs` and `AutomationSessionOptions.cs`, `docs/Libraries/Testing/UI.Automation/readme.md` and `CLAUDE.md` name 1.78.
- The diff has one engine change that matters here (`TestExecutor.cs`, released in 1.77/1.78): when the body passed but an After(Test) hook or an early end receiver threw, the result is set to Failed before the late end receivers run. `UiTest.EndUiTest` (an After(Test) hook) still sees Passed and still claims the attempt first, so nothing is reported twice; the comment in `UiTest.cs` that described the old order now describes both.
- The 28 tests of `UI.Automation.Runtime.TUnit.UnitTests` and the 227 of `UI.Automation.Runtime.UnitTests` pass. The UI suite itself (`UI.Admin.AutomationTests`) builds; it was not run against a target.

### 3. System.Linq.Async

- Removed from `Async.UnitTests.csproj` (it used no async LINQ) and from the catalog.

### 4. Final gate

- **Clean build.** All 237 `bin` and `obj` folders under `src` were deleted, then `dotnet build src` (SDK 10.0.403): 0 errors. Compared by file and line with a clean build of the merged baseline (`caeb70a4`, 3 errors), the new warnings are the StyleCop ones of phase 1 and `AV0013` of phase 2; `CS0618` went from 9 to 7.
- **Audit.** No `NU1901`–`NU1904` in the `project.assets.json` of any project (the baseline had `MessagePack`, `System.Security.Cryptography.Pkcs` 8.0.1 and 10.0.10, `Microsoft.AspNetCore.Http` 2.1.0, `Newtonsoft.Json` 11.0.2 and `SSH.NET` 2025.1.0).
- **Target frameworks.** `git ls-files '*.csproj'` with `grep "<TargetFramework"`: 104 projects on `net10.0`; `netstandard2.0`: `Crumble/SourceGenerator.Orleans`, `Monads`, `Monads.Trees`, `Roslyn/Compose`; `netstandard2.1`: `Texo/Core.Markdown`; `net10.0;netstandard2.0`: `Crumble/Abstractions`, `Crumble/Volume.Abstractions`; `net10.0;netstandard2.1`: `Utils/Async`. (The `WorkerExtensions.csproj` that the Functions SDK generates under `obj` targets `net8.0` by its own design.)
- **Tests.** `dotnet test src --no-build` with Docker and `STORYTELLER_TESTS_USE_CONTAINER=true`: 28 assemblies, 12,628 tests, 2 skipped (the `UnixFact` tests of `Configuration.UnitTests` on Windows), as in the baseline. Failures:
  - `Backend.CosmosDb.UnitTests` 442 and `Access.Certificates.IntegrationTests` 73, exactly as in the baseline: the Linux Cosmos DB emulator container (`azure-cosmos-emulator:latest`, reused by Testcontainers) crashes on this machine (exit code 1 with a core dump), so the tests that need it fail with "The SSL connection could not be established". Container mode does not support stored procedures anyway. Run one project at a time, with a fresh emulator container and nothing else on the 2 CPUs of Docker Desktop, they pass: `Access.Certificates.IntegrationTests` 73 of 73, `Backend.CosmosDb.UnitTests` 641 of 642, and the one left, `ConcurrentWrites_ProduceConsecutiveVersions` (a 408 timeout of the emulator), passes alone. The failures of the full run come from the shared machine: the emulator needs about 3 minutes and 1.5 GB to start its 11 partitions, the Testcontainers wait strategy lets the tests start before it serves TLS, and the parallel test projects and other containers starve it until it crashed.
  - `KeyValues.Azure.KeyVault.IntegrationTests` 1 of 13 (Lowkey Vault TLS under the parallel run, as in the baseline), and `Backend.Core.UnitTests` `NameQueryTests.IsMatch_CatastrophicPattern_AnswersWithoutBacktracking(30)` and `Binding.JsonE.UnitTests` `OrdinalSearchTests.Contains_PeriodicWorstCase_IsLinear`, two timing tests under the load of the parallel run (the baseline had `AttackCorpusTests.Vector_StopsOnLimit` instead). All three projects pass completely when run alone.
  - `Repo.Generator.Tests` and `Supervisor/Abstraction/test` build and contain no tests.

### 5. Documentation

- `CLAUDE.md`: Traversal 4.1.82, the SDK pin, TUnit 1.78, the `netstandard` exceptions and the Roslyn 5.3 rule of the generator under "Project File Conventions", FluentAssertions `[7.2.2]`, and the `mrepo.json` note (`dotnetVersion` 10 is what `mrepo new` scaffolds).
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`: the troubleshooting row about the `net6.0` project that broke `dotnet build src/Platform` is gone.

### 6. Packages held back

| Package | Stays at | Newest | Reason |
|---|---|---|---|
| `xunit` | 2.9.3 | (`xunit.v3` 4.0.2) | The v3 migration together with the MTP runner is Linear 424-151. |
| `Microsoft.Playwright` | 1.63.0 | 1.64.1 | BrowserStack's table ends at 1.63 (checked 2026-11-01), and a Playwright server refuses a client of another minor; the CI image follows the pin. |
| `Microsoft.ApplicationInsights.WorkerService` | 2.23.0 | 3.2.0 | `Microsoft.Azure.Functions.Worker.ApplicationInsights` 2.51.0 (the newest) is built on AI 2.x and will not support 3.x (azure-functions-dotnet-worker#3322); 3.x means moving the three Functions apps to OpenTelemetry. |
| `FluentAssertions` | `[7.2.2]` | 8.11.0 | 8.x is under the Xceed Community License (paid for commercial use). |
| `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`, `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` | `2.0.0-preview-prop-naming` | 2.0.0-preview2, 1.6.0 | Custom build of the unmerged naming-strategy PR #646; no public build contains it. |
| `Microsoft.Build`, `.Framework`, `Microsoft.NET.StringTools` | 18.9.6 | 18.11.0 | `Microsoft.Build` 18.10+ ships only `net11.0` and `net472`; `.Framework` and `StringTools` stay on the same 18.9.x, the MSBuild of the .NET 10.0.4xx SDK that Microsoft.Build.Locator loads at run time. |
| `Microsoft.CodeAnalysis.CSharp`, `.Analyzers` (only `Crumble/SourceGenerator.Orleans`) | 5.3.0 | 5.9.0 | The generator must load in compilers from SDK 10.0.2xx on; the root catalog has 5.9.0. |
| `Sentry`, `Sentry.Serilog` | 6.12.1 | 7.0.0-rc.3 | 7.x is a release candidate only. |
| `global.json` SDK | 10.0.401 (minimum) | 10.0.403 | The SDK of the Playwright 1.63 image of `ui-admin-automation.yml`; `latestFeature` takes 10.0.403 wherever it is installed. |

Every other entry of `Directory.Packages.props` and `src/Texo/Directory.Packages.props`, and `Aspire.AppHost.Sdk`, is on the newest stable version (or, for `StyleCop.Analyzers`, the newest prerelease) on nuget.org on 2026-11-01.

### 7. Fixes from the final review

Four read-only reviewers (MSBuild and central package management, the C# changes, completeness against the spec, CI and runtime risk) went over the whole diff, and a skeptic per dimension checked each finding. Nine of twelve findings held; the fixes:

- **Cosmos DB emulator of the Platform AppHost.** In Aspire 13.1.2, `RunAsPreviewEmulator` with the image override `azure-cosmos-emulator:latest` ended up as the classic emulator, because 13.1.2 decided "preview" from the image name. In 13.6.4 `RunAsEmulator` sets the resource up for vNext whatever the image is (an HTTP health check on `/ready` at port 8080, the vNext connection string, Data Explorer), and the classic `latest` image serves none of it, so `cosmos` would never become healthy and `db-creator` and the Functions API would wait for ever. The override is gone: the AppHost runs Aspire's own `vnext-latest` image, as its comment and `docs/Platform/Aspire.Host/local-development.md` ("Linux vNext emulator ... with Data Explorer") always said. **The persistent `cosmos` container is recreated with the new image, so local data has to be seeded again with `db-creator`.** The AppHost was built, not started.
- **`ReadonlyFile.OpenHandle`** refuses `FileOptions.DeleteOnClose`, which would have deleted the real file through the read-only file system.
- **Visual Studio's fast up-to-date check** of `Examples`: the generated files are no longer evaluation items, so `Examples.csproj` lists the intermediate assembly of `Examples.Crumble` as an `UpToDateCheckInput`; it is rewritten whenever the generated files are.
- **Catalog comments** reworded so they name no other package id (see section 1), and the `Microsoft.Build` comment now gives the right reason for `.Framework` and `StringTools`.
- **Orphaned entries.** `System.Collections.Immutable`, `System.Diagnostics.DiagnosticSource` and `System.Text.Json` had no consumer after the `NU1510` clean-up of phase 1; their entries are gone.
- **Scaffolding.** The template-only `NUnit` 5.0.0, `NUnit3TestAdapter` 6.3.1 and `SonarAnalyzer.CSharp` 10.35.0.4138 in `RootDirectoryPackagesPropsT4` (`.tt` and `.cs`).

Not taken: the reviews were still being written when the reviewers ran (they exist now); `docs/Platform/development.md` already defers the SDK version to `global.json`; the `microsoft.dotnet-msidentity` local tool of the legacy `Api.Web` (2.0.5, newest 2.0.8) is a .NET tool, not a package of the catalogs, and nothing runs it.

After the fixes `dotnet build src` has no errors, the audit is clean, and `UI.Automation.Runtime.TUnit.UnitTests` 28, `UI.Automation.Runtime.UnitTests` 227, `Utils/Async` 5 and `Storyteller/Aspire.Hosting` 164 tests pass.

### 8. PR #60

- The branch contains PR #60's head (`e91d883d`) through the merge commit `caeb70a4`, so GitHub marks akobr/mono.me#60 as merged once this branch is merged into `main`.

## Follow-ups

- Move the three Functions apps from Application Insights 2.x to OpenTelemetry (`Microsoft.Azure.Functions.Worker.OpenTelemetry` with the Azure Monitor exporter), which unblocks `Microsoft.ApplicationInsights.WorkerService` 3.x.
- Publish the custom OpenAPI extension to a feed that `nuget.config` lists (or `.artifacts`), so a clean machine, CI and the Dockerfiles can restore it; fix the stale `COPY` paths of both Dockerfiles.
- A style pass for the warnings of StyleCop 1.3.
- Raise Playwright together with BrowserStack's table, the CI image and the browsers.
- Let `mrepo`'s `PackagesDefinitionManager` match `Include="<id>"` exactly instead of the first line that contains the id.
- Get the Cosmos-backed tests (`Backend.CosmosDb.UnitTests`, `Access.Certificates.IntegrationTests`) running on a machine where the emulator container works, and run the playground AppHosts once with the vNext emulator and the new Orleans dashboard.
