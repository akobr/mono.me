# Phase 4 of CLI Automation Testing Project

Linear: [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend).

## Overview

Phase 4 is the fourth row of section 7 of [CLI Automation Testing Project](../2026-11-08%20CLI%20Automation%20Testing%20Project.md): section 4 of the spec. The catalogue [test-cases.md](../../test-cases.md) is aligned with the CLI of phase 1 and covers every command, alias, option form, argument and fixed value (4.1). Its 229 cases are in Testiny project `2SC`, with their ids written back to the catalogue (4.4). The coverage is enforced by `CommandCoverageTests` (4.2), and every row is checked against its test by `CatalogueTests`. The fake editor of mode E exists, `src/Platform/Cli.AutomationTests/editor` (4.3), with its API for the tests. The 12 Elementary tests of phase 3 carry their `[TestCase]` ids. The docs are [automation-tests.md](../../automation-tests.md) (new sections: the fake editor, the command coverage, the two json files, the catalogue check, automating a case) and the project's `README.md`. The catalogue and Testiny parts were done earlier in the phase (commit `8d06a62e`); the defects the catalogue found were fixed in the CLI afterwards (`b38d72a5`, [review](../../../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md)). This review covers the whole phase.

| Run (Windows 11, no target) | Result |
|---|---|
| `dotnet build src/Platform/Cli.AutomationTests/src` | 0 errors, no warning in the project or the editor (builds `src/Platform/Cli/src` and `src/Platform/Cli.AutomationTests/editor` through the project references) |
| `dotnet test --project src/Platform/Cli.AutomationTests/src` | 89 tests: 75 passed, 12 skipped (the Elementary tests without `Target:BaseUrl`), 2 failed as expected: the two gaps below |
| `… --treenode-filter "/**[Category=Coverage]"` | 9 tests: 7 passed, 2 failed (the gaps) |
| The gap of `CommandCoverageTests` | 907 of 946 items uncovered: commands 90 of 99, aliases 33 of 35, option forms 580 of 600, arguments 74 of 81, values 130 of 131 |
| The gap of `CatalogueTests` | 217 of 229 cases without a test (12 automated); the 12 Elementary tests match their rows |

