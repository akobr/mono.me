# Migrate All Test Projects to xUnit v3 and Microsoft.Testing.Platform

Linear: [424-151 Migrate all test projects to xUnit v3 and the Microsoft.Testing.Platform runner](https://linear.app/424fun/issue/424-151/migrate-all-test-projects-to-xunit-v3-and-the-microsofttestingplatform)

This spec lives in `docs/specs/` because the change covers every test project of the `src/` tree (Libraries, Monorepo and Platform), like the NuGet upgrade of 424-143 before it. Its reviews go to `docs/specs/reviews/`, one per phase.

## Problem

- `dotnet test` cannot run the TUnit UI automation suite (`src/Platform/UI.Admin.AutomationTests/src`, 424-133) together with the rest of the tests. In the .NET 10 SDK, `dotnet test` runs either in VSTest mode (today: `global.json` has no `test` section) or in Microsoft.Testing.Platform (MTP) mode (`"test": { "runner": "Microsoft.Testing.Platform" }`), for the whole repository.
- The 29 xUnit 2.9.3 projects (`xunit` + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`) run only under VSTest. TUnit 1.78 runs only on MTP 2.5, which aborts a VSTest-mode `dotnet test` of its project ("Testing with VSTest target is no longer supported"). In MTP mode it is the other way round: one VSTest project fails the whole run before anything is built.
- The workaround of 424-133 (decision D1 of its Phase 1 review): `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj` build the UI suite but leave it out of their `Test` target (`<ProjectReference Update="**\UI.Admin.AutomationTests.csproj" Test="false" />`), and the suite runs with `dotnet run`.
- Two test projects without any test source are left over (`src/Monorepo/Repo.Generator/test`, `src/Platform/Supervisor/Abstraction/test`; both moved to `net10.0` by 424-143, neither is in `42.mono.slnx`).
- `dotnet build src` fails on Linux before any test runs (see "Linux" below), so "pass on Windows and Linux" cannot be checked with the documented command today.

## Current State

### SDK and repository settings

- `global.json`: SDK `10.0.401`, `rollForward: latestFeature`, `allowPrerelease: false`; `msbuild-sdks` `Microsoft.Build.Traversal` 4.1.82 (the newest on nuget.org). No `test` section, so VSTest mode. The machine of this work has SDK 10.0.403 (installed for this work in `%USERPROFILE%\.dotnet`), which `latestFeature` selects; 10.0.401 stays the floor because the Playwright 1.63 image of `ui-admin-automation.yml` ships it.
- `Directory.Packages.props` (test entries): `coverlet.collector` 10.1.0 (no consumer), `FluentAssertions` `[7.2.2]` (the last Apache-2.0 line), `Microsoft.NET.Test.Sdk` 18.11.0, `Moq` 4.21.0, `Shouldly` 4.3.0, `Testcontainers*` 4.16.0, `TUnit`/`TUnit.Core` 1.78.0, `xunit` 2.9.3 (its comment names 424-151), `xunit.runner.visualstudio` 4.0.1.
- No `Directory.Build.targets`, `TreatWarningsAsErrors`, `xunit.runner.json`, `*.runsettings` or `testconfig.json` anywhere. `.editorconfig:159` lowers some CA/IDE rules for `[**/{test,samples,perf}/**.{cs,vb}]`, a glob that does not match `testint/`.
- `42.mono.slnx` lists 113 projects: every project on disk except the two empty test projects; it includes the UI suite and the two `.esproj` files.

### Test projects

- 31 xUnit 2.9.3 projects, all `net10.0`, none sets `OutputType` (they are executables only because `Microsoft.NET.Test.Sdk` sets `OutputType=Exe`). 29 have tests (about 3,840 `[Fact]` and 1,100 `[Theory]`); the two empty ones have no `.cs` file at all.
- 8 use FluentAssertions, the others Shouldly; 7 use Moq. Assertions never use `Xunit.Assert` (one `Record.ExceptionAsync` in `Hosting/test/Informer/ListTests.cs:207`).
- Testcontainers: `Backend.CosmosDb/test` (Cosmos DB emulator and Lowkey Vault), `Access.Certificates/testint` (Cosmos DB emulator), `KeyValues.Azure.KeyVault/testint` (Lowkey Vault). There are no Azurite or PostgreSQL fixtures.
- The TUnit suite `UI.Admin.AutomationTests` (6 tests, all skipped without `Target:BaseUrl`) and the xUnit project `UI.Automation.Runtime.TUnit.UnitTests`, which gets `TUnit.Core` (no engine, no entry point) from the library under test.

### Code that xUnit v3 breaks

- `using Xunit.Abstractions;` (the namespace is gone, `ITestOutputHelper` is in `Xunit`): `Binding.JsonE/test/SpecificationParityTests.cs:9`, `Binding.Object/test/AttackCorpusTests.cs:8`, `Access.AuthKit/testint/M2MApplicationLifecycleTests.cs:10`, `Api.Functions/testint/HostRoutingTests.cs:5`, `Mcp/testint/TokenExchangeTests.cs:11`.
- `IAsyncLifetime` with `Task` methods (v3: `ValueTask`, and it derives from `IAsyncDisposable`): `Backend.CosmosDb/test/Startup.cs:40,66`, `Backend.CosmosDb/test/KeyValueEndToEndTests.cs:31,47`, `Access.Certificates/testint/CosmosFixture.cs:27,92`, `KeyValues.Azure.KeyVault/testint/LowkeyVaultFixture.cs:21,40`. None of them also implements `IDisposable`.
- `TheoryData` rows read as `object[]` (v3 rows are `TheoryDataRow<…>`): `Backend.Core/test/RelationNamesJsonTests.cs:34-36,172`, `Api.Functions/test/AnnotationsHttpTests.cs:410`.
- `Configuration/test/UnixFactAttribute.cs`: a `FactAttribute` subclass that sets `Skip` in its constructor; v3 still supports this, but the constructor should forward `[CallerFilePath]`/`[CallerLineNumber]` (analyzer `xUnit3003`).
- Not affected: collection fixtures and string-named `[Collection]` (incl. the implicit `nameof(BearerValidationMode)` collection), `IDisposable` test classes, `InlineData`/`MemberData`/`TheoryData` providers (all public static, parameterless), the local `ITestContext` of `Backend.CosmosDb/test` (it wins over v3's `Xunit.ITestContext`). No `async void` tests, custom `DataAttribute`, orderers, `[assembly: CollectionBehavior]` or `Xunit.Sdk` usages.
- About 6,000 calls of methods that take a `CancellationToken`: the v3 analyzer `xUnit1051` (warning) asks for `TestContext.Current.CancellationToken` at each of them.

### Baseline

Measured on `030eb6c9d` (origin/main) in a separate clone, SDK 10.0.403, VSTest mode, TRX logger; Windows with Docker and `STORYTELLER_TESTS_USE_CONTAINER=true` (`dotnet build src`, then `dotnet test src --no-build`), Linux in WSL Ubuntu 22.04 (2 CPUs) without the variable, so the Cosmos fixtures fail fast against an empty `localhost:8081` (`dotnet build 42.mono.slnx`, then `dotnet test <project> --no-build` per xUnit project, because `dotnet build src` fails on Linux). The two empty projects wrote empty TRX files on Windows; the UI suite was not part of `dotnet test`.

| Assembly (`42.` prefix and `.dll` left out) | Windows: results / passed / failed / skipped | Linux: results / passed / failed / skipped |
|---|---|---|
| `platform.cli.unittests` | 1062 / 1062 / 0 / 0 | 1062 / 1062 / 0 / 0 |
| `platform.storyteller.access.authkit.integrationtests` | 1 / 1 / 0 / 0 | 1 / 1 / 0 / 0 |
| `platform.storyteller.access.authkit.unittests` | 180 / 180 / 0 / 0 | 180 / 180 / 0 / 0 |
| `platform.storyteller.access.azuread.unittests` | 140 / 140 / 0 / 0 | 140 / 140 / 0 / 0 |
| `platform.storyteller.access.certificates.integrationtests` | 73 / 0 / 73 / 0 | 73 / 0 / 73 / 0 |
| `platform.storyteller.access.certificates.unittests` | 114 / 114 / 0 / 0 | 114 / 113 / 1 / 0 |
| `platform.storyteller.access.federation.unittests` | 472 / 472 / 0 / 0 | 472 / 472 / 0 / 0 |
| `platform.storyteller.access.keycloak.unittests` | 138 / 138 / 0 / 0 | 138 / 138 / 0 / 0 |
| `platform.storyteller.api.functions.integrationtests` | 4 / 4 / 0 / 0 | 4 / 4 / 0 / 0 |
| `platform.storyteller.api.functions.unittests` | 2743 / 2743 / 0 / 0 | 2743 / 2743 / 0 / 0 |
| `platform.storyteller.aspire.hosting.unittests` | 164 / 164 / 0 / 0 | 164 / 164 / 0 / 0 |
| `platform.storyteller.backend.core.unittests` | 1221 / 1221 / 0 / 0 | 1221 / 1221 / 0 / 0 |
| `platform.storyteller.backend.cosmosdb.unittests` | 642 / 446 / 196 / 0 | 642 / 200 / 442 / 0 |
| `platform.storyteller.binding.jsone.unittests` | 2429 / 2429 / 0 / 0 | 2429 / 2429 / 0 / 0 |
| `platform.storyteller.binding.language.unittests` | 192 / 192 / 0 / 0 | 192 / 192 / 0 / 0 |
| `platform.storyteller.binding.object.unittests` | 239 / 237 / 2 / 0 | 239 / 239 / 0 / 0 |
| `platform.storyteller.configuration.keyvault.unittests` | 87 / 87 / 0 / 0 | 87 / 87 / 0 / 0 |
| `platform.storyteller.configuration.unittests` | 641 / 639 / 0 / 2 | 641 / 641 / 0 / 0 |
| `platform.storyteller.configurationformats.unittests` | 416 / 416 / 0 / 0 | 416 / 416 / 0 / 0 |
| `platform.storyteller.hosting.unittests` | 218 / 218 / 0 / 0 | 218 / 218 / 0 / 0 |
| `platform.storyteller.keyvalues.azure.keyvault.integrationtests` | 13 / 11 / 2 / 0 | 13 / 11 / 2 / 0 |
| `platform.storyteller.mcp.integrationtests` | 2 / 2 / 0 / 0 | 2 / 2 / 0 / 0 |
| `platform.storyteller.mcp.unittests` | 728 / 728 / 0 / 0 | 728 / 728 / 0 / 0 |
| `platform.storyteller.sdk.nswag.unittests` | 338 / 338 / 0 / 0 | 338 / 338 / 0 / 0 |
| `testing.system.io.abstractions.trace.unittests` | 9 / 9 / 0 / 0 | 9 / 9 / 0 / 0 |
| `testing.ui.automation.reporting.testiny.unittests` | 111 / 111 / 0 / 0 | 111 / 111 / 0 / 0 |
| `testing.ui.automation.runtime.tunit.unittests` | 28 / 28 / 0 / 0 | 28 / 28 / 0 / 0 |
| `testing.ui.automation.runtime.unittests` | 227 / 227 / 0 / 0 | 227 / 227 / 0 / 0 |
| `utils.async.unittests` | 5 / 5 / 0 / 0 | 5 / 5 / 0 / 0 |
| **29 assemblies** | **12637 / 12362 / 273 / 2** | **12637 / 12119 / 518 / 0** |

The failures are the environment, as in 424-143: the Cosmos DB emulator container (`Backend.CosmosDb.UnitTests`, `Access.Certificates.IntegrationTests`; on Linux no emulator at all), Lowkey Vault TLS under the parallel run (`KeyVaultKeyProviderTests.EnvelopeProtector_EndToEndWithTheVault`, `Unwrap_RejectsAnotherVaultOrAlgorithm`), two load-sensitive tests on Windows (`AttackCorpusTests.Vector_StopsOnLimit`, `ConfigurationBindingResolverTests.Resolve_IsoTimestampStaysAString`) and on Linux `ClientCertificateValidatorTests.ValidateAsync_ForeignCaCert_ReturnsNull`. The 2 Windows skips are the `UnixFact` tests, which run on Linux. 424-143 reported 28 assemblies and 12,628 tests; the difference is `System.IO.Abstractions.Trace.UnitTests`, whose 9 tests were added on `main` after that count (`a4fc62b5`).

### Probe of MTP mode (SDK 10.0.403, Traversal 4.1.82)

Done in a scratch clone with `"test": { "runner": "Microsoft.Testing.Platform" }` and two projects migrated to `xunit.v3` 4.0.2:

- **Traversal does not work.** `dotnet test --project src/Libraries/probe.proj` (a traversal with the two migrated projects and the UI suite) and `dotnet test` in a folder with only a `dirs.proj`: "No test projects were found", exit 1. The SDK reads `IsTestingPlatformApplication` only on the project it is given; Traversal 4.1.82 has no MTP support. `dotnet test` of Traversal projects starts with .NET 11 Preview 7 (dotnet/sdk#51316, PR 55297), and even then needs `IsTraversal=true` and ignores `Test="false"`. `dotnet build <traversal> -t:Test -p:SkipNonexistentTargets=true` runs MTP projects through MTP's own `Test` target, but gives no combined summary and silently skips anything that is not an MTP project.
- **The solution works.** `dotnet test --solution probe.slnx` with the two migrated projects and the UI suite: exit 0, 39 tests (5 + 28 passed, 6 skipped). With one xUnit v2 project in the solution: exit 1 before any build ("All projects must use that test runner. The following test projects are using VSTest test runner: …"). `.slnx` is supported.
- **The UI suite alone** (`dotnet test --project src/Platform/UI.Admin.AutomationTests/src`): 6 skipped, exit 0. MTP 2.3+ treats an all-skipped module as run (`--zero-tests-policy allow-skipped`, the default). A module that discovers no test at all exits 8 and fails the whole run on SDK 10.0.x.
- **xUnit v3 packages.** `xunit.v3` 4.0.2 is the newest; in the 4.x line it only forwards to `xunit.v3.mtp-v2` (MTP v1 support ended with 4.0; `xunit.v3.mtp-v1` stops at 3.2.2). It needs `OutputType=Exe` (its `_XunitValidateBuild` target fails the build otherwise) and sets `IsTestingPlatformApplication`, `UseAppHost` and the entry point itself. It brings MTP 2.5.1, TUnit 1.78 brings 2.5.2; both are MTP v2 and run side by side.
- **Mixed project.** `UI.Automation.Runtime.TUnit.UnitTests` on `xunit.v3`: one entry point (xUnit's), no TUnit registration, 28 of 28.
- **Options.** `--report-trx` needs `Microsoft.Testing.Extensions.TrxReport` (TUnit has it; xUnit has only its own `--report-xunit-trx`), otherwise the module exits 5. Framework options at solution scope reach every module: xUnit's `--filter-class` makes TUnit exit 5, TUnit's `--treenode-filter` makes xUnit exit 5. `--results-directory-layout` exists from SDK 10.0.402, not on the 10.0.401 floor. Reports of `dotnet test` go to `<current directory>/TestResults/` unless `--results-directory` is given.
- **Per-project arguments.** `<TestingPlatformCommandLineArguments>` in a project file reaches both `dotnet test` and `dotnet run`; without `UseMicrosoftTestingPlatformRunner=true`, `dotnet run` of an xUnit v3 project starts xUnit's own console runner, which does not know the MTP options.

### Linux

On Linux, the `GeneratePackageJsonProps` task of `Microsoft.VisualStudio.JavaScript.Sdk` 1.0.6887863 writes `$(IntermediateOutputPath)\package.g.props` (`Sdk.targets:208`) without normalizing it, so the restore of `ui.admin.esproj` and `sdk.typescript.esproj` creates a folder literally named `obj\Debug`. From then on every `**` glob below it returns its literal pattern (MSBuild cannot enumerate a name with a backslash), and `dotnet build src` stops with `MSB3202: The project file "**/*.*?proj" was not found` (`Traversal.targets:151`). Normalizing the path in a target that runs before `GeneratePackageJsonProps` keeps the file in `obj/Debug/` (probed in WSL).

### Entry points that change

- `dotnet test src` and `dotnet test src/Platform` (CLAUDE.md, docs): traversals, refused in MTP mode.
- Positional `dotnet test <project folder>` (`storyteller-conformance.yml:96,99`, `mrepo.json:50,64`, docs): works in MTP mode from SDK 10.0.401, but `--project` is the documented form.
- VSTest options in docs: `--filter "FullyQualifiedName~…"` (`docs/Platform/Storyteller/conformance.md:199`, `src/Platform/Storyteller/conformance/README.md:16`, `src/Platform/Storyteller/Sdk.NSwag/README.md:31`, `src/Platform/Storyteller/sdk.typescript/README.md:100`) and `--logger "console;verbosity=detailed"` (`docs/Platform/Storyteller/mcp.md:253`).
- `mrepo build -t` at the repository level runs `dotnet test Directory.Build.proj` (`src/Monorepo/Cli/src/Commands/BuildCommand.cs:332`), which MTP mode refuses like any traversal.

### The coding plan on the issue

CodeRabbit attached a coding plan, and the owner replied with its file. It was used as a second opinion. This spec follows it on the solution as the single test command (as the owner's comment on the issue prefers until .NET 11), the deletion of the two empty projects, `ValueTask` in the four fixtures, the removed `Xunit.Abstractions` usings, keeping FluentAssertions 7.x, keeping `ui-admin-automation.yml` on `dotnet run`, the `--project` form in CI and `mrepo.json`, and the per-project baseline. It differs where the evidence says otherwise:

- **`xunit.v3`, not `xunit.v3.mtp-v2`.** In the 4.x line the plain package is the MTP v2 package; the plan's reason (3.x defaults to MTP v1) applies only to 3.x.
- **`docs/specs/`, not `docs/Monorepo/specs/`**, as for the repository-wide 424-143.
- **TRX for every module.** The xUnit projects also get `Microsoft.Testing.Extensions.TrxReport`, so `--report-trx` works for the whole solution, as it does for TUnit.
- **More code sites.** The plan missed the three `TheoryData` rows read as `object[]` (compile errors in v3) and the `xUnit1051` warnings.
- **Linux.** The plan does not mention that `dotnet build src` fails on Linux; this spec fixes it (Phase 3).

## Proposed Changes

The work is delivered in three phases, one or more commits each, with one review per phase. Each phase ends with the **phase gate**: `dotnet build src` without errors, and the tests of the touched area. The last phase runs the **final gate** on Windows and Linux: `dotnet build src`, then `dotnet test --solution 42.mono.slnx --report-trx` under the baseline conditions, compared per assembly with the baseline.

Rules for every phase:

- `Directory.Packages.props` stays alphabetical, without grouping; no version in a `.csproj`. A catalog comment names no other package id (`mrepo`'s `PackagesDefinitionManager` edits the first line that contains an id, and `xunit` is a substring of `xunit.v3`).
- The Cosmos and Lowkey Vault fixtures keep their startup, cleanup and container behaviour.
- Out of scope: the TUnit adapter library, the `mrepo` scaffolding templates (`src/Monorepo/Cli/src/Templates/*`, which generate projects and catalogs for other repositories too), the code of `mrepo` itself, and adopting `TestContext.Current.CancellationToken` in the tests.

### 1. Phase 1: xUnit v3 on Microsoft.Testing.Platform

1. **Remove the two empty test projects** `src/Monorepo/Repo.Generator/test/` and `src/Platform/Supervisor/Abstraction/test/` (only a `.csproj` each, no source, not in `42.mono.slnx`, nothing references them). Migrated, each would exit 8 for running zero tests; kept on the old packages, they would not restore once the catalog drops them.
2. **Catalog.** Add `Microsoft.Testing.Extensions.TrxReport` 2.5.2 (the version TUnit 1.78 uses) and `xunit.v3` 4.0.2. Remove `coverlet.collector` (a VSTest collector, no consumer, does not work under MTP; MTP's coverage is `Microsoft.Testing.Extensions.CodeCoverage`, which TUnit already brings), `Microsoft.NET.Test.Sdk`, `xunit` and `xunit.runner.visualstudio` once nothing references them. `FluentAssertions` `[7.2.2]` (xUnit v3 support since 7.1.0; it finds `xunit.v3.assert` by name), `Moq`, `Shouldly`, `Testcontainers*` and `TUnit*` stay.
3. **The 29 project files** under `src/**/test/` and `src/**/testint/`: `xunit` → `xunit.v3`, add `Microsoft.Testing.Extensions.TrxReport`, remove `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`, and add to the first `PropertyGroup`:

   ```xml
   <!-- xUnit v3 on Microsoft.Testing.Platform: the tests are an executable; 'dotnet run' takes the options of 'dotnet test'. -->
   <OutputType>Exe</OutputType>
   <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
   ```

   In every project file, not in `Directory.Build.props`: the props are evaluated before the project body, so they cannot see `IsTestProject`, and the SDK reads `OutputType` before `Directory.Build.targets` would be imported. `IsPackable`, `IsTestProject`, the other packages and all project references stay.
4. **Code.**
   - Remove `using Xunit.Abstractions;` from the five files listed in "Current State"; `ITestOutputHelper` comes from `using Xunit;` or the global usings.
   - `IAsyncLifetime`: `InitializeAsync` and `DisposeAsync` return `ValueTask` in the four fixtures (`ValueTask.CompletedTask` for the no-op disposals); bodies unchanged.
   - `TheoryData` rows: `row.Data` instead of `row[0]`/`row[1]` at the three sites.
   - `UnixFactAttribute` forwards `[CallerFilePath]` and `[CallerLineNumber]` to `FactAttribute`.
   - Any other compile error or new warning of the v3 build is fixed with the smallest change, and listed in the review.
5. **Analyzers.** `xUnit1051` (use `TestContext.Current.CancellationToken`) would add thousands of warnings to test code that passes no token today. `.editorconfig` gets a section for `[**/{test,testint}/**.cs]` that sets `dotnet_diagnostic.xUnit1051.severity = suggestion`; adopting the token is a follow-up.
6. **Runner.** `global.json` gets `"test": { "runner": "Microsoft.Testing.Platform" }` (SDK, `rollForward` and `msbuild-sdks` unchanged).
7. **Traversals.** The `Test="false"` line and its comment leave `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj`; the traversals stay the build entry points (`dotnet build src`). The comment in `UI.Admin.AutomationTests.csproj` that says the traversals do not test the project is updated.
8. **Phase gate.** `dotnet build src` without errors on Windows; each migrated project runs on its own with `dotnet test --project <path>` with its baseline count, including `UI.Automation.Runtime.TUnit.UnitTests` (28); one deliberately failing FluentAssertions assertion in a scratch copy reports as a failed xUnit v3 test.

### 2. Phase 2: Commands, CI and documentation

1. **The single test command** is `dotnet test --solution 42.mono.slnx` (one project: `dotnet test --project <path>`); `dotnet test src` is gone until .NET 11 brings traversal support. The UI suite is part of it and skips without `Target:BaseUrl` (exit 0).
2. **CI.** `storyteller-conformance.yml`: both steps → `dotnet test --project <csproj>`. `ui-admin-automation.yml` stays on `dotnet run … -- --treenode-filter … --report-trx --results-directory …`, which keeps working.
3. **`mrepo.json`.** The two `"test": "dotnet test test"` scripts → `"dotnet test --project test"`; `docs/Monorepo/mrepo-json.md` follows.
4. **`CLAUDE.md`.** Tech Stack (xUnit v3 on MTP), the `global.json` line of the structure, Build Commands (the solution command, a single project, `dotnet test src` gone), Testing (`xunit.v3` with `OutputType Exe` and `UseMicrosoftTestingPlatformRunner`, no `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet`; xUnit filters `--filter-class`, `--filter-method`, `--filter-namespace`, `--filter-trait`, `--filter-query`; TUnit's `--treenode-filter`; framework options only with `--project`; `--report-trx`; the IDE settings for MTP), and the TUnit paragraph without the VSTest/`Test="false"` explanation.
5. **Docs and readmes.** `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`, `docs/Libraries/Testing/UI.Automation/readme.md` and `src/Platform/UI.Admin.AutomationTests/README.md`: the suite runs under `dotnet test` (solution or `--project`), skips without a target and exits 0; the `Test="false"` rule of the recipe is gone; the `dotnet run` examples stay. The other commands listed under "Entry points that change" move to `--project` and the MTP options (`--filter-namespace`/`--filter-class` instead of `--filter "FullyQualifiedName~…"`, `--output Detailed` instead of `--logger "console;verbosity=detailed"`), including `docs/Platform/development.md`, `src/Platform/Storyteller/Binding.JsonE/src/VENDORED.md` and the comment in `Api.Functions/testint/HostRoutingTests.cs`.
6. **Phase gate.** The changed commands run as documented (the conformance projects with `--project`, the filters, `mrepo.json`'s form).

### 3. Phase 3: Linux and the final gate

1. **`obj\Debug` on Linux.** `ui.admin.esproj` and `sdk.typescript.esproj` get a target that runs before `GeneratePackageJsonProps` and sets `PackagePropsPath` to `$([MSBuild]::NormalizePath('$(MSBuildProjectDirectory)', '$(IntermediateOutputPath)', 'package.g.props'))`.
2. **Final gate, Windows**: `dotnet build src`, then `dotnet test --solution 42.mono.slnx --report-trx --results-directory <dir>` with Docker and `STORYTELLER_TESTS_USE_CONTAINER=true`; **Linux** (WSL, fresh clone): `dotnet build src`, then the same `dotnet test` without the variable. Expected: the 29 assemblies with the baseline counts and the same environmental failures, plus the UI suite with 6 skipped tests; every difference is explained in the review (folded theory rows, duplicate rows, display names). If the Cosmos or Lowkey Vault suites interfere more than in the baseline under MTP's parallel modules, the run is repeated with `--max-parallel-test-modules` and the review records it; the cleanup logic does not change.
3. **Review** per phase in `docs/specs/reviews/`.

### 4. Follow-ups (not in this change)

- `mrepo build -t` at the repository level: a traversal under `dotnet test` needs .NET 11 (or a `test` script / the solution).
- The `mrepo` scaffolding (`ProjectTestCsprojT4`, `RootDirectoryPackagesPropsT4`, `GlobalJsonT4`, `MrepoJsonT4`) still generates xUnit v2/VSTest projects; in this repository a `tests-xunit` project from `mrepo new project` would not restore after this change.
- Pass `TestContext.Current.CancellationToken` in the tests and raise `xUnit1051` back to warning.
- Isolate the Cosmos DB and Lowkey Vault fixtures (per-module databases and containers) so the infrastructure suites can run in parallel.
- On .NET 11: `dotnet test` of the traversals (`IsTraversal`) and the forgiving aggregation of exit code 8.
