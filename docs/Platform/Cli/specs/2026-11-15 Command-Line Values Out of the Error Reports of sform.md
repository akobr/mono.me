# Command-Line Values Out of the Error Reports of sform

Linear: [424-75 Stop sform from sending raw command-line arguments to Sentry](https://linear.app/424fun/issue/424-75/stop-sform-from-sending-raw-command-line-arguments-to-sentry).

Line numbers refer to the tree this plan was written against (`7944671ee`). The issue has one comment: PR #194 (424-248) handled part of it. There is no CodeRabbit plan.

## Problem

`sform` reports its failures to Sentry. A report must say how the tool was used, which command ran and which options were given. It must not carry what the customer typed: an argument, an option value, a name, a path or a secret, for example the value of `sform storyteller kv set <key> <value> --secret`.

The issue's answers set the rules:

- Sentry never gets a value. The command path and the names of the options are enough.
- The local log file may keep a value that helps debugging, for example the project, but never a secret. It does not keep the raw arguments.
- An unknown command is reported by its command path and option names only, not by the token that was mistyped or by the nearest matches.
- An exception goes to Sentry as its type, its stack and a scrubbed message.

Done when no positional argument or option value from the command line reaches Sentry, the events show the command path and the option names, and unit tests cover a secret value, an unknown command and a crash.

## Current State

All paths are relative to `src/Platform/Cli/` unless they start with `docs/`.

### What 424-248 already changed

Commit `30a77db9d` removed `args` from every message template in `Program.RunAsync` (`src/Program.cs:132-294`). The templates now use `{command}`. What is left:

- Lines 152-155, `UnrecognizedCommandParsingException`: `"Unrecognized input at {command}; NearestMatches: {nearestMatches}"` with `parsingException.Command.Name`, the leaf name and not the path.
- Lines 168-170, `CommandParsingException`: `"Invalid input at {command}"`, also with the leaf name.
- Lines 177-180, 189-193 and 318 pass the exception (`UnauthorizedAccessException`, `AuthenticationException`, `HttpRequestException`, the timeout). Line 291, the crash, passes any exception. Sentry gets each message, raw, and the messages of the inner exceptions.
- Lines 238-240 (`"The API answered {statusCode}"`) and 318 (`"The service could not be reached"`) do not name the command.
- No event names the options that were given.

### How a log entry reaches Sentry

- `src/Startup.cs:222-244`, `ConfigureLogging`: one Serilog logger at `Information`, with `.WriteTo.Sentry(minimumEventLevel: Warning)` and the daily file `logs/cli<date>.log` (`{Message:lj}{NewLine}{Exception}`, so the file gets `exception.ToString()`).
- That overload of the Sentry sink does not initialize the SDK. `Program.LogWithSentry` (`src/Program.cs:427-440`) does: `using (SentrySdk.Init(ConfigureSentry))` around one log call. Only the eight warnings and errors of `RunAsync` can become events.
- `ConfigureSentry` (`src/Program.cs:442-451`) sets the DSN and the tag `posthog_distinct_id`. There is no `BeforeSend`, no `BeforeBreadcrumb` and no PII setting.
- An invalid DSN would make `SentrySdk.Init` throw inside a catch block of `Main`.
- `DO_NOT_TRACK` and `SFORM_TELEMETRY_OPTOUT` stop PostHog only (`src/Telemetry/TelemetrySettings.cs:42`). `docs/42for.net/cli/sform.md:509` says "With telemetry off, `sform` sends nothing".

### What a Sentry event carries (Sentry 6.12.1, from its source at that tag)

- `exception.values[].value`: the raw `Exception.Message` of every exception in the chain, inner and aggregated ones included. `mechanism.data`: `HResult` and every entry of `Exception.Data`. `mechanism.help_link`.
- From the Serilog sink: `logentry.formatted` (the rendered message), every property of the log event as an extra (`command`, `SourceContext`), and every `Information` entry while the SDK is on as a breadcrumb.
- `user.id`: always set to the SDK's installation id, a GUID it writes to `%LOCALAPPDATA%\Sentry\<hash>\.installation`, or, with file writes off, the MAC address of the first network interface. `SendDefaultPii` does not change this.
- `debug_meta.images[].code_file`: the full path of each assembly in the stack trace. For an installed tool this is under `%USERPROFILE%\.dotnet\tools\.store\`, with the user's name.
- Contexts `app`, `device`, `os`, `runtime`, `Current Culture`, `Memory Info`, `ThreadPool Info`, `Dynamic Code`, `trace`; `modules` (the loaded assemblies and their versions); `release` and `environment`, from `SENTRY_RELEASE` and `SENTRY_ENVIRONMENT` unless set in code.
- The SDK never reads the process command line: `Sentry.dll` has no `GetCommandLineArgs` or `CommandLine` reference.
- `new SentryClient(options)` runs the whole pipeline of an event (exception processors, the enricher, `BeforeSend`) without the static hub and without any integration. `Dispose` flushes, bounded by `ShutdownTimeout`.

### The messages that can hold values

- `AuthenticationException` quotes the `-p` value and the machine's project (`src/Commands/CommandContext.cs:141`: "…belong to {organization}.{project}, not {organization}.{project}."), file paths, base URLs, and the server texts of WorkOS and MSAL.
- `UnauthorizedAccessException`, `IOException` and `FileNotFoundException` name the paths of `-o`, `-e`, `-i` and the user profile.
- An NSwag `ApiException` outside 400-599 goes to the crash path. Its message has the first 512 characters of the response body after its first line, and its `ToString()` has the whole body (`Storyteller/Sdk.NSwag/src/ApiSdk.g.cs:28299-28310`). Its inner Newtonsoft `JsonException` can quote a value.
- McMaster quotes the raw token in `UnrecognizedCommandParsingException.Message` ("Unrecognized option '--pasword=hunter2'") and in the conversion errors ("'notanumber' is not a valid number."). `NearestMatches` holds model names only.

### The parser (McMaster.Extensions.CommandLineUtils 5.1.0)

- The root's `OnParsingComplete` (`src/Program.cs:395-405`, `CommandRun.RecordParsing`) runs before the handlers of the subcommands. Its options still hold only the tokens from the command line (or a response file): `HasValue()` means "given" there. Later, the attribute model writes the default of a property into `Values` (`--format` of `story export`, `--timeout`).
- On a parse that fails in the processor (an unknown command or option, a missing value), `exception.Command` is the deepest command matched, and its options hold the tokens parsed so far.
- `ResponseFileHandling.ParseArgsAsLineSeparated` expands `@file` at the root: `args` then holds `@file`, and the values are only in the options' and arguments' `Values`. Values after `--` are in `RemainingArguments`.
- `McMaster.Extensions.Hosting.CommandLine` logs the raw `args` at `Debug` ("Constructing CommandLineApplication<…> with args […]"). The `Information` minimum filters it today.

### The option model

- `-p|--projectKey` (`<organization>.<project>`) and `-v|--view` come from `src/Commands/BaseContextCommand.cs:17-21` in about 79 commands; `account set` has its own `-p|--projectKey` and `-v|--viewName`.
- Values that are secrets or customer data: `kv set <key> [value]` (a secret with `--secret`), `config set -x path=value`, `story set -c key=value`, `account settings set <key> <value>`, the paths of `-o`, `-e`, `-i`, the keys, machine ids and names of most commands. No option takes a password, a client secret or a token.

### Tests and docs

- `test/ProgramTests.cs` calls `Program.RunAsync(Func<Task<int>>, string[])` without a host: logging and Sentry are never exercised. `CurrentRun` is static and shared by tests that run in parallel.
- `test/Telemetry/CommandPathTests.cs:96-134` builds `CommandLineApplication<PlatformCommand>` with mocked services (a private helper).
- `test/ProgramProcessTests.cs` runs the built CLI with `DO_NOT_TRACK=1` and the committed DSN of `app.config.json`: three of its runs reach `ReportUnreachableServiceAsync` and send a warning to the real Sentry project.
- `src/Platform/Storyteller/Api.Functions/test/RecordingTransport.cs` keeps the envelopes as they would be sent.
- `docs/42for.net/cli/sform.md:490-519` (Telemetry) covers PostHog; Sentry appears only at line 507.

## Proposed Changes

### 1. The usage of a run: `CommandUsage`

New `src/Telemetry/CommandUsage.cs`, an `internal sealed record CommandUsage(string Command, IReadOnlyList<string> Options)`:

- `Command`: `CommandPath.Of(command)`.
- `Options`: the names of the options given, from the model, never from the tokens: `--<LongName>`, or `-<ShortName>` / `-<SymbolName>` when an option has no long name. They come from the command and its parents (the command first, then each parent), in the order of the model, without repeats. An option is given when `Values` is not empty and is not just the `DefaultValue` (a value the model wrote in a previous parse). A name that does not match `^--?[A-Za-z0-9?][A-Za-z0-9-]{0,63}$` is left out.
- `ToString()`: the path and the names, `sform storyteller kv set --secret --projectKey`.
- `static CommandUsage Of(CommandLineApplication? command)` never throws; on any failure it is `Unknown` (`unknown`, no options).

### 2. The values of a run: `CommandLineValues`

New `src/Telemetry/CommandLineValues.cs`, what the reports must not show:

- From the model: every value in the `Values` of the options and arguments of the command and its parents, and their `RemainingArguments`. This covers response files.
- From `args`: every token that is not a bare option name: a plain token, the part after the first `=` or `:` of an option token (`--pasword=hunter2`), the path of `@file`. This covers the token of an unknown command or option, which the model never saw.
- Each value also gives its parts split on `=`, `,`, `;`, `.`, `/` and `\` when they are four characters or more (`acme.billing` gives `acme` and `billing`, `-x db.password=x` gives `db.password`, `db`, `password` and `x`).
- `LocalValues`: the values of the options that may go to the local log: `--projectKey`, `--view` and `--viewName`, the project and the view. An allow-list: an option is hidden unless it is listed.
- `Hidden`: every value and part, except the values of `LocalValues` and their parts.
- `All`: `Hidden` and the values of `LocalValues`.

`CommandRun` (`src/Telemetry/CommandRun.cs`) gets `Usage` and `Values`:

- `RecordParsing(selectedCommand)` sets them along with `Command`, from the root's `OnParsingComplete` as today.
- `RecordFailedParsing(command)` sets them for a `CommandParsingException` and its subclass, from `exception.Command`.
- `RunAsync` adds the values of `args`.

### 3. Scrubbing texts: `ReportScrubber`

New `src/ErrorReporting/ReportScrubber.cs`, `internal static`, regular expressions with a timeout, culture invariant:

- `ScrubSecrets(text, values)`, for the local log:
  - each of `values`, longest first, case-insensitive, also in its JSON-escaped and URL-encoded forms, becomes `***`; a value shorter than three characters only as a whole word;
  - then the secret shapes of the API's `SentryScrubber` (`Api.Functions/src/ErrorReporting/SentryScrubber.cs`): PEM blocks, JWTs and JWEs, an authorization scheme and its credential, a Storyteller API key (`2s.<…>.<…>`), the `name=value` secrets of connection strings and forms, secret JSON members (plain and escaped), and long opaque tokens judged by their entropy.
- `ScrubForSentry(text, values)`, for an exception message in Sentry:
  - the first line only;
  - `ScrubSecrets` with `values`;
  - then customer data: quoted text (`'…'`, `"…"`, `‘…’`, `“…”`) becomes `'***'`; a URL keeps its scheme, host and port, and its path and query become `/***`; an absolute file path (a drive, a UNC path, `~/…` or a Unix path of two or more segments) becomes `***`; e-mail addresses and GUIDs become `***`;
  - cut at 300 characters.

Names stay readable where the customer did not type them: types, status codes, hosts.

### 4. The local log: `RedactedException`

New `src/ErrorReporting/RedactedException.cs`, an `Exception` that is never thrown and only logged:

- `ToString()` writes the chain like `Exception.ToString()`: `<type>: <message>`, ` ---> ` for the inner exception, `--- End of inner exception stack trace ---`, and each exception's own `StackTrace`. The messages go through `ScrubSecrets`; the stack traces stay as they are (they hold method names and source paths of the build, no values). An `AggregateException` writes each inner exception.
- `Message` is the scrubbed message; `StackTrace` is the original's.

The file sink renders `{Exception}` with `ToString()`, so the file keeps its format.

### 5. Reporting: `ErrorReporter`

New `src/ErrorReporting/ErrorReporter.cs`, replacing `LogWithSentry` and `ConfigureSentry`:

```csharp
internal sealed class ErrorReporter
{
    public static ErrorReporter None { get; }

    public static ErrorReporter Create(ILogger? logger, ErrorReportingSettings settings);

    // what: a fixed text, with only a status code or a reason enum formatted in; never a value.
    public Task ReportAsync(LogLevel level, string what, CommandRun run, string[] args, Exception? exception = null);
}

internal sealed record ErrorReportingSettings(
    string? Dsn,              // logging.sentryDsn
    bool IsOptedOut,          // DO_NOT_TRACK or SFORM_TELEMETRY_OPTOUT
    string? InstallationId,   // the PostHog installation id, when telemetry is on
    string Environment,       // the host environment, lower case
    string Release,           // 42.Platform.Cli@<informational version>, as before
    IReadOnlyList<string> ContextValues); // the default organization, project and view, STORYTELLER_PROJECT and _VIEW
```

`ReportAsync` never throws. It writes to the local log, then sends to Sentry when it is on:

- **The local log** (the `ILogger` of `Program`): `"{what} at {command}{values}"` with the usage, and with `; --projectKey acme.billing --view prod` when `LocalValues` has any, and a `RedactedException` scrubbed of `Hidden`.
- **Sentry**, on only with a DSN and without an opt-out, and so off in `ProgramProcessTests` (`DO_NOT_TRACK=1`):
  - Its own `SentryClient` per report, no `SentrySdk.Init`: no static hub, no integration, no window in which another log entry becomes an event. The client is disposed after the capture, which flushes. A DSN the client refuses is caught; the report is only written to the log.
  - The event: `new SentryEvent(exception)` at the level; `Message = { Message = "{what} at {command}", Formatted = "{what} at <usage>" }`; the tag `command` (the path); the context `command` with `name` and `options`; the tag `posthog_distinct_id` when there is an installation id.
  - The options: `SendDefaultPii = false`, `IsEnvironmentUser = false`, `AttachStacktrace = false`, `MaxBreadcrumbs = 0` and a `BeforeBreadcrumb` that drops every breadcrumb, `AutoSessionTracking = false`, `TracesSampleRate = null`, `EnableLogs = false`, `CaptureFailedRequests = false`, `DisableFileWrite = true` (the SDK writes nothing outside `SFORM_HOME`), `Release` and `Environment` set in code (`SENTRY_*` variables of the machine do not change them), `SetBeforeSendTransaction` dropping everything.
  - `BeforeSend` (`ReportScrubber.ScrubEvent`):
    - every `exception.value` through `ScrubForSentry` with `All` and the context values; `mechanism.data` keeps only `HResult`; `mechanism.description` scrubbed; `mechanism.help_link` removed;
    - `debug_meta.images[].code_file` keeps the file name only;
    - only the tags `command` and `posthog_distinct_id` stay; every extra becomes `[Filtered]` (the SDK cannot remove one); the contexts come from an allow-list (the API's list and `command`);
    - `user` is replaced: `id` is the installation id of PostHog when there is one, otherwise empty, never the SDK's id or a MAC address; `request` is cleared and `server_name` removed;
    - the message is ours, built from the model and fixed texts, and is not scrubbed by values (a value equal to a command name would cut the path).

`Program.InitializeHost` builds the reporter after the telemetry, from `LoggingOptions`, `TelemetrySettings.IsOptedOut`, `_telemetry.InstallationId`, the environment, `AccessDefaultOptions` and `MachineSignInSettings`.

### 6. Program

- `RunAsync(run, args)` becomes `RunAsync(run, args, CommandRun currentRun, ErrorReporter reporter)`; the two-argument form passes `CurrentRun` and the reporter of the host (`ErrorReporter.None` without one), so `ProgramTests` keep working, and a test can pass its own run and reporter.
- `RunAsync` records the values of `args` in the run first.
- The reports (texts unchanged where they existed; every one names the command now):

| Catch | Level | `what` | Exception |
|---|---|---|---|
| `UnrecognizedCommandParsingException` | Warning | `Unrecognized input` | none |
| `CommandParsingException` | Warning | `Invalid input` | none |
| `UnauthorizedAccessException` | Warning | `Unauthorized access` | `InnerException ?? exception` |
| `AuthenticationException` | Warning | `Sign-in failed (<reason>)` | yes |
| `ApiException` 401, 403 | Warning | `The API refused the request with <status>` | none |
| `ApiException` 400-599 | Warning | `The API answered <status>` | none |
| `HttpRequestException`, timeout | Warning | `The service could not be reached` | yes |
| any other | Error | `Unhandled exception` | yes |

- The parsing catches call `RecordFailedParsing(parsingException.Command)`: the usage is the path of the deepest command matched and its options, never `NearestMatches` or the message.
- `LogWithSentry`, `ConfigureSentry` and `using Sentry` go.

### 7. Startup and packages

- `ConfigureLogging` loses `.WriteTo.Sentry(...)`: no log entry reaches Sentry; only `ErrorReporter` sends. The file sink stays at `Information` (the automation test GEN-CLI-06 expects a file).
- A filter keeps `McMaster.Extensions.Hosting.CommandLine` below `Warning` out of every provider, so its log of the raw `args` never reaches the file, whatever the levels.
- `Cli.csproj` drops `Sentry.Serilog` and keeps `Sentry`. `Directory.Packages.props` stays (mrepo and DbCreator use `Sentry.Serilog`).

### 8. Telemetry opt-out

`TelemetrySettings` gets `IsOptedOut` (the existing check of `DO_NOT_TRACK` and `SFORM_TELEMETRY_OPTOUT`). With it set, `sform` sends no error report either: the variables are the general "do not track" signal, and the docs already promise that `sform` then sends nothing.

### 9. Tests

In `test/` (xUnit v3, Shouldly), every Sentry test reading the envelope as it would be sent (`test/ErrorReporting/RecordingTransport.cs`, as in the API's tests); no static hub, so the tests can run in parallel:

- `test/Telemetry/SformApplication.cs`: the helper of `CommandPathTests` (the application with mocked services), shared.
- `CommandUsageTests`: `story kv set key value --secret -p acme.billing` gives `sform storyteller kv set` and `--secret`, `--projectKey`; an `=` form; an alias; `--help`; a default of the model (`story export --format`) is not given; an unknown command (`story kv sett`) gives `sform storyteller kv`; an unknown option keeps the options before it.
- `CommandLineValuesTests`: the values from the model and from `args` (`--pasword=hunter2`, `@file`, after `--`), the parts, `LocalValues` only for the listed options.
- `ReportScrubberTests`: each rule of both methods, the first line, the cut, the short values.
- `ErrorReporterTests`: off without a DSN, with an opt-out and with a DSN the client refuses (nothing thrown, the log still written); the options; the scrubbing of the event (`user`, `server_name`, debug images, tags, extras, contexts, `mechanism`).
- `ProgramReportTests`, `Program.RunAsync` with its own `CommandRun` and a reporter over the recording transport and a recording logger, the three cases of the issue:
  - **a secret value**: `story kv set db.password s3cr3t-value --secret -p acme.billing`, with a command that fails with an exception quoting the value: neither Sentry nor the log has `s3cr3t-value`; Sentry has `sform storyteller kv set --secret --projectKey` and not `acme.billing`; the log has `acme.billing`;
  - **an unknown command**: `story kv sett --pasword=hunter2`: Sentry and the log have `Unrecognized input at sform storyteller kv`, and neither `sett` nor `hunter2`;
  - **a crash**: a `FileNotFoundException` for the path of `-o` inside an `InvalidOperationException`: the event keeps both types and the stack frames, and the message keeps its shape without the path; the log keeps the stack and not the path.
- `TelemetrySettingsTests`: `IsOptedOut`.

### 10. Documentation

- `docs/42for.net/cli/sform.md`:
  - Telemetry gets "Error reports": what a report carries (the command and the option names, the type of the failure, its stack, its message without values, paths, addresses or quoted text, the installation id), what it never carries, and that the telemetry opt-out stops it too. The line about Sentry at 507 moves there.
  - "Where sform keeps its files": what the log in `logs/` keeps (the command, the options, the project and the view; never the other values or a secret).
- `docs/Platform/Storyteller/error-reporting.md`: one line that `sform` reports to its own project with its own rules, and a link to the user doc.

### 11. Out of scope

- Values a command reads from a prompt, the standard input or a file: the reporter does not know them. In Sentry the first line, the quoted text, paths, URLs and secret shapes still go; in the local log only the secret shapes do.
- The file log's HTTP entries of `HttpClientFactory`, which keep the path of each request (organization, project, view and key names; the query is redacted by .NET). They are local and hold no secret.
- Sharing the secret rules with the API in one library.
- The console text of `UnauthorizedAccessException`, which is the API's refusal text even for a file that cannot be written.

### 12. Phases and commits

One phase:

1. `docs(platform)`: this plan.
2. `fix(platform)`: the usage and the values of a run (sections 1-2) with their tests.
3. `fix(platform)`: the scrubber, the reporter, `Program`, `Startup`, the opt-out (sections 3-8) with their tests.
4. `docs(platform)`: the user docs (section 10).
5. `docs(platform)`: the review.
