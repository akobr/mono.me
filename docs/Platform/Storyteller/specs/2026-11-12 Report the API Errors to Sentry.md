# Report the API Errors to Sentry

Linear: [424-248](https://linear.app/424fun/issue/424-248/report-the-apis-errors-to-sentry).

Line numbers refer to the tree this plan was written against (`22da6eab6`). No application code is changed by this document.

## Problem

The Storyteller API (`Api.Functions`) reports its errors only to Application Insights. The admin UI reports to Sentry (the project `2s-admin-ui`); `sform` and DbCreator have the Sentry code, and `sform` ships without a DSN. The API's exceptions are therefore missing next to the clients' errors: no alert, no grouping by release, no view of a backend failure beside the UI error it caused.

The production spec left the SDK out on purpose: [Azure Production Environment of the 2S Platform](../../Deployment/specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md), section 8 ("The API has no Sentry SDK; adding one is out of scope (its errors are in Application Insights)") and section 13 ("A Sentry SDK in the API"). This spec adds it.

The main risk is data. A request to the API carries bearer tokens, API keys, certificate headers, key/value values and configuration documents. Exception messages of the Azure and Cosmos SDKs can quote resource paths, and a structured log argument becomes a searchable Sentry tag. Nothing of that may leave the API.

## Current State

### Logging and Application Insights

- `Api.Functions/src/Program.cs`:
  - `new HostBuilder().ConfigureFunctionsWebApplication(worker => …)` (lines 29-43). The middlewares run in this order: `ExceptionHandlingMiddleware`, `TransportSecurityMiddleware`, `MachineAuthenticationMiddleware`, `BearerAuthenticationMiddleware` (lines 39-42). `ConfigureFunctionsWebApplication` puts its ASP.NET Core proxying middleware before them; that middleware executes an `IActionResult` after the inner middlewares return.
  - Application Insights: `AddApplicationInsightsTelemetryWorkerService()` and `ConfigureFunctionsApplicationInsights()` (lines 60-61), with adaptive sampling (the package default); the rule that limits its logger to Warning is removed (lines 78-89). The workspace has a daily ingestion cap (`Deployment/modules/monitoring.bicep`).
  - Development only: user secrets (lines 47-50) and the debug logger (lines 91-97).
  - `KeyValueUsageFlushService` (line 111) is a hosted service: it logs outside any invocation.
- No `appsettings.json`, and no `Logging` section is bound. `host.json` (lines 1-18) configures only the host's Application Insights sampling and the queues; it has no `extensions.http.routePrefix`, so every route is under `api/`.
- The environment: `IHostEnvironment.EnvironmentName` comes from `AZURE_FUNCTIONS_ENVIRONMENT`. Production sets `Production` (`Deployment/modules/functions.bicep:81`, `main.bicep:110` `runtimeEnvironment`); Core Tools set `Development` locally. The admin UI's Sentry environment is `production` (`Deployment/scripts/Set-GitHubEnvironment.ps1:24-25`, `docs/Platform/Deployment/decisions.md:58`), and Sentry environment names are case-sensitive.
- The version: the build generates `AssemblyInformationalVersion`, e.g. `0.10.33-alpha+22da6eab60` (42.Monorepo.GitVersioning; `src/Platform/version.json`). `ConditionalReads/BindingRuntimeVersion.cs:51` already reads the attribute. No workflow builds or deploys the API; it is published by hand (cookbook 16.1, runbook 11.1).

### Error paths

- `ErrorHandling/ExceptionHandlingMiddleware.cs:41`: `LogError(exception, "Error processing invocation")` is the path of an unexpected exception inside the middlewares. An HTTP request gets a `500`; a queue or timer invocation rethrows (line 47), and the worker's `FunctionsApplication` logs the same exception again at Error ("Function '{functionName}', Invocation id '{invocationId}': An exception was thrown by the invocation."). An exception of an `IActionResult`'s execution (e.g. the `FileStreamResult` of `Transfers/ViewTransferHttp.cs:142`) is logged only by the worker, outside the middlewares.
- A cancelled invocation (the caller aborted, the host drains the instance) throws `OperationCanceledException`; `ErrorResponseMapping` does not map it, so it is logged at Error as an unexpected exception.
- Other Error entries of the API: `Security/BearerAuthenticationMiddleware.cs:106` (signing keys not retrieved, with the request path), `Transfers/ViewTransferFunctions.cs:29,45` (a dropped queue message, with its text), `V1/AnnotationsHttp.cs:419` (catch-all of `SetAnnotation`) and `:1271` (an unreadable member).
- Client input logged at Error: `V1/ConfigurationHttp.cs:312,401`, `V1/ConfigurationSchemaHttp.cs:122,273,438`, `V1/TemplateHttp.cs:147,221` log a JSON parse failure of the request body and answer `400`. `ProcessClientErrorAsync` (`ExceptionHandlingMiddleware.cs:80-87`) logs every other client error at Warning.
- Libraries in the process log Error with customer names as arguments: `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:1336,1365` (`{AnnotationKey}`, `{PartitionKey}`, `{ErrorMessage}`), `Backend.Core/src/Transferring/ViewTransferService.cs:238,250` (`{Organization}.{Project}.{View}`), `Backend.Core/src/KeyValues/Protection/EnvelopeValueProtector.cs:245` (`{Key}`), and others.
- Warning and Information entries quote the request path with organization, project, view and key names (`MachineAuthenticationMiddleware.cs`, `BearerAuthenticationMiddleware.cs`, `ExceptionHandlingMiddleware.cs:75`) and the messages of domain exceptions (`ExceptionHandlingMiddleware.cs:83-87`). No entry logs a token, an API key, a key/value value or a connection string directly; `key-values.md` ("Plaintext never reaches Cosmos DB or the logs") and `binding.md` ("It never logs the message") state that rule.

### Routes

- Every HTTP function of the API declares `[HttpTrigger(…, Route = Definitions.Routes…)]`. Several templates have inline constraints (`Definitions.cs:18-27`), e.g. `v1/{organization}/{project}/{view:regex(^(?!usage$))}/key-values`.
- No production code maps a function to its route. The tests do it by reflection: `test/RouteTableTests.cs:16-26`, `test/RouteDispatchTests.cs:184-196`.
- Non-HTTP functions: `ProcessViewTransfer`, `AbandonViewTransfer` (queues), `PurgeViewTransfers`, `KeyValueProtectionTimer`, `KeyValueUsageSnapshotTimer` (timers). The OpenAPI extension adds four HTTP functions from its own assembly.

### Sentry in the repo

- `Directory.Packages.props:136-137`: `Sentry` and `Sentry.Serilog` 6.12.1.
- DbCreator and `sform` use the Serilog sink at Warning and `SentrySdk.Init` around a single log call (`DbCreator/src/Program.cs:112-125`, `Cli/src/Program.cs:427-451`); neither sets a release, an environment or a filter. Their only reusable rule is "no DSN, no SDK".
- The admin UI (`ui.admin/src/app/boot/sentry.ts`, `app/utils/telemetry-privacy.ts`) sends errors only, no tracing, no query strings, no headers except `user-agent`, no bodies; it replaces URLs with route templates and keeps organization, project, view and key names in API paths ("names, not values"). It also reports every `5xx` answer of the API as a message (`sentry.ts:61-82`). Its DSN is the GitHub variable `VITE_SENTRY_DSN`.

### Sentry SDK 6.12.1

Checked against the source of the `6.12.1` tag:

- `Sentry.Azure.Functions.Worker` has no 6.x release; it is deprecated since 6.0.0 and its 5.x line derives from types that are internal in 6.x. The supported way for the isolated worker is `Sentry.Extensions.Logging` (`ILoggingBuilder.AddSentry`).
- `AddSentry` registers the logger provider with a filter that lets every category through; `SentryLoggingOptions.MinimumEventLevel` (default Error) and `MinimumBreadcrumbLevel` (default Information) decide. `AddLogEntryFilter` drops entries by category, level and exception.
- The hub is created when the logger provider is first resolved, during the host's build. `Dsn.Parse` runs then: a malformed DSN throws and stops the host. `Dsn = ""` disables the SDK unless `SENTRY_DSN` is set; `null` without `SENTRY_DSN` throws. `AddSentry` binds `Logging:Sentry` (only when a `Logging` configuration is added to the logging builder), not `Sentry`, so `Sentry__Dsn` must be read by our code.
- A structured log argument of a primitive type becomes an event tag (`SentryLogger.CreateEvent`); `TagFilters` does not apply there. A logging scope becomes scope tags, or the extra `state`. `Exception.Data` goes to the mechanism's data; its `sentry:tag:`, `sentry:context:` and `sentry:` keys become tags, contexts and extra. `Exception.HelpLink` becomes the mechanism's help link.
- `AddSentry` adds `SentryHttpMessageHandler` to every `IHttpClientFactory` client: its breadcrumbs carry the full URL with the query, `CaptureFailedRequests` (default true) turns an outgoing `5xx` into an event with `Request.QueryString`, and `TracePropagationTargets` (default `.*`) sends `sentry-trace` to every host. `DisableSentryHttpMessageHandler` turns that off.
- `SendDefaultPii = false` keeps the user name, IP and server name out, but `User.Id` is still the installation id (a GUID file, a MAC address or a machine-name hash).
- `PushScope` clones the current scope with its breadcrumbs and propagation context. The logger provider pushes the root scope during the host's build, so a scope pushed per invocation starts with the breadcrumbs logged outside invocations and with the instance's trace id.
- `SetBeforeSend` runs after the scope, the exception processors and the event processors; an exception in it drops the event. `SetBeforeBreadcrumb` runs when a breadcrumb is added; breadcrumbs are immutable afterwards. Events are sent by a background worker.
- Release defaults to `SENTRY_RELEASE`, then `{entry assembly}@{informational version}`; environment defaults to `SENTRY_ENVIRONMENT`, then `debug` under a debugger, else `production`. Neither reads `IHostEnvironment`.

### Deployment

- `Deployment/modules/functions.bicep`: optional settings are left out while empty, e.g. `var authKitClientIdSetting = empty(authKitClientId) ? {} : { Auth__AuthKit__ClientId: authKitClientId }` (line 122), merged by `union(...)` (line 215). `main.bicep` declares `param authKitClientId string = ''` (line 50) and passes it (line 253); `functionMaximumInstanceCount` is 10 (line 95). `parameters/prod.bicepparam` holds every value of the environment as a literal ("they are not secrets", lines 2-3), each with its cookbook step (lines 30-36).
- `bicepconfig.json` makes unused parameters and variables errors; `.github/workflows/Platform.Deployment.yml` builds both templates and both parameter files.
- `docs/Platform/Deployment/cookbook-azure-production.md`, step 14 (line 486): one Sentry project `2s-admin-ui`; line 489: "The API has no Sentry SDK; its errors are in Application Insights." The runbook's phase 9 (line 339) has the same. The runbook's live changes have their own sections (e.g. "Adding the custom AuthKit domain").
- Sentry's Developer plan has 5k errors a month for the organization, shared by every project; over the quota, events are dropped (`429`). Per-project rate limits are for Business plans only.

## Proposed Changes

### 1. Package

- `Directory.Packages.props`: `<PackageVersion Include="Sentry.Extensions.Logging" Version="6.12.1" />` between `Sentry` and `Sentry.Serilog`, the same version as the rest of the Sentry line.
- `Api.Functions/src/Api.Functions.csproj`: `<PackageReference Include="Sentry.Extensions.Logging" />`.

### 2. Settings

A `Sentry` section, read by the API (`ErrorReporting/ErrorReportingOptions.cs`):

| Setting | App setting | Default | Meaning |
|---|---|---|---|
| `Sentry:Dsn` | `Sentry__Dsn` | empty | The DSN of the API's project. Empty or blank: Sentry is not registered at all. |
| `Sentry:RepeatInterval` | `Sentry__RepeatInterval` | `01:00:00` | The same error (section 8) is sent once in this interval; `00:00:00` sends every repeat. |
| `Sentry:MaxEventsPerHour` | `Sentry__MaxEventsPerHour` | `10` | Events an instance sends in a clock hour; `0` is no limit. |
| `Sentry:MaxEventsPerDay` | `Sentry__MaxEventsPerDay` | `40` | Events an instance sends in a UTC day; `0` is no limit. |

No other Sentry option comes from configuration: the privacy options of section 6 are fixed in code. `SENTRY_DSN`, `SENTRY_ENVIRONMENT` and `SENTRY_RELEASE` are ignored by design: without `Sentry:Dsn` nothing is registered, and with it the DSN, the environment and the release are always set.

### 3. Registration

`ErrorReporting/ErrorReportingEntryPoint.cs`:

```csharp
public static class ErrorReportingEntryPoint
{
    public const string SectionName = "Sentry";

    public static bool IsErrorReportingEnabled(this IConfiguration configuration);

    public static IServiceCollection AddErrorReporting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);
}
```

- `IsErrorReportingEnabled` is true for a DSN of the right shape: an absolute `https` (or `http`) URI with a public key (user info) and a project id (the last path segment). A malformed DSN must not stop the API: it is treated as no DSN, and a hosted service logs one Warning at startup ("Sentry:Dsn is not a valid DSN; errors are not reported to Sentry."), without the value.
- Without a valid DSN, `AddErrorReporting` registers nothing else: no logger provider, no hub, no HTTP handler filter. Local runs (`local.settings.json` has no DSN), Aspire (`AppHost.cs` passes none) and the tests send nothing.
- With a DSN: `services.AddLogging(logging => logging.AddSentry())`, then `services.PostConfigure<SentryLoggingOptions>(…)`, so a `Logging:Sentry` value, if one is ever bound, cannot weaken them:
  - `Dsn` = the trimmed setting;
  - `Environment` = `environment.EnvironmentName` in lower case (`production`, like the admin UI);
  - `Release` = `42.Platform.Storyteller.Api.Functions@<AssemblyInformationalVersion>` of the API assembly, e.g. `…@0.10.33-alpha+22da6eab60`: the version and the commit, in the `package@version` form the SDK gives `sform`, so the API's releases stay apart from the admin UI's commit releases;
  - `MinimumEventLevel = Error`, `MinimumBreadcrumbLevel = Information`, `MaxBreadcrumbs = 30`;
  - the privacy options of section 6, `BeforeSend`, `BeforeBreadcrumb`, and `BeforeSendTransaction` returning `null`;
  - no tracing (`TracesSampleRate` stays unset), `AutoSessionTracking = false`, `EnableLogs = false`.
- `Program.cs`:
  - `services.AddErrorReporting(context.Configuration, context.HostingEnvironment)` after the Application Insights registration and its filter rule. Application Insights is unchanged and receives everything it did.
  - `ConfigureFunctionsWebApplication((context, worker) => …)`: when `context.Configuration.IsErrorReportingEnabled()`, `worker.UseMiddleware<SentryScopeMiddleware>()` **before** `ExceptionHandlingMiddleware`, so its scope wraps the error log of line 41. The callback runs while the services are configured, after the app configuration (with the user secrets) is built.

### 4. What becomes an event

- An entry of level Error or Critical becomes an event, with its exception when it has one. `ExceptionHandlingMiddleware.cs:41` logs every unexpected exception of the middlewares and the function. The worker's second Error entry for a failed queue or timer invocation carries the same exception instance and is dropped by Sentry's duplicate detection. An exception of an `IActionResult`'s execution reaches Sentry through the worker's entry only; section 5 names it.
- Information and Warning become breadcrumbs of the invocation's scope, only from the platform's own categories (`_42.`). Other categories (ASP.NET Core hosting, `System.Net.Http`, Azure SDK, Cosmos, the Functions worker) reach Sentry only at Warning and above (`AddLogEntryFilter`): their Information entries quote request URLs.
- Debug and Trace never reach Sentry.
- Sentry does not see: errors while the host starts (before the hub exists), the Functions host's own failures (timeouts, worker restarts) and the front end's answers. Application Insights and Sentry's uptime monitor cover them.

### 5. Transaction names and the scope

`ErrorReporting/FunctionTransactionNames.cs` maps a function name to its transaction name, built once by reflection over the `[Function]` methods of the API assembly, the way `RouteDispatchTests` reads them:

- HTTP: `"{METHOD} /api/{template}"`, where the template is the trigger's `Route` parsed by `RoutePatternFactory` and written back without constraints, defaults and optional markers. For example `PUT /api/v1/{organization}/{project}/{view}/key-values/{key}`. `METHOD` is the request's method, or the trigger's declared methods joined by `|` when the request is not known; the prefix `api` is the default of `host.json`, which a test checks.
- Queue and timer: `queue ProcessViewTransfer`, `timer KeyValueProtectionTimer`.
- A function of another assembly (the OpenAPI extension's): its function name.

`ErrorReporting/SentryScopeMiddleware.cs` (an `IFunctionsWorkerMiddleware` with `IHub`):

```csharp
using (_hub.PushScope())
{
    _hub.ConfigureScope(scope =>
    {
        scope.ClearBreadcrumbs();
        scope.TransactionName = transactionName;
        scope.SetTag("function", context.FunctionDefinition.Name);
        scope.Contexts["function"] = new Dictionary<string, string> { ["name"] = …, ["invocation_id"] = context.InvocationId };
    });
    _hub.ContinueTrace((string?)null, null); // a trace id of its own, not the instance's
    await next(context);
}
```

- Every event and breadcrumb of an invocation lands in its own scope, which starts empty: neither the breadcrumbs logged outside invocations (the hosted service, refresh callbacks) nor another invocation's reach it, and invocations do not share a trace id.
- The invocation id is the one Application Insights records, so an event leads to the invocation's telemetry. It is a context, not a tag (one value per invocation).
- No URL, route value, binding data, header or body is put on the scope.
- An event without a transaction that carries the worker's `functionName` tag (the worker's own Error entry, e.g. a failed `IActionResult`) gets the transaction, the `function` tag and the `function` context in `BeforeSend`, from `FunctionTransactionNames` and its `invocationId` tag, before the tags are removed.

### 6. Privacy: what is never sent

Fixed options:

- `SendDefaultPii = false`, `IsEnvironmentUser = false`;
- `CaptureFailedRequests = false` and `DisableSentryHttpMessageHandler = true`: no outgoing-request breadcrumbs, no events from outgoing `5xx`, no `sentry-trace` headers to WorkOS, a federated issuer or Keycloak.

`ErrorReporting/SentryScrubber.cs` (internal, static, pure) is the `BeforeSend` and `BeforeBreadcrumb` filter:

- **The event's request** is replaced by an empty `SentryRequest`: no URL, query string, headers (`Authorization` with `Bearer` or `ApiKey`, `Cookie`, `X-ARR-ClientCert`), cookies, body or environment. The SDK sets none of them without the ASP.NET Core integration; the filter makes it a guarantee, also for a future integration.
- **The user** is replaced by an empty `SentryUser` (no installation id); `ServerName` is cleared.
- **Tags**: only the API's own (`function`) stay. The tags of structured log arguments, logging scopes and `Exception.Data` are removed; the values of log arguments are still in the formatted message, after the text scrubbing below.
- **Extra** values are replaced by `[Filtered]`.
- **Contexts** are kept only from an allow-list: `function`, `throttle` (section 8), `trace`, and the SDK's `app`, `device`, `os`, `runtime`, `gpu`, `Current Culture`, `Current UI Culture`, `Memory Info`, `ThreadPool Info`, `Dynamic Code`. Every other context (e.g. one of `Exception.Data["sentry:context:…"]`) is removed.
- **Exceptions**: the mechanism's `Data` keeps only `HResult`, its help link is cleared, its description and every exception's `Value` (inner and aggregated ones included) are scrubbed as text.
- **Text scrubbing** of the message (template and formatted; its parameters are dropped), the exception values, and every breadcrumb's message and data values. The patterns (case-insensitive where it matters) and their replacement `[Filtered]`:
  1. PEM blocks: `-----BEGIN …-----` to `-----END …-----`;
  2. JWTs and compact JWEs: `eyJ…` with two to four dot-separated parts;
  3. an authorization scheme and its credential: `Bearer …`, `ApiKey …`, `Basic …`;
  4. a Storyteller API key: `2s.<base64url>.<secret>` (`Access.ApiKeys/src/StructuredApiKey.cs`);
  5. `name=value` secrets of connection strings, query strings and form bodies keep the name and lose the value: `AccountKey`, `SharedAccessKey`, `SharedAccessSignature`, `AccessKey`, `Password`, `Pwd`, `ClientSecret`, `client_secret`, `Secret`, `Token`, `access_token`, `refresh_token`, `id_token`, `subject_token`, `actor_token`, `client_assertion`, `code_verifier`, `ApiKey`, `api_key`, `sig`, `InstrumentationKey`;
  6. JSON members with a secret's name, also JSON-escaped inside a string, with a string, object, array or scalar value: `value`, `password`, `secret`, `token`, `access_token`, `refresh_token`, `id_token`, `client_secret`, `clientSecret`, `apiKey`, `accessKey`, `privateKey`, `private_key`, `connectionString`, `credentials`, `plaintext`, and the members of a client-encrypted value, a JWE in the JSON serialization (`protected`, `encrypted_key`, `iv`, `ciphertext`, `tag`); `Value` is the member of a key/value write;
  7. query strings: `?name=value&…`;
  8. e-mail addresses;
  9. long opaque tokens: a run of 32 or more characters of `[A-Za-z0-9+/=_-]` with a Shannon entropy of at least 4.2 bits per character is replaced whole (base64 keys, secrets, certificates); a run that starts with `/` is a path, and its segments are judged one by one. Key/value names (`Payments_Gateway_ApiKey_Version2`), GUIDs, hex thumbprints and paths stay.
- A breadcrumb of type `http` is dropped.
- The regular expressions have a match timeout; an exception in the filter drops the event (the SDK's behaviour), never sends it unfiltered.

### 7. Customer names: decision

Organization, project, view and annotation keys and key/value **names** stay readable where the code puts them in a message: a failed write of `acme.billing` is reproduced only with that key, and the admin UI already sends the same names in its API paths. They are identifiers, not values. They are **not** sent as tags (section 6), and the transaction name is the route template, so they are neither indexed nor aggregated. Values (key/value values, configuration documents, secrets, tokens) are never sent. User account ids are pseudonymous ids and stay in messages that have them; e-mail addresses are scrubbed. `docs/Platform/Storyteller/error-reporting.md` states this.

### 8. Volume and cost

The organization's Developer plan has 5k errors a month for the admin UI and the API together, and the admin UI also reports every `5xx` it gets. The API's normal volume is small: an event is an unexpected `500` or a failed background job; client errors and cancellations are Warnings (section 9). The risk is a storm: a Cosmos DB outage turns every request into an event, on up to 10 instances, and would use the month's quota in minutes, hiding the admin UI's errors for the rest of the month.

`ErrorReporting/SentryEventThrottle.cs` (with a `TimeProvider`, one lock around its state) is the first step of `BeforeSend`:

- the signature of an error: the transaction name, the logger category, the message template, the exception types and the method that threw the innermost exception (`TargetSite`), so two defects on one route are two signatures;
- the same signature is sent once per `RepeatInterval` (1 hour); a repeat is dropped and counted, and the next event of the signature carries the count in the context `throttle` (`suppressed`);
- an instance sends at most `MaxEventsPerHour` (10) per clock hour and `MaxEventsPerDay` (40) per UTC day; a signature the instance has not sent before passes the hourly cap (a root cause is not hidden behind its symptoms) but not the daily one;
- the recent signatures are pruned when they grow past 1,000 entries.

The arithmetic: a storm on all 10 instances costs at most 400 events a day; the usual one or two instances, 40-80. A whole day of a full storm takes 8% of the month's quota. Application Insights keeps the occurrences as sampled counts (adaptive sampling) until its daily cap; the `throttle` context gives Sentry's own count of the dropped repeats. No sampling in Sentry: a rare error must not be lost. Sentry's spike protection is turned on for the organization (cookbook 14).

### 9. Warnings, not errors

- The seven parse failures of "Error paths" (`ConfigurationHttp.cs:312,401`, `ConfigurationSchemaHttp.cs:122,273,438`, `TemplateHttp.cs:147,221`) are client errors that answer `400`; their `try` blocks wrap only the parsing. They are logged at Warning like `ProcessClientErrorAsync`, and become breadcrumbs, not events. `AnnotationsHttp.cs:419` stays at Error: it is a catch-all that also catches server faults.
- `ExceptionHandlingMiddleware`: an `OperationCanceledException` while the invocation's token is cancelled is logged at Warning ("The invocation was cancelled."); the answer and the rethrow of a queue or timer invocation are unchanged. An `OperationCanceledException` of a timeout (the token is not the invocation's) stays an error.

### 10. Deployment

- `modules/functions.bicep`: `@description('The DSN of the API\'s Sentry project; the setting is left out while it is empty.') param sentryDsn string`, `var sentryDsnSetting = empty(sentryDsn) ? {} : { Sentry__Dsn: sentryDsn }`, added to the `union(...)`.
- `main.bicep`: `param sentryDsn string = ''` with its description (known after step 14), passed to the module.
- `parameters/prod.bicepparam`: `// Step 14: the DSN of the API's Sentry project (2s-api).` and `param sentryDsn = ''` until the project exists. Like every value there, the DSN is a literal and not a secret: it only lets a sender add events to the project, which Sentry's inbound filters and spike protection contain. Creating the project is a manual step (cookbook 14); until its DSN is committed, the setting stays out and the API reports nothing.
- The run that adds the DSN: `what-if` shows only `Modify prod-2s-api-fa/appsettings: + Sentry__Dsn`. The order of publishing the code and running `main.bicep` does not matter: the old code ignores the setting, the new code without it reports nothing.

### 11. Tests

In `Api.Functions/test` (xUnit, Shouldly, handwritten doubles). The tests that start the SDK share `[Collection("SentrySdk")]`, use a recording `ITransport` with a syntactically valid fake DSN, disable the process-wide capture integrations, flush before they assert, count envelope items of type `event`, and close the SDK afterwards. A function definition double takes a name and an invocation id.

- `ErrorReportingEntryPointTests`:
  - no DSN (`null`, `""`, `"   "`) and a malformed one (`not a dsn`, no key, no project id): no Sentry provider or hub is registered and `IsErrorReportingEnabled` is false; a malformed one registers the startup warning;
  - with a DSN: the options carry the DSN, the lower-case environment, the release of the API assembly, `SendDefaultPii = false`, `CaptureFailedRequests = false`, `DisableSentryHttpMessageHandler = true`, the levels of section 4; with a `Logging` configuration added to the logging builder, `Logging:Sentry:SendDefaultPii=true` does not turn it on while a control value of `Logging:Sentry` does arrive;
  - the Application Insights logger provider stays registered next to Sentry's.
- `SentryScrubberTests`: each pattern of section 6 in a message, an exception value, a breadcrumb message and breadcrumb data, including the emulator's Cosmos key, a base64 certificate, a PEM block, an escaped JSON value and a JWE in JSON; texts with names only (`No Administrator access to 'acme.web'.`, an annotation key, a 34-character key/value name, a GUID, an `/api/v1/…` path) are unchanged; tags outside the allow-list, extra, contexts outside the allow-list, the help link, request, user and server name are cleared; the worker's tags give the transaction.
- `FunctionTransactionNamesTests`: every HTTP function of the assembly has a name `METHOD /api/v1/…` without a constraint, default or optional marker; examples for a key/value route, a queue and a timer; `host.json` has no route prefix.
- `SentryEventThrottleTests` (a manual `TimeProvider`): repeats in the interval are dropped and counted, sent again after it with the count; another throw site is another signature; the hourly cap with its exception for new signatures; the daily cap; `0` and `00:00:00` turn them off; concurrent calls keep the caps.
- `ErrorReportingLeakTests` (the acceptance test), with the throttle off:
  - a background Warning with names is logged before the invocation;
  - a `PUT` of a key/value pair goes through `SentryScopeMiddleware` and `ExceptionHandlingMiddleware`; the request has `Authorization: Bearer <JWT>`, `Cookie`, `X-ARR-ClientCert`, a body `{"Value":"<secret>"}` and a query string; the invocation also fills `scope.Request` (URL with the query, headers with `Authorization: ApiKey 2s.<…>.<secret>`, cookies, data, env) and `scope.User`, as an integration would;
  - inside it: a Warning with a secret and an exception, a `BeginScope` dictionary and a `BeginScope` object with secrets, an Error with a secret as a structured argument and no exception; then the function throws an exception whose message holds a Cosmos connection string, with an inner exception, an `AggregateException` child, `Exception.Data` entries (plain, `sentry:tag:`, `sentry:context:`) and a help link with secrets;
  - the serialized envelopes hold none of the secrets, the events have the route template as `transaction`, `production` as environment and the API's release, no request headers, cookies, query string or data, no user, and the invocation's breadcrumbs without the background one;
  - a second invocation sees neither the first one's transaction, tags or breadcrumbs, and has another trace id;
  - a cancelled invocation sends no event; a failed queue invocation rethrows, and the worker's entry of the same exception (logged after the middlewares) sends no second event.

### 12. Documentation

- New `docs/Platform/Storyteller/error-reporting.md`: settings, events and breadcrumbs, what Sentry does not see, environment and release, transaction names, privacy (section 6 and the decision of section 7), volume and its arithmetic, off without a DSN, Application Insights next to it, trying it locally with a user secret, and checking it in production.
- `docs/Platform/development.md`, "API settings": `Sentry:Dsn` is empty locally.
- `src/Platform/Deployment/README.md`: the parameter table (`sentryDsn` → `Sentry__Dsn`, step 14) and section 5.
- `docs/Platform/Deployment/cookbook-azure-production.md`: step 8.3 (the setting), step 14 (a second project `2s-api`, its DSN in `prod.bicepparam`, spike protection, line 489 replaced), step 17 (verify an event) and step 18 (the shared quota).
- `docs/Platform/Deployment/runbook-azure-production.md`: phase 2 (left out while empty), phase 9 (the project, and its DSN committed before the next `main.bicep` run), phase 12 (verify), and a section "Adding the API's Sentry project" for the running environment, modelled on "Adding the custom AuthKit domain": create `2s-api`, commit the DSN through a pull request, what-if (`+ Sentry__Dsn` only), create, publish the API if the running build predates the SDK, verify.
- The check in production: an invalid message on the queue `configuration-transfers` (`az storage message put --auth-mode login` with a temporary `Storage Queue Data Message Sender` role) makes `ProcessViewTransfer` log Error; expect one event with the transaction `queue ProcessViewTransfer`, the environment `production`, the API's release and no request, user or extra data. A second check within the repeat interval is throttled.
- The production spec's out-of-scope note gets a dated pointer to this spec, as `2026-10-15 Export and Import of a View.md:874` does; its text is not rewritten.

### 13. Decisions

- **D1.** `Sentry.Extensions.Logging` and our own middleware, not `Sentry.Azure.Functions.Worker` (no 6.x release) and not OpenTelemetry (no tracing is wanted).
- **D2.** Errors only, no performance tracing: transactions cost quota and the admin UI sends no trace headers.
- **D3.** Lower-case environment from the host, so the API and the admin UI share `production`.
- **D4.** Release `42.Platform.Storyteller.Api.Functions@<informational version>`.
- **D5.** Customer names readable in messages, never as tags; values never (section 7).
- **D6.** A client-side throttle sized for 10 instances instead of sampling (section 8).
- **D7.** Client input errors and cancellations at Warning (section 9).
- **D8.** A malformed DSN turns Sentry off with a startup Warning instead of stopping the API.

### 14. Out of scope

- Following a UI error to the API event. The admin UI sends no trace headers, and the `500` body is the subject of [424-139](https://linear.app/424fun/issue/424-139/the-generic-500-of-the-functions-api-returns-the-whole-exception-stack); an error id in the response can come with it.
- The admin UI's own reports of the API's `5xx` (they now duplicate the API's events in the shared quota).
- Creating the Sentry project and committing its DSN (a manual step, cookbook 14).
- Sentry for the Scheduler, Supervisor and MCP server; moving `sform` and DbCreator to the same filters.

### Phases

1. The SDK in the API: sections 1-9 and 11.
2. Deployment and documentation: sections 10 and 12.
