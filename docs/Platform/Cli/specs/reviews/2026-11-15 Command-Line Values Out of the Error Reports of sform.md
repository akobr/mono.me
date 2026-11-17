# Command-Line Values Out of the Error Reports of sform

## Overview

This review covers all of [Command-Line Values Out of the Error Reports of sform](../2026-11-15%20Command-Line%20Values%20Out%20of%20the%20Error%20Reports%20of%20sform.md) (Linear 424-75), delivered in one phase with three rounds of fixes after adversarial reviews and one after a fuzz of the scrubber. Only `ErrorReporter` sends to Sentry now, with its own `SentryClient` per report: what happened, the command path and the names of the options, and for an exception its types, its stack and the first line of each message scrubbed of every value of the run, the names of the configured target and credential, customer data and secrets. No log entry reaches Sentry. The local log keeps the project and the view given with `-p` and `-v`, never the other values or a secret. `DO_NOT_TRACK` and `SFORM_TELEMETRY_OPTOUT` stop the reports. Nothing was sent to the real Sentry project and nothing was run against a live API.

| Commit | Content |
| --- | --- |
| `ea352b596` | The spec |
| `16e9a1d22` | `CommandUsage`, `CommandLineValues`, `CommandRun` (sections 1-2) and their tests |
| `ba095ccd3` | `ReportScrubber`, `RedactedException`, `ErrorReporter`, `Program`, `Startup`, the opt-out (sections 3-8) and their tests |
| `cab01bc10` | The user docs (section 10) |
| `cb9af69d3` | Fixes of the first review round |
| `fdaf71990` | Fixes of the second review round |
| `fb51418eb` | The user docs made exact |
| `421fd0f51` | Fixes of the third review round |
| `c208e2fbb` | Fixes from the fuzz of the scrubber, and its seeded test |

## What Was Done

All paths are relative to `src/Platform/Cli/`.

### 1. The usage of a run: `CommandUsage`

As planned, in `src/Telemetry/CommandUsage.cs`. `IsGiven(option)` is `Values` not empty and not just the `DefaultValue`, so a default the model wrote in an earlier parse (`story export --format`, `--timeout`) is not an option given. Names come from the model (`--<LongName>`, else `-<ShortName>` / `-<SymbolName>`), checked against `^--?[A-Za-z0-9?][A-Za-z0-9-]{0,63}$`; the command first, then each parent, without repeats.

### 2. The values of a run: `CommandLineValues`

`src/Telemetry/CommandLineValues.cs`, with `Hidden`, `All` and `LocalValues` as planned (`LocalValues` for `--projectKey`, `--view` and `--viewName`). Deviations and additions:

