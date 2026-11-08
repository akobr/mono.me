# Short Descriptions of the Export and Import Commands of sform

## Overview

`sform story --help` and `sform story kv --help` now list `export` and `import` with one line each. The detail used to fill six to nine lines of the list. It now sits in the help of each command, after its options: what the archive or file holds, the formats, the secrets, and the rights of users and machines. Nothing of the old text is lost, and the options and arguments are unchanged.

The work also found that every `sform story kv` command crashed, `--help` included. `Startup` now registers the `TimeProvider` that `KeyValueListCommand` needs.

This review covers [Short Descriptions of the Export and Import Commands of sform](../2026-11-08%20Short%20Descriptions%20of%20the%20Export%20and%20Import%20Commands%20of%20sform.md) in full, in its three phases, plus the fixes from a code review (section 6). Linear: [424-216](https://linear.app/424fun/issue/424-216/simplify-the-descriptions-of-sform-story-exportimport-and-sform-story).

## What Was Done

### 1. Register the `TimeProvider` (phase 1)

As specified:

- `src/Platform/Cli/src/Startup.cs`: `ConfigureServices` registers `TimeProvider.System` as a singleton, with a comment that says why. It uses `services.AddSingleton` instead of `TryAddSingleton`, like the other registrations of the method; nothing registers a `TimeProvider` before it.
- `src/Platform/Cli/test/StartupTests.cs` is new and has two tests:
  - `EveryCommand_ResolvesFromTheServicesOfStartup` runs `Startup.ConfigureServices` on a `ServiceCollection`, with an empty configuration and a production `IHostEnvironment`. It walks every command below `PlatformCommand` through `[Subcommand]`. For each parameter of a public constructor, it asks `IServiceProviderIsService` whether the type is registered, or checks that the type is one the host or McMaster provides. A command passes when one public constructor resolves. That is McMaster's rule both for a single constructor and for several.
  - `TimeProvider_IsTheSystemClock`.
  - Without the registration, the first test fails and names only `_42.Platform.Cli.Commands.KeyValues.KeyValueListCommand`. With it, both tests pass. Calling `ConfigureServices` in the test leaves no log file in the test's output folder.

In the built tool, `sform story kv`, `sform story kv --help` and `sform story kv export --help` work again.

### 2. Short descriptions and the detail of `sform story export` and `import` (phase 2)

- `StorytellerExportCommand`: `Description = "Export the view (for administrators)."`, `ExtendedHelpText = HelpDetail`.
- `StorytellerImportCommand`: `Description = "Import into the view (for administrators)."`, `ExtendedHelpText = HelpDetail`.
- `HelpDetail` is an `internal const string` of each command. It is a raw string literal that starts with an empty line. A raw string is a constant, so it works as an attribute argument, and its source looks the way it prints. The line ends are those of the checkout: CRLF on Windows, LF on Linux. The tests compare lines, not line ends.
- Section 6 changed the second paragraph of both texts. The rest is as in the spec.

The built tool prints:

```text
  export                   Export the view (for administrators).
  get                      Retrieve a specific annotation.
  history                  Show who changed an annotation, when, and what.
  import                   Import into the view (for administrators).
```

`sform story export --help` ends with:

```text
  -?|-h|--help          Show help information.

The zip holds the annotations, schemas, templates and configurations of the
view, or the kinds of --include: a folder of JSON files with the versions
(--format folder), or a git repository with every version as a commit
(--format git).

A user needs the Administrator role on the project. A machine needs no role
but the scope Configuration.Transfer and the read scope of every included
kind: Annotation.Read for annotations, and Configuration.Read for schemas,
templates and configurations.
```

`docs/Platform/Storyteller/export-import.md`, CLI section: the paragraph on the help no longer quotes the old sentences. It gives the two lines of the list, and says that the help of each command ends with three things: what the archive holds, the role, and the scopes per kind.

### 3. Short descriptions and the detail of `sform story kv export` and `import` (phase 3)

- `KeyValueExportCommand`: `Description = "Export the key/value pairs of the view."`.
- `KeyValueImportCommand`: `Description = "Import key/value pairs into the view."`.
- Both have a `HelpDetail`, as in section 2.

One sentence differs from the spec. The spec had "A Reader exports the public pairs, and a ContributorWithSecrets the values of secrets too." The code has "A Reader exports the pairs; the values of secrets need ContributorWithSecrets." A Reader's export also lists the secrets, without their values, so "the public pairs" was wrong. Section 6 changed two more sentences.

`docs/Platform/Storyteller/key-values.md`, **Files** paragraph of the CLI section:

- `export` has no content to look at, so its guess goes by the name alone. Before, the paragraph said "guessed from the file name and the content" for both commands. It now gives the rule of `KeyValueFormats.Detect(OutputPath, null)`, as corrected in section 6.
- One sentence is new: the list gives `export` and `import` one line each, and their own help sums up the formats, the secrets and the rights after the options.

### 4. Tests

- `StorytellerTransferCommandTests`:
  - `Commands_NameTheAdministratorRole_AndWhatAMachineNeeds` expects the short description. In the detail, it expects the Administrator role and the sentence on the machine's scopes per kind.
  - `Commands_SayWhatTheArchiveHolds` is new and checks the first paragraph of each detail.
  - The detail is compared as one line of words (`TransferHelpTests.Unwrap`), so the wrapping and the line ends do not matter.
- `TransferHelpTests` is new and covers all four commands. It renders the help with a `DefaultHelpTextGenerator` of a fixed width of 80:
  - `ParentList_GivesTheCommandOneLine`: the parent's list shows the command with its description. The next line is the next command, not a continuation.
  - `Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions`: the first line is the description. The help ends with the detail, right after the line of `-?|-h|--help` and a blank line.
  - `Detail_FitsAnEightyColumnTerminal`: no line of the detail is longer than 79 characters or ends with a space.
  - `ArgumentsAndOptions_StayAsTheyWere`: the arguments and the option templates match the base commit.
  - `KeyValueDetail_GivesTheFormatsTheSecretsAndTheRights`: the whole detail of the two key/value commands, and no "administrators" in their description.
- The tests build the commands as `CommandLineApplication<StorytellerListCommand>` with the default conventions. Rendering the help creates no model, so the tests need no services.

All 1,084 tests of `src/Platform/Cli/test` pass. The changed files add no build warning.

### 5. Documentation

As in sections 2, 3 and 6. No other document quotes the help of the four commands. `docs/42for.net/cli/sform.md`, `platform/annotations.md`, `platform/admin-ui.md` and `platform/configuration.md` name the rights, not the help text.

### 6. Fixes from the code review

A review workflow had four reviewers: the accuracy of the view commands, the accuracy of the key/value commands, the code and tests, and the docs and spec. A skeptic checked each finding. Four of the nine findings held, all of them low. All four are fixed in one commit:

1. **Management scopes are not write scopes.** The import help called `Annotation.Manage`, `Schema.Write` and `Template.Write` "the write scopes of every included kind". The server's catalog (`Scopes.cs`), `sform account machine create` and `export-import.md` call them management scopes. A reader could take "write scopes" to mean the `-w` set. The help now says "the scopes of its direct writes", as the server's `403` does (`ResponseTransferForbiddenImport`). Both texts also say "the scope Configuration.Transfer", so it does not read as a role.
2. **`.env.*` names.** `Detect` checks for `.env`, `*.env` and `.env.*` before `*.json`, so `-o .env.json` writes `env`. The export help and `key-values.md` now give the rules in that order: `env` for `.env`, `*.env` and `.env.*`, `json` for another `*.json`, `env` for any other name, and `json` for the standard output. `Detect` itself is unchanged, because a change would also change the guess of the import.
3. **Exit code 2 or 45.** An item that is not valid, such as a new secret without its value (`SecretValueMissing`), fails the plan with `400 ImportInvalid`. Nothing is written and the exit code is `2`. Only a write that fails after the plan gives `45`. The import help and `key-values.md` now say both, where they had said "45 when an item failed".
4. **StyleCop `SA1202`.** A private helper of `TransferHelpTests` came before a public test, and now comes after it. The nullable warning on `command.Description` is gone as well (`ShouldNotBeNull` first).

Five findings were refuted:

- `--include-secrets` without the rights is refused with `403`. The help never says it exports without values.
- `-y` also skips the view-name prompt of a `replace`. The help says that `-y` skips the confirmation, and that prompt is the confirmation.
- The nullable warning. It is real, and it was fixed anyway.
- The review file was not committed yet when the reviewers ran.
- The **Tests** paragraph of `export-import.md` did not name `TransferHelpTests`. That paragraph was still correct, but it now names the class.
