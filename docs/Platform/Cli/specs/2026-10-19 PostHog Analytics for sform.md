# PostHog Analytics for sform

Linear: [424-52](https://linear.app/424fun/issue/424-52/connect-posthog-to-client-applicaitons).

Line numbers refer to the tree this plan was written against (`1614279b`). No application code is changed by this document.

424-52 covers both client applications of the platform. This spec is the `sform` half; the admin UI half is [Sentry and PostHog in the Admin UI](../../ui.admin/specs/2026-10-19%20Sentry%20and%20PostHog%20in%20the%20Admin%20UI.md). Both use the same PostHog project in the EU cloud. The plan takes the CodeRabbit coding plan attached to the issue as input; where this spec decides differently, the section says so.

## Problem

`sform` reports failures to Sentry when a DSN is configured, but nothing tells how the tool is used: which commands run, how often they fail, how long they take, which versions are out there. The issue asks to connect `sform` to PostHog next to the existing Sentry, using the PostHog .NET library, with the project token `phc_uGDiou…` and the host `https://eu.i.posthog.com`.

The issue's .NET example targets ASP.NET Core (`builder.AddPostHog()`, `appsettings.json`, a personal API key). `sform` is a short-lived generic-host console app, so the example does not fit as is:

- the process exits right after the command, before the SDK's background flush (every 5 s) runs;
- the host, and every singleton it created, is disposed before `Main` gets the exit code or the exception;
- a personal API key is only needed for local feature flag evaluation, which `sform` does not use.

The data must stay anonymous: no arguments, option values, organization, project, view or key names, tokens, e-mails or file paths.

## Current State

### Entry point and exit codes

- `Program.Main` (`src/Platform/Cli/src/Program.cs`, lines 33-130) builds the host in `InitializeHost` (lines 132-151) and runs `return await Host.RunCommandLineApplicationAsync();` (line 42). The success exit code exists only on that line.
- The catches map exceptions to exit codes: `UnrecognizedCommandParsingException` → `-1` (line 44), `UnauthorizedAccessException` → `44` (line 55), `AuthenticationException` → `2`, `3` or `44` (line 67), `ApiException` 401/403 → `44` (line 84), anything else → `1`, or `-1` for the McMaster #541 hot fix (line 103).
- `RunCommandLineApplicationAsync` (McMaster.Extensions.Hosting.CommandLine 5.0.1) runs `host.RunAsync()`, which disposes the host, and only then returns `CommandLineState.ExitCode` or rethrows the stored exception. Container-created singletons and `IHttpClientFactory` clients are disposed by then (`ObjectDisposedException` from `DefaultMeterFactory`); an instance passed to `AddSingleton(instance)` is not. The comment on line 28 already says so for `_machineSignIn` (line 139).
- On Ctrl+C, `CommandLineLifetime` cancels the key press and stops the host; `RunCommandLineApplicationAsync` returns `0` while the command is still unwinding.
- A CLU validation failure (a `[Required]` option) is not an exception: `ExecuteAsync` returns `1`, the same value as `ExitCodes.ERROR_CRASH` (`ExitCodes.cs`, line 11).

### Selected command

- `Program.ConfigureConsoleApplication` (lines 165-172) configures the root `CommandLineApplication<PlatformCommand>`; it runs inside `HostBuilder.Build()`, after the conventions built the subcommand tree and before parsing.
- `CommandLineApplication.OnParsingComplete(Action<ParseResult>)` handlers of the root run for every selected subcommand, root first, with `ParseResult.SelectedCommand` the leaf. They run for `--help` (with `OptionHelp.HasValue()`) and `--version` (`OptionVersion.HasValue()`), but not for unrecognized input or a missing option value, which throw `CommandParsingException` (`UnrecognizedCommandParsingException`) with `.Command` the deepest matched command. A handler that throws aborts the run.
- `sform assist` parses twice (`BaseParentCommand`, `src/Libraries/CLI/Toolkit/src/BaseParentCommand.cs`, lines 19-36), so the handler fires twice: `sform assist`, then `sform assist key`.
- Every command and alias name is a constant from `Commands/CommandNames.cs`. `Name` is the primary name, so `sform login` reports `sform account` and `story` reports `storyteller`.
- The toolkit's `SetLongVersionGetter` (line 171) reads the version of `42.CLI.Toolkit`, not of `sform` (`src/Libraries/CLI/Toolkit/src/CommandLineApplicationExtensions.cs`, line 25).

### Sentry and logging

- `Startup.ConfigureLogging` (`Startup.cs`, lines 153-175) adds Serilog with `.WriteTo.Sentry(minimumEventLevel: Warning)` and a daily file. The sink does not initialize Sentry.
- `Program.LogWithSentry` (lines 174-187) wraps a single log call in `using (SentrySdk.Init(_loggingOptions.SentryDsn))` when `logging.sentryDsn` is set. The repository's `app.config.json` keeps it blank since `c62a84ad`; the released `app.config.json` is edited by hand before packing.
- Every warning and error logged in `Main` includes the raw `args` (lines 48-52, 58-61, 69-73, 86-89 and 127).

### Configuration and per-user files

- `Startup.ConfigureApplication` (lines 69-80) adds `app.config.json` (required), `access.default.json` and `editor.config.json`. The host has no environment-variable source, so variables are read directly, as `MachineSignInSettings.FromEnvironment(System.Environment.GetEnvironmentVariable)` does (line 89).
- `ConfigureOptions` (lines 182-189) binds one options class per section of `ConfigurationSections` (`logging`, `access`, `authentication`, `general`, `editor`).
- `app.config.json`, `access.default.json`, `editor.config.json`, `auth.discovery.json` and `logs/` sit in the tool's per-version folder, which `dotnet tool update` replaces.
- Per-user state that must survive updates lives in `Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net")` (`%LOCALAPPDATA%\.42for.net` on Windows, `$HOME/.42for.net` elsewhere): `ProtectedTokenStore.cs` line 28, `EntraIdAuthenticationService.cs` line 129. `ProtectedTokenStore` has a public `ctor(IFileSystem)` chained to an internal `ctor(IFileSystem, string directory)` for tests.
- `sform` writes command output to standard output (for example `sform storyteller kv export` without `-o`), so a notice must go to standard error.

### PostHog .NET SDK

Verified against `PostHog` 2.15.8 (NuGet, 2026-10-09):

- Targets `net8.0`, `netstandard2.0` and `netstandard2.1`. Its `net8.0` dependencies are `Microsoft.Extensions.Caching.Memory`, `Http`, `Logging` and `System.Text.Json` **10.0.12**. With the repository pins at 10.0.5, restore fails with `NU1605` (downgrades of `Microsoft.Extensions.Configuration` and `Configuration.Binder`). Raising only those two breaks `Access.Certificates.Azure.KeyVault` and `KeyValues.Azure.KeyVault` (downgrade of `Configuration.Abstractions`). Raising the whole 10.0.5 family restores the whole solution.
- `PostHogClient(IOptions<PostHogOptions> options, IFeatureFlagCache? = null, IHttpClientFactory? = null, ITaskScheduler? = null, TimeProvider? = null, ILoggerFactory? = null)`; `IPostHogClient : IDisposable, IAsyncDisposable`.
- `PostHogOptions`: `ProjectToken` (`ProjectApiKey` is obsolete), `HostUrl` (`Uri`; a relative or non-http(s) value falls back silently to `https://us.i.posthog.com`), `IsServer` (default `true`, adds `$is_server`), `FlushAt` 100, `FlushInterval` 5 s, `MaxRetries` 3 with 1/2/4 s backoff, `EnableCompression` (gzip).
- `Capture(distinctId, eventName, properties, groups, flags, timestamp)` only queues and returns `bool`. `Task FlushAsync()` takes no token and rethrows `OperationCanceledException` when `HttpClient` times out. `DisposeAsync()` flushes again without a time limit; `Dispose()` blocks on it. The default internal `HttpClient` has the 100 s timeout. A refused connection with the default retries took 15 s.
- Batch events carry `$lib`, `$lib_version` and `$geoip_disable: true` by default; `$process_person_profile: false` makes an event personless.

## Proposed Changes

### 1. Package

- `Directory.Packages.props`: add `<PackageVersion Include="PostHog" Version="2.15.8" />` between `Polly` and `QuikGraph`.
- In the same file, raise the 21 entries pinned at 10.0.5 of the `Microsoft.Extensions.*` family to 10.0.12: `Microsoft.Bcl.AsyncInterfaces`; `Microsoft.Extensions.Caching.Abstractions`, `Caching.Memory`, `Configuration`, `Configuration.Abstractions`, `Configuration.Binder`, `Configuration.Json`, `DependencyInjection`, `DependencyInjection.Abstractions`, `FileProviders.Abstractions`, `FileProviders.Physical`, `FileSystemGlobbing`, `Hosting`, `Hosting.Abstractions`, `Http`, `Logging`, `Logging.Abstractions`, `Logging.Console`, `Options`, `Options.ConfigurationExtensions`; `System.Text.Json`. `Microsoft.Extensions.Http.Resilience` and `ServiceDiscovery` follow another version line and stay; the ASP.NET Core, `System.CodeDom`, `System.Collections.Immutable` and `System.Diagnostics.DiagnosticSource` pins stay.
- This raise is its own commit (`build`), checked with `dotnet build src` and the unit tests before the feature commit.
- `src/Platform/Cli/src/Cli.csproj`: `<PackageReference Include="PostHog" />`. The core package only; not `PostHog.AspNetCore` and not `AddPostHog()`.

### 2. Configuration (`TelemetryOptions`)

- `ConfigurationSections.TELEMETRY = "telemetry"`.
- `Configuration/TelemetryOptions.cs`: `PostHogProjectToken` (`string?`) and `PostHogHost` (`string?`), with a one-line comment each, like `AuthenticationOptions`.
- `app.config.json`:

  ``` json
  "telemetry": {
    "postHogProjectToken": "phc_uGDiouxjcaRUArCSCi3knvAbub5eoCorCg8c9Wam7yke",
    "postHogHost": "https://eu.i.posthog.com"
  }
  ```

  A PostHog project token is a publishable, write-only key meant to ship inside client applications, so it is committed, unlike the Sentry DSN, which stays blank. A blank token turns telemetry off.
- `Startup.ConfigureOptions` binds the section.

### 3. When telemetry is on (`TelemetrySettings`)

`Telemetry/TelemetrySettings.cs`, built once by `TelemetrySettings.Resolve(TelemetryOptions options, Func<string, string?> getVariable)`:

- **Off** when the token is blank; when the host is not an absolute `https` URL (no silent fallback to the US cloud); when `DO_NOT_TRACK` or `SFORM_TELEMETRY_OPTOUT` is `1`, `true`, `yes` or `on` (case-insensitive).
- **On** otherwise, with the token and the host `Uri`.
- The variables are read through the injected function, as `MachineSignInSettings.FromEnvironment` does.

The CodeRabbit plan reads only `DO_NOT_TRACK`; `SFORM_TELEMETRY_OPTOUT` is added so an opt-out can target `sform` alone. Development builds are not switched off: their events carry `environment: Development`, so they can be filtered out, and the owner can see the integration work without a release build.

### 4. Anonymous installation id (`InstallationIdStore`)

`Telemetry/InstallationIdStore.cs`:

- File `telemetry.json` in the per-user folder `Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net")`, the folder of `ProtectedTokenStore`, which survives tool updates: `{ "installationId": "<Guid, format N>" }`. `Constants.TELEMETRY_JSON = "telemetry.json"`.
- Public `ctor(IFileSystem)` chained to an internal `ctor(IFileSystem, string directory)`, like `ProtectedTokenStore`.
- `bool TryGetOrCreate(out string id, out bool created)`: reads the file; a missing, empty, damaged or non-Guid value is replaced by a new `Guid.NewGuid()` written after `Directory.CreateDirectory`. `IOException`, `UnauthorizedAccessException`, `NotSupportedException` and `JsonException` never escape.
- When the id cannot be persisted, `TryGetOrCreate` returns `false` and telemetry is off for that run. The CodeRabbit plan falls back to a per-process id; this spec does not, because every run would then count as a new installation and the first-run notice (section 5) could not be shown once.
- The id is random. It is not derived from the machine, the user or a token, and it is never sent anywhere but as the PostHog `distinct_id` and the Sentry tag of section 8.

### 5. First-run notice

When `TryGetOrCreate` reports `created`, `sform` writes to standard error, before the command runs:

```
sform sends anonymous usage data (the command, its outcome and duration) to PostHog.
Set DO_NOT_TRACK=1 to turn it off: https://42for.net/cli/sform#telemetry
```

The notice is written with `System.Console.Error` in dark gray. It is not written when telemetry is off.

### 6. The event (`CommandRun`, `CommandTelemetryEvent`)

One event per process, named `cli_command_executed`, captured at the end of `Main`.

`Telemetry/CommandRun.cs` collects the run in `Program` statics (strings, booleans and the exception only; never the `CommandLineApplication`, which is disposed with the host):

- `Command`, set by the root `OnParsingComplete` handler (last value wins, for `assist`) and, for parse failures, from `CommandParsingException.Command`.
- `Information` (`help`, `version` or none) from `SelectedCommand.OptionHelp` / `OptionVersion`.
- `Cancelled`, set by a `System.Console.CancelKeyPress` handler registered in `Main` (`Program.cs` aliases `Console` to `Colorful.Console`, so the type is written in full).
- `Exception`, the exception `Main` caught, if any, and `Crashed`, set by the last catch ("Total mayhem") but not by its #541 hot fix.
- A `Stopwatch` started in `Main`.

`Telemetry/CommandPath.cs`: `Of(CommandLineApplication?)` walks `Parent` and joins `Name`s (`sform storyteller kv get`). It returns `unknown` for a null command, a name that does not match `^[a-z][a-z0-9-]{0,31}$`, or more than 8 levels. Names never come from `args`.

`Telemetry/CommandOutcome.cs`: the `outcome` property, in this order:

| Outcome | When |
|---|---|
| `cancelled` | Ctrl+C was pressed |
| `help`, `version` | the information was shown |
| `input_error` | a `CommandParsingException`, or an exit code below 0 |
| `crash` | an exception reached the last catch of `Main` (exit code 1) |
| `success` | exit code 0 |
| `warning` | exit code 40 or above |
| `error` | any other exit code (1-39) |

Properties (an allowlist; nothing else is added):

| Property | Value |
|---|---|
| `command` | `CommandPath` |
| `outcome` | the table above |
| `exit_code` | the exit code `Main` returns |
| `duration_ms` | the stopwatch |
| `exception` | `GetType().Name` of the caught exception, when any; never its message |
| `authentication_failure` | `AuthenticationFailureReason` of an `AuthenticationException`, when any |
| `api_status` | `ApiException.StatusCode`, when any |
| `cli_version` | `AssemblyInformationalVersionAttribute` of `typeof(Program).Assembly` (not the toolkit getter) |
| `os` | `windows`, `linux`, `macos` or `other` |
| `os_architecture` | `RuntimeInformation.OSArchitecture` |
| `runtime` | `Environment.Version` |
| `environment` | `IHostEnvironment.EnvironmentName` |
| `ci` | `true` when `CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI` or `JENKINS_URL` is set |
| `machine_sign_in` | `MachineSignInSettings.IsConfigured` |
| `$process_person_profile` | `false` (personless) |
| `$geoip_disable` | `true` (the SDK default, set explicitly) |

Never sent: arguments, option values, exception messages, organization, project, view or key names, URLs, paths, the user's name or e-mail, the machine name, tokens.

### 7. Sending (`ICommandTelemetry`)

`Telemetry/ICommandTelemetry.cs`:

``` csharp
internal interface ICommandTelemetry
{
    string? InstallationId { get; }

    Task SendAsync(CommandRun run, int exitCode);
}
```

- `NoCommandTelemetry`: does nothing; `InstallationId` is null. Used when section 3 says off or section 4 cannot persist the id. No `PostHogClient` is built then, so the SDK's "disabled" warnings never appear.
- `PostHogCommandTelemetry(IPostHogClient client, string installationId, …)`: builds the properties of section 6, calls `client.Capture(installationId, "cli_command_executed", properties, groups: null, flags: null)`, then `await client.FlushAsync().WaitAsync(FlushTimeout)` (1.5 s) and `await client.DisposeAsync().AsTask().WaitAsync(DisposeTimeout)` (0.5 s). Every exception, including `TimeoutException` and `TaskCanceledException`, is caught and logged at Information to the file log; none reaches `Main`.
- `CommandTelemetry.Create(...)` builds the client outside the container: `new PostHogClient(new PostHogOptions { ProjectToken, HostUrl, IsServer = false, MaxRetries = 0 }, httpClientFactory: <own factory, HttpClient.Timeout 1.5 s>, loggerFactory: null)`. Its own `HttpClient` and no logger factory, because the host's are disposed when `Main` sends; the short `HttpClient` timeout is what bounds the abandoned flush and the dispose, since both wait on the same lock.
- The wait limits only `Main`; a request still running when `Main` returns ends with the process. The added latency is one HTTPS request on a working network, at most about 2 s otherwise. A failed event is dropped.

### 8. `Program`

- `InitializeHost` resolves `IOptions<TelemetryOptions>`, `IFileSystem`, `IHostEnvironment`, `MachineSignInSettings` and `ILogger<Program>` from `Host.Services` and keeps `CommandTelemetry.Create(...)` in a static `_telemetry`, like `_machineSignIn`. A host that fails to build records nothing.
- `ConfigureConsoleApplication` adds `application.OnParsingComplete(OnParsingComplete)`. The handler sets `_run.Command` and `_run.Information` inside a `try`/`catch` that swallows everything, because a throwing handler aborts the command.
- `Main` keeps today's body as `RunAsync(args)` and becomes:

  ``` csharp
  if (!InitializeHost(args)) return ExitCodes.ERROR_CRASH;
  System.Console.CancelKeyPress += OnCancelKeyPress;
  var exitCode = await RunAsync(args);
  await _telemetry.SendAsync(_run, exitCode);
  return exitCode;
  ```

  Each catch of `RunAsync` records the exception in `_run` (and, for `CommandParsingException`, the command path) and returns the same exit code as today. No exit code and no console output changes.
- `LogWithSentry` initializes Sentry with options instead of the DSN string and, when `_telemetry.InstallationId` is set, tags the event `posthog_distinct_id = <installation id>`, so a Sentry event can be found in PostHog. With telemetry off there is no tag.

### 9. Tests

In `src/Platform/Cli/test/Telemetry/` (namespace `_42.Platform.Cli.UnitTests.Telemetry`, xUnit, Shouldly, Moq, `MockFileSystem` with `MockUnixSupport.Path`):

- **`TelemetrySettingsTests`**: blank token, `http://` or relative host, each opt-out variable and value → off; a valid token and host → on; `0`/`false` do not opt out.
- **`InstallationIdStoreTests`**: the first call writes and reports `created`, the second reads the same id; a damaged or non-Guid file is replaced; an unwritable directory returns `false` without throwing.
- **`CommandPathTests`**: `Parse` of the real `PlatformCommand` tree (the `StorytellerReadCommandsTests` pattern) gives `sform storyteller kv get` for `story kv get some-key`, `sform account` for `login`; every command of the tree gives a path that matches the name rule; null gives `unknown`.
- **`CommandOutcomeTests`**: each row of the table in section 6.
- **`PostHogCommandTelemetryTests`** with a mocked `IPostHogClient`: the captured properties are exactly the allowlist; no argument or option value appears; a client whose `FlushAsync` never completes, or throws, ends `SendAsync` within the time limits without an exception.
- **`CommandTelemetryTests`**: `Create` returns `NoCommandTelemetry` when off or when the id cannot be stored; with a real `PostHogClient` over `StubHttpClientFactory(ScriptedHttpHandler)` and `EnableCompression = false`, the request goes to `https://eu.i.posthog.com/batch` with the event, the installation id and `$process_person_profile: false`.

Must pass: `dotnet build src`, `dotnet test src/Platform/Cli/test`.

### 10. Documentation

- `docs/42for.net/cli/sform.md`: a `## Telemetry` section after "Sign in from a pipeline": what is sent (the table of section 6 in plain words), what never is, where the id lives, and the opt-out variables with a PowerShell example.
- `docs/Platform/Storyteller/authentication.md`, the paragraph about `~/.42for.net`: mention `telemetry.json`.

Do not edit this spec to match later deviations; record those in the review.

### Phases

One phase, in two commits: the package raise (section 1), then sections 2-10.

### Out of scope

- The raw `args` in the Sentry and file log messages of `Main` (section "Sentry and logging"). It is an existing privacy risk; it deserves its own issue, because removing the arguments also removes context from Sentry reports.
- The wrong version of `sform --version` (the toolkit getter).
- Sending the first-run notice through `IExtendedConsole`, PostHog feature flags, person profiles or linking CLI and UI users.
- A release pipeline that writes `logging.sentryDsn`.
