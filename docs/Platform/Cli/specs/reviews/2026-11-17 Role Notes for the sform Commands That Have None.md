# Role Notes for the sform Commands That Have None

## Overview

The `sform` commands that need a role but had no note now have one, in the style of 424-222: a note such as "(for administrators)" or "(for contributors with secrets)" at the end of the first sentence, naming the lowest role. Every candidate of the issue, and every other command below `sform account` and `sform story`, was checked against the API before a note was added. Commands whose description wrapped in their parent's list take one line there now, and their detail follows the options of their own help. Where a note names only the lowest of several roles, the detail gives the role of each case. The plan is [2026-11-17 Role Notes for the sform Commands That Have None](../2026-11-17%20Role%20Notes%20for%20the%20sform%20Commands%20That%20Have%20None.md) (Linear 424-275). It was delivered in the four commits of its section 7, and one more commit of fixes and one of docs after an independent review of the diff.

## What Was Done

### 1. When a command gets a note

As planned. A command on the data of a view gets a note when it needs more than Reader to read or more than Contributor to write. A command of access management gets a note naming its lowest role. A group gets a note only when it and every command below it need the same role. A role that depends on the values or the data is a rule, given in the option or in the detail. An option gets a note when using it at all raises the role.

The roles come from two passes over every command below `sform account` and `sform story`, each from the CLI command through the NSwag SDK to the Function and the services. The first pass mapped each area, and a second, independent pass checked every claim. They agreed on every command. The table in the plan's Current State is the result.

### 2. `sform story kv` (phase 1)

Commit `fix(platform): add the role notes of sform story kv that had none (424-275)`.

| Command | Description | File |
| --- | --- | --- |
| `protect` | "Re-protect keys (for contributors with secrets)." + detail | `KeyValueBulkCommands.cs` |
| `reencrypt` | "Re-encrypt hyok data (for contributors with secrets)." + detail | `KeyValueReencryptCommand.cs` |
| `policy byok-setup` | "How to bring your own key (for administrators)." + detail | `KeyValuePolicyCommand.cs` |
| `policy byok-verify` | "Verify the customer key (for administrators)." + detail | `KeyValuePolicyCommand.cs` |
| `policy rotate` | "Rotate a key-encryption key (for administrators)." + detail | `KeyValuePolicyCommand.cs` |
| `recipients add` | "Register a recipient (for administrators)." + detail | `KeyValueRecipientsCommand.cs` |
| `recipients enable` | "Enable a recipient (for administrators)." | `KeyValueRecipientsCommand.cs` |
| `recipients disable` | "Disable a recipient (for administrators)." + detail | `KeyValueRecipientsCommand.cs` |
| `recipients remove` | "Remove a recipient (for administrators)." + detail | `KeyValueRecipientsCommand.cs` |

The texts and details are as planned. `reencrypt` takes exactly the 53 columns of the `sform story kv` list.

