# Role Notes in the Help of sform

Linear: [424-222 Align the role notes in the descriptions of sform commands](https://linear.app/424fun/issue/424-222/align-the-role-notes-in-the-descriptions-of-sform-commands).

Line numbers refer to the tree this plan was written against (`7944671e`). The issue has no comments and no CodeRabbit plan.

## Problem

The help of `sform` says in two ways which role a command needs:

- `sform story export` and `sform story import` end their one-line description with "(for administrators)". It came from 424-216 (PR #171).
- The other commands put "(Administrator)", "(Contributor)" or "(Administrator or Owner)" into the sentence, often in the middle of it. `sform account points get` says "You need to be administrator or owner." as a second sentence, and the `-m|--manage` option of `sform account machine create` says "Needs the Administrator role".

A reader of `--help` should see one pattern everywhere. Several of these descriptions are also long and wrap over two or three lines in the list of their parent.

## Current State

All paths are relative to `src/Platform/Cli/` unless they start with `docs/`.

### The role notes

| Command or option | File, line | Text today |
| --- | --- | --- |
| `sform story export` | `src/Commands/Storyteller/StorytellerExportCommand.cs`, 12 | "Export the view (for administrators)." |
| `sform story import` | `src/Commands/Storyteller/StorytellerImportCommand.cs`, 14 | "Import into the view (for administrators)." |
| `sform story delete --permanent` | `src/Commands/Storyteller/StorytellerDeleteCommand.cs`, 34 | "Delete permanently, without a way to restore it (Administrator or Owner)." |
| `sform story kv audit` | `src/Commands/KeyValues/KeyValueAuditCommand.cs`, 12 | "The audit trail of the project's secrets, newest first (Administrator). -v filters by view." |
| `sform story kv usage` | `src/Commands/KeyValues/KeyValueAuditCommand.cs`, 82 | "The key/value usage of the project over a range of days (the last 30 by default), with estimated charges when the deployment has prices (Administrator)." |
| `sform story kv purge` | `src/Commands/KeyValues/KeyValueBulkCommands.cs`, 27 | "Remove soft-deleted keys with every version before their retention ends (Administrator)." |
| `sform story kv delete --permanent` | `src/Commands/KeyValues/KeyValueDeleteCommand.cs`, 21 | "Remove the key and every version for good (Administrator)." |
| `sform story kv policy set` | `src/Commands/KeyValues/KeyValuePolicyCommand.cs`, 35 | "Change the policy (Administrator); members not given keep their value." |
| `sform account machine issuer list` | `src/Commands/MachineAccess/MachineIssuerListCommand.cs`, 9 | "List the trusted issuers of the project (Contributor)." |
| `sform account machine issuer set` | `src/Commands/MachineAccess/MachineIssuerSetCommand.cs`, 12 | "Create or replace a trusted issuer of the project (Administrator): settings not given are cleared, and the issuer URL of an existing one cannot change." |
| `sform account machine issuer delete` | `src/Commands/MachineAccess/MachineIssuerDeleteCommand.cs`, 10 | "Delete a trusted issuer of the project (Administrator), even while federation rules reference it." |
| `sform account points get` | `src/Commands/AccessPoints/AccessPointGetCommand.cs`, 15 | "Get an access point with list of users and role. You need to be administrator or owner." |
| `sform account machine create -m\|--manage` | `src/Commands/MachineAccess/MachineCreateCommand.cs`, 52 | "A management scope to grant: … Repeat it or separate the values with commas. Needs the Administrator role; never part of -r or -w." |

Found with `grep -rn -i "administrator\|contributor\|owner\|reader" src/Commands` on the `Description` of every `[Command]`, `[Option]` and `[Argument]`. No description is built from a constant, and only `export`, `import`, `kv export`, `kv import` and `account settings` set `ExtendedHelpText`.

Not role notes, and they stay as they are:

- `--scope` of `sform account machine create` (line 49): "A secrets scope needs the ContributorWithSecrets role, a management scope the Administrator role." The role depends on the value, so it is a rule, not a note on the option.
- `-r|--role` of `sform account points grant` and `revoke`: the role names are the values of the option.
- "(secret rights needed)" of `kv get --reveal` and `kv export --include-secrets`, and "Secrets need secret rights." of `kv diff`: the secret rights are a user's `ContributorWithSecrets` or a machine's `Secrets.Read` and `Secrets.Write`, not one role. The docs use the same term (`docs/42for.net/platform/admin-ui.md`, line 276; `docs/Platform/Storyteller/key-values.md`, "Who may do what").

### The roles the API checks

The notes must name the roles the server enforces:

- Roles are ordered: `Reader` < `Contributor` < `ContributorWithSecrets` < `Administrator` < `Owner` (`Storyteller/Abstractions.Access/src/AccountRole.cs`). Every check takes a minimal role, so an Owner passes every Administrator check.
- `kv audit`, `kv usage`, `kv purge`, `kv delete --permanent` and `kv policy set`: `Administrator`, and never a machine (`docs/Platform/Storyteller/key-values.md`, "Who may do what": "Permanent delete, purge, policy changes, … audit, usage | `Administrator` | never"; `Storyteller/Api.Functions/src/V1/KeyValueRequests.cs`, line 11).
- Trusted issuers (`Storyteller/Api.Functions/src/V1/FederationHttp.cs`): the list `Contributor` (line 102), `PUT` and `DELETE` `Administrator` (lines 157 and 234), all through `CheckUserAccessToProjectAsync`, which calls `EnsureUser`: no machine. `docs/Platform/Storyteller/authentication.md` has the same table (lines 1315-1320).
- `GET` of an access point: `Administrator` (`Storyteller/Api.Functions/src/V1/AccessHttp.cs`, line 178). `AccessPointGetCommand` also checks `role < AccountRole.Administrator` itself (line 65).
- A permanent delete of an annotation: "A permanent delete needs a user with the Administrator role." (`Storyteller/Api.Functions/src/V1/AnnotationsHttp.Deleted.cs`, line 195).
- A management scope for a machine: only an Administrator grants it (`AccessHttp.EnsureRoleForMachineScopesAsync`, lines 711-726).

### How the help is rendered

As described by 424-216 (`docs/Platform/Cli/specs/2026-11-08 Short Descriptions of the Export and Import Commands of sform.md`): with the output redirected McMaster wraps the lists to 80 columns with a hanging indent, and writes `ExtendedHelpText` as it is after the options, without wrapping. The width left for a description is 80 minus the first column of the list:

| List | First column | Room for a description |
| --- | --- | --- |
| `sform story kv --help` | 27 (`-k\|--continuation-token`) | 53 |
| `sform story kv policy --help` | 19 (`-p\|--projectKey`) | 61 |
| `sform account machine issuer --help` | 19 | 61 |
| `sform account points --help` | 16 (`-?\|-h\|--help`) | 64 |

Today `kv audit`, `kv purge` and `issuer delete` take two lines of their list, `kv usage` and `issuer set` three, and `points get` two.

### Tests and docs

- `test/Commands/MachineCreateCommandTests.cs`, `ManagementScopeHelp_NamesTheScopesAndTheAdministratorRole` (line 629), asserts the text of `-m`.
- `test/Commands/TransferHelpTests.cs` renders the help at 80 columns for the four transfer commands, with private helpers `Command` (from `StorytellerListCommand`) and `Render`. `test/Commands/HelpTexts.cs` has `Unwrap`, `Words` and `Lines`.
- No other test asserts the texts above.
- No document quotes them. `docs/Platform/Storyteller/export-import.md` (line 644) quotes the lines of `export` and `import`, which stay. "(Administrator)" in `docs/Platform/Cli.AutomationTests/test-cases.md` (KV-BLK-07) and in comments of `Cli.AutomationTests` is the wording of the test cases, not the help.

## Proposed Changes

### 1. The style

A command or option that as a whole needs a role ends the first sentence of its description with a note in parentheses, before the full stop:

- "(for readers)", "(for contributors)", "(for contributors with secrets)", "(for administrators)" or "(for owners)";
- the note names the lowest role, and a higher role may run it too. So "(for administrators)" replaces "(Administrator or Owner)";
- the note says nothing about machines. Where that matters, the detail after the options says it in full sentences ("A user needs the Administrator role on the project. A machine cannot …"), as the detail of `export` and `import` does.

A description that would wrap in the list of its parent gets a short first line, and the rest moves to `ExtendedHelpText`: a `HelpDetail` raw-string constant of the command, with an empty first line and no line longer than 79 characters, as 424-216 did. Every changed command then takes one line in its parent's list. Options have no `ExtendedHelpText`; their descriptions keep their wrapping.

### 2. `sform story kv` (phase 1)

| Command or option | Description | Detail |
| --- | --- | --- |
| `audit` | "The audit trail of secrets (for administrators)." | yes |
| `usage` | "The project's key/value usage (for administrators)." | yes |
| `purge` | "Purge soft-deleted keys (for administrators)." | yes |
| `policy set` | "Change the policy (for administrators)." | yes |
| `delete --permanent` | "Remove the key and every version for good (for administrators)." | an option |

The details (drafts; the final wrapping may differ):

`audit`:

```text

The events of the project's secrets, newest first: who revealed, wrote,
deleted, restored, exported, imported or re-protected them, who changed the
policy or the keys, and the aggregated binding reads; never a value. Without
-v the events of every view are listed; -v, --key, --operation, --from and
--to narrow them.

A user needs the Administrator role on the project. A machine cannot read the
audit trail, whatever its scopes.
```

`usage`:

```text

The daily meters of the project's key/value pairs and their totals over a
range of days: the last 30 by default, or --from to --to (at most 400 days),
with the estimated charges when the deployment has prices.

A user needs the Administrator role on the project. A machine cannot read the
usage, whatever its scopes.
```

`purge`:

```text

The keys, given as arguments or by --prefix (an empty prefix means every
key), are removed with every version before their retention ends, without a
way to restore them. The purge asks for a confirmation, for a prefix the view
name; -y skips it.

A user needs the Administrator role on the project. A machine cannot purge,
whatever its scopes.
```

`policy set`:

```text

Only the members given change; the others keep their value. --allow lists
every allowed level and replaces the stored list: public is always allowed,
and a level still in use has to stay listed.

A user needs the Administrator role on the project. A machine cannot change
the policy, whatever its scopes.
```

The facts come from `docs/Platform/Storyteller/key-values.md` (the CLI section and "Who may do what") and the OpenAPI descriptions of `GetKeyValueAudit` and `GetKeyValueUsage` (`Storyteller/Api.Functions/src/V1/KeyValueUsageHttp.cs`, lines 35 and 74). Without `-v` the audit command sends no view (`ViewName` is the explicit option of `BaseContextCommand`), and the API then returns the events of every view.

### 3. The account commands and `sform story delete` (phase 2)

| Command or option | Description | Detail |
| --- | --- | --- |
| `account machine issuer list` | "List the trusted issuers of the project (for contributors)." | no, it fits |
| `account machine issuer set` | "Create or replace a trusted issuer (for administrators)." | yes |
| `account machine issuer delete` | "Delete a trusted issuer (for administrators)." | yes |
| `account points get` | "Get an access point with its users (for administrators)." | no |
| `account machine create -m\|--manage` | "A management scope to grant (for administrators). One of ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage; repeat it or separate the values with commas. Never part of -r or -w." | an option |
| `story delete --permanent` | "Delete permanently, without a way to restore it (for administrators)." | an option |

`-m` gets a first sentence of its own so that the note ends it; with "(for administrators):" before the list, the note would sit in the middle of a sentence again. `points get` drops "and role", because "with its users and their roles" does not fit one line; its output shows every user with the role.

`issuer set`:

```text

Settings not given are cleared, as with the issuer PUT, and the issuer URL
(--issuer) of an existing issuer cannot change. The keys come from the
issuer's discovery document, unless --jwks-uri or --jwks-file gives them.

A user needs the Administrator role on the project. A machine cannot change
the trusted issuers, whatever its scopes.
```

`issuer delete`:

```text

The issuer is deleted even while federation rules reference it: it is the
kill switch. Those rules stop matching within a minute, so their machines can
no longer exchange tokens of the issuer.

A user needs the Administrator role on the project. A machine cannot delete a
trusted issuer, whatever its scopes.
```

The facts come from `docs/Platform/Storyteller/authentication.md` (the CLI bullets, line 1312, and the rules of trusted issuers, lines 1347-1349: a delete is allowed while rules reference it, and those rules stop matching within the 60-second cache of the issuer list) and the comment of `MachineIssuerSetCommand` (lines 10-11).

### 4. Tests (phase 3)

- `test/Commands/HelpTexts.cs` gets the helpers of `TransferHelpTests`, so both test classes use them: `Command(Type root, string path)`, which follows names and aliases (`story` is an alias of `storyteller`), and `Render(CommandLineApplication command)` at 80 columns. `TransferHelpTests` keeps its tests and uses the shared helpers.
- A new `test/Commands/RoleNoteTests.cs`:
  - `EveryDescription_NamesItsRole_InTheNoteStyle`: walks every command below `PlatformCommand` and collects the descriptions of the commands, options and arguments. None has a role name right after an opening parenthesis (`(Administrator`, `(Contributor`, `(Owner`, …). Every "(for …)" note (but "(for example") is one of the five forms of section 1, is followed by a full stop, and is in the first sentence.
  - `ParentList_GivesTheCommandOneLine`, for the eight commands of sections 2 and 3: the line in the parent's list is the new description, and the next line is not a continuation.
  - `Help_EndsWithTheDetail_AfterTheOptions`, `Detail_FitsAnEightyColumnTerminal` and `Detail_NamesTheRole` for the six commands with a detail: an empty first line, the end of the help after `-?|-h|--help`, no line longer than 79 characters or with trailing spaces, and the sentence "A user needs the Administrator role on the project.".
  - The texts of the three options (`kv delete --permanent`, `story delete --permanent`, `machine create -m`).
- `MachineCreateCommandTests.ManagementScopeHelp_NamesTheScopesAndTheAdministratorRole` expects the new text of `-m`.

### 5. Documentation

- `docs/42for.net/cli/sform.md`: a short paragraph after the introduction on the help: `-?|-h|--help` on every command, the role note at the end of the first sentence, the lowest role, and the detail after the options of some commands.
- No document quotes the changed texts, so nothing else changes. `export-import.md` quotes the lines of `export` and `import`, which stay.

### 6. Out of scope

- Commands that need a role but carry no note today, for example `kv policy rotate`, `byok-setup`, `byok-verify`, `kv recipients add`, `points grant` and `revoke`, or `machine machine-auth` with values. Adding notes means checking each against the API; a follow-up issue can do it.
- The messages `sform` prints, such as "Only an Administrator or Owner can delete permanently." (`StorytellerDeleteCommand.cs`, line 131).
- The rules of `--scope` and the secret rights (see Current State).
- The test case names of `docs/Platform/Cli.AutomationTests/test-cases.md` and the comments of `Cli.AutomationTests`.

### 7. Phases and commits

1. `fix(platform): give the role notes of sform story kv one style (424-222)`: section 2.
2. `fix(platform): give the role notes of the account commands and story delete of sform one style (424-222)`: section 3, with the change of `MachineCreateCommandTests`.
3. `test(platform): guard the style of the role notes in the help of sform (424-222)`: section 4.
4. `docs(platform): describe the role notes in the help of sform (424-222)`: section 5.