- `FromArguments` also takes what follows the letter of a short option (`-pacme.billing`, a cluster), besides `--name=value`, `--name:value` and `@file`.
- `Merge` leaves out a token of `args` that names a command of the parsed chain (`story`, `kv`, `set`), so command names do not cut messages; a value of the model always stays.
- `WithParts` grew in the review rounds: each line of a value of several lines, both sides of the first `=` whatever their length (`-x db.password=x9/k2;Qz` gives `db.password` and `x9/k2;Qz`), pieces of four characters or more between `= , ; . / \` without a leading `@` (a full key) and none ending with `:` (a URL scheme). A file path gives its segments, its file name and its stem instead (`out/acme-billing.env` gives `acme-billing`), and `ErrorReporter` adds its full form. No piece is a common word of paths and URLs (`Users`, `home`, `AppData`, `default`, `https`, …): a piece is replaced in every message, and the second round showed `Users` and `home` cutting common texts. The spec's example (`-x db.password=x` gives `db`) contradicted its own four-character rule; the rule won for pieces, and the sides of `=` cover the example's intent.
- `CommandRun` records the usage and the values from the root's `OnParsingComplete` (`RecordParsing`), a failed parse (`RecordFailedParsing`) and `Main`'s arguments (`RecordArguments`).

### 3. Scrubbing texts: `ReportScrubber`

`src/ErrorReporting/ReportScrubber.cs`. The rule set is the plan's, with these changes from the review rounds:

- **Order.** The shapes go first, then the values: a value replaced inside a shape (`acme` of `ada@acme.com`, `https:` of a URL, `password` of `Password=…`, a hex part of a GUID, a drive letter) hid the shape from its rule and left the rest of it. In return, what a shape left of a value next to its mask (`kV9#` of `kV9#[Filtered]`, the host of a URL value) is masked with it (`ScrubRemnants`), and adjacent masks collapse into one. A value is replaced whole first, then its remnants; the search goes on after the masks it wrote, a remnant may hold any character but no part of a mask (also of the quoted `"[Filtered]"` of a JSON member), and is looked for up to 512 characters. Every rewrite takes characters outside the masks, so the rewrites end (the third round found a loop without end for a value such as `**/*.json`).
- **Paths.** A path runs across a space while a separator follows on the line (`C:\Users\Ada B Lovelace\…`, `OneDrive - Contoso\…`) and across an apostrophe inside a word (`O'Brien`); a quote inside a quoted path goes on when a letter, or a separator before the next quote, follows it (`'C:\Shares\Chris' Projects\x.pfx'`), so two quoted paths of a line stay apart.
- **Cut quotes.** An opening quote the first line cut off is masked to the end, only when the line was cut (Newtonsoft writes a lone quote into its messages).
- **Short values.** A value under four characters (the spec said three) is replaced as a whole word only, also next to an escape (`%22`, `\n`).
- The cut at 300 characters never splits a surrogate pair.
- `ScrubEvent` also removes `debug_meta.images[].debug_file` folders and every frame's `abs_path` (a build from source has the developer's profile there), besides `code_file`, the user, the request, the server name, the tags, extras and contexts outside the allow-lists, and `Exception.Data`.
- A regex that times out: `BeforeSend` throws and the SDK drops the event; `RedactedException` writes the chain without messages.

The secret rules are a copy of the API's `SentryScrubber` rules; sharing them in a library stays out of scope.

### 4. The local log: `RedactedException`

As planned, but its text is built once in `RedactedException.Of`, so no scrubbing runs inside the file sink, where Serilog swallowed a timeout and dropped the exception. When a rule times out, the chain keeps its types and stack traces and every message reads `(the message is left out)`. `ToString` writes the chain like `Exception.ToString` (`AggregateException` with each inner exception); `FileNotFoundException.FileName` and `Exception.Data` are not written.

### 5. Reporting: `ErrorReporter`

`src/ErrorReporting/ErrorReporter.cs`, as planned, with these deviations:

- `ReportAsync(level, what, run, exception)` has no `args`: `RunAsync` records them in the run (sections 2 and 6 of the spec; the signature in section 5 was a slip).
- `Create(logger, settings, configure, fileSystem)`: `configure` is the tests' transport; the file system only gives `Path.GetFullPath` for the full form of a value that names a file (`../ops/db.env` is `C:\work\ops\db.env` in a message of `System.IO`), added with both separators. `None` is a static field.
- `ErrorReportingSettings` (`src/ErrorReporting/ErrorReportingSettings.cs`) is a record with `Create(…)` from what `InitializeHost` captured:
  - `ContextValues`: the default organization, project and view, `STORYTELLER_PROJECT` and `_VIEW`, the parts of `STORYTELLER_KEY`, the machine of `STORYTELLER_API_KEY`, the values of `STORYTELLER_ENDPOINT`, `_API_URL`, `_CLIENT_ID`, `_TOKEN_URL`, `_TOKEN_SCOPE`, `_KEY` (also when it is no valid key), the certificate and token file variables, `ACTIONS_ID_TOKEN_REQUEST_URL`, `SFORM_HOME` and the `SFORM_<section>__<key>` settings (not switches such as `true` or `file`), the user name, the machine name and the profile folder. The first round took every `STORYTELLER_*` and `SFORM_*` value; switches such as `SFORM_TOKEN_STORE=file` then cut `file` from every message, so the second round lists the variables. The variables are read without regard to case, the first of two that differ only in case kept (`http_proxy` and `HTTP_PROXY` on Linux threw in a case-insensitive dictionary and emptied both lists), and the person values are added apart.
  - `SecretValues`: `STORYTELLER_API_KEY`, `_CLIENT_SECRET`, `_CLIENT_CERTIFICATE_PASSWORD`, `_CERTIFICATE_PASSWORD` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN`, hidden from the local log too.
  - `ReportTimeValues`, read when a report is made: the identity of the loaded credential and `IMachineSignIn.KnownNames`, the organizations, projects and machine ids the credential named while it loaded, also when it failed (a certificate of another project than its API key). `KnownNames` is new on `IMachineSignIn` (a default of `[]`) and `MachineAuthenticationService`.
- The opt-out is read in `InitializeHost` with `TelemetrySettings.IsOptedOutBy`, not taken from the telemetry: `CommandTelemetry.Create` turns itself off on any failure and would lose it.

### 6. Program

As planned: the two-argument `RunAsync` passes `CurrentRun` and the reporter of the host; the four-argument one takes them from a test. Every catch reports through `ErrorReporter` with the texts of the table in the spec. `ConfigureConsoleApplication(application, run)` is internal, so the tests parse with Program's own configuration (response files, the separator, the root's `OnParsingComplete`); the one-argument overload passes `CurrentRun`. `LogWithSentry`, `ConfigureSentry` and `using Sentry` are gone.

### 7. Startup and packages

As planned: no Sentry sink, `Sentry.Serilog` dropped from `Cli.csproj` (still used by mrepo and DbCreator). The filter of `McMaster.Extensions.Hosting.CommandLine` below `Warning` is both a `LoggingBuilder` filter and a Serilog `MinimumLevel.Override`: `AddSerilog` lets every level through to Serilog, so the filter alone had no effect there (`StartupTests` checks it).

### 8. Telemetry opt-out

`TelemetrySettings.IsOptedOutBy(getVariable)` is the one check, used by `Resolve` and `InitializeHost`. The `IsOptedOut` property of the plan was added and removed again: production never read it.

### 9. Tests

`dotnet test --project src/Platform/Cli/test`: 2128 passed (2086 before the change). New or changed:

- `test/Telemetry/SformApplication.cs` (the application of `CommandPathTests`, configured by `Program.ConfigureConsoleApplication`), `CommandUsageTests`, `CommandLineValuesTests`.
- `test/ErrorReporting/`: `RecordingTransport` and `RecordingLogger`; `ReportScrubberTests` (each rule, the order, remnants and their end, paths with spaces and apostrophes, cut quotes, escapes, surrogate pairs, `ScrubEvent`), `ReportScrubberFuzzTests` (1,500 seeded texts and values of the characters the rules care about: the scrubbing ends, throws nothing and keeps no value of six characters or more that it was given whole; 40,000 cases passed once); `ErrorReporterTests` (off without a DSN, with the opt-out and with a refused DSN; the options; the event; full paths; secrets of the environment; values known at report time; a timeout); `ErrorReportingSettingsTests`; `ProgramReportTests` with the three cases of the issue — a secret value, an unknown command, a crash — and an invalid value and a response file.
- `StartupTests.TheHostingOfMcMaster_LogsNothingBelowWarning`, `TelemetrySettingsTests` (`IsOptedOutBy`), `MachineAuthenticationServiceTests` (`KnownNames` of a mismatch).

`dotnet test --project src/Platform/Cli.AutomationTests/src --treenode-filter "/**[(Category=SelfTest)|(Category=Coverage)]"`: 88 passed. The Sentry tests read the envelope as it would be sent and use no static hub, so they run in parallel. `ProgramProcessTests` set `DO_NOT_TRACK=1`, so they no longer send warnings to the real Sentry project.

### 10. Documentation

- `docs/42for.net/cli/sform.md`: what the log in `logs/` keeps (the address of every request, the lines of the reported failures, never the other values or a secret, and nothing for a failure a command handles itself); a new "Error reports" subsection of Telemetry (which failures are reported, what is sent, what becomes `***` or `[Filtered]`, what is never sent, the opt-out); the variables table says the opt-out turns the error reports off too.
- `docs/Platform/Storyteller/error-reporting.md`: one line on sform's own rules, linked to the user doc.

### 11. Out of scope

As planned. Also left: a file name with a space in an unquoted path keeps its last words (`*** key.pfx`); a value a command reads from a prompt, the standard input or a file is not known to the reporter; the HTTP entries of `HttpClientFactory` in the local log keep the path of each request (names, no secrets), which the user docs now say.

### Review

Three adversarial rounds ran on the branch, the findings verified with scratch tests in separate worktrees.

- **First round** (15 findings, 13 confirmed, all low or medium): the value pass before the shapes (`ada.lovelace@***.com`, `***//host/path?query`, `***=Hunter2xyz`); `STORYTELLER_KEY` and the credential's names in sign-in messages; paths with a space or an apostrophe; the first line of a multi-line value; relative paths in the local log; the short sides of `path=value`; a regex timeout inside `RedactedException.ToString`; the unused `IsOptedOut`; leftovers; nullable warnings; the tests' copy of Program's parser configuration. All fixed in `cb9af69d3`.
- **Second round** on `cb9af69d3`: switches and path pieces of the new context values cut common words (`file`, `json`, `Users`); a value a shape covered in part left its rest; the cut-quote rules hid Newtonsoft's positions; a certificate of another project than its API key; `ACTIONS_ID_TOKEN_REQUEST_TOKEN`; build paths in `debug_file` and `abs_path`; and eleven inaccuracies of the user docs (the HTTP entries of the log, failures a command handles, `[Filtered]`, the sign-in service's text, the context values). Fixed in `fdaf71990` and `fb51418eb`.
- **Third round** on `fdaf71990`: two variables that differ only in case emptied the scrub lists; `ScrubRemnants` did not end for a value of mask characters or next to a mask of its own; the lazy quoted-path rule stopped at `Chris' Projects`; a file path lost its pieces (`acme-billing` of a temporary file, `contoso` of `contoso.example/api`); `STORYTELLER_TOKEN_SCOPE` and an invalid `STORYTELLER_KEY`; the cost of long remnants. Fixed in `421fd0f51`.
- **Fuzz** of `421fd0f51` (30,500 seeded cases of values and texts, 24,000 random environments for `ErrorReportingSettings.Create`): no hang and no exception. Leaks of a value given whole: a remnant rewrite ate part of an occurrence before the value was replaced (a value with `*` in it, or one that starts with its own end); a value a shape cut where it had a bracket; the rest of an unquoted JSON member next to `"[Filtered]"`; and a surrogate pair split by the cut. Fixed in `c208e2fbb`.
- **Reviews of PR #201** (CodeRabbit, Grok): the interior of a value whose start and end two shapes covered stayed between their masks; `BeforeSend` relied on the SDK to drop an event whose scrubbing threw; a second secret variable whose name differs only in case was not hidden; compiled patterns delayed the first failure; `double.TryParse` depended on the culture; the fuzz test timed each call. Fixed afterwards. Not changed: how much the scrubbing hides from a message (a deliberate choice for privacy), `KeepNames` (the credential loads once), `ShutdownTimeout` (already 2 s by default) and caching `CommandRun.Values`.

### Not done

- Sharing the secret rules with the API's `SentryScrubber` in one library.
- The console text of `UnauthorizedAccessException`, which is the API's refusal text even for a file that cannot be written.