**Added after the review:** `kv set --secret` now says "A secret (for contributors with secrets). It gets the project's default secret level." (commit `fix(platform): note the role of kv set --secret and give the rule of points create in sform (424-275)`). The plan missed it, though the first pass had listed it: without `--protection`, `--secret` always writes at the project's default secret level, and that level is always a secret level (`CosmosKeyValuePolicyService` refuses any other default), so the option always needs the secret write right. The text "A secret at the project's default secret level (for contributors with secrets)." would break the note after "(for" in the 58-column option column, so the note ends a short first sentence.

### 3. `sform account` (phase 2)

Commit `fix(platform): add the role notes of the sform account commands that had none (424-275)`.

| Command or option | Description | File |
| --- | --- | --- |
| `points grant` | "Grant access to point to another user (for administrators)." + detail | `AccessPointGrantCommand.cs` |
| `points revoke` | "Revoke access to point to another user (for administrators)." + detail | `AccessPointRevokeCommand.cs` |
| `shared-cert` | "List and manage shared certificates (for administrators)." | `SharedCertListCommand.cs` |
| `shared-cert issue` | "Issue a new shared certificate (for administrators)." | `SharedCertIssueCommand.cs` |
| `shared-cert revoke` | "Revoke a shared certificate (for administrators)." | `SharedCertRevokeCommand.cs` |
| `machine create` | "Create new machine access (for contributors)." | `MachineCreateCommand.cs` |
| `machine reset` | "Reset secret for a machine access (for contributors)." + detail | `MachineResetCommand.cs` |
| `machine delete` | "Delete a machine access (for contributors)." + detail | `MachineDeleteCommand.cs` |
| `machine machine-auth` | "Show or set the authentication policy (for contributors)." + detail | `MachineAuthSetCommand.cs` |
| `machine machine-auth -k\|--credential-kind` | "An enabled credential kind (for administrators). One of …; repeat the option or separate the kinds by commas. The kinds given replace the enabled ones." | `MachineAuthSetCommand.cs` |
| `machine rules` | "Show or replace the federation rules (for contributors)." + detail | `MachineRulesCommand.cs` |

**Deviations, all from the review of the diff:**

- `-k` of `machine-auth`: the plan's "A credential kind to enable (for administrators)." read as adding one kind, but `-k` replaces the whole list of enabled kinds, and a kind left out stops its machines. The option now names a member of the list and says that the kinds given replace the enabled ones.
- The typo "an machine" is also gone from the machine id argument of `reset` and `delete` ("The id of a machine access."), not only from the description of `reset`.
- `points create` has no note, as planned, because a new organization needs no role. It now has a detail with its rule: "A new organization is created with the project, and you become the Owner of both. A project in an existing organization needs the Owner role on the organization. A machine cannot create a project, whatever its scopes." (`CosmosAccessService.CreateAccessPointAsync`, lines 176-181). Without it, the rule of the docs (section 5) would not hold for this command.

### 4. Tests (phase 3)

Commit `test(platform): guard the new role notes in the help of sform (424-275)`, extended by the fix commit.

`Cli/test/Commands/RoleNoteTests.cs`:

- `EveryDescription_NamesItsRole_InTheNoteStyle` expects at least 34 notes: the 13 earlier ones and the 21 of this change (19 commands, `-k` and `--secret`). The comment above the count now has the blank line StyleCop asks for (SA1515).
- `ParentList_GivesTheCommandOneLine` covers the 19 commands of sections 2 and 3.
- `Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions` covers the 14 planned details and the detail of `points create`.
- `Detail_EndsWithTheRole_AndThatAMachineCannot` covers the six Administrator commands of `kv policy` and `kv recipients` that have a detail.
- **Deviation:** the planned `Detail_EndsWithTheRoles` is named `Detail_EndsWithTheRoles_InFullSentences`. It compares the whole last paragraph of the detail, unwrapped, with the expected role sentences, for the eight planned commands and `points create`.
- **Not planned:** `GroupNote_IsTheNoteOfEveryCommandBelowIt` checks the group rule of section 1 on `account shared-cert`: the group and every command below it end with the same note.
- `Detail_KeepsWhatTheDescriptionSaid` covers the ten commands whose description moved to the detail.
- `Option_EndsItsFirstSentenceWithTheNote` covers `machine-auth --credential-kind` and `kv set --secret`.

`dotnet test --project src/Platform/Cli/test`: 2117 tests passed, 99 of them in `RoleNoteTests` (38 before). The help of every command was also rendered at 80 columns from the build and read: every changed command takes one line in its parent's list, and no line of a new detail is longer than 79 characters. The coverage checks of `Cli.AutomationTests` (`--treenode-filter "/**[Category=Coverage]"`) pass: no command or option was added or renamed.

### 5. Documentation

Commits `docs(platform): describe the role rules in the help details of sform (424-275)` and `docs(platform): name the exceptions of the role notes of sform (424-275)`.

**Deviation:** the plan only added the role of each case to the paragraph of `docs/42for.net/cli/sform.md` (line 9). The paragraph now also states the rule of section 1: a note when a command or option needs more than Reader to read or Contributor to change the data of a view, or when it manages access. It also names the exceptions: a group has a note only when every command below it needs the same role (so `sform account machine` has none), and a role that depends on the values or the data is given as a rule in the option (`--scope`, "(secret rights needed)") or after the options (`machine delete`, `points create`). The first version stated the rule without the exceptions. The review found that this overstated what the help does.

No document quotes the changed texts. Line 145 of the same page already gave the roles of the machine commands, and the new help matches it.

### 6. Out of scope

As planned, with these notes from the review:

- `kv export --include-secrets` keeps "(secret rights needed)", as the issue asks. The plan's Current State gives the wrong reason for this, "needed only for secret keys". For this option, the rights are needed whatever the data: the API refuses the whole export without them (`KeyValuesHttp.cs`, lines 537-541). A follow-up could replace the "secret rights" notes with notes in the style, and give machines' `Secrets.*` scopes in a detail, as `protect` and `reencrypt` do.
- The two defects of the `-r` option of `points grant` and `revoke` (plan section 6) are not fixed here. `revoke` without `-r` sends Reader, not "all roles", and `-r` does not list `ContributorWithSecrets`.
- A correction to the plan's Current State: the first column of `sform account --help` is 21 because of `-o|--organization`, not `shared-cert`. The room of 59 columns is right.

### 7. Phases and commits

1. `docs(platform): plan the role notes of the sform commands that had none (424-275)`
2. `fix(platform): add the role notes of sform story kv that had none (424-275)`
3. `fix(platform): add the role notes of the sform account commands that had none (424-275)`
4. `test(platform): guard the new role notes in the help of sform (424-275)`
5. `docs(platform): describe the role rules in the help details of sform (424-275)`
6. `fix(platform): note the role of kv set --secret and give the rule of points create in sform (424-275)`: the fixes of the review (sections 2 to 4).
7. `docs(platform): name the exceptions of the role notes of sform (424-275)`: section 5.
8. `docs(platform): review the role notes of the sform commands that had none (424-275)`: this review.
