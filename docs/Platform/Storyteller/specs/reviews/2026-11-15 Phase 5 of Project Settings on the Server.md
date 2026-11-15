# Phase 5 of Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

Phase 5 closes [Project Settings on the Server](../2026-11-15%20Project%20Settings%20on%20the%20Server.md) with the MCP server (spec section 9) and the remaining documentation (section 10). `list_projects` already returned each project's `Settings` once the C# SDK was regenerated in phase 1. This phase describes them to the assistant, asks it to talk in the project's type names while using the type codes in calls, and tests that the settings reach the assistant. The documentation of the conformance suite now names every user and runner of the catalog of project settings, and `CLAUDE.md` follows. The MCP server does not write settings. The phase ends with the final regression run of the whole branch (spec section 13), which passed without a fix.

| Commit | Subject |
| --- | --- |
| `810353d4a` | feat(platform): return the project settings from the MCP discovery (424-266) |
| `56bf63aa3` | docs(platform): align the documentation of the project settings (424-266) |
| `4a149eac6` | fix(platform): add the reviews of phases 4 and 5 of the project settings on the server (424-266) |

The final regression run made no commits: nothing failed.

## What Was Done

### 9. MCP server

As planned, with these details:

- `Mcp/src/Tools/DiscoveryTools.cs`: the description of `list_projects` names the project's `Settings`:
  - the names (`Label`, `Plural`), colors and icons of the annotation types in `Types`, by type code; a type left out keeps its built-in name, such as Responsibility for `rst`;
  - `DefaultTimeZone`, an IANA id, absent for `UTC`;
  - `DateFormat`, absent for `yyyy-mm-dd`;
  - "Talk to the person in the project's type names; keys and arguments always take the type codes."

  The tool itself did not change. It returns `GET v1/access/projects` through the SDK, whose `ProjectSummary` has had `Settings` since phase 1.
- `Mcp/src/McpInstructions.cs`: one line under "Addressing". A project can name its annotation types (`Settings.Types` of `list_projects`, by type code); the assistant talks to the person in those names, such as "Solution CRM" for `rst.crm` when `rst` is named "Solution", but uses the type codes and keys in calls.
- `Mcp/test/DiscoveryToolsTests.cs` (new), on `McpTestHost` and `RecordingApiHandler`:
  - `ListProjects_ReturnsTheSettingsOfEachProject`: one `GET …/api/v1/access/projects`. The settings of a project that has them (types, time zone, date format, version, `Modified`) and of one that never had them (no types, no time zone, no date format, no `Modified`, version 0) reach the structured result and the text as the API returns them.
  - `ListProjects_TellsTheAssistantToTalkInTheTypeNames`: the tool's description names `Settings`, `DefaultTimeZone`, `DateFormat` and the type codes; the tool stays read-only; the server instructions mention `Settings.Types of list_projects`.
- `docs/Platform/Storyteller/mcp.md`:
  - the Discovery table: `list_projects` returns the role, the description and the settings;
  - a paragraph below it: the type names, the defaults of a missing time zone and date format, and that the server only reads the settings (`sform project settings` and the admin UI change them);
  - under Limits, "project settings writes" among what is not offered.

### 10. Documentation (phase-5 rows and the rest)

As planned, with these details:

- `docs/Platform/Storyteller/conformance.md`:
  - the `project-settings` kind says that `sform` and DbCreator read and check settings with the same types as the API, and that the admin UI checks its own lists against the catalog;
  - a new runner row: the admin UI's `test/unit/utils/project-settings-catalog.test.ts` (the UI's colors, icons, date formats, limits, defaults and built-in names against the catalog, the browser's time zones in it, and every vector through the UI's reading and writing);
  - its command under "Run", and that `Platform.UI.Admin.yml` runs it, which a change of `project-settings.json` triggers too.
- `src/Platform/Storyteller/conformance/README.md`: the same users and the admin UI's runner with its command.
- `CLAUDE.md`:
  - the line of the conformance suite mentions the catalog of project settings (`project-settings.json`, embedded by `Abstractions.Annotations` as `ProjectSettingsCatalog`), its users and the admin UI as a runner;
  - the line of `Platform.Storyteller-conformance.yml` names the project settings runner of Backend.Core (`ProjectSettingsConformanceTests`).
- The other rows of section 10 came with their phases: `access.md`, `authentication.md`, `key-values.md` and the SDK READMEs in phase 1; `admin-ui.md` and the admin UI's README in phase 2; `sform.md`, the automation test cases and their counts in phase 3; `data-sets.md` in phase 4.
- A sweep of `access.md`, `authentication.md`, `key-values.md`, `data-sets.md`, `sform.md`, `admin-ui.md` and `docs/github/workflows.md` against the code found nothing else out of date; the case count 269 and the workflow paths match.

### 13. Verification: the final regression run

As planned, with these details:

- The run was made on `4a149eac6`, the head of the branch after the fixes of the review. It covers every command of section 13 and more: the C# SDK, the Hosting library, the whole `Cli.AutomationTests` and `UI.Admin.AutomationTests` without a target, three more classes of `Backend.CosmosDb/test` that the branch touches (`*MachineAuthentication*`, `*KeyValuePolicy*`, `*KeyValueAudit*`), and the admin UI's lint, build and unit tests.
- `dotnet build src/Platform` first, then every .NET test project with `--no-build`. A full `--no-incremental` rebuild gave 0 errors. The ten projects of `src/Libraries/Platform` were built one by one, with 0 errors and 0 to 4 warnings each.
- The Cosmos DB suites ran with `STORYTELLER_TESTS_USE_CONTAINER=true` on the reused vNext container: `Backend.CosmosDb/test` in five class chunks, then the whole `DbCreator/testint` in one run. No other test host was running.
- The TypeScript SDK: `npm run generate` and `git diff --exit-code -- generated` showed no change (only line-ending notices), then `npm test` and `npm run build`.
- The admin UI: `npm run prepare:local` found the SDK and the UI up to date. Vitest ran with `--no-experimental-webstorage`. The UI build rewrote `components.d.ts` and `typed-router.d.ts` with line-ending changes only; both were restored, and the working tree was clean at the end.
- Nothing failed, so the run made no commits.

