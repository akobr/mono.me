# Role Notes in the Help of sform

## Overview

The help of `sform` now uses one style for the role a command or an option needs. The style is a note at the end of the first sentence of the description, such as "(for administrators)" or "(for contributors)". The note names the lowest role. "(Administrator)", "(Contributor)", "(Administrator or Owner)", "You need to be administrator or owner." and "Needs the Administrator role" are gone. Commands whose description wrapped in their parent's list now take one line there, and their detail follows the options of their own help. A new test guards the style across every command of `sform`. The plan is [2026-11-15 Role Notes in the Help of sform](../2026-11-15%20Role%20Notes%20in%20the%20Help%20of%20sform.md) (Linear 424-222). It was delivered in four commits, one per phase of section 7 of the plan.

## What Was Done

### 1. The style

As planned. A note ends the first sentence of the description of a command or option, and it names the lowest role ("(for administrators)" covers Owners). A note says nothing about machines. The six new details say it in full sentences: "A user needs the Administrator role on the project. A machine cannot …, whatever its scopes." Each detail is a `HelpDetail` raw-string constant of its command, in the form 424-216 introduced: an empty first line, and no line longer than 79 characters.

One rule was added during implementation: **a note must not break across two lines.** McMaster wraps a description at a space, so a description that is too long for its column can end a line with "(for" and start the next with "administrators).". Two of the planned option texts did this (see section 3), so they are shorter, and the guard of section 4 checks every help.

### 2. `sform story kv` (phase 1)

Commit `fix(platform): give the role notes of sform story kv one style (424-222)`.

| Command or option | Description | File |
| --- | --- | --- |
| `audit` | "The audit trail of secrets (for administrators)." + detail | `KeyValueAuditCommand.cs` |
| `usage` | "The project's key/value usage (for administrators)." + detail | `KeyValueAuditCommand.cs` |
| `purge` | "Purge soft-deleted keys (for administrators)." + detail | `KeyValueBulkCommands.cs` |
| `policy set` | "Change the policy (for administrators)." + detail | `KeyValuePolicyCommand.cs` |
| `delete --permanent` | "Remove the key for good (for administrators)." | `KeyValueDeleteCommand.cs` |

The details are as planned. The only change is the wrapping of the role sentence of `audit` and `usage`.

**Deviation:** the plan gave `delete --permanent` the text "Remove the key and every version for good (for administrators).". In the 58-column column of the option list of `kv delete`, that breaks the note after "(for". The option says "Remove the key for good (for administrators)." instead. The command's own description already says that `--permanent` removes the key with every version.

`sform story kv --help` now lists `audit`, `purge` and `usage` on one line each, and `sform story kv policy --help` lists `set` on one line.

### 3. The account commands and `sform story delete` (phase 2)

Commit `fix(platform): give the role notes of the account commands and story delete of sform one style (424-222)`.

| Command or option | Description | File |
| --- | --- | --- |
| `account machine issuer list` | "List the trusted issuers of the project (for contributors)." | `MachineIssuerListCommand.cs` |
| `account machine issuer set` | "Create or replace a trusted issuer (for administrators)." + detail | `MachineIssuerSetCommand.cs` |
| `account machine issuer delete` | "Delete a trusted issuer (for administrators)." + detail | `MachineIssuerDeleteCommand.cs` |
| `account points get` | "Get an access point with its users (for administrators)." | `AccessPointGetCommand.cs` |
| `account machine create -m\|--manage` | "A management scope to grant (for administrators). One of … ; repeat it or separate the values with commas. Never part of -r or -w." | `MachineCreateCommand.cs` |
| `story delete --permanent` | "Delete permanently (for administrators)." | `StorytellerDeleteCommand.cs` |

`MachineCreateCommandTests.ManagementScopeHelp_NamesTheScopesAndTheAdministratorRole` expects the new text of `-m`.

**Deviations:**

- `story delete --permanent`: the planned "Delete permanently, without a way to restore it (for administrators)." breaks the note in the 61-column column of the option list. The option says "Delete permanently (for administrators)." instead. "Permanently" says it. The command's own description says that a normal delete can be restored until a purge.
- The detail of `issuer set` leaves out "as with the issuer PUT". That is a term of the API, not of the CLI. It now starts: "Settings not given are cleared, and the issuer URL (--issuer) of an existing issuer cannot change."

### 4. Tests (phase 3)

Commit `test(platform): guard the style of the role notes in the help of sform (424-222)`.

- `test/Commands/HelpTexts.cs` has the helpers that `TransferHelpTests` had as private methods: `Width` (80), `Command<TRoot>(path)` and `Render(command)`. `Command<TRoot>` matches a word to a name or an alias, so `story` finds `storyteller`, and an empty path returns the root. `TransferHelpTests` uses them, and its tests do not change.
- New `test/Commands/RoleNoteTests.cs`:
  - `EveryDescription_NamesItsRole_InTheNoteStyle` walks every command below `PlatformCommand`. For each command, it reads the descriptions of the command and of its own options and arguments: more than 100 texts, with at least the 13 notes of sections 2 and 3. No text has a role right after an opening parenthesis. Every "(for …)" note other than "(for example …)" is one of the five forms, is followed by a full stop, and comes before the first ". " of its text.
  - `NoNote_BreaksAcrossLines_InAnyHelp` renders the help of every command at 80 columns. It checks that no line has a "(for …" without its closing parenthesis. This test was not in the plan. It guards the rule of section 1.
  - `ParentList_GivesTheCommandOneLine` covers the eight commands of sections 2 and 3, plus `story export` and `story import`.
  - `Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions` covers the six commands with a detail. It also checks the 79-column width and trailing spaces. The plan had planned this as a separate test.
  - `Detail_EndsWithTheRole_AndThatAMachineCannot` checks the role sentences. `Detail_KeepsWhatTheDescriptionSaid` checks that what left the description is still in the detail. `Option_EndsItsFirstSentenceWithTheNote` checks the three options.
- `dotnet test --project src/Platform/Cli/test`: 2051 tests passed, 33 of them new. `sform --help` of every changed command and its parent was also rendered from the build, redirected, and checked by eye.

### 5. Documentation

Commit `docs(platform): describe the role notes in the help of sform (424-222)`.

`docs/42for.net/cli/sform.md` has a new paragraph after the introduction. It covers `--help` on every command and the note at the end of the first sentence. The note names the lowest role, so an Owner can run what is for administrators. A machine has no role, and where it matters, the help says after the options what a machine needs or that it cannot (`story export`, `story kv audit`). No document quoted the changed texts. The lines of `story export` and `story import` that `export-import.md` quotes did not change.

### 6. Out of scope

As planned. Some commands need a role but have no note. These are the Administrator operations of `kv policy` (`rotate`, `byok-setup`, `byok-verify`), the recipient changes of `kv recipients`, and `points grant` and `revoke`. They are left for a follow-up, which has to check each one against the API. The messages `sform` prints, the rules of `--scope`, the secret rights, and the wording of the test cases of `Cli.AutomationTests` are unchanged.
