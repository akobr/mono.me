# Role Notes for the sform Commands That Have None

Linear: [424-275 Add role notes to the sform commands that need a role but have none](https://linear.app/424fun/issue/424-275/add-role-notes-to-the-sform-commands-that-need-a-role-but-have-none).

Follows [2026-11-15 Role Notes in the Help of sform](2026-11-15%20Role%20Notes%20in%20the%20Help%20of%20sform.md) (424-222, PR #197), whose section 6 left these commands out. Line numbers refer to the tree this plan was written against (`a115f910`). The issue has no comments and no CodeRabbit plan.

## Problem

424-222 gave the role notes of `sform` one style: a note at the end of the first sentence of a description, such as "(for administrators)", that names the lowest role. It changed only the descriptions that already named a role. Other commands need a role too, but their help does not say so. In `sform story kv policy --help`, `set` says "(for administrators)", while `rotate`, `byok-setup` and `byok-verify` say nothing, although all four need the Administrator role. The same holds for the recipient changes of `kv recipients`, `points grant` and `revoke`, the shared certificates, and several commands of `sform account machine`.

## Current State

All paths are relative to `src/Platform/` unless they start with `docs/`.

### The style and its guard

- The style: `docs/Platform/Cli/specs/2026-11-15 Role Notes in the Help of sform.md`, section 1. The five notes are "(for readers)", "(for contributors)", "(for contributors with secrets)", "(for administrators)" and "(for owners)". A note says nothing about machines. A description that would wrap in its parent's list gets a short first line, and the rest moves to `ExtendedHelpText`: a `HelpDetail` raw-string constant, with an empty first line and no line longer than 79 characters. The detail ends with the role in full sentences ("A user needs the Administrator role on the project. A machine cannot …, whatever its scopes.").
- Not role notes, and they stay: the rule of `--scope` of `machine create` (the role depends on the value), the values of `-r|--role` of `points grant` and `revoke`, and the "secret rights" of `kv get --reveal`, `kv export --include-secrets` and `kv diff` (a user's `ContributorWithSecrets` or a machine's `Secrets.*` scopes, needed only for secret keys).
- The guard: `Cli/test/Commands/RoleNoteTests.cs`. `EveryDescription_NamesItsRole_InTheNoteStyle` (line 30) walks every command and checks the form and place of every note, with at least 13 notes (line 66). `NoNote_BreaksAcrossLines_InAnyHelp` (line 72) renders every help at 80 columns. `ParentList_GivesTheCommandOneLine` (line 97), `Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions` (line 129), `Detail_EndsWithTheRole_AndThatAMachineCannot` (line 153), `Detail_KeepsWhatTheDescriptionSaid` (line 168) and `Option_EndsItsFirstSentenceWithTheNote` (line 185) list the commands and options of 424-222. `Cli/test/Commands/HelpTexts.cs` renders a help at 80 columns.

### How the help is rendered

With the output redirected, McMaster wraps the lists at 80 columns: a description may reach column 80. The room for a description is 80 minus the first column of the list:

| List | First column | Room |
| --- | --- | --- |
| `sform account --help` | 21 (`shared-cert`) | 59 |
| `sform account points --help` | 16 (`-?\|-h\|--help`) | 64 |
| `sform account shared-cert --help` | 19 (`-p\|--projectKey`) | 61 |
| `sform account machine --help` | 19 | 61 |
| `sform story kv --help` | 27 (`-k\|--continuation-token`) | 53 |
| `sform story kv policy --help` | 19 | 61 |
| `sform story kv recipients --help` | 19 | 61 |
| options of `sform account machine machine-auth` | 24 (`-k\|--credential-kind`) | 56 |

### The roles the API checks

Every command below `sform account` and `sform story` was followed from the CLI command through the NSwag SDK to the Function and every role check on the way, in the services too (`Backend.CosmosDb`), by one pass per area and an independent second pass that checked each claim; both agreed on every command. Roles are ordered `Reader` < `Contributor` < `ContributorWithSecrets` < `Administrator` < `Owner` (`Storyteller/Abstractions.Access/src/AccountRole.cs`). The candidates of the issue and the other commands that need more than their kind implies (see section 1):

| Command | Lowest user role | Machines | Evidence |
| --- | --- | --- | --- |
| `story kv policy rotate` | Administrator | never | `Storyteller/Api.Functions/src/V1/KeyValuePolicyHttp.cs:165`, `CheckAdministratorAsync` (`KeyValueRequests.cs:12-16`: `EnsureUser`, then Administrator) |
| `story kv policy byok-setup` | Administrator (a read) | never | `KeyValuePolicyHttp.cs:115` |
| `story kv policy byok-verify` | Administrator | never | `KeyValuePolicyHttp.cs:138` |
| `story kv recipients add` | Administrator | never | `KeyValuePolicyHttp.cs:224` |
| `story kv recipients enable`, `disable` | Administrator | never | `KeyValuePolicyHttp.cs:264` (one route, `UpdateRecipient`) |
| `story kv recipients remove` | Administrator | never | `KeyValuePolicyHttp.cs:302` |
| `story kv protect` | ContributorWithSecrets | `Configuration.Write`, `KeyValue.Manage`, `Secrets.Write`, under the mTLS rule | `KeyValuesHttp.cs:339` (Contributor), then `EnsureSecretWriteRights` for the whole request (line 358), before any key is read |
| `story kv reencrypt` | ContributorWithSecrets | `Configuration.Read`, `Configuration.Write`, `Secrets.Read`, `Secrets.Write`, under the mTLS rule | No route of its own: `Cli/src/Commands/KeyValues/KeyValueReencryptCommand.cs:53-96` reveals each hyok value (secret read right) and writes it again at the level hyok (secret write right); hyok is a secret level |
| `account points grant`, `revoke` | Administrator on the access point; Owner to grant or revoke `Owner` | never | `AccessHttp.cs:289` and `324` (`EnsureUser`), `Storyteller/Backend.CosmosDb/src/Accessing/CosmosAccessService.cs:772-783` (`EnsureCanManage`, in every build) |
| `account shared-cert` (lists), `issue`, `revoke` | Administrator | never | `CertificatesHttp.cs:137`, `164`, `216` |
| `account machine create` | Contributor; a secrets scope ContributorWithSecrets, a management scope Administrator | never | `AccessHttp.cs:419`, `455`, `702-727` (`EnsureRoleForMachineScopesAsync`) |
| `account machine reset`, `delete` | Contributor; for a machine with a secrets scope ContributorWithSecrets, with a management scope Administrator | never | `AccessHttp.cs:500`/`508` and `543`/`551`, `659-673` |
| `account machine machine-auth` | Contributor to show; Administrator to set (`-k`, with `-d` and `-l`) | never | `AccessHttp.cs:597` (GET), `625` (PUT); `-d` or `-l` without `-k` stop in the CLI (`MachineAuthSetCommand.cs:68-72`) |
| `account machine rules` | Contributor to show; to replace, the role the machine's scopes need | never | `AccessHttp.cs:386` (GET), `FederationHttp.cs:265` and `274` |

The other commands, with no note:

- Groups whose commands need different roles: `account machine` (lists the machines, Contributor; `renew` needs a machine), `account machine issuer` (lists, Contributor; `set` and `delete` Administrator), `story kv policy` (shows, Reader), `story kv recipients` (lists, Reader), `account points` (the caller's own access map, no role).
- No project role: `account`, `account register`, `account set`, `account ca`, `account logout`, `account settings` and its commands (local).
- A role that depends on the values or the data, and no lowest role above the kind's: `account points create` (no role for a new organization, Owner of an existing one, `CosmosAccessService.cs:176-181`), `story config export` (secret bindings), `story kv set`, `delete`, `restore`, `diff` and `import` (secret keys need the secret rights).
- `account machine renew`: only the machine itself, over mTLS with its current certificate; no user role passes (`CertificatesHttp.cs:52-81`).
- Every other command of `sform story` (`get`, `set`, `edit`, `delete`, `restore`, `deleted`, `history`, `config`, `template`, `schema`, `kv`, `kv get`, `versions`, `export`): Reader for a read, Contributor for a write. Schema and template writes need Contributor for users; only machines need the management scopes `Schema.Write` and `Template.Write`.
- Already noted by 424-222: `story export`, `import`, `delete --permanent`, `kv audit`, `usage`, `purge`, `delete --permanent`, `policy set`, `machine create -m`, `machine issuer list`, `set`, `delete`, `points get`. All match the API.

### Tests and docs

- No test asserts the descriptions that change, other than `RoleNoteTests` (searched in `Cli/test` and `Cli.AutomationTests`).
- No document quotes them (searched in `docs/` and `src/**/*.md`, specs left out). `docs/42for.net/cli/sform.md` line 145 already gives the roles of the machine commands, and line 9 describes the notes.

## Proposed Changes

### 1. When a command gets a note

`AccountRole` describes the roles by what they do: a Reader reads annotations, a Contributor also creates, changes and removes them, and the Administrator manages access. So a note is added where the role is not the one the kind of command implies:

- a command on the data of a view (annotations, configurations, schemas, templates, key/value pairs) gets a note when it needs more than Reader for a read or more than Contributor for a write;
- a command of access management (access points, machines, trusted issuers, shared certificates, the key/value policy and its recipients) gets a note naming its lowest role, whatever it is, because the reader cannot guess it;
- a group gets a note only when it and every command below it need the same role;
- a role that depends on the values or the data is a rule, not a note (as `--scope` in 424-222). When the note of a command names the lowest of several roles, its detail gives the rule in full sentences;
- an option gets a note when using it at all raises the role.

Adding "(for contributors)" to every write of the data (about 30 commands) and "(for readers)" to every read would say what the role names already say and hide the notes that matter.

### 2. `sform story kv` (phase 1)

| Command | Description | Detail |
| --- | --- | --- |
| `protect` | "Re-protect keys (for contributors with secrets)." | yes |
| `reencrypt` | "Re-encrypt hyok data (for contributors with secrets)." | yes |
| `policy byok-setup` | "How to bring your own key (for administrators)." | yes |
| `policy byok-verify` | "Verify the customer key (for administrators)." | yes |
| `policy rotate` | "Rotate a key-encryption key (for administrators)." | yes |
| `recipients add` | "Register a recipient (for administrators)." | yes |
| `recipients enable` | "Enable a recipient (for administrators)." | no |
| `recipients disable` | "Disable a recipient (for administrators)." | yes |
| `recipients remove` | "Remove a recipient (for administrators)." | yes |

`reencrypt` has 53 columns; "Re-encrypt hyok values" with the note would take 55. "hyok keys" would read as cryptographic keys, so the line says "hyok data" and the detail says "values".

The details (drafts; the final wrapping may differ):

`protect`:

```text

The keys, given as arguments or by --prefix (an empty prefix means every key),
move to the server-side level of --protection (standard, hsm or byok) as a new
version each; older versions keep their level until they expire. Public and
hyok need a new value from 'sform story kv set'.

A user needs the ContributorWithSecrets role on the project. A machine needs no
role but the scopes Configuration.Write, KeyValue.Manage and Secrets.Write, and
a certificate (mTLS) when the policy requires it.
```

`reencrypt`:

```text

The client-encrypted (hyok) values of the keys, given as arguments or by
--prefix (an empty prefix means every hyok key), are decrypted here with an
existing recipient's private key (--private-key and --kid) and encrypted again
for the current recipients, the enabled ones, as a new version each. A new
recipient can read the existing values only after this.

A user needs the ContributorWithSecrets role on the project. A machine needs no
role but the scopes Configuration.Read, Configuration.Write, Secrets.Read and
Secrets.Write, and a certificate (mTLS) when the policy requires it.
```

`policy byok-setup`:

```text

What the customer needs to bring their own key (byok): the application to
consent to, the role to grant its service principal on the key, and the
commands that set and verify the key.

A user needs the Administrator role on the project. A machine cannot read the
setup, whatever its scopes.
```

`policy byok-verify`:

```text

The customer key set in the policy is checked: enabled, RSA 2048+, wrap and
unwrap allowed, and a round trip works. The result shows its health, its
version and the time of the check.

A user needs the Administrator role on the project. A machine cannot verify the
key, whatever its scopes.
```

`policy rotate`:

```text

The project's standard or hsm key-encryption key (--protection) is rotated now,
and the re-wrap of the stored values starts; 'sform story kv policy' shows its
progress. The customer rotates a byok key in their own vault.

A user needs the Administrator role on the project. A machine cannot rotate a
key, whatever its scopes.
```

`recipients add`:

```text

A recipient is a name (--name) and an RSA public key (3072 bits or more) as a
PEM or JWK file, or inline (--public-key). Existing hyok values are not
encrypted for it until 'sform story kv reencrypt' encrypts them again.

A user needs the Administrator role on the project. A machine cannot change the
recipients, whatever its scopes.
```

`recipients disable`:

```text

New values are not encrypted for a disabled recipient, until 'sform story kv
recipients enable' enables it again.

A user needs the Administrator role on the project. A machine cannot change the
recipients, whatever its scopes.
```

`recipients remove`:

```text

Values already encrypted for it stay readable with its private key until they
are encrypted again ('sform story kv reencrypt'). The removal asks for a
confirmation; -y skips it.

A user needs the Administrator role on the project. A machine cannot change the
recipients, whatever its scopes.
```

The facts come from the commands (`KeyValueBulkCommands.cs:81-110`, `KeyValueReencryptCommand.cs`, `KeyValuePolicyCommand.cs:129-210`, `KeyValueRecipientsCommand.cs`, `KeyValueCommandBase.cs:51-90`), `KeyValueClientEncryption.Encrypt` (`Storyteller/Sdk.NSwag/src/ClientSideEncryption/KeyValueClientEncryption.cs:34`: only enabled recipients) and `docs/Platform/Storyteller/key-values.md` ("Who may do what").

### 3. `sform account` (phase 2)

| Command or option | Description | Detail |
| --- | --- | --- |
| `points grant` | "Grant access to point to another user (for administrators)." | yes |
| `points revoke` | "Revoke access to point to another user (for administrators)." | yes |
| `shared-cert` | "List and manage shared certificates (for administrators)." | no |
| `shared-cert issue` | "Issue a new shared certificate (for administrators)." | no |
| `shared-cert revoke` | "Revoke a shared certificate (for administrators)." | no |
| `machine create` | "Create new machine access (for contributors)." | no: `--scope` and `-m` give the rest |
| `machine reset` | "Reset secret for a machine access (for contributors)." | yes |
| `machine delete` | "Delete a machine access (for contributors)." | yes |
| `machine machine-auth` | "Show or set the authentication policy (for contributors)." | yes |
| `machine machine-auth -k\|--credential-kind` | "A credential kind to enable (for administrators). One of ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated; repeat the option or separate the kinds by commas." | an option |
| `machine rules` | "Show or replace the federation rules (for contributors)." | yes |

`machine reset` also loses the typo "an machine". Only `-k` of `machine-auth` gets a note: `-d` and `-l` without `-k` stop in the CLI, so `-k` is what switches to the Administrator's set form.

The details:

`points grant` (and `revoke` with "revoke"):

```text

A user needs the Administrator role on the access point, and the Owner role to
grant the Owner role. A machine cannot grant access, whatever its scopes.
```

`machine reset` (and `delete` with "delete a machine access"):

```text

A user needs the Contributor role on the project; for a machine with a secrets
scope the ContributorWithSecrets role, and with a management scope the
Administrator role. A machine cannot reset a secret, whatever its scopes.
```

`machine machine-auth`:

```text

Without options the machine authentication policy of the project is shown.
With -k it is set as a whole: the enabled credential kinds (-k), the default
kind (-d, else the first kind given) and the default certificate lifetime (-l,
else the deployment's default).

A user needs the Contributor role on the project to show the policy, and the
Administrator role to set it. A machine cannot show or set it, whatever its
scopes.
```

`machine rules`:

```text

Without rule options the rules of the federated machine are shown. With
--issuer, --audience, --subject and --claim, or with --rules-file, every rule
is replaced; the new rules apply to new token exchanges.

A user needs the Contributor role on the project; to replace the rules of a
machine with a secrets scope the ContributorWithSecrets role, and with a
management scope the Administrator role. A machine cannot show or replace the
rules, whatever its scopes.
```

The facts come from the commands (`MachineAuthSetCommand.cs:11-13`, `MachineRulesCommand.cs:11-12`), the comment of `AccessHttp.EnsureRoleForMachineScopesAsync` (lines 655-658) and `docs/42for.net/cli/sform.md` line 145.

### 4. Tests (phase 3)

`Cli/test/Commands/RoleNoteTests.cs`:

- `EveryDescription_NamesItsRole_InTheNoteStyle`: at least 33 notes, the 13 of 424-222 and the 20 of sections 2 and 3.
- `ParentList_GivesTheCommandOneLine`: the 19 commands of sections 2 and 3.
- `Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions`: the 14 commands with a detail.
- `Detail_EndsWithTheRole_AndThatAMachineCannot`: the six Administrator commands of `kv policy` and `kv recipients` with a detail.
- New `Detail_EndsWithTheRoles`: the whole role paragraph of the other eight details (`protect`, `reencrypt`, `points grant`, `revoke`, `machine reset`, `delete`, `machine-auth`, `rules`), unwrapped.
- `Detail_KeepsWhatTheDescriptionSaid`: what left the descriptions of `protect`, `reencrypt`, `byok-setup`, `byok-verify`, `rotate`, `recipients add`, `disable`, `remove`, `machine-auth` and `rules`.
- `Option_EndsItsFirstSentenceWithTheNote`: `machine machine-auth --credential-kind`.

### 5. Documentation

- `docs/42for.net/cli/sform.md`, line 9: the help also says after the options which role each case needs when the role depends on the values or the data, as for `sform account machine delete`.
- No document quotes the changed texts, so nothing else changes.

### 6. Out of scope

- Groups whose commands need different roles, and roles that depend on the values or the data without a lowest role above the kind's (see Current State).
- Two defects of the option texts of `points`, found on the way: `points revoke -r` says "If not specified all roles will be revoked", but the CLI then sends `Reader` (`AccessPointRevokeCommand.cs:75-78`), so the API answers `409 ElevatedRole` for a member with a higher role; and `-r` of `grant` and `revoke` does not list `ContributorWithSecrets`, which the API accepts. A follow-up issue can fix them.
- The messages `sform` prints, and the wording of the test cases of `Cli.AutomationTests`.

### 7. Phases and commits

1. `fix(platform): add the role notes of sform story kv that had none (424-275)`: section 2.
2. `fix(platform): add the role notes of the sform account commands that had none (424-275)`: section 3.
3. `test(platform): guard the new role notes in the help of sform (424-275)`: section 4.
4. `docs(platform): describe the role rules in the help details of sform (424-275)`: section 5.