The 75 passing tests are the 39 self tests of phase 3, 13 self tests of the fake editor, 4 self tests of mode E through sform, 12 self tests of the coverage and catalogue rules, and 7 coverage checks (the 4 of `CommandCoverageTests` that are not the gap, the 3 of `CatalogueTests` that are not the gap). The runs are those after the review of the phase ([below](#review-findings)).

**Consequence for the branch**: the two gap tests run in every run, also in `dotnet test --solution 42.mono.slnx` (the spec's "a new option without a test fails the build"), so the solution's run fails until phase 5 has automated every case. Phase 5 has to finish before the branch can merge with a green solution run.

## What Was Done

### 4.1 What "every" means: the catalogue

The draft catalogue of the plan (219 cases, commit `dc2d22e6`) was aligned with the CLI of phase 1 ([review](../../../Cli/specs/reviews/2026-11-08%20Phase%201%20of%20CLI%20Automation%20Testing%20Project.md)) in commit `8d06a62e`, case by case against the code:

- **229 cases** (was 219), 10 sections and 38 test classes. New: a context case per section (MCH-CTX-01, CRT-SHC-06, ANN-CTX-01, CFG-CTX-01, TPL-TPL-09, SCH-CTX-01, KV-CTX-01, KV-CTX-02), KV-AUD-05 (each of the nineteen audit operations) and KV-POL-08 (HSM). 12 Elementary, 65 High, 119 Medium and 33 Low; 81 P cases (all Owner), 15 F, 13 M, 10 T, 9 E, 5 X.
- **Every command, alias, option form, positional argument and fixed option value** of the CLI appears in the Covers column at least once, except `-p` of the context commands (the rule below). Throwaway scripts checked it against the command tree read from the sources while the catalogue was written. `CommandCoverageTests` (4.2) now does the same against the tests.
- **Header rules added**: the Owner and Machine roles; the P mode (the Owner creates the project with `account points create`, a "second user" is granted a role in the Pre, a disposable project's default policies); the features of mode X (`KeyVaultReference` dropped: `kv set --reference` stores the reference as text); the Covers rules (a token belongs to the command of the steps that uses it, fixed values as `--option=value`); the context rule (`-p` of a context command is covered by the command's own cases, `--projectKey`, `-v`, `--view` by the context case of each section); `<A>`, `<B>` and `<view>` of the context cases; the role projects; `<AuthKit organization id>`; a prompt on redirected input (2 or 43 with the "cannot ask for" line and the option); a machine's credential is checked on its first call; the target project enables every credential kind; the notation of the steps (no `sform`, the `story` area commands without `story`); the line-number gutter of formatted text.
- **Defects found**: writing the expectations from the code found six defects, recorded as D1 to D6 in the catalogue's header: `config set` crashed (1) on a 403; the schema writes ended a 403 with 1; `config patch` crashed on a failing `test` operation; `story get` crashed on an invalid key; `account machine renew` wrote no certificate and crashed outside the renewal window; `config set -c|-l` were parsed and never sent. Further crash paths were listed: `points revoke -r` below the member's role, `shared-cert issue` without CertificateAndApiKey, a file that is not JSON for `-i`. They were fixed in the CLI after this phase's catalogue work (commit `b38d72a5`): one mapping of API refusals (401/403 to 44, other 4xx to 2, 408/429/5xx to 3, with the server's message), `machine renew` writes the renewed certificate, and `config set` no longer offers `-c` and `-l`. The catalogue and its Testiny cases now expect the fixed behaviour (CFG-SET-05, CFG-SET-10, CFG-SET-12, SCH-DEF-02, SCH-DEF-03, ANN-SET-07, MCH-CRD-05, MCH-CTX-01), and the D rules are gone from its header.

### 4.2 Enforced by a coverage test

New folder `src/Platform/Cli.AutomationTests/src/Coverage/`, category `Coverage` (`CliCategories.Coverage`). These are plain TUnit classes: not `CliTest`, no target, not Elementary.

- **`CommandTreeReader`** reads `42.Platform.Cli.dll` with `System.Reflection.MetadataLoadContext`. The package was added to `Directory.Packages.props` (10.0.14, in alphabetical order) and referenced by the test project. The path is the `SformPath` metadata of phase 3 (the build of the source; `Cli:Path` is not used). The resolver gets every dll of the CLI's folder and of the runtime's folder. No code of the CLI or of McMaster runs. It follows McMaster 5.1's reflection path. I checked McMaster's behaviour with a scratch program, and sform's build has no generated command metadata:
  - commands from `_42.Platform.Cli.Commands.PlatformCommand` along `[Subcommand]`, named by `[Command]` (constructor names, `Name =`, else the class name without `Command` in kebab case);
  - `[Option]` and `[VersionOption]` on properties and fields of the class and its base classes (the most derived member wins). That includes `-p|--projectKey` and `-v|--view` of `BaseContextCommand`, also through `SchemaApiCommand` and `KeyValueCommandBase`. Template forms come from `-a|--long <V>`, `--long=<V>` and `-a|--long:<V>`. Without a template, from `ShortName`/`LongName`, else the member name in kebab case and its first letter. McMaster's reflection derives `FooBar` as `-f|--foo-bar`; its source-generated metadata, which sform does not use, gives `--foobar` without a short form. McMaster 5.1 allows `[Option]` on properties only (a field does not compile), so fields are read only in case a later version allows them. `[HelpOption]` and the conventions' `-?|-h|--help` are no items;
  - `[Argument(order, name)]` sorted by order, `arg0`…;
  - help-only groups: the command has subcommands and its `OnExecute`/`OnExecuteAsync` IL calls a member reference named `ShowHelp` (read through `System.Reflection.Metadata` from the method's RVA). That finds `sform` and the four schema groups, and not `assist`, which takes the application too but asks which subcommand to run;
  - enum types of options (`T`, `T?`, arrays, generic collections), and the member names of the enums `coverage-values.json` names.
  The model: `CommandTree`, `CliCommand`, `CliOption`, `CliArgument`. At the current CLI: 99 commands (as the spec counted), 35 aliases, 600 option forms, 81 arguments, and 130 fixed values from the json, so 945 items.
- **`CoversUsage`** reads every `[Covers]` of the assembly, on methods and on classes. `CoversAttribute` now allows classes too, and its comment states the rules. A usage records whether its method is a `[Test]`, its `[RequiresFeature]` features and TUnit's `[Skip]`.
- **`CoverageModel`** resolves a `[Covers]` by the catalogue's rules:
  - the path (with an optional leading `sform`) covers its command when it ends with the canonical name, every alias typed anywhere, and every help-only group it names;
  - the context `-p` counts when the path leads to a context command (any `[Covers]` of the command, with or without items, as the catalogue's rule says); `--projectKey`, `-v`, `--view` do not;
  - items: option forms exactly, `--option=value` by any form and a value in any case (the canonical value from the json), `argN` by position.
  A word the path or the items cannot place is a problem with what the command has instead (its subcommands, options or arguments). `CheckValues` validates the json (also that an entry's command is written canonically, since `CoverageValues.For` compares canonical paths), `Evaluate` returns a `CoverageReport` (uncovered, gated-only, unlisted and stale exceptions, invalid `[Covers]`, grouped messages).
- **`CommandCoverageTests`**:
  - `CommandTree_IsReadFromTheCli`: the reader on stable commands;
  - `Covers_NameOnlyWhatSformHas`;
  - `CoverageValues_MatchTheCli`;
  - `CoverageExceptions_ListTheFeatureGatedItems`;
  - `EveryCommandOptionArgumentAndValue_IsCovered`: the gap, grouped by command in the tree's order with the class and the aliases, one line per kind (`command`, `alias`, `option`, `argument (name)`, `value`), with a summary of counts per kind.
  A broken copy of the values file was tried by hand: an enum that does not exist, a value missing from an enum and an unknown option were each reported.
- **`coverage-values.json`** (committed, copied to the output): `command` (canonical path), `option` (long form), `values` (every value the CLI accepts and acts on), `source` (where the CLI keeps the set, and who refuses what is left out), and optional `enum` with `except` (members the CLI itself refuses with exit 2), `refused` (members the CLI sends and the API refuses) and `extra`. Unknown fields fail the load; `$comment` is allowed on the file and on each entry. It holds 30 options, started from the table of the catalogue work and re-checked in the code:
  - Enum-backed (24): `points grant`/`revoke --role` (`PermissionRole`, `None` refused by the API); `machine create --scope` (`MachineScope`) and `--manage` (`MachineScope` except the six runtime scopes, which the CLI refuses); `-k` of `create` (`MachineAccessCreateCredentialKind`) and `-k`/`-d` of `machine-auth` (`MachineCredentialKind`); `export -f` (`ViewExportCreateFormat`); `export`/`import -i` (`ViewArchiveContent`); `import -e` (`ViewImportExisting`); `config -f` (`ConfigurationFormat` plus `json`); `--mask` (`MaskMode`, internal); the `--protection` of `kv`, `kv set`, `kv export` and `--allow` (`KeyValueProtection`); `kv protect --protection` (except Public and Hyok, which the CLI refuses); `kv import --protection` (public, standard, hsm, byok; Hyok refused by the API); `--default` (Public refused by the API); `rotate --protection` (only Standard and Hsm, the rest refused by the CLI); `kv export`/`import --format` (`KeyValueFileFormat`); `kv audit --operation` (`KeyValueAuditEventOperation`).
  - String sets: `story -b` (rst, responsibility, sbt), `kv import --mode`, `--require-mtls` (`bool.TryParse`), the three schema `diff --format` (json, unified; passed to the API).
  - Re-checking moved one value after the review: `kv import --protection` parses all five levels; hyok is refused by the API (a value that is not a client-encrypted document), but **public is accepted** and imports the keys `--secret` matches as public values, so it is a value to cover. No row of the catalogue covers it yet, so the gap holds `storyteller kv import --protection=public` until a case is added (or the CLI refuses public as `kv protect` does, which moves it to `except`).
  - No option of sform has an enum type at phase 1 (all are strings), so the check that enum-typed options are listed currently finds none. The `enum` field brings the same drift detection to the string options parsed into an enum.
- **`coverage-exceptions.json`** (committed, copied to the output): `command`, `item` (missing for the command), `feature`, `reason`. It is pre-filled from the catalogue's X cases with the 13 items no ungated case covers:
  - `kv policy set --byok-key`, `--byok-tenant`, `--allow=byok`, `--default=byok`, `--allow=hsm`, `--default=hsm`;
  - `--protection=byok|hsm` of `kv set`, `kv protect` and `kv import`;
  - `kv policy rotate --protection=hsm`.
  SGN-DEV-06 (`-o`) and SGN-OUT-04 (`register`) are covered by ungated cases too. The check:
  - an item covered only by gated tests must be listed;
  - an entry is stale when it names nothing, has no reason, names a feature that is none of `CliFeatures`, is listed twice, is covered by a test that runs everywhere, or names another feature than its tests;
  - an entry whose item no test covers yet is not stale (its test is still to come).
- **The root `.gitignore` ignores `coverage*.json`** (coverage results), which would have kept both files out of git. `src/Platform/Cli.AutomationTests/src/.gitignore` re-includes them (`!coverage-values.json`, `!coverage-exceptions.json`).

**`CatalogueTests`** (same folder and category) is not in the spec; it was asked for with this phase:

- `RepositoryRoot` finds the first folder with `42.mono.slnx` above the test assembly; without one (a copy of the output) the tests skip with the reason.
- `Catalogue` parses the section table (`| [Name](#…) | \`Tests/<Folder>\` | N |`), the `##` sections, the `### <Title> (\`<Class>\`)` sub-folders and the 8-cell rows (a `|` inside backticks stays in its cell).
- `Catalogue_IsWellFormed` checks:
  - parse problems;
  - unique references, Testiny ids (numbers) and planned tests;
  - known priorities, roles, modes, features (`CliFeatures`);
  - the section counts;
  - P implies Owner, Machine implies M;
  - no empty Covers.
- `EveryCase_IsAutomated` is the gap: per class file, each missing row with the attributes it needs (`ExpectedAttributes`: `[Test] [TestCase(id)] [Elementary] [UserRole(Role.X)]` or `[MachineOnly]`, `[FreshSignIn]`, `[Terminal]`, `[DisposableProject]`, `[RequiresFeature(CliFeatures.X)]`) and its Covers.
- `EveryAutomatedCase_MatchesItsRow` checks each existing test against its row:
  - place: the namespace `Tests.<Folder>`, the file `Tests/<Folder>/<Class>.cs`, and a `// <Reference>:` comment in the folder;
  - exactly the row's `[TestCase]`;
  - the role (Machine is `[MachineOnly]` and no `[UserRole]`);
  - F, T, P and X in both directions (M and E are not attributable, so not checked);
  - Elementary in both directions;
  - the Covers by the coverage model's resolution (`CatalogueCovers`, a static class the self tests call directly): each command or alias token of the row is covered by a `[Covers]`; a `[Covers]` without items must cover a command token of the row; each item token of the row is an item of at least as many commands of the test as the row lists it (counted once per command, so `story config` and `story configuration` with `arg0` are one `arg0`, while the `arg0` of `set` and of `get` are two), and every item token of a `[Covers]` is in the row. A token the row lists once may be covered on any number of commands, as the context cases and KV-POL-07 need.
- `EveryTest_HasACase` checks that every `[Test]` of `Tests/` is planned by a row, its folder is a section, and its `[TestCase]` ids exist in the catalogue.
- The first run found only the missing `[TestCase]` of the 12 Elementary tests, which were then added. Their roles, modes, places, comments and `[Covers]` already matched.

### 4.3 Commands with an editor or a file

- **How sform runs a custom editor** (`EditorService`):
  - `ParseCustomCommand` splits `CustomCommand` at its first space and appends `"<path>"`;
  - the file is `<temp>/<prefix>-<8 random>.json`, written with the document indented (UTF-8 with a BOM) and deleted after the run;
  - the process starts with `UseShellExecute=false` and `CreateNoWindow=true`, and inherits sform's environment and standard streams;
  - sform waits for the editor and reads the file back. A non-zero exit is "Editor exited with code N." and 1. An equal document is "No changes detected." and 42. Invalid JSON is "Invalid JSON: …" and "Nothing was saved." with 2 on redirected input, and an offer to open the editor again in a terminal. A changed document is shown as a diff and saved with `-y`.
- **The helper** `src/Platform/Cli.AutomationTests/editor` (`Cli.AutomationTests.Editor.csproj`, `42.Platform.Cli.AutomationTests.Editor`, `_42.Platform.Cli.AutomationTests.Editor`, net10.0, `OutputType Exe`, `UseAppHost`, not packable, not a test project, TestableIO for its files). It is added to `42.mono.slnx`. The test project references it with `ReferenceOutputAssembly="false"` and `Private="false"`, and `RecordSformPath` records its dll as `[assembly: AssemblyMetadata("EditorPath", …)]`. Its last argument is the file. Its variables:
  - `CLI_EDITOR_MODE`: steps per invocation of a run (the last repeats): `replace`, `merge` (RFC 7396), `none`, `invalid`, `exit[:<code>]`;
  - `CLI_EDITOR_FILE_<n>` / `CLI_EDITOR_FILE`: the step's file;
  - `CLI_EDITOR_LOG`: one JSON line per invocation, with the file, the arguments, the step, the exit code, the original and written texts, and the error;
  - `CLI_EDITOR_RUN`: invocations are counted per run in one log.
  It writes nothing to stdout, which it shares with sform. Its own exit codes are 64 (no file), 65 (no or unknown mode), 66 (missing file) and 67 (merge of non-JSON).
- **The tests' API** (`src/Editing/`):
  - `FakeEditor`: `ResolveCommand` gives the apphost next to the recorded dll (`.exe` on Windows); when that path has a space it copies the editor's build output once per build to `<temp>/sform-automation/fake-editor/<hash>/` (on Windows through the 8.3 path of a temporary folder with a space) and gives that apphost, and only when the temporary folder has a space too does it throw with what to change. `With(change, options?)` writes the change files and returns the `SformRunOptions` of one run, with the variables, a new run name (`RunOf(options)`) and `DOTNET_ROOT` when the machine has none and the dotnet the tests use is a real installation (links followed, `host/fxr` present). `ReadLog()` reads the log of every run, `ReadLog(options)` that of one run.
  - `EditorChange`: `Replace`, `Merge` (text or `JsonNode`), `None`, `Invalid`, `Exit(code)`, `Sequence(…)`.
  - `EditorLog` (`Invocations`, `Count`, `Run`, `Single()`, `Last()`, `OfRun(run)` and `OfRun(options)`, each an `EditorLog` of one run) and `EditorInvocation` (`File`, `FileName`, `Arguments`, `Step`, `ExitCode`, `Original`/`OriginalJson()`, `Written`/`WrittenJson()`, `Error`).
  - `CliTest.UseEditorAsync(home?)` replaces the phase 3 placeholder: it writes `editor.config.json` with the apphost and returns the `FakeEditor`, whose log (`editor.log`) and change files live in a folder of its own in the working directory (`fake-editor/<id>/`). `UseEditorAsync(customCommand, home?)` stays for other programs.
- **Self tests**:
  - `FakeEditorSelfTests` (13) start the apphost as `EditorService` does (quoted path in the argument string, no shell): built and recorded without a space; replace; merge (nested merge, null removal, array replacement); none; invalid; exit code; a sequence counted per run; the log of one run (`ReadLog(options)`); two editors of one test kept apart; a build path with a space copied to a folder without one, whose apphost edits like the build's; `DOTNET_ROOT` only for a real installation; the missing mode (65, stderr and the log); a quoted path with spaces as one argument.
  - `CoverageRulesSelfTests` (12) check the coverage and catalogue rules on a command tree built by hand (no sform, no catalogue): aliases in the middle and at the end of a path, help-only groups, the implicit `-p` only on context commands, values in any form and case and the problems of a `[Covers]`; listed, unlisted and stale exceptions (a running test, a wrong or unknown feature, listed twice, no reason, a missing command, and an uncovered entry that is not stale); the drift checks of `coverage-values.json` (an alias path, a missing member, unknown or doubled `except`/`refused`, `except` without an enum, an enum-typed option without an entry); the strict loading of both files; and `CatalogueCovers` on a context row, a value listed once for several commands, a token listed twice, and each kind of mismatch.
  - **Mode E end to end without a target**: no sform edit path works offline against a closed port, so `EditorThroughSformSelfTests` (4) runs the built sform's `story config edit <key> -y` as a machine with a structured API key against `StubApi`, a minimal HTTP/1.1 server on the loopback (TcpListener, Content-Length bodies, `Connection: close`). The stub answers `GET …/configuration/<key>/versions` with `[]`, so sform opens the editor on `{}`, and the creating `POST …/configuration/<key>` with the saved model. Merge is saved ("has been saved (version 1)"), with the merged JSON in the POST and `Authorization: ApiKey 2s.…`. None ends with 42 and no write; invalid with 2, "Invalid JSON" and no write; `Exit(3)` with 1 and "Editor exited with code 3.". This shows that sform starts the apphost from `editor.config.json` of `SFORM_HOME`, with its temporary file, and waits for it.
- Files of `-i`, `-o`, `-e` and the rest live in the test's working directory, as phase 3 made it (outside the artifacts, listed on failure).

### 4.4 The catalogue and Testiny

- Testiny project **`2SC`** (id 2) was created by hand. Its cases were created from the catalogue through the Testiny API in commit `8d06a62e`:
  - 10 top-level folders, one per section, and one sub-folder per test class (38; checked against Testiny with this review);
  - 229 cases on the STEPS template, each with a precondition holding the Reference, Role and Mode, the Covers and "Automated: `<Class>.<Method>`";
  - type Functional, status Ready for Test, automation Automated, priority Elementary 0, High 1, Medium 2, Low 3.
  The ids were written back to the catalogue's Testiny column. The cases changed by the defect fixes were updated in Testiny with commit `b38d72a5`.
- The 12 Elementary tests got `[TestCase(<id>)]` with the numeric overload, as the admin UI's tests use it: SGN-DEV-01 234, SGN-MSI-01 243, ACC-SET-01 268, MCH-LIF-03 259, ANN-SET-01 275, ANN-SET-08 293, CFG-GET-01 368, CFG-GET-02 369, CFG-SET-01 395, CFG-GHA-01 436, KV-PAR-01 349, KV-SEC-01 380. `CatalogueTests` keeps the ids and the rows in step from now on.

### Docs

- [automation-tests.md](../../automation-tests.md):
  - Run: the `Coverage` filter, and the gap tests failing until phase 5;
  - the example with `[TestCase]`, and `UseEditorAsync` in the `CliTest` table;
  - the attributes (`[TestCase(<id>)]`, `[Covers]` on classes, the `Coverage` category);
  - an example of an E test, and the new section "The fake editor (mode E)";
  - Self tests;
  - the new sections "Command coverage" (reader, rules, tests, the gap message), "`coverage-values.json`", "`coverage-exceptions.json`", "The catalogue check" and "Automating a case (phase 5)";
  - Testiny (the cases exist);
  - Troubleshooting.
- `src/Platform/Cli.AutomationTests/README.md`: `editor/`, `src/Editing/`, `src/Coverage/`, the json files, the Coverage run.
- `CLAUDE.md` is left to the orchestrator, as in phase 3.

## Deviations

- **The gap tests fail in the solution's run until phase 5.** That is what the spec intends ("a new option without a test fails the build"), but it means the branch cannot merge with a green solution run before phase 5 is done.
- **New**:
  - `CatalogueTests` with its checks of place, ids, role, modes, priority, Covers and the reference comment;
  - `coverage-values.json`'s `enum`/`except`/`extra` drift detection for string options parsed into enums (the spec named only the file of exceptions, and the task the enum-typed options, of which sform has none);
  - a `feature` field in `coverage-exceptions.json`;
  - `[Covers]` on classes;
  - the end-to-end editor self tests with `StubApi`.
- **Covers semantics made precise**:
  - a path covers its command only when it ends with the canonical name (an alias leaf covers only the alias);
  - a help-only group is covered when it is named in a path;
  - the context `-p` counts for any `[Covers]` of the command, including machine cases, where the catalogue's scripts excluded machine cases. That is more lenient for M cases, but every context command also has user cases.
  - The catalogue comparison is not a plain equality, because the catalogue lists a command token only when the case claims the command, while a `[Covers]` always has a path. The rules are in the docs.
- **The editor helper is reached through its apphost**, with a run name, a log and `DOTNET_ROOT` (when needed) added. A build path with a space is handled by a copy of the editor's output in a temporary folder without one. The real fix would be quoting in `ParseCustomCommand`, which is in the CLI.
- **The fake editor uses JSON lines** for its log (one line per invocation), so the tests get the original document, which sform deletes with its temporary file.

## Review findings

A review of the phase found nine issues; each was checked against the code and the catalogue, and all nine were fixed.

1. **High: the row comparison could not pass for the context cases.** `CatalogueTests` compared the item tokens of a row with the `[Covers]` counted once per command, so a token the row lists once could be covered on one command only. The context cases (MCH-CTX-01, CRT-SHC-06, ANN-CTX-01, CFG-CTX-01, TPL-TPL-09, SCH-CTX-01, KV-CTX-01, KV-CTX-02) list `--projectKey`, `-v` and `--view` once and are, by the catalogue's own rule, what covers those forms of every context command; KV-HYK-04 lists `arg0` once for `disable` and `enable`, and KV-POL-07/KV-POL-08 list `--protection=byok`/`hsm` once for `kv set`, `kv protect` and `kv import`. About 220 items could not have been covered without failing the row comparison. The comparison moved to `Coverage/CatalogueCovers.cs`: a row's count is now a lower bound (each item token of the row is an item of at least as many commands of the test as the row lists it), and every item token of a `[Covers]` must be in the row. The rows that list a token twice (ANN-SET-01, TPL-TPL-05, SCH-TYP-01, SCH-TYP-03, SCH-ANN-01, SCH-ANN-03, SCH-DSC-01, SCH-DSC-03, KV-PAR-01, KV-BLK-09) all list it for two different commands, so they still need two. A value token of a `[Covers]` now also compares as the catalogue writes it (`ResolvedCovers.ItemTokens`: `-r=reader` is `--role=Reader`), as the coverage already counted it, where before the row comparison wanted the exact text. Self tests cover a context row, a value listed once for several commands, a token listed twice, a value in another form and case, and the mismatches.
2. **Low: `except` hid values the CLI accepts.** `kv import --protection public` imports the keys `--secret` matches as public values and nothing refuses it; `hyok` there, `kv policy set --default public` and `points grant|revoke -r None` are sent and refused by the API. `except` is now only for members the CLI itself refuses (`kv protect`, `kv policy rotate`, `machine create -m`), a new `refused` field holds those the API refuses (with the place in `source`), and `public` became a value of `kv import --protection`. That item is in the gap until the catalogue has a case for it, or the CLI refuses it.
3. **Low: an alias path in `coverage-values.json` silently dropped its values, and unknown fields were ignored.** `CheckValues` now reports an entry whose command is not canonical, both files load with `JsonUnmappedMemberHandling.Disallow` (`$comment` is a modelled property on the file and the entries) and name the file in the error, and an exception whose feature is none of `CliFeatures` is stale.
4. **Low: the coverage rules had no direct tests.** `SelfTests/CoverageRulesSelfTests.cs` (12 tests) runs them on a hand-built command tree, including the exceptions branches that today's data never reaches (every pre-filled exception is still uncovered).
5. **Medium: `DOTNET_ROOT` could point at a link's folder.** `With` set it to the folder of the dotnet found on `PATH` as is, so on Linux `/usr/bin/dotnet` (a link) gave `DOTNET_ROOT=/usr/bin` and the apphost would end with 131. `FakeEditor.RuntimeRoot` now leaves it to the machine's own `DOTNET_ROOT`/`DOTNET_ROOT_<ARCH>` when set, else follows the links and sets it only when `host/fxr` exists, and otherwise leaves it out so the apphost searches the global installation.
6. **Low: a build path with a space relied on 8.3 names.** Data volumes usually have them off, and Linux has none. The editor's build output is now copied once per build to `<temp>/sform-automation/fake-editor/<hash>/`, which a self test checks with a copy of the build in a folder with spaces.
7. **Low: two editors of one test shared their change files.** Each `FakeEditor` has its own folder, `fake-editor/<id>/`, with `editor.log` and its `<run>-step-<n>.json` files.
8. **Low: a test could not read the log of one run.** `With` makes one run per call; `FakeEditor.RunOf(options)`, `ReadLog(options)` and `EditorLog.OfRun(options)` give that run's invocations, so `Single()` works in the E cases that run `edit` several times. The docs say that one `With` is one run, and list the edit runs of each E case.
9. **Low: the README** said that only the self tests run without a target. It now names the coverage checks and the two gap tests that fail until phase 5.
