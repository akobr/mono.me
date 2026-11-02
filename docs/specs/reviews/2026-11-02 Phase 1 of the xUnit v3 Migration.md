# Phase 1 of the xUnit v3 Migration: xUnit v3 on Microsoft.Testing.Platform

## Overview

Phase 1 of [Migrate All Test Projects to xUnit v3 and Microsoft.Testing.Platform](../2026-11-02%20Migrate%20All%20Test%20Projects%20to%20xUnit%20v3%20and%20Microsoft.Testing.Platform.md) (Linear [424-151](https://linear.app/424fun/issue/424-151/migrate-all-test-projects-to-xunit-v3-and-the-microsofttestingplatform)): the two empty test projects are gone, the 29 xUnit 2.9.3 projects run on `xunit.v3` 4.0.2 with the TRX extension of Microsoft.Testing.Platform (MTP), `global.json` runs `dotnet test` on MTP, and the traversals no longer leave the TUnit UI suite out of their `Test` target. Every assembly runs the baseline's number of tests, and the build has the baseline's warnings, no more and no fewer. Commits: `b4ca7ae9` (the empty projects), `4363ac90` (the migration).

## What Was Done

### 1. The two empty test projects

- `src/Monorepo/Repo.Generator/test/Repo.Generator.Tests.csproj` and `src/Platform/Supervisor/Abstraction/test/Abstraction.Tests.csproj` are deleted, with their folders (`b4ca7ae9`). Nothing referenced them; only historical reviews under `docs/**/specs/reviews/` name them.

### 2. Catalog

- Added: `Microsoft.Testing.Extensions.TrxReport` 2.5.2 (between `Microsoft.Playwright` and `Microsoft.VisualStudio.Azure.Containers.Tools.Targets`), `xunit` 2.9.3 → `xunit.v3` 4.0.2 (in its place, before `YamlDotNet`; its comment named the Linear issue and is gone).
- Removed: `coverlet.collector` 10.1.0, `Microsoft.NET.Test.Sdk` 18.11.0, `xunit.runner.visualstudio` 4.0.1. Nothing in the repository build references them any more; the `mrepo` scaffolding templates have their own catalog text (out of scope, see the spec's follow-ups).
- The xUnit projects resolve `Microsoft.Testing.Platform` 2.5.2 (the TRX extension raises the 2.5.1 of `xunit.v3.core.mtp-v2`), the same as the UI suite through TUnit 1.78 (`obj/project.assets.json` of `Backend.Core/test` and of the UI suite).
- `FluentAssertions` `[7.2.2]` stays: a deliberately failing `41.Should().Be(42, …)` in a scratch `xunit.v3` 4.0.2 project is reported as a failed test with FluentAssertions' message ("Expected value to be 42 because the answer is 42, but found 41"), exit code 2.

### 3. The 29 project files

- A script changed every project under `src/**/test/` and `src/**/testint/` that referenced `xunit`: `xunit` → `xunit.v3`, `Microsoft.Testing.Extensions.TrxReport` added in alphabetical order, `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` removed, and after `IsTestProject`:

  ```xml
  <!-- xUnit v3 on Microsoft.Testing.Platform: the tests are an executable, and 'dotnet run' takes the options of 'dotnet test'. -->
  <OutputType>Exe</OutputType>
  <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  ```

- `Async.UnitTests.csproj` listed its packages out of order (`FluentAssertions` last); its group is now alphabetical.
- `UI.Automation.Runtime.TUnit.UnitTests` (the xUnit project that gets `TUnit.Core` from the library under test) needed nothing more: one entry point (xUnit's), no TUnit registration.

### 4. Code

- `using Xunit.Abstractions;` removed from `Binding.JsonE/test/SpecificationParityTests.cs`, `Binding.Object/test/AttackCorpusTests.cs`, `Access.AuthKit/testint/M2MApplicationLifecycleTests.cs`, `Api.Functions/testint/HostRoutingTests.cs`, `Mcp/testint/TokenExchangeTests.cs` (with the blank line of a using group that became empty).
- `IAsyncLifetime`: `InitializeAsync`/`DisposeAsync` return `ValueTask` in `Backend.CosmosDb/test/Startup.cs` and `KeyValueEndToEndTests.cs`, `Access.Certificates/testint/CosmosFixture.cs` and `KeyValues.Azure.KeyVault/testint/LowkeyVaultFixture.cs`; the no-op disposals return `ValueTask.CompletedTask`. Bodies unchanged.
- `TheoryData` rows: `var (key, member) = row.Data;` and `row.Data.Item1`/`Item2` in `Backend.Core/test/RelationNamesJsonTests.cs`, `row.Data.Item2` in `Api.Functions/test/AnnotationsHttpTests.cs`.
- `Configuration/test/UnixFactAttribute.cs` takes `[CallerFilePath]`/`[CallerLineNumber]` and passes them to `FactAttribute`; the two `[UnixFact]` tests still skip on Windows and run on Linux.
- Found by the warning comparison (section 5): `{ null }` in a `TheoryData<string?>` binds to xUnit v3's new `Add(TheoryDataRow<string?>)` overload and gives `CS8625`. The three rows (`Access.Federation/test/StorytellerUserTokenValidationTests.cs:41,52`, `Configuration/test/References/SecretReferenceTests.cs:115`) are `(string?)null` now, which binds to `Add(string?)`.

### 5. Analyzers

- `.editorconfig` has a new section `[**/{test,testint}/**.cs]` with `dotnet_diagnostic.xUnit1051.severity = suggestion` and a comment; the first v3 build had 1,343 `xUnit1051` warnings.
- Warnings compared by file, line and code between a full rebuild of the baseline clone (`030eb6c9d`) and a clean build of the branch: 1,054 distinct warnings on both sides, the same set. `xUnit1031` at `Async/test/AsyncLazyTests.cs:47` existed under the v2 analyzers too.

### 6. Runner

- `global.json` has `"test": { "runner": "Microsoft.Testing.Platform" }`; `sdk` and `msbuild-sdks` are unchanged.

### 7. Traversals

- The `Test="false"` line and its comment are gone from `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj`.
- The comment in `UI.Admin.AutomationTests.csproj` now says that `dotnet test` runs the project with the solution or on its own. Its first wording quoted the `--project` option, and MSBuild refused the file (`MSB4025`: an XML comment cannot contain `--`).

### 8. Phase gate

- **Build.** `dotnet build src` without errors. Notable: the first full build of the worktree failed once in `Crumble/SourceGenerator.Orleans` (`CS2012`, then `MSB4018` of the versioning task's `CompareFiles`): the traversal builds the generator, and `Examples.Crumble` and `Examples.Full` build it again through their analyzer reference with `SetTargetFramework`, two builds with different global properties in the same `obj` folder. The next build passed, and the clean build of the final gate passed at once. It is a race of the Crumble playground, not of this change.
- **Tests.** Each of the 30 test projects of `42.mono.slnx` ran on its own (`dotnet test --project <path> --no-build --report-trx`, Docker, `STORYTELLER_TESTS_USE_CONTAINER=true`): every xUnit assembly has the baseline's number of results, and the UI suite reports 6 skipped tests and exit code 0.

  | Assembly | Baseline (VSTest, `dotnet test src`) results / failed | Phase gate (MTP, one project at a time) results / failed |
  |---|---|---|
  | 27 assemblies without infrastructure | 11,909 / 2 | 11,909 / 0 |
  | `platform.storyteller.access.certificates.integrationtests` | 73 / 73 | 73 / 73 |
  | `platform.storyteller.backend.cosmosdb.unittests` | 642 / 196 | 642 / 2 |
  | `platform.storyteller.keyvalues.azure.keyvault.integrationtests` | 13 / 2 | 13 / 1 |
  | `platform.ui.admin.automationtests` (TUnit) | not run | 6 skipped / 0 |
  | **Total** | **12,637 / 273** (2 skipped) | **12,643 / 76** (8 skipped) |

  The failures are the environment, as in the baseline: the Cosmos DB emulator container restarted while `Access.Certificates.IntegrationTests` ran ("The SSL connection could not be established"); alone, `Backend.CosmosDb.UnitTests` failed only `KeyValueEndToEndTests.Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos` (Lowkey Vault retries) and `CosmosConfigurationTemplateServiceTests.Invalidation_MoreThan100ConfigurationsInPartition` (connection refused by the emulator), and `KeyValues.Azure.KeyVault.IntegrationTests` failed `CustomerKeyProviderTests.WrapAndUnwrap_UseTheCustomerKeyAndFollowItsRotation` (Lowkey Vault retries). The two load-sensitive tests of the baseline's parallel run passed.
- `dotnet run --project src/Libraries/Utils/Async/test --no-build -- --filter-method "*HasNoValue"` runs MTP (1 test), so `UseMicrosoftTestingPlatformRunner` gives `dotnet run` the options of `dotnet test`.
- Display names keep the v2 form (`Vector_StopsOnLimit(name: "…")`), and the TRX of `dotnet test` lists every theory row as a result, folded rows included (`Access.Keycloak.UnitTests` 138 results of 135 definitions, `Api.Functions.UnitTests` 2,743 of 2,712, `Mcp.UnitTests` 728 of 659, as in the VSTest TRX).
