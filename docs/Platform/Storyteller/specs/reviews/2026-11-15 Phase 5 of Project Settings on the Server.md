# Phase 5 of Project Settings on the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

Phase 5 closes [Project Settings on the Server](../2026-11-15%20Project%20Settings%20on%20the%20Server.md) with the MCP server (spec section 9) and the remaining documentation (section 10). `list_projects` already returned each project's `Settings` once the C# SDK was regenerated in phase 1. This phase describes them to the assistant, asks it to talk in the project's type names while using the type codes in calls, and tests that the settings reach the assistant. The documentation of the conformance suite now names every user and runner of the catalog of project settings, and `CLAUDE.md` follows. The MCP server does not write settings.

| Commit | Subject |
| --- | --- |
| `810353d4a` | feat(platform): return the project settings from the MCP discovery (424-266) |
| `56bf63aa3` | docs(platform): align the documentation of the project settings (424-266) |

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

### Test results

Run again for this review:

| Project | Result |
| --- | --- |
| `Mcp/test` | 753 passed |

### Reviews

The review of phases 4 and 5 confirmed two findings, both low and both documentation: this review and the one of [phase 4](2026-11-15%20Phase%204%20of%20Project%20Settings%20on%20the%20Server.md) were missing, and a row of `data-sets.md` (phase 4) still said that an existing project is used as it is. Both are fixed; the details are in the phase 4 review.

### Deviations from the spec

1. **No change of the tool's result.** The spec expected `list_projects` to return the settings "once the C# SDK is regenerated". That happened in phase 1, so this phase changed only the description and the instructions.
2. **Two host tests instead of one.** The second checks the description, the read-only hint and the instructions, which tell the assistant how to use the settings.
3. **`mcp.md` explains the type names** in a paragraph below the Discovery table, besides the table row and the Limits line that the spec names.
4. **The admin UI as a runner of the conformance kind.** Section 10 gave the runners of `conformance.md` to phase 1. The admin UI's catalog test came with the UI phase, so its row, its command and its workflow were added here, with `sform` and DbCreator as users of the catalog.

### Left for later

- `list_projects` was not tried with a real assistant against a deployed API. The host test covers the path from the API's answer to the tool's result.