### Test results

Run again for this review:

| Project | Result |
| --- | --- |
| `Mcp/test` | 753 passed |

The final regression run (`4a149eac6`):

| Run | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| `dotnet build src/Platform` | 0 errors | | | |
| `Backend.Core/test` | 1573 | 1573 | 0 | 0 |
| `Api.Functions/test` | 3158 | 3158 | 0 | 0 |
| `Sdk.NSwag/test` | 424 | 424 | 0 | 0 |
| `Mcp/test` | 753 | 753 | 0 | 0 |
| `Cli/test` | 2292 | 2292 | 0 | 0 |
| `DbCreator/test` | 166 | 166 | 0 | 0 |
| `Libraries/Platform/Storyteller/Configuration/test` | 655 | 653 | 0 | 2 (`OnUnix_*` of `LastKnownGoodCacheTests`, on Windows) |
| `ConfigurationFormats/test` | 616 | 616 | 0 | 0 |
| `Hosting/test` | 348 | 348 | 0 | 0 |
| `Cli.AutomationTests` (no target) | 359 | 90 | 0 | 269 |
| `UI.Admin.AutomationTests` (no target) | 234 | 0 | 0 | 234 |
| `Backend.CosmosDb/test` `*ProjectSettings*` | 17 | 17 | 0 | 0 |
| `Backend.CosmosDb/test` `*CosmosAccessServiceTests` | 58 | 58 | 0 | 0 |
| `Backend.CosmosDb/test` `*MachineAuthentication*` | 4 | 4 | 0 | 0 |
| `Backend.CosmosDb/test` `*KeyValuePolicy*` | 15 | 15 | 0 | 0 |
| `Backend.CosmosDb/test` `*KeyValueAudit*` | 8 | 8 | 0 | 0 |
| `DbCreator/testint` (whole, 7 min 13 s) | 19 | 19 | 0 | 0 |
| TypeScript SDK `npm test` | 484 | 483 | 0 | 1 (one of 18 files) |
| TypeScript SDK `npm run generate`, `git diff --exit-code -- generated`, `npm run build` | passed, no change | | | |
| Admin UI `npm run lint` (oxlint, ESLint) and `npm run build` | passed | | | |
| Admin UI `npm test` (116 files) | 2209 | 2209 | 0 | 0 |

### Reviews

The review of phases 4 and 5 confirmed two findings, both low and both documentation: this review and the one of [phase 4](2026-11-15%20Phase%204%20of%20Project%20Settings%20on%20the%20Server.md) were missing, and a row of `data-sets.md` (phase 4) still said that an existing project is used as it is. Both are fixed; the details are in the phase 4 review. No finding was refused.

The final regression run found no failure. It noted four StyleCop warnings that `8615f64ca` (phase 3) added; the other warnings in files of the branch were there before it (by `git blame`). The run changes no code, so they are left (see Left for later):

- `Cli/src/Commands/Schemas/SchemaOperations.cs:9` and `Cli/src/Output/TransferConsoleExtensions.cs:5`: SA1210, `using _42.Platform.Cli.Services.Projects;` before `using _42.Platform.Cli.Services;`;
- `Cli/test/Services/Projects/ProjectDisplayCommandsTests.cs:87`: SA1116;
- `Cli/test/Services/Projects/ProjectSettingsProviderTests.cs:207`: SA1201.

### Deviations from the spec

1. **No change of the tool's result.** The spec expected `list_projects` to return the settings "once the C# SDK is regenerated". That happened in phase 1, so this phase changed only the description and the instructions.
2. **Two host tests instead of one.** The second checks the description, the read-only hint and the instructions, which tell the assistant how to use the settings.
3. **`mcp.md` explains the type names** in a paragraph below the Discovery table, besides the table row and the Limits line that the spec names.
4. **The admin UI as a runner of the conformance kind.** Section 10 gave the runners of `conformance.md` to phase 1. The admin UI's catalog test came with the UI phase, so its row, its command and its workflow were added here, with `sform` and DbCreator as users of the catalog.
5. **The final regression run differs from the commands of section 13 in two places.** `Cli.AutomationTests` ran as a whole without a target instead of only `[Category=Coverage]`; its 90 passed tests include the coverage checks. `npm ci` was not run: `npm run prepare:local` found the packages of the SDK and the UI up to date.

### Left for later

- `list_projects` was not tried with a real assistant against a deployed API. The host test covers the path from the API's answer to the tool's result.
- The four StyleCop warnings of `8615f64ca` (see Reviews): a small `style` commit can fix them.
- The final regression run left out the other classes of `Backend.CosmosDb/test`, `Access.Certificates/testint`, the classic emulator, and the admin UI's Playwright e2e and visual tests. The branch's Cosmos DB code is covered by the classes that ran; `Platform.Storyteller.CosmosDb.yml` runs the whole suites on both emulators on the pull request.
- The automation cases against a deployed API: the eleven `ACC-PRS` cases of phase 3 (not run yet) and the admin UI's settings cases of phase 2 (run against the mock only).
