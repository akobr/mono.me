# Error reporting of the API

The Storyteller API (`Api.Functions`) reports its errors to Sentry, next to Application Insights. Application Insights keeps every log entry and request as before; Sentry gets the errors, grouped by route and release, with alerts. Production reports to the Sentry project `2s-api` (EU, `de.sentry.io`); the admin UI reports to `2s-admin-ui` in the same organization.

The code is in `src/Platform/Storyteller/Api.Functions/src/ErrorReporting/`; the design and its decisions are in the spec [Report the API Errors to Sentry](specs/2026-11-12%20Report%20the%20API%20Errors%20to%20Sentry.md).

## Settings

| Setting | App setting | Default | Meaning |
|---|---|---|---|
| `Sentry:Dsn` | `Sentry__Dsn` | empty | The DSN of the project. Empty: nothing of Sentry is registered and nothing is sent. |
| `Sentry:RepeatInterval` | `Sentry__RepeatInterval` | `01:00:00` | The same error is sent once in this interval; `00:00:00` sends every repeat. |
| `Sentry:MaxEventsPerHour` | `Sentry__MaxEventsPerHour` | `10` | Events an instance sends in a clock hour; `0` is no limit. An error the instance hasn't sent before passes it. |
| `Sentry:MaxEventsPerDay` | `Sentry__MaxEventsPerDay` | `40` | Events an instance sends in a UTC day; `0` is no limit. |

