# Short Descriptions of the Export and Import Commands of sform

Linear: [424-216 Simplify the descriptions of sform story export/import and sform story kv export/import in the parent help](https://linear.app/424fun/issue/424-216/simplify-the-descriptions-of-sform-story-exportimport-and-sform-story).

Line numbers refer to the tree this plan was written against (`a6974e62`). The issue has no comments and no CodeRabbit plan.

## Problem

`sform story --help` lists every subcommand with its description. The descriptions of `export` and `import` are long paragraphs about formats, kinds, versions and machine scopes. In the list they wrap over six and nine lines and push the other subcommands out of view. A list should only say what a command is for.

`sform story kv export` and `sform story kv import` have the same problem in `sform story kv --help`, with shorter texts that still list the formats and the confirmation of the import.

The detail must not be lost. It belongs in the help of the command itself: `sform story export --help` and the other three.

While checking the help of the key/value commands, a second problem showed up: **every `sform story kv` command crashes**, `--help` included. `sform story kv`, `sform story kv --help` and `sform story kv export --help` all end with "Total mayhem, the tool is broken!" and exit code `1`. The log says:

```text
System.InvalidOperationException: The constructor of type '_42.Platform.Cli.Commands.KeyValues.KeyValueListCommand' contains the parameter of type 'System.TimeProvider' is not registered, ...
```

Without a fix, the acceptance criteria of the key/value commands cannot be checked, so this plan fixes it first.

## Current State

All paths are relative to `src/Platform/Cli/` unless they start with `docs/`.

### The descriptions

`Description` of `[Command]` is both the line in the parent's list and the first line of the command's own help.

| Command | File | Description today |
| --- | --- | --- |
| `sform story export` | `src/Commands/Storyteller/StorytellerExportCommand.cs`, line 12 | "Export the view as a zip (Administrator): its annotations, schemas, templates and configurations, as a folder of JSON files with the versions, or as a git repository with every version as a commit. A machine needs the read scopes of the included kinds and Configuration.Transfer instead of a role." |
| `sform story import` | `src/Commands/Storyteller/StorytellerImportCommand.cs`, line 14 | "Import a zip or a folder into the view (Administrator): annotations, schemas, templates and configurations, each recognized by its file name. Only current documents are imported, never versions, and nothing is deleted. A machine needs the write scopes of the included kinds, with Annotation.Manage for annotations, Schema.Write for schemas and Template.Write for templates, and Configuration.Transfer instead of a role." |
| `sform story kv export` | `src/Commands/KeyValues/KeyValueExportCommand.cs`, line 12 | "Export the pairs of the view as a key/value document (json), a flat JSON object (flat) or .env lines (env)." |
| `sform story kv import` | `src/Commands/KeyValues/KeyValueImportCommand.cs`, line 13 | "Import pairs from a key/value document (json), a flat JSON object (flat) or a .env file (env): the plan is shown first, then confirmed." |

The parents are `StorytellerListCommand` (`sform story`) and `KeyValueListCommand` (`sform story kv`).

The other descriptions of `sform` are imperative sentences ("Delete an annotation and its descendants; …", "List and manage the key/value pairs of the view, secrets included."). Several end with `(Administrator)`.

### How McMaster renders the help

McMaster.Extensions.CommandLineUtils 5.1.0, `DefaultHelpTextGenerator`:

- The header is the `Description`, then a blank line. Then the usage, the arguments, the options and the commands.
- The descriptions in the lists are wrapped to the console width with a hanging indent. With the output redirected the width is 80. In `sform story --help` the first column is 27 characters wide, so a description of up to 53 characters stays on one line.
- The footer is `ExtendedHelpText` as it is: no blank line before it, no wrapping. It comes right after the options of a command without subcommands.

No command of `sform` sets `ExtendedHelpText`. `codedoc` sets one (`src/c0ded0c/Cli/src/C0ded0cCommand.cs`, line 15).

### What the rights are

The detail of the key/value commands comes from `docs/Platform/Storyteller/key-values.md`, not from memory:

- the **Files** paragraph of the CLI section (line 389): the formats, the guess without `--format`, the levels of `flat` and `env`, secrets without `--include-secrets`, the plan and its confirmation, the view name of a `replace` that deletes keys, and the exit code `45`;
- the table "Who may do what" (lines 296-309): an export without secrets needs `Reader`, with secrets `ContributorWithSecrets`; an import of public items needs `Contributor`, with secrets `ContributorWithSecrets`. A machine needs `Configuration.Read` and `Configuration.Transfer` for an export, plus `Secrets.Read` for its secrets, and `Configuration.Write` and `Configuration.Transfer` for an import, plus `Secrets.Write` for its secrets, under the mTLS rule;
- the section "Secret delivery" (lines 96-107): a machine reads and writes secrets with its mTLS certificate, or without one when the project policy has `RequireMtlsForSecretDelivery = false`;
- line 446: a caller who may not write secrets imports public items only; a secret value, a change to or from a secret level, or a `replace` that would delete a secret answers `403`.

The code agrees. Without `--format`, `export` calls `KeyValueFormats.Detect(OutputPath, null)` (`KeyValueExportCommand.cs`, line 83): a `.env` name is `env`, a `*.json` name `json`, any other file `env`, and the standard output `json`. `import` passes the content as well. A failed item makes `WriteImportResult` return `ExitCodes.WARNING_PARTIAL_RESULT` (`45`).

The detail of the view commands comes from `docs/Platform/Storyteller/export-import.md`, section Access (lines 552-566): users need `Administrator`, and a machine needs `Configuration.Transfer` and, per included kind, for an export `Annotation.Read` (annotations) or `Configuration.Read` (schemas, templates, configurations), and for an import `Annotation.Write` and `Annotation.Manage`, `Configuration.Write` and `Schema.Write`, `Configuration.Write` and `Template.Write`, and `Configuration.Write`.

### The crash of `sform story kv`

- `KeyValueListCommand` (line 33) has one public constructor: `(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, TimeProvider? timeProvider = null)`.
- McMaster's `ConstructorInjectionConvention` ignores default values. With a single public constructor, every parameter must resolve, or creating the model throws.
- `Startup.ConfigureServices` (`src/Startup.cs`, lines 39-64) registers no `TimeProvider`, and neither does the host.
- McMaster creates the models of the parents of the selected command when parsing completes. So every command under `sform story kv` creates a `KeyValueListCommand` and crashes, also with `--help`.
- The tests of the key/value commands create the commands with `new`, so they never saw it.

### Tests and docs

- `test/Commands/StorytellerTransferCommandTests.cs`, `Commands_NameTheAdministratorRole_AndWhatAMachineNeeds` (lines 56-66), expects the long descriptions: `(Administrator)` in them and the sentence about machines at their end.
- `docs/Platform/Storyteller/export-import.md`, CLI section (line 621), quotes the end of both descriptions. No other document quotes the help of the four commands. `docs/42for.net/cli/sform.md`, `annotations.md`, `admin-ui.md` and `configuration.md` name the rights, but not the help text.

## Proposed Changes

### 1. Register the `TimeProvider` (phase 1)

`Startup.ConfigureServices` registers `TimeProvider.System` as a singleton (`services.TryAddSingleton(TimeProvider.System)`). `KeyValueListCommand` then resolves, and its default value stays for the tests. The other classes that take a `TimeProvider` (`AuthKitAuthenticationService`, `MachineAuthenticationService`) take it in internal constructors only, which the container does not use, so nothing else changes.

A new test, `test/StartupTests.cs`, guards against the same mistake in any command. It runs `Startup.ConfigureServices` on a `ServiceCollection`, then walks every command below `PlatformCommand` through `[Subcommand]`. Each command needs a public constructor whose parameters are all registered, or provided by the host or McMaster (`IConfiguration`, `IHostEnvironment`, `IHostApplicationLifetime`, `CommandLineApplication`, `IConsole`, `CommandLineContext`). With a single public constructor that constructor must resolve, as McMaster requires. The test asks `IServiceProviderIsService`, so it constructs nothing.

### 2. Short descriptions and the detail of `sform story export` and `import` (phase 2)

Each command gets a one-line `Description` and moves the rest to `ExtendedHelpText`.

- `export`: "Export the view (for administrators)."
- `import`: "Import into the view (for administrators)."

The issue suggests "Export of the view (for administrators)." `export` gets the imperative instead, like every other description of `sform`. "(for administrators)" replaces "(Administrator)", as the issue asks. Both lines fit the 53 characters of the list.

The detail is a constant of each command, `HelpDetail`, in a raw string literal. It starts with an empty line, so a blank line separates it from the options. It is wrapped by hand so that no line is longer than 79 characters. It reads well in an 80-column terminal, and in a wider one it keeps its width.

`export`:

```text

The zip holds the annotations, schemas, templates and configurations of the
view, or the kinds of --include: a folder of JSON files with the versions
(--format folder), or a git repository with every version as a commit
(--format git).

A user needs the Administrator role on the project. A machine needs no role
but Configuration.Transfer and the read scope of every included kind:
Annotation.Read for annotations, and Configuration.Read for schemas,
templates and configurations.
```

`import`:

```text

The zip or folder holds annotations, schemas, templates and configurations,
each recognized by its file name. Only current documents are imported, never
versions, and nothing is deleted.

A user needs the Administrator role on the project. A machine needs no role
but Configuration.Transfer and the write scopes of every included kind:
Annotation.Write and Annotation.Manage for annotations, Configuration.Write
and Schema.Write for schemas, Configuration.Write and Template.Write for
templates, and Configuration.Write for configurations.
```

Everything of the current descriptions is still there. The scopes of the machine are now named per kind, as in `export-import.md`, instead of "the read scopes of the included kinds".

### 3. Short descriptions and the detail of `sform story kv export` and `import` (phase 3)

- `export`: "Export the key/value pairs of the view."
- `import`: "Import key/value pairs into the view."

No "(for administrators)": a Reader exports public pairs, a Contributor imports them, and `ContributorWithSecrets` is needed for secrets.

`export`:

```text

The formats are json, the 2s.key-values.v1 document with the levels; flat,
one JSON object of key: "value"; and env, .env lines (KEY=VALUE, with
db.password as DB__PASSWORD). Without --format, a *.json file gets json, any
other file env, and the standard output json.

Secrets have a value only with --include-secrets and the secret rights.
Without it, json lists them without a value, and flat and env leave them out
with a note.

A Reader exports the public pairs, and a ContributorWithSecrets the values of
secrets too. A machine needs Configuration.Read and Configuration.Transfer,
and for the values of secrets Secrets.Read and its mTLS certificate, unless
the project's policy turns the mTLS rule off.
```

`import`:

```text

The file is json, the 2s.key-values.v1 document with the levels; flat, one
JSON object of key: "value"; or env, .env lines (KEY=VALUE, with DB__PASSWORD
as db.password). Without --format, the format is guessed from the file name
and the content. flat and env carry no levels: keys matching a --secret glob
become secrets of the --protection level, or of the project's default secret
level, and the other keys are public.

The plan, a dry run on the server, is shown first and then confirmed: -y skips
the confirmation, and --dry-run stops after the plan. A replace that deletes
keys asks you to type the view name. The exit code is 45 when an item failed.

A Contributor imports public pairs. Secret values, changes to or from a secret
level, and a replace that deletes a secret need ContributorWithSecrets. A
machine needs Configuration.Write and Configuration.Transfer, and for secrets
Secrets.Write and its mTLS certificate, unless the project's policy turns the
mTLS rule off.
```

The final wrapping may differ from these drafts; the test of section 4 holds every line to 79 characters.

### 4. Tests

- `StorytellerTransferCommandTests.Commands_NameTheAdministratorRole_AndWhatAMachineNeeds` changes: the description is the short line with "(for administrators)", and the detail names the Administrator role, `Configuration.Transfer` and every scope of its kinds.
- A new `test/Commands/TransferHelpTests.cs` renders the help with a `DefaultHelpTextGenerator` of a fixed width of 80, for all four commands:
  - the parent's list (`sform story`, `sform story kv`) shows `export` and `import` with the short description on one line;
  - the command's own help starts with the short description and ends with the detail, after a blank line that follows the options;
  - no line of the detail is longer than 79 characters;
  - the options and arguments are the same as before (their templates), as the acceptance criteria ask.
- The detail of the key/value commands is checked for the facts of section 3: the three formats, `--include-secrets`, the plan, `-y`, the view name, `--secret`, the roles and the machine scopes.
- `StartupTests` of section 1.

### 5. Documentation

- `docs/Platform/Storyteller/export-import.md`, CLI section (line 621): the help lists the commands with "(for administrators)", and `sform story export --help` and `import --help` give the Administrator role and the scopes of a machine per kind after the options. The two quoted sentences go.
- `docs/Platform/Storyteller/key-values.md`, CLI section: one sentence that the help of `export` and `import` sums up the formats and the rights after the options.
- No other document quotes the help text.

### 6. Out of scope

- The descriptions of the other commands, also the long ones (`kv usage`, `issuer set`, …).
- A help text generator that wraps `ExtendedHelpText` to the console width. A subcommand copies the generator of its parent when it is created, and the conventions create the whole tree before `ConfigureConsoleApplication` runs, so it would have to be set on every command. The hand-wrapped text is simpler.
- The options and arguments of the four commands.

### 7. Phases and commits

1. `fix(platform): register the TimeProvider that sform story kv needs (424-216)`: section 1.
2. `fix(platform): give sform story export and import a short description (424-216)`: section 2, with its tests and `export-import.md`.
3. `fix(platform): give sform story kv export and import a short description (424-216)`: section 3, with its tests and `key-values.md`.
