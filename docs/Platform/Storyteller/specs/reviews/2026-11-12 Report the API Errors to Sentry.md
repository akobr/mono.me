# Report the API Errors to Sentry

Linear: [424-248](https://linear.app/424fun/issue/424-248/report-the-apis-errors-to-sentry).

## Overview

This review covers the whole of the spec [Report the API Errors to Sentry](../2026-11-12%20Report%20the%20API%20Errors%20to%20Sentry.md), both phases: the Sentry SDK in `Api.Functions` (sections 1-9 and 11) and the deployment and documentation (sections 10 and 12). It was implemented as planned, with the deviations named below, most of them from a review of the implementation (section "Review"). Commits: `83c67ead2` (the SDK in the API), `570248dd9` (deployment and docs), `a4d7c0257` (fixes of the review). The Sentry project `2s-api` doesn't exist yet: until its DSN is committed, production reports nothing to Sentry (section "Notes").

## What Was Done

### 1. Package

As planned: `Sentry.Extensions.Logging` 6.12.1 in `Directory.Packages.props`, between `Sentry` and `Sentry.Serilog`, and referenced by `Api.Functions.csproj`.

### 2. Settings

`ErrorReporting/ErrorReportingOptions.cs`: `Dsn`, `RepeatInterval` (1 hour), `MaxEventsPerHour` (10), `MaxEventsPerDay` (40) of the section `Sentry`.

**Deviation:** the volume settings are not bound with `Get<ErrorReportingOptions>()` but read one by one (`ErrorReportingEntryPoint.ReadOptions`): the binder throws on a value such as `1h`, which would stop the API even without a DSN. A malformed or negative value keeps its default and is named (never its value) in a start Warning; without a DSN they are not read at all.

### 3. Registration

`ErrorReporting/ErrorReportingEntryPoint.cs`: `IsErrorReportingEnabled(IConfiguration)` and `AddErrorReporting(IServiceCollection, IConfiguration, IHostEnvironment)`, with an internal overload that takes the tests' `configure` and the throttle's `TimeProvider`.

- Without a DSN nothing is registered. A malformed DSN registers only `ErrorReportingWarnings`, a hosted service that logs "Sentry:Dsn is not a valid DSN; errors are not reported to Sentry." at start (it replaced the planned `InvalidDsnWarning`, so the volume settings share it).
- **Deviation:** `IsValidDsn` applies the rule of Sentry's `Dsn.Parse`: the project id is what follows the last `/` of the absolute URI. The first version trimmed a trailing `/`, so `…/1/` passed our check and stopped the host in `SentrySdk.InitHub`.
- With a DSN: `AddLogging(logging => logging.AddSentry())` and a `PostConfigure<SentryLoggingOptions>` with the DSN, the lower-case environment, the release `42.Platform.Storyteller.Api.Functions@<informational version>` (`GetRelease`), the levels, `MaxBreadcrumbs = 30`, the privacy options, `BeforeSend`, `BeforeBreadcrumb`, `BeforeSendTransaction` returning `null`, no tracing, no sessions, no Sentry logs.
- `Program.cs`: `ConfigureFunctionsWebApplication((context, worker) => …)` registers `SentryScopeMiddleware` first when `IsErrorReportingEnabled`; `services.AddErrorReporting(...)` after the Application Insights registration, which is unchanged.

### 4. What becomes an event

As planned: Error and Critical become events, Information and Warning breadcrumbs; `IsFilteredOut` (the `AddLogEntryFilter`) drops entries below Warning of categories outside `_42.` and an `OperationCanceledException` that the worker logs.

**Deviation:** breadcrumbs are kept only while an invocation runs (`SentryScopeMiddleware.IsInInvocation`, an `AsyncLocal` checked by `BeforeBreadcrumb`). The plan cleared the breadcrumbs of the invocation's scope only, but the logs outside invocations still gathered in the root scope, which the worker's own scope is cloned from, so the worker's error entry after the middlewares carried other organizations' background breadcrumbs. An error outside an invocation is now an event without breadcrumbs.

### 5. Transaction names and the scope

- `ErrorReporting/FunctionTransactionNames.cs`: a frozen map of every `[Function]` of the assembly, read once by reflection. HTTP: `METHOD /api/<template>`, the template written back from `RoutePatternFactory.Parse` without constraints, defaults and optional markers; the declared methods joined by `|` when the request isn't known. Other triggers: `queue ProcessViewTransfer`, `timer KeyValueProtectionTimer` (the attribute name without `TriggerAttribute`). A function of another assembly keeps its name.
- `ErrorReporting/SentryScopeMiddleware.cs`: per invocation `PushScope`, `ContinueTrace(null, null)` (a trace id of its own), `ClearBreadcrumbs`, the transaction, the tag `function` and the context `function` (name, invocation id), and the in-invocation flag around `next`.
- `SentryScrubber.CompleteFunction`: an event without a transaction that carries the worker's `functionName` tag gets the transaction, the tag and the context in `BeforeSend`.

### 6. Privacy

`ErrorReporting/SentryScrubber.cs` as planned: the request, the user and the server name are cleared; only the tag `function` stays; extra values become `[Filtered]`; contexts are kept from the allow-list (`function`, `throttle`, `trace`, the SDK's own); the mechanism keeps only `HResult` in its data and loses its help link; the message (template and formatted, without parameters), exception values, mechanism descriptions and breadcrumbs (message and data) are scrubbed by nine rules. The long-token rule uses the entropy of a run of `[A-Za-z0-9+/=_-]` (at least 32 characters, 4.2 bits per character) and judges a run that starts with `/` segment by segment. A breadcrumb of type `http` is dropped. The rules have a 250 ms match timeout.

The authorization rule requires a credential of at least eight characters with a digit or a symbol, so prose such as "The Bearer token was rejected" stays readable. The `Breadcrumb` constructor with a timestamp is internal in 6.12.1, so the scrubbed breadcrumb takes the moment it is added, which is the same moment.

A real event of the leak test, dumped once while writing the tests, had only the route template as `transaction`, `request: {}`, `user: {}`, the tag `function`, the allowed contexts, `extra.state = "[Filtered]"` and scrubbed exception values.

### 7. Customer names

As decided: names stay in messages, never as tags; values never. Documented in `docs/Platform/Storyteller/error-reporting.md`, section "Privacy".

### 8. Volume and cost

`ErrorReporting/SentryEventThrottle.cs` as planned (signature of transaction, logger, template, exception types and `TargetSite`; repeat interval; hourly cap that a new signature passes; daily cap; the `throttle` context with `suppressed`; one lock; at most 1,000 signatures, pruned by expiry, then the oldest half). It is the first step of `BeforeSend` after `CompleteFunction`, so a worker entry is throttled by its route too.

### 9. Warnings, not errors

As planned: the seven parse failures log at Warning, and `ExceptionHandlingMiddleware` logs an `OperationCanceledException` at Warning when the invocation's token is cancelled ("The invocation was cancelled."); the answer and the rethrow are unchanged.

### 10. Deployment

As planned: `param sentryDsn` in `modules/functions.bicep` (`sentryDsnSetting`, added to the `union`) and `main.bicep` (default `''`), and `param sentryDsn = ''` in `parameters/prod.bicepparam` with its step. `bicep build` of both templates and `build-params` of both parameter files pass without warnings. The descriptions say "the Sentry project of the API": a `'` in a Bicep string would need an escape.

### 11. Tests

`Api.Functions/test`, 2,943 tests pass (112 of them on error reporting):

- `ErrorReportingLeakTests`: the acceptance test (a key/value `PUT` with secrets in the request, a scope request and user set as an integration would, a Warning with an exception, `BeginScope` with a dictionary and an object, an Error with a structured argument, an exception with a connection string, an inner `AggregateException`, `Exception.Data` with `sentry:` keys and a help link; the envelopes hold none of 24 secrets); concurrent invocations (one waits while the other runs) and the worker's entry after both; the worker's entry inside its own logging scope after a background breadcrumb; a timeout's cancellation stays an error; a cancelled invocation sends nothing; a failed queue invocation is sent once; the throttle end to end with the `suppressed` count; only the platform's Information as breadcrumbs.
- `ErrorReportingEntryPointTests`: no DSN, malformed DSNs (including `…/1/` and `…/1#/`), valid DSNs that really start the SDK, malformed volume settings, the fixed options, `Logging:Sentry` and a later `Configure` that cannot weaken them, the lower-case environment, Application Insights next to Sentry, the log filter.
- `SentryScrubberTests`, `SentryEventThrottleTests`, `FunctionTransactionNamesTests` (every HTTP function, the normalization, `host.json` without a route prefix) and `ClientParseFailureLoggingTests` (the seven parse failures answer `400` and log one Warning).
- Doubles: `ErrorReportingHarness`, `RecordingTransport` (reads the serialized envelopes), `NamedFunctionDefinition`, `ManualTimeProvider`, `SentrySdkCollection` (`DisableParallelization`: the SDK's hub is static); `TestFunctionContext.Invocation` sets the invocation id.

Two mutations were run by hand: keeping breadcrumbs outside invocations, and dropping the per-invocation `PushScope`; both fail the leak tests.

### 12. Documentation

As planned: the new `docs/Platform/Storyteller/error-reporting.md` (settings, events and breadcrumbs, what Sentry doesn't see, the content of an event, privacy and the decision on names, volume and its arithmetic, the check in production with a temporary `Storage Queue Data Message Sender` role, the tests); `docs/Platform/development.md` (the empty local DSN); the Deployment README (the parameter and section 5); the cookbook (steps 8.3, 14, 17, 18); the runbook (phases 2, 9, 12 and the section "Adding the API's Sentry project"); dated pointers in the production spec's out-of-scope notes.

### 13. Decisions

D1-D8 as in the spec.

### 14. Out of scope

Unchanged: following a UI error to the API event (with 424-139), the admin UI's own reports of the API's `5xx`, Sentry for the Scheduler, Supervisor and MCP server, and `sform` and DbCreator.

## Review

Before the implementation, three critics (privacy, SDK, operations) read the spec; their findings changed it before its commit: the shared root scope and its trace id, the request and user that the acceptance test must set to be able to fail, the entropy rule instead of character classes, PEM, escaped JSON and JWE members, a malformed DSN that must not stop the host, the worker's entry outside the middlewares, the throttle sized for 10 instances, cancellations at Warning, and the runbook section for the running environment.

After the implementation, three reviewers (correctness, privacy, tests) and a verifier per finding: 9 of 13 findings were confirmed and fixed in `a4d7c0257`:

- a DSN with a trailing `/` passed `IsValidDsn` and stopped the host (high);
- a malformed volume setting stopped the API, even without a DSN;
- the worker's entry after the middlewares carried the root scope's background breadcrumbs (two findings);
- the two-invocation test passed without `PushScope`; the `throttle` context's allow-list, the pruning and the seven Warnings were not covered by any test.

Refuted: a timeout's cancellation dropped by the worker filter (ASP.NET Core aborts a failing file result itself), the missing test of the `Program.cs` wiring, and `PostConfigure` against `Configure` (a test of a later `Configure` was added anyway, and one of a timeout's cancellation).

## Notes

To turn it on in production (runbook, "Adding the API's Sentry project"):

1. Create the Sentry project `2s-api` (EU, .NET) and turn on spike protection.
2. Commit its DSN as `sentryDsn` in `parameters/prod.bicepparam`, then what-if (`+ Sentry__Dsn` only) and create.
3. Publish the API from `main` after this change is merged.
4. Check with an invalid message on `configuration-transfers` (error-reporting.md, "Check it in production"): the first acceptance criterion of 424-248 (an exception in Sentry with the route template, environment and release) is verified only then.