- In production, `main.bicep` writes `Sentry__Dsn` from `sentryDsn` of `parameters/prod.bicepparam`, and leaves the setting out while it is empty ([Deployment README](../../../src/Platform/Deployment/README.md#parameters)).
- A malformed DSN doesn't stop the API: Sentry stays off, and the API logs one Warning at start, *"Sentry:Dsn is not a valid DSN; errors are not reported to Sentry."*, without the value.
- `SENTRY_DSN`, `SENTRY_ENVIRONMENT` and `SENTRY_RELEASE` are ignored, and so is a `Logging:Sentry` section: everything else is fixed in code.
- Locally, under Aspire and in the tests there is no DSN, so nothing is sent. To try it, set the DSN of a test project as a user secret of the API (`dotnet user-secrets set "Sentry:Dsn" "<dsn>"`); the environment is then `development`.

## What becomes an event

- A log entry of level **Error** or **Critical** becomes an event, with its exception. Every unexpected exception of a function reaches `ExceptionHandlingMiddleware`, which logs it at Error: an HTTP request answers `500`, a queue or timer invocation fails and is delivered again. The worker logs the same exception again; that entry carries the same exception and is not sent twice.
- **Information** and **Warning** become breadcrumbs of the invocation: the trail an event of the same invocation shows. Only the platform's own categories (`_42.…`) leave breadcrumbs below Warning; the Information of ASP.NET Core, `HttpClient`, the Azure SDK, Cosmos DB and the Functions worker quote URLs and stay out.
- **Debug** and **Trace** never reach Sentry.
- Client errors are Warnings, not errors: a rejected request (`4xx` of a domain rule), a body that isn't valid JSON, a binding that can't be evaluated. So is a cancelled invocation (the caller went away, or the host drains the instance).
- Sentry doesn't see: errors while the host starts (before Sentry exists), the Functions host's own failures (timeouts, restarts), and what the front end answers. Application Insights and Sentry's uptime monitor on `GET /api/v1/auth/configuration` cover them.

## What an event carries

- **Transaction**: the route template of the function, never its URL: `PUT /api/v1/{organization}/{project}/{view}/key-values/{key}`, `queue ProcessViewTransfer`, `timer KeyValueProtectionTimer`. Events group by route.
- **Environment**: the host's environment (`AZURE_FUNCTIONS_ENVIRONMENT`) in lower case, `production` like the admin UI.
- **Release**: `42.Platform.Storyteller.Api.Functions@<version>`, the informational version of the build with its commit, e.g. `…@0.10.33-alpha+22da6eab60`.
- **Tag** `function`: the function's name. No other tag.
- **Context** `function`: the name and the **invocation id**, which is the invocation's id in Application Insights: an event leads to its requests, traces and dependencies there.
- The message, the exceptions with their stack traces, and the breadcrumbs, all scrubbed (below).
- Each invocation has a scope of its own: its own breadcrumbs only, its own trace id. No performance tracing: no transactions are sent, only errors.

## Privacy

Never sent:

- the **request**: no URL, query string, headers (`Authorization` with `Bearer` or `ApiKey`, `Cookie`, `X-ARR-ClientCert`), cookies or body;
- the **user** and the machine: no user, no installation id, no server name, no IP (`SendDefaultPii` is off);
- tags other than `function`: a structured log argument, a logging scope or an `Exception.Data` entry would become a tag; extra values are filtered, and only Sentry's own contexts (`app`, `device`, `os`, `runtime`, `trace`, culture, memory and thread pool) and the API's `function` and `throttle` stay;
- outgoing requests: no breadcrumbs of `HttpClient` calls, no events of their `5xx`, no trace headers to WorkOS, a federated issuer or Keycloak.

Texts (messages, exception values, breadcrumbs) are scrubbed before they are sent; each match becomes `[Filtered]`:

- PEM blocks, JWTs, an authorization scheme with its credential (`Bearer …`, `ApiKey …`, `Basic …`) and Storyteller API keys (`2s.<…>.<secret>`);
- the values of `name=value` secrets in connection strings, query strings and form bodies (`AccountKey`, `SharedAccessKey`, `SharedAccessSignature`, `Password`, `client_secret`, `subject_token`, …);
- the values of JSON members with a secret's name, also escaped inside a string: `Value` (a key/value write), `password`, `secret`, `token`, `privateKey`, `connectionString`, and the members of a client-encrypted value;
- query strings, e-mail addresses, and long opaque tokens (base64 keys, secrets and certificates, recognised by their entropy).

**Names stay readable, values never leave.** Organization, project and view names, annotation keys and key/value names are kept where the code writes them in a message: a failed write of `acme.billing` is reproduced only with that key, and the admin UI sends the same names in its API paths. They are identifiers, not values, and they are never tags, so Sentry neither indexes nor aggregates them; the transaction is the route template. Key/value values, configuration documents, secrets and tokens are never in a message (the rule of [key-values](key-values.md) and [binding](binding.md)), and the scrubbing is the second line. Account ids are pseudonymous and stay where a message has them; e-mail addresses are filtered.

A failure of the scrubbing (for example a regular expression that times out) drops the event; it is never sent unfiltered.

## Volume

The organization's Developer plan has 5k errors a month for both projects, and the admin UI also reports the API's `5xx` it gets. The API's normal volume is small. A storm (a Cosmos DB outage turns every request into an error, on up to 10 instances) would use the month's quota in minutes, so each instance throttles itself:

- the same error (route, logger, message template, exception types and the method that threw) is sent once per `RepeatInterval`; its dropped repeats are counted, and the next event of the error carries the count in the context `throttle` (`suppressed`);
- at most `MaxEventsPerHour` events per clock hour, which an error the instance hasn't sent before passes, so a root cause isn't hidden behind its symptoms;
- at most `MaxEventsPerDay` events per UTC day.

A whole day of a storm on all 10 instances costs at most 400 events, 8% of the month; on the usual one or two instances, 40 to 80. Sentry's spike protection is on for the organization. Application Insights keeps the occurrences (sampled, until its daily cap).

## Check it in production

An invalid message on the queue `configuration-transfers` makes `ProcessViewTransfer` log an Error. Your account needs **Storage Queue Data Message Sender** on the storage account for the check; remove the role afterwards.

```powershell
$account = 'prod2s42fornetsa'
$scope = az storage account show -n $account --query id -o tsv
$me = az ad signed-in-user show --query id -o tsv
az role assignment create --assignee $me --role 'Storage Queue Data Message Sender' --scope $scope
# a minute later (the role propagates)
$message = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('sentry-check'))
az storage message put --account-name $account --auth-mode login --queue-name configuration-transfers --content $message
az role assignment delete --assignee $me --role 'Storage Queue Data Message Sender' --scope $scope
```

Expect one event in `2s-api`: the message *"A view transfer message is not valid and was dropped: sentry-check"*, the transaction `queue ProcessViewTransfer`, the environment `production`, the release `42.Platform.Storyteller.Api.Functions@<version>`, the tag `function` and no request, user or extra data. A second check within an hour is throttled.

## Tests

`Api.Functions/test`: `ErrorReportingLeakTests` runs requests and errors full of secrets through `SentryScopeMiddleware`, `ExceptionHandlingMiddleware` and the real SDK with a recording transport, and asserts that the serialized envelopes hold none of them; `ErrorReportingEntryPointTests`, `SentryScrubberTests`, `SentryEventThrottleTests` and `FunctionTransactionNamesTests` cover the registration, the scrubbing, the throttle and the transaction names.
