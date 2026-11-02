# Phase 2 of the xUnit v3 Migration: Commands, CI and Documentation

## Overview

Phase 2 of [Migrate All Test Projects to xUnit v3 and Microsoft.Testing.Platform](../2026-11-02%20Migrate%20All%20Test%20Projects%20to%20xUnit%20v3%20and%20Microsoft.Testing.Platform.md) (Linear [424-151](https://linear.app/424fun/issue/424-151/migrate-all-test-projects-to-xunit-v3-and-the-microsofttestingplatform)): every documented and automated test command moves to the forms that Microsoft.Testing.Platform (MTP) mode takes on the .NET 10 SDK, `dotnet test --solution 42.mono.slnx` for everything and `dotnet test --project <path>` for one project, and the UI automation docs describe the suite under `dotnet test`. Commit: `7a7a50b2`.

## What Was Done

### 1. The single test command

- `dotnet test --solution 42.mono.slnx` replaces `dotnet test src` everywhere it was documented (`CLAUDE.md`); the UI suite is part of it and skips without `Target:BaseUrl`. `dotnet build src` stays the build command.

### 2. CI

- `.github/workflows/storyteller-conformance.yml`: both steps run `dotnet test --project <csproj>`, with a comment on why. Locally both projects pass that way (`ConfigurationFormats.UnitTests` 416, `Configuration.UnitTests` 641 with the 2 `UnixFact` skips on Windows).
- `ui-admin-automation.yml` is unchanged (`dotnet run … -- --treenode-filter … --report-trx --results-directory …`).

### 3. `mrepo.json`

- Both `"test": "dotnet test test"` scripts (types `dotnet-project` and `dotnet-azure-functions`) are `"dotnet test --project test"`; `docs/Monorepo/mrepo-json.md` follows. Run in an item's folder the form works (`ConfigurationFormats`: 416 passed).
- Not changed: the example in `src/Monorepo/Cli/src/mrepo.schema.json` and `MrepoJsonT4`, which belong to the scaffolding for other repositories (out of scope).

### 4. `CLAUDE.md`

- Tech Stack: "xUnit v3 (primary, `xunit.v3` 4.0 on Microsoft.Testing.Platform)". The structure's `global.json` line names the test runner. The UI automation row of the Platform table: run with `dotnet test` (also as part of the solution) or `dotnet run`.
- Build Commands: `dotnet test --solution 42.mono.slnx`, one project with `--project` and an xUnit filter, the UI suite through `dotnet test --project … --treenode-filter …` next to the `dotnet run` lines.
- Testing: xUnit v3; a new bullet on MTP (the project shape: `xunit.v3`, `Microsoft.Testing.Extensions.TrxReport`, `OutputType Exe`, `UseMicrosoftTestingPlatformRunner`, no `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet.collector`; a test project belongs in the solution and needs at least one test; options that reach every module and the framework options that go only with `--project`; the IDE settings; `xUnit1051` as a suggestion). The TUnit paragraph says the suite is part of the solution run instead of explaining VSTest mode and `Test="false"`. "Run a specific test project" uses `--project`.

### 5. Docs and readmes

- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`: the "Not `dotnet test`" bullet is now "`dotnet test`" (the solution run, `--project` with the options of `dotnet run` but without `--`, TUnit's options only with `--project`); two `dotnet test` lines in the run block; where the reports go under `dotnet test` (`TestResults/` of the current directory or `--results-directory`; the runtime's `artifacts/<runId>/` stays next to the assembly, `Execution:ArtifactsPath` is relative to it); the troubleshooting row about the VSTest error is replaced by two rows: the "All projects must use that test runner" error and exit code 5 of a TUnit option at solution scope.
- `docs/Libraries/Testing/UI.Automation/readme.md`: step 1 of the recipe adds the project to `42.mono.slnx` instead of `Test="false"` to the traversals; step 6 and the run block name `dotnet test --project`; the last note of "Running" says that `dotnet test` runs a TUnit project only in MTP mode.
- `src/Platform/UI.Admin.AutomationTests/README.md`: `dotnet run` or `dotnet test --project`, and the solution run.
- `--project` in `docs/Platform/development.md`, `docs/Platform/Storyteller/conformance.md`, `src/Platform/Storyteller/conformance/README.md`, `src/Platform/Storyteller/Sdk.NSwag/README.md`, `src/Platform/Storyteller/sdk.typescript/README.md`, `src/Platform/Storyteller/Binding.JsonE/src/VENDORED.md` and the comment of `Api.Functions/testint/HostRoutingTests.cs`.
- **Deviation:** the spec replaced `--filter "FullyQualifiedName~…"` with `--filter-namespace`/`--filter-class`. xUnit v3 4.0 takes the VSTest filter syntax under MTP, with the same results as VSTest (`Configuration.UnitTests` `~Conformance` 113 tests, `Api.Functions.UnitTests` `~OpenApiExportTests` 23, both forms and both runners), so the commands keep their filter and only gain `--project`.
- `docs/Platform/Storyteller/mcp.md`: `--logger "console;verbosity=detailed"` → `--output Detailed`, which shows the "Standard output" of the passing tests (the two `Skipped: …` lines of `Mcp.IntegrationTests`).
- The final review (Phase 3, section 4) added that `dotnet test` does not show the suite's `ui-automation:` lines of a passing run, in `automation-tests.md` and the library readme.
- Not changed: the passages of `automation-tests.md` and of `ui-admin-automation.yml` about the CI step with the secrets. A check during the research of this work found that `dotnet run --no-build` still runs the `ComputeRunArguments` target and the targets hooked to it, so "`dotnet run --no-build` runs no build targets there" (`automation-tests.md`, "CI") overstates what `--no-build` keeps out; that wording predates this change and is a follow-up.

### 6. Phase gate

- The changed commands ran as documented: the two conformance projects with `--project`, both filters, `--output Detailed`, and the `mrepo.json` form in an item's folder. A search outside `docs/**/specs/**` and the scaffolding templates finds no command or instruction left that uses `dotnet test src`, a positional `dotnet test <path>`, `Test="false"`, `--logger` or a VSTest-only package.
