# PostHog Analytics for sform

Linear: [424-52](https://linear.app/424fun/issue/424-52/connect-posthog-to-client-applicaitons).

## Overview

The single phase of [PostHog Analytics for sform](../2026-10-19%20PostHog%20Analytics%20for%20sform.md), sections 1-10. `sform` now sends one personless `cli_command_executed` event to PostHog in the EU cloud after every run, keyed by a random installation id, with an allowlist of properties built from the parser's command path, the exit code and the platform. The PostHog client is built outside the host and its flush is bounded to about two seconds; every exit code and every console output of the tool stay as they were. The admin UI half of 424-52 is reviewed in [Sentry and PostHog in the Admin UI](../../../ui.admin/specs/reviews/2026-10-19%20Sentry%20and%20PostHog%20in%20the%20Admin%20UI.md).

Commits:
- `8a696e23` build(dependencies): raise Microsoft.Extensions to 10.0.12 for the PostHog SDK
- `ac6a56df` feat(platform): send one anonymous PostHog event per sform command

Deviations from the plan:
- The run record in `Program` is the static readonly field `CurrentRun`, not `_run` (StyleCop SA1311).
- There is no `CommandTelemetryEvent` type (the heading of section 6 names it, no section defines it). The event name and the property allowlist live in `PostHogCommandTelemetry` (`EVENT_NAME`, `GetProperties`).
- The parsing logic is `CommandRun.RecordParsing(CommandLineApplication)`, so it can be tested; `Program.OnParsingComplete` calls it inside a `try`/`catch`.
- `TelemetrySettings` also reads the `ci` property, through the same injected variable function.
- The `exception` property drops the arity suffix of a generic type: `ApiException<ErrorResponse>` is `ApiException`, not ``ApiException`1`` (code review).
- The Sentry tag is set through `SentryOptions.DefaultTags` in the new `ConfigureSentry`.

## What Was Done

### 1. Package

As planned.
- `Directory.Packages.props`: the 21 entries of the `Microsoft.Extensions.*` family pinned at 10.0.5 are at 10.0.12 (`8a696e23`); every other pin is unchanged. `dotnet restore src` gives the same warnings as before, line for line, and no `NU1605`/`NU1608`.
- `PostHog` 2.15.8 between `Polly` and `QuikGraph`; `<PackageReference Include="PostHog" />` in `Cli.csproj`.

### 2. Configuration (`TelemetryOptions`)

As planned. `ConfigurationSections.TELEMETRY`, `Configuration/TelemetryOptions.cs` (`PostHogProjectToken`, `PostHogHost`), the `telemetry` section of `app.config.json` with the project token of the issue and `https://eu.i.posthog.com`, bound in `Startup.ConfigureOptions`.

### 3. When telemetry is on (`TelemetrySettings`)

As planned. `TelemetrySettings.Resolve(options, getVariable)` is off for a blank token, a host that is not an absolute `https` URL, and `DO_NOT_TRACK` or `SFORM_TELEMETRY_OPTOUT` set to `1`, `true`, `yes` or `on` (case-insensitive). The variable names are constants of the class. `ci` is true when `CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI` or `JENKINS_URL` is set.

### 4. Anonymous installation id (`InstallationIdStore`)

As planned, in `~/.42for.net/telemetry.json` (`Constants.TELEMETRY_JSON`), with the two constructors of `ProtectedTokenStore`.
- A stored Guid in any format is kept and returned in format `N`; an all-zero Guid counts as damaged and is replaced.
- An id that cannot be written turns telemetry off for the run.

### 5. First-run notice

As planned: two dark gray lines on standard error, written only when the id was created. `CommandTelemetry.Create` writes it after the PostHog client is built; if anything in `Create` fails, the notice included, telemetry is off for the run.

### 6. The event

As planned: `CommandRun` (command, information, cancelled, exception, crashed, stopwatch), `CommandPath.Of` (primary names walking `Parent`, `unknown` for a null command, a name outside `^[a-z][a-z0-9-]{0,31}$` or more than 8 levels), `CommandOutcome.Of` in the order of the spec table, and the properties of the allowlist. `os_architecture` is `RuntimeInformation.OSArchitecture.ToString()` (`X64`), `cli_version` the full informational version with the commit.

### 7. Sending (`ICommandTelemetry`)

As planned.
- `NoCommandTelemetry.Instance` when off; no `PostHogClient` is built then.
- `PostHogCommandTelemetry.SendAsync`: `Capture`, `FlushAsync().WaitAsync(1.5 s)`, `DisposeAsync().AsTask().WaitAsync(0.5 s)`; every exception is caught and logged at Information through a guarded wrapper, so a logger that fails after the host is disposed cannot reach `Main` either.
- `CommandTelemetry.Create` builds `PostHogClient` with `IsServer = false`, `MaxRetries = 0`, no logger factory and its own `IHttpClientFactory` whose `HttpClient` times out after 1.5 s. A public overload serves `Program`; an internal one takes the settings, the store, the HTTP client factory and the notice writer for tests. Neither throws.

### 8. `Program`

As planned.
- `InitializeHost` keeps `CommandTelemetry.Create(...)` in `_telemetry`, next to `_machineSignIn`.
- `ConfigureConsoleApplication` registers `OnParsingComplete`; the handler falls back to `unknown` and never throws.
- `Main` starts the stopwatch, builds the host, hooks `System.Console.CancelKeyPress`, runs today's body as `RunAsync`, sends and returns the unchanged exit code. Each catch records the exception; the `UnrecognizedCommandParsingException` catch and a `CommandParsingException` in the last catch record the command path; the "Total mayhem" branch sets `Crashed`, its #541 hot fix does not.
- `LogWithSentry` initializes Sentry through `ConfigureSentry(SentryOptions)`: the same DSN, plus the `posthog_distinct_id` tag when telemetry is on.

### 9. Tests

81 new tests in `src/Platform/Cli/test/Telemetry/`: `TelemetrySettingsTests`, `InstallationIdStoreTests`, `CommandPathTests` (the real `PlatformCommand` tree, aliases, help and version), `CommandOutcomeTests`, `PostHogCommandTelemetryTests` (the exact allowlist, no argument value, a flush that never completes or throws, a throwing `Capture` that still disposes, `api_status`, the generic `ApiException` name) and `CommandTelemetryTests` (off paths, the notice only on creation, and a real `PostHogClient` over `ScriptedHttpHandler` posting to `https://eu.i.posthog.com/batch`).

Results:
- `dotnet test src/Platform/Cli/test`: 653 passed (572 before).
- After the package raise, the 12 net10.0 unit test projects of `src/Platform`: 6,070 passed.
- `dotnet build src`: no new error or warning. It still fails on two errors that `main` already has: `Monorepo/Repo.Generator/test` and `Platform/Supervisor/Abstraction/test` target net6.0, which `Microsoft.NET.Test.Sdk` 18.3.0 rejects.
- A run with `DO_NOT_TRACK=1`: `sform --version` exits 0 without a notice; `sform foo` exits -1 as before; no `telemetry.json` is created. No event was sent while testing.

### 10. Documentation

As planned: the `## Telemetry` section of `docs/42for.net/cli/sform.md` (what is sent, with examples; what never is; where the id lives; the opt-out variables), and `telemetry.json` in the `~/.42for.net` paragraph of `docs/Platform/Storyteller/authentication.md`.

### Review

An adversarial review (correctness, privacy, documentation) with a skeptic per finding:
- Confirmed and fixed: the arity suffix of the `exception` property (above).
- Refuted: after Ctrl+C, `Main` waits for the bounded flush (at most about two seconds, as section 7 accepts); two first runs at the same moment can each write an id (one wins, and the other run's event counts as one more installation; rare and harmless).

### Not done

- The raw `args` in the Sentry and file log messages of `Main` (spec, out of scope). With a Sentry DSN configured, a secret passed on the command line can still reach Sentry; it needs its own issue.
- The PostHog project setting for client IP data is not part of the code; it should be checked in the PostHog project (the events set `$geoip_disable`, but ingestion still sees the request IP).
