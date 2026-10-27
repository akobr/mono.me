# Configuration Provider for .NET

The package `42.Platform.Storyteller.Configuration` loads one Storyteller configuration into the `IConfiguration` of a .NET application. It reads the configuration by its full key, polls it with `If-None-Match`, keeps the applied snapshot in memory and, when `CacheDirectory` is set, a last-known-good copy on disk, switches the active key at runtime, and, in `Resolved` mode with a `ReferenceResolver` configured, resolves secret references locally, with the application's own identity. The same package gives jobs a service that reads any configuration on demand. `42.Platform.Storyteller.Configuration.KeyVault` adds a resolver for Azure Key Vault.

The code is in:

- `src/Platform/Storyteller/Configuration/src/`: the options and their environment (`Settings/`), the transport (`Transport/`), the reader (`Reading/`), the last-known-good cache (`Caching/`), the engine with its refresh and rollback (`Engine/`), the container integration (`Container/`), the substitution of references (`References/`) and the documents on demand (`Documents/`);
- `src/Platform/Storyteller/ConfigurationFormats/src/`: the flattener (`DocumentFlattener`, `JsonPaths`), in the shared library of the [configuration formats](configuration-formats.md) that the provider references;
- `src/Platform/Storyteller/Configuration.KeyVault/src/`: `KeyVaultReferenceResolver`;
- `src/Platform/Storyteller/Sdk.NSwag/src/`: the conditional reads (`ApiSdk.Conditional.cs`), the machine credentials (`Credentials/`) and the resilience pipeline (`Resilience/`), described in the [SDK readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md).

The design and its decisions are in [the spec](specs/2026-10-23%20Configuration%20Provider%20for%20.NET.md). The server side of the reads (tags, headers, the `304`) is in [Conditional reads](inheritance.md#conditional-reads).

## Packages

| Package | Namespace | Contents |
| --- | --- | --- |
| `42.Platform.Storyteller.Configuration` | `_42.Platform.Storyteller.Sdk.Configuring` | The provider, its options and refresher, the resolver contract, `SecretReference`, and `IStorytellerDocuments` |
| `42.Platform.Storyteller.Configuration.KeyVault` | `_42.Platform.Storyteller.Sdk.Configuring.KeyVault` | `KeyVaultReferenceResolver` |

- `AddStoryteller` is in the namespace `Microsoft.Extensions.Configuration`, like every Microsoft and Azure configuration extension. `AddStorytellerConfiguration` and `AddStorytellerDocuments` are in `Microsoft.Extensions.DependencyInjection`. None of them needs a `using`.
- The namespace is not `_42.Platform.Storyteller.Configuration`, because that is the `Configuration` model of `Abstractions.Annotations`.
- The credentials come from the C# SDK, `42.Platform.Storyteller.Sdk.NSwag`, in the namespace `_42.Platform.Storyteller.Sdk.Credentials`.
- The flattening rules come from `42.Platform.Storyteller.ConfigurationFormats`, a dependency of the provider, in the namespace `_42.Platform.Storyteller.ConfigurationFormats` (see [Configuration formats](configuration-formats.md)).
- The Key Vault package does not reference `Azure.Identity`. The application passes a `TokenCredential`, for example `DefaultAzureCredential` from that package.

## A minimal Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// STORYTELLER_ENDPOINT, STORYTELLER_KEY and the variables of the machine's credential come from the environment.
builder.Configuration.AddStoryteller();

// The provider's logging, and IStorytellerConfigurationRefresher in the container.
builder.Services.AddStorytellerConfiguration();

// IOptionsMonitor<InvoicingOptions> follows every applied document.
builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection("invoicing"));

var app = builder.Build();
app.Run();
```

The same with the options set in code:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.KeyVault;
using _42.Platform.Storyteller.Sdk.Credentials;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddStoryteller(options =>
{
    options.Key = "@house.main.default.exe.northwind.invoicing.production"; // or STORYTELLER_KEY
    options.Mode = StorytellerReadMode.Resolved;
    options.RefreshInterval = TimeSpan.FromSeconds(30);
    options.Optional = builder.Environment.IsDevelopment();
    options.Credential = StorytellerCredential.FromEnvironment();          // what an unset Credential does anyway
    options.ReferenceResolver = new KeyVaultReferenceResolver(new DefaultAzureCredential());
    options.Placement = SourcePlacement.BeforeEnvironmentVariables;        // appsettings.json < Storyteller < env < command line
});

builder.Services.AddStorytellerConfiguration();
```

- `AddStoryteller` reads the environment and validates the options at once. A missing or invalid setting throws `StorytellerConfigurationException`.
- The platform is read at the first `Load()` of the source. A `ConfigurationManager`, which `WebApplication.CreateBuilder` and `Host.CreateApplicationBuilder` use, loads a source when it is added, so the read happens inside `AddStoryteller`. A `ConfigurationBuilder` reads at `Build()`.
- Without `AddStorytellerConfiguration()`, or `options.LoggerFactory`, the provider logs nothing.

## Settings

### Options and variables

The `configure` callback runs first. Every option it left `null` is then read from its variable; a blank string counts as `null`. A value set in code therefore wins over the environment.

| Option | Variable | Default and rule |
| --- | --- | --- |
| `Endpoint` | `STORYTELLER_ENDPOINT` | Required. The API base URL, `/api` included. `https`, or `http` to `localhost` or a loopback address literal (`127.0.0.1`, `[::1]`), never to a name that DNS resolves. No user info, query or fragment. |
| `Key` | `STORYTELLER_KEY` | Required. A full key, `@{organization}.{project}.{view}.{annotation key}`, of any annotation type, units of execution included. |
| `Mode` | `STORYTELLER_MODE` | `resolved` or `effective`, in any case. `Resolved` when unset. |
| `RefreshInterval` | `STORYTELLER_REFRESH_INTERVAL` | 30 s. `0` turns the refresh off; otherwise from 1 s to 45 days. |
| `Optional` | `STORYTELLER_OPTIONAL` | `true`, `false` (any case), `1` or `0`. `false` when unset. |
| `CacheDirectory` | `STORYTELLER_CACHE_DIR` | None: no disk cache. Made a full path when the source is added. |
| `Credential` | the [credential variables](#credentials) | `StorytellerCredential.FromEnvironment()`. Without credential variables, requests are anonymous and the API answers `401`. |
| `ReferenceResolver` | – | None: no substitution. |
| `SecretRefreshInterval` | – | 1 h. Positive, or `Timeout.InfiniteTimeSpan` to resolve a value once. |
| `ReadTimeout` | – | 30 s. Bounds one whole run, retries and token requests included. Positive, or `Timeout.InfiniteTimeSpan` for no limit. |
| `Resilience` | – | The defaults of `StorytellerResilienceOptions`: 3 retries of `GET` from 500 ms, an attempt timeout of 5 s, and a circuit breaker. |
| `Placement` | – | `BeforeEnvironmentVariables`. |
| `CacheProtector` | – | None. |
| `LoggerFactory` | – | None: silent until `AddStorytellerConfiguration()` hands over the container's factory. |
| `TimeProvider` | – | `TimeProvider.System`. It drives the poll timer, the token cache, the secret refresh and the status times. |
| `FileSystem` | – | A `FileSystem` over `System.IO`. |
| `ConfigurePrimaryHandler` | – | None. Configures the `SocketsHttpHandler` of the API and token requests, for proxies and TLS settings. |

- **Durations** are written `500ms`, `30s`, `5m`, `1h`, as plain seconds (`30`), or as `TimeSpan` text (`00:00:30`).
- **Values** are trimmed, and an empty variable is unset.
- **Validation** runs once, when the source is added. The message names the variable or the property and never prints a value that could be secret; an invalid endpoint is not echoed, because it may carry user info.
- **`Resilience.AttemptTimeout`** is 5 s by default, because a cold Functions host, or a resolved read that calls Key Vault, often takes several seconds.

### Credentials

`StorytellerCredential.FromEnvironment()` reads the names of the shared integration contract. `sform` reads its machine credential with the same contract of the SDK and prints these names in `sform account machine create`, so one set of variables drives `sform` and the provider. The older `sform` names are deprecated, but still read when the canonical name is unset.

| Variable | Deprecated | Meaning |
| --- | --- | --- |
| `STORYTELLER_ENDPOINT` | `STORYTELLER_API_URL` | The API base URL, `/api` included |
| `STORYTELLER_API_KEY` | – | The structured `2s.` API key, sent as `Authorization: ApiKey`. A leading `ApiKey ` is removed. |
| `STORYTELLER_CLIENT_CERTIFICATE` | `STORYTELLER_CERTIFICATE_FILE` | The machine's PKCS#12 file (`.pfx`). It needs a private key. |
| `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` | `STORYTELLER_CERTIFICATE_PASSWORD` | Its password |
| `STORYTELLER_CLIENT_ID` | – | The client id of a `ClientCredentials` machine, or the machine id of a `Federated` one |
| `STORYTELLER_CLIENT_SECRET` | – | The client secret of a `ClientCredentials` machine |
| `STORYTELLER_TOKEN_URL` | – | The token URL that `machine create` printed. Without it, only an AuthKit client id (`client_…`) works, through discovery. |
| `STORYTELLER_TOKEN_SCOPE` | – | The token scope of client credentials, or the Storyteller scopes a federated exchange asks for |
| `STORYTELLER_TOKEN_FILE` | `STORYTELLER_SUBJECT_TOKEN_FILE`, for a federated machine only | Alone: a file with a bearer token, read again before every request. With `STORYTELLER_CLIENT_ID` and no client-credentials variable: the platform token of a federated machine, read again before every exchange. `STORYTELLER_SUBJECT_TOKEN_FILE` is never a bearer token file: set, it makes a federated machine, which needs `STORYTELLER_CLIENT_ID`. |
| `STORYTELLER_SUBJECT_TOKEN_TYPE` | – | A federated machine, optional: `jwt` (the default), `id_token`, `access_token`, or the full URN |
| `ACTIONS_ID_TOKEN_REQUEST_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN` | – | Set by GitHub Actions in a job with `permissions: id-token: write`. Not credential variables: they only give a federated machine without a token file its platform token, see below. |

| Kind | Variables |
| --- | --- |
| API key | `STORYTELLER_API_KEY` |
| Certificate | `STORYTELLER_CLIENT_CERTIFICATE` and, when the file has one, its password; alone or with an API key |
| Client credentials | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, and usually `STORYTELLER_TOKEN_URL` and `STORYTELLER_TOKEN_SCOPE` |
| Federation | `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_FILE`, without a client-credentials variable, or `STORYTELLER_CLIENT_ID` in a GitHub Actions job; optionally `STORYTELLER_SUBJECT_TOKEN_TYPE` and `STORYTELLER_TOKEN_SCOPE` |
| Token file | `STORYTELLER_TOKEN_FILE` without `STORYTELLER_CLIENT_ID` |

- **Deprecated names.** A name and its deprecated name set to different values are an error that names both. The same value in both is accepted.
- **One kind.** Variables of two kinds at once are an error that names them. So is `STORYTELLER_CLIENT_ID` alone (or with `STORYTELLER_TOKEN_SCOPE`), unless it federates through GitHub Actions; the message names both ways, a client secret, or a token file or a GitHub Actions job.
- **GitHub Actions.** In a job with `permissions: id-token: write`, `STORYTELLER_CLIENT_ID` without a token file federates with the job's ID token when the id is a GUID, as every federated machine id is, or, for any id, together with `STORYTELLER_SUBJECT_TOKEN_TYPE`. The credential asks the runner for a new ID token before every exchange, for the audience of the token endpoint of the project of `Key`, built from `Endpoint` as written. The GitHub variables alone mean anonymous requests. A runner that is unreachable or answers `408`, `429` or `5xx` is a transient failure, so the cache entry or `Optional` applies; a runner that refuses (a redirect, another `4xx`, or no token) is definitive, like a refused exchange. See the [SDK readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md#github-actions).
- **Not read:** `STORYTELLER_PROJECT` and `STORYTELLER_VIEW`, which `STORYTELLER_KEY` replaces. `sform` reads all three: a machine targets the project and view of `STORYTELLER_KEY` when `-p` and `STORYTELLER_PROJECT` are not set, so a job that sets the key once runs `sform` in the project its configuration belongs to (see [Machine sign-in for pipelines](authentication.md#machine-sign-in-for-pipelines)).
- **Code wins.** A `Credential` set in code replaces the credential variables; they are not read.
- **Binding.** The credential, from the environment or from code, is bound to the validated `Endpoint`, the organization and project of `Key`, `ConfigurePrimaryHandler`, `TimeProvider` and `Resilience`. A value set in the credential's own options wins; the binding fills what they leave unset. A key set in code therefore also decides the project at whose token endpoint a federated machine exchanges its token, unless the credential's own options name the organization and project.

The token cache, the renewal after a `401` and AuthKit discovery are described in the [SDK readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md#machine-credentials).

### Precedence and placement

`AddStoryteller` puts the source among the sources of the builder:

| `Placement` | Where | Precedence, lowest first |
| --- | --- | --- |
| `BeforeEnvironmentVariables` (default) | In front of the last environment-variables source without a prefix | `appsettings*.json` < Storyteller < environment variables < command line |
| `Last` | Appended | every source added before, environment variables and command line included < Storyteller |

- **No environment source.** A builder without an unprefixed environment-variables source (an empty `ConfigurationBuilder`, or `DisableDefaults`) gets the source appended, also with `BeforeEnvironmentVariables`.
- **Strict mode.** With `Last`, nothing local overrides the platform. Sources added after `AddStoryteller` still come after it.
- **Cost of the insert.** On a `ConfigurationManager`, inserting a source builds and loads every source added before it again. Storyteller itself is not read again: its state lives in an engine that every build shares. Network-backed providers added earlier (Key Vault, App Configuration) are read again, so call `AddStoryteller` before them. They are appended after the command line either way, so they still override Storyteller.

## Startup

The first `Load()` of a source runs the startup read. It blocks the caller, runs off any `SynchronizationContext`, and ends within `ReadTimeout`. Concurrent first loads share one startup.

1. Read the cache entry of the configuration, when there is a `CacheDirectory`.
2. Read the document. The `ETag` of a [protected](#protection) cache entry is sent, and a `304` applies that entry.
3. Apply it: flatten, validate, and resolve references.

| Outcome | Required (`Optional = false`) | Optional |
| --- | --- | --- |
| Applied | Start | Start |
| Transient failure, usable cache entry | Start from the cache, degraded | Start from the cache, degraded |
| Transient failure, no usable cache entry | Throw | Start empty, degraded |
| Definitive failure, withheld secrets, or an invalid document | Throw | Throw |
| Resolver failure, transient | Throw | Start empty, degraded |
| Resolver failure, definitive | Throw | Throw |

- **Optional is about reachability only.** `STORYTELLER_OPTIONAL` starts without the platform when it is unreachable and nothing is cached. A wrong key (`404`), a refused credential (`401`), withheld secrets and an invalid document fail an optional provider too.
- **A usable cache entry** is one that applies, references included. The cache holds the document before resolution, so an entry whose references cannot be resolved now is not used. The text of a reference is never applied in place of its secret. The cache stands in for an unreachable platform, not for a document that failed to apply, and its fallback runs within what is left of `ReadTimeout`.
- **Throwing.** A required provider throws `StorytellerConfigurationException` out of `Load()`, so building the configuration fails and the application does not start. A `ConfigurationManager` is not usable after a `Load()` threw, so do not catch the exception and carry on.
- **Retry.** A failed startup is not remembered: the next `Load()` runs it again.
- **Degraded starts poll**, so the provider recovers when the platform answers.
- **Later loads** publish the current document without a request. `IConfigurationRoot.Reload()` therefore does not read the platform; the refresher does.

### Failure classes

| Class | Failures |
| --- | --- |
| Transient | A network error, an attempt timeout, `ReadTimeout`, an open circuit; `408`, `424`, `429` and `5xx` except `501` and `505`; a `2xx` whose body does not parse; a `304` to a read that sent no tag the provider can apply; a credential that is `Unavailable`, or that throws anything but `StorytellerAuthenticationException`; a resolver failure marked transient |
| Definitive | `400`, `401`, `403`, `404` (with or without a body, `AnnotationDeleted` included), `422`, `501`, `505`, a redirect (never followed), and any other `4xx`; a credential failure with the reason `Configuration` or `Rejected`; an invalid document; any other resolver failure |
| Withheld | `X-2S-Secrets-Withheld` on a resolved read, handled as definitive |

Every failure is a `StorytellerConfigurationException` with `Key`, `StatusCode`, `ErrorCode` (from the platform's `ErrorResponse`), `IsTransient`, and the inner exception. Messages name the key, the status and the fix, never a secret.

- **Withheld secrets fail loudly.** The platform sends the header to a machine with `Secrets.Read` that presented no certificate, under a project policy that delivers secrets only over mTLS. The header depends on the caller, not on the document, so such a machine fails even on a document without secrets. The message names the fix: present the machine certificate (`STORYTELLER_CLIENT_CERTIFICATE`), or remove `Secrets.Read` from the machine.
- **Unbound bindings.** A machine without `Secrets.Read` gets secret bindings as their `@…` expressions, without the header. In `Resolved` mode the provider logs one warning per applied document, with the count and up to 20 JSON paths of the strings that start with `@`, never their values. The strings are applied as written.

## Flattening

The `Content` object of the configuration becomes `IConfiguration` keys through `DocumentFlattener.Flatten` of the shared library `42.Platform.Storyteller.ConfigurationFormats`, the same rules that the `flat` format and the other [configuration formats](configuration-formats.md) apply:

- Properties join with `:`, and array items with their index: `db:hosts:0`.
- A string is its value. A number or a boolean is its JSON text: `true`, `5432`, `0.5`.
- `null`, an empty object and an empty array make no key.
- Property names keep their casing.

A document is invalid, and never applied, when:

- its content is not a JSON object;
- a property name is empty or contains `:`;
- two keys differ only in case, because `IConfiguration` compares keys case-insensitively.

The error names the JSON path, for example `$.db['a:b']`, and never a value. A bracketed name in a path has `\` and `'` escaped with a backslash, its C0 control characters (U+0000 to U+001F) and DEL written as `\n`, `\r`, `\t` or `\uXXXX`, and `#` written as `\u0023`; every other character, C1 controls and U+2028 and U+2029 included, is kept. A path therefore never holds a CR, an LF or a `#`, so a crafted name cannot start a new line in a log or an error message, nor a workflow command of GitHub Actions (`##[command]`, which the runner finds anywhere in a line). Every path in the provider's warnings and errors, of unbound bindings and secret references too, is written the same way. `Configuration/test/Vectors/flattening.json` pins every rule in a shape that a provider in another language can reuse; the library's tests run it.

The SDK reads numbers as `double`, so `1.50` arrives as `1.5`. Binding to `decimal` or `double` is not affected.

## Refresh

A run is the startup, a timer refresh, a `TryRefreshAsync` or a switch. Runs never overlap, and each is bounded by `ReadTimeout`.

1. **Read** with the stored `ETag`.
2. **`304`.** Nothing is applied, unless the resolved references are older than `SecretRefreshInterval` (see [Substitution](#substitution)). A `no-store` answer deletes the plain cache entry.
3. **`200`.** Flatten, validate and resolve references.
4. **Swap** in the new document. Readers see the previous data until then; a run never blocks a reader.
5. **Notify.** Every configuration root built from the source gets the new data and raises its change token. `IOptionsMonitor<T>` rebinds on that thread.
6. **Cache.** Write the cache entry, once every root took the document.

- **Timer.** One-shot, armed again after each run. Each delay is `RefreshInterval × (0.9 + 0.2 × r)`, with `r` random in [0, 1), so a fleet started together drifts apart. `RefreshInterval = 0` starts no timer; `TryRefreshAsync` still works.
- **No change.** A `200` with a new tag but the same keys and values reloads nothing.
- **Failures** keep the applied document, log a warning without values, and count in the [status](#status).
- **Notifications** run one at a time, so a reload error belongs to the document of its notification.

### A document that does not bind

When a reload throws in step 5, the document did not bind. Usually that is an `IOptionsMonitor<T>` rebind that failed validation (`ValidateDataAnnotations`, `Validate`) or a conversion; any change callback that throws counts the same. The provider then:

- restores the previous document as a new snapshot and notifies again, so the options are rebuilt from valid data;
- records the error, and the status stays degraded. The error names the exception types only, because a binder's message may quote the value that did not convert;
- keeps the rejected `ETag`. Later polls send it, so an unchanged document answers `304` and is not applied and rolled back every interval. A new document on the server is tried again;
- writes no cache entry for the rejected document.

A rotated secret that does not bind, the same document with a secret resolved again on a `304`, is rolled back the same way. The provider keeps the rejected values instead of a tag: a `304` whose references resolve to the same values applies nothing, and the status stays degraded. A value that rotates again, or a new document, is applied.

A rollback never undoes a newer document that another run swapped in meanwhile. If the restored document does not bind either, the error is logged and the restored document stays.

### The refresher

`IStorytellerConfigurationRefresher` controls one source. It is the same instance however often the builder builds. Get it from `AddStoryteller(configure, out var refresher)`, or from the container after `AddStorytellerConfiguration()`:

- `IStorytellerConfigurationRefresher` resolves to the refresher of the only Storyteller source in the application's `IConfiguration`. None or several throw `InvalidOperationException`.
- `IStorytellerConfigurationRefresherProvider.Refreshers` lists every source. It walks `IConfigurationRoot.Providers` on each access, chained configurations included, so it follows a rebuilt configuration.

| Member | Does |
| --- | --- |
| `ActiveKey` | The full key of the applied document |
| `Status` | The [status](#status) |
| `TryRefreshAsync(cancellationToken)` | Reads now and returns whether the run succeeded |
| `SwitchKeyAsync(key, cancellationToken)` | Applies another configuration of the same project |

**`TryRefreshAsync`**

- It never throws for a failed read; it returns `false`. It also returns `false` before the first `Load()` and after the source stopped.
- Concurrent callers share a run that has not started yet. A run that has already sent its request is never joined, so every call sees a read made after the call.
- The task completes once `IConfiguration` and `IOptionsMonitor<T>` show the result. The run belongs to every caller, so a caller's token ends only its own wait.

**`SwitchKeyAsync`** changes the active configuration, and the whole configuration reloads.

- `key` is a full key, or an annotation key in the active organization, project and view.
- The organization and project must stay those of the configured key, because a machine credential belongs to one project. Anything else throws `ArgumentException`. The mode stays.
- It waits for a running run to finish its read and swap, without cancelling it, then reads the new configuration. The `ETag` of the new key's protected cache entry is sent, and a `304` applies that entry.
- The new key becomes active with the swap, so a change callback sees the new `ActiveKey` together with the new data. The caller's token is honoured until the swap.
- A failure throws `StorytellerConfigurationException`, and the previous key and document stay. A new document that does not bind throws too, and the rollback restores the previous key and document.
- A switch to the active key is a refresh that throws on failure.
- Before the first `Load()` it throws `InvalidOperationException`; during the startup it waits for it. After the source stopped it throws `InvalidOperationException`.
- A switch lasts for the process. A restart starts from the configured key.

**Calls from change callbacks.** `TryRefreshAsync` and `SwitchKeyAsync`, called from a change callback that a notification of the same source runs (`IOptionsMonitor<T>.OnChange`, or `ChangeToken.OnChange` on the root), complete at their swap, not after the notification. The callback runs inside the current notification, and the new document is notified once that ends. Even `.GetAwaiter().GetResult()` in a callback does not deadlock. A switch from a callback whose document then does not bind does not throw: it is rolled back, and the rejection shows only in the status. So a `true` from a nested `TryRefreshAsync`, or a nested `SwitchKeyAsync` that returns, means the document was read and swapped in, not that it bound; check `Status` after the notification when that matters.

### Status

`Status` is an immutable `StorytellerConfigurationStatus`, for health checks and diagnostics.

| Member | Meaning |
| --- | --- |
| `ActiveKey` | The full key of the applied document |
| `ETag` | The tag the next poll sends, if any: none after a start from a plain cache entry or an empty start, the rejected tag after a rollback |
| `Origin` | `Platform`; `Cache` after a start from the cache; `Empty` after an optional start without the platform |
| `LastAppliedAt` | When a document was last applied to `IConfiguration` |
| `LastSucceededAt` | When the platform last answered a read with a `200` or a `304` |
| `LastError`, `LastErrorAt` | The last failure. It stays after a success; `ConsecutiveFailures` tells whether it is current. |
| `ConsecutiveFailures` | Failed runs since the last successful one |
| `TotalFailures` | All failed runs |
| `IsDegraded` | The origin is `Cache` or `Empty`, the last run failed, or the platform's document, or a secret value resolved for it again, is one that did not bind |

- A `304` that answers a rejected `ETag` sets `LastSucceededAt`, because the platform answered, but the status stays degraded.
- After a start from the cache or an empty start, the first `200`, or a `304` that confirms a protected cache entry, turns the origin to `Platform`.
- `ToString()` prints `LastError` by its message only.

### Logging

- **Category.** `_42.Platform.Storyteller.Sdk.Configuring.StorytellerConfigurationProvider`.
- **Hand-over.** The provider starts before the container and its logger factory exist. `AddStorytellerConfiguration()` registers an `IHostedLifecycleService` that hands the container's `ILoggerFactory` to every source at `StartingAsync`, unless `options.LoggerFactory` is set. A source that started degraded logs its status then, so a start from the cache is reported although it happened before any logger existed. The warnings of the applied document (unbound bindings, unresolved references) are logged again then too, because a document that does not change is applied only once, at the startup, and so is the notice of a `no-store` answer that is not cached on disk. At `StoppedAsync` the factory is taken back, because the host disposes it before the configuration.
- **Information:** a loaded or applied version, and once per configuration, a `no-store` answer that is not cached on disk.
- **Warning:** a degraded start, a failed run, a document that did not bind, unbound bindings, unresolved references, and cache problems.
- **Error:** a restored document that does not bind either, and an unexpected exception in a refresh.
- No message contains a configuration value or a resolved secret.

### Lifetime

- **One engine per source.** `AddStoryteller` creates it. Every `Build()` makes a new provider attached to it, so a source has one poller and one cache writer however often it is built.
- **A copy per root.** Each provider holds its own copy of the data. `IConfiguration[key] = value` on one root does not reach another, and lasts until the next applied document.
- **Stop.** Disposing a root detaches its provider. When the last one detaches, which every host shutdown does, the source stops: the timer goes, the run in flight is cancelled, and the connections close. A later `Build()` starts it again.
- **Temporary roots.** A root that is built and never disposed keeps its provider attached, and the source keeps polling. Dispose temporary roots.

## Transport

The provider builds its own `HttpClient`, not one of `AddStorytellerSdk`:

```text
StorytellerCredentialHandler        Authorization, one renewal after a 401
  └─ ResilienceHandler              retries of GET, circuit breaker, attempt timeout
       └─ SocketsHttpHandler        client certificate, no redirects, connections renewed every 5 minutes
```

- **Why its own.** The provider runs before the container exists. A client from the container would also collide with the application's own `ISdkConfiguration`, and get a second resilience handler from Aspire's `ConfigureHttpClientDefaults(AddStandardResilienceHandler)`. `IStorytellerDocuments` uses the same transport.
- **Timeouts.** `HttpClient.Timeout` is infinite. `ReadTimeout` bounds a run, and `Resilience.AttemptTimeout` one attempt.
- **Proxies.** `ConfigurePrimaryHandler` runs on the `SocketsHttpHandler`, and the client certificate and `AllowAutoRedirect = false` are set again after it. Token and discovery requests of the credential get the same callback, without the certificate.
- **Local host.** For an `http` endpoint on `localhost`, the certificate also goes in `X-ARR-ClientCert`, as `sform` sends it to the local Functions host.
- **Reads.** `GetConfigurationConditionalAsync` (`Effective`) and `GetResolvedConfigurationConditionalAsync` (`Resolved`) of the C# SDK.
- **Identity.** A tag is sent only for the identity it came with: the endpoint, organization, project, view, annotation key (type code lower-cased) and mode.

## Last-known-good cache

With a `CacheDirectory`, the provider keeps the last applied server document of each configuration on disk, for a start while the platform is unreachable.

### Entries

- **One file per cache identity:** the identity of the document plus a fingerprint of the credential, so an entry written under one principal is never applied under another. The file name is the first 32 hex characters of SHA-256 over the cache identity, with the extension `.cache`.
- **Content:** the format version, the identity, the `ETag`, the time it was stored, the `Cache-Control` of the answer, and the server's document **before local reference resolution**. A secret that the application resolved never reaches the disk. A secret that the platform resolved does, under the rules below.
- **Parsing** uses `DateParseHandling.None`, as the SDK's reads do, so a document flattens to the same keys from the cache as from the platform.
- **Writing.** An entry is written after a `200`, once every root took the document, and only if no newer document replaced it meanwhile.

### `no-store`

The platform answers a resolved read with `Cache-Control: no-store` when the caller may read secrets, also for a document without secrets. Every other read gets `private, no-cache`.

Without a `CacheProtector`:

- In `Resolved` mode, an entry is written only when `Cache-Control` is present and has no `no-store`. A missing header counts as `no-store`: the platform always sends one of its two values, so its absence means that something in between changed it.
- In `Effective` mode, an entry is written unless the answer has `no-store`.
- A `no-store` answer, a `200` or a `304`, deletes the configuration's plain entry (RFC 9111, section 5.2.2.5), and is logged once per configuration. A protected entry, which another process with a protector may share, stays.
- A caller that may read secrets therefore gets no disk cache in `Resolved` mode.

With a `CacheProtector`, every entry is protected and written, whatever its `Cache-Control`.

### Protection

`IStorytellerCacheProtector` encrypts the entries:

```csharp
public interface IStorytellerCacheProtector
{
    byte[] Protect(byte[] plaintext, string identity);

    // Throws when the data was not protected for this identity, or was changed.
    byte[] Unprotect(byte[] protectedData, string identity);
}
```

- **Authenticated encryption.** The `identity` is the purpose or the associated data, so a blob copied into the file of another configuration does not unprotect.
- **One blob.** The whole serialized entry is protected, behind a format marker outside the blob.
- **No downgrade.** A reader with a protector ignores plain files, and a reader without one ignores protected files, each with a warning.
- **Not shipped.** The package has no protector. The application chooses one: ASP.NET Core Data Protection, DPAPI, or a key mounted from a secret store.

A protector over ASP.NET Core Data Protection. The provider runs before the container exists, so it uses a standalone `DataProtectionProvider`, not the container's `AddDataProtection()`:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.AspNetCore.DataProtection;

public sealed class DataProtectionCacheProtector : IStorytellerCacheProtector
{
    private const string Purpose = "42.Platform.Storyteller.Configuration.Cache";

    private readonly IDataProtectionProvider _provider;

    public DataProtectionCacheProtector(IDataProtectionProvider provider)
    {
        _provider = provider;
    }

    // The identity is a sub-purpose: the blob of one entry does not unprotect as another.
    public byte[] Protect(byte[] plaintext, string identity)
    {
        return _provider.CreateProtector(Purpose, identity).Protect(plaintext);
    }

    public byte[] Unprotect(byte[] protectedData, string identity)
    {
        return _provider.CreateProtector(Purpose, identity).Unprotect(protectedData);
    }
}
```

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

// The key ring: outside the cache directory, on storage that survives a restart, and encrypted at rest.
var keyRing = new DirectoryInfo("/var/lib/invoicing/data-protection");
var keyRingCertificate = X509CertificateLoader.LoadPkcs12FromFile("/etc/invoicing/key-ring.pfx", password: null);
var dataProtection = DataProtectionProvider.Create(keyRing, setup => setup
    .SetApplicationName("invoicing")
    .ProtectKeysWithCertificate(keyRingCertificate));   // or, on Windows, .ProtectKeysWithDpapi()

builder.Configuration.AddStoryteller(options =>
{
    options.CacheDirectory = "/var/cache/invoicing/storyteller";
    options.CacheProtector = new DataProtectionCacheProtector(dataProtection);
});
```

- **Outside the cache directory.** Whoever can read the cache directory must not find the keys next to the entries.
- **Protected at rest.** A key ring in plain files is as readable as the entries it protects. `ProtectKeysWithCertificate`, or `ProtectKeysWithDpapi` on Windows, encrypts it.
- **Not ephemeral.** A key ring that does not survive a restart, such as `EphemeralDataProtectionProvider` or a directory in a container's writable layer, makes every entry unreadable after the restart. The provider then ignores the entries with a warning, and a start while the platform is unreachable fails, or starts empty when optional.
- **Shared directories.** Every process that shares the cache directory needs the same key ring and application name.
- **Package.** `DataProtectionProvider` is in `Microsoft.AspNetCore.DataProtection.Extensions`, which is part of the ASP.NET Core shared framework. A console application adds the package.

### Trust

- **A protected entry** is authenticated. Its `ETag` is sent on the first read of a startup or a switch, and a `304` applies it.
- **A plain entry** is not: whoever can write the directory could keep its tag and change its document. Its `ETag` is never sent. It is used only as the fallback after a transient failure at startup, and the first poll after a start from it is unconditional.

### Files

- **Unix.** The directory is created with mode `0700` and the files with `0600`. An existing directory that its group or others can write is used, with a warning.
- **Windows.** The directory inherits its ACL. Use a per-user location, for example under `%LOCALAPPDATA%`.
- **Writes** go to a unique temporary file in the same directory (`{name}.{guid}.tmp`), which then replaces the entry. Reads let other processes write and delete meanwhile, so several processes can share a directory.
- **Clean-up.** Temporary files older than one hour are deleted when the directory is first used.
- **Errors.** An IO error is logged and changes nothing else: the applied document and the result of the run stay, and the next applied document tries again.
- **Bad files.** A file that does not parse, cannot be unprotected, or names another identity is ignored, with a warning.

### The fingerprint of a credential

The fingerprint is not secret. It is the type of the credential plus a hash over its `ToString()`, the thumbprint of its client certificate, and the machine segment of an API key (`2s.{machine}.{secret}`). The SDK's credentials print their principal in `ToString()` (the client id, the token file, the subject token path, the certificate thumbprint) and never a secret.

A credential of the application, a subclass of `StorytellerCredential`, is told apart by its type, its `ToString()` and its certificate. The machine of an `ApiKey` authorization counts only when `GetAuthorizationAsync` completes synchronously: the provider does not wait for a token, because the token request may be what fails when the cache is needed. **A credential that fetches its key or token asynchronously, for example from a vault, must name its principal in `ToString()`, without secrets.** Otherwise all its principals share one cache entry per configuration.

```csharp
public override string ToString()
{
    return $"VaultApiKeyCredential {{ Machine = {_machineId} }}";   // never the key
}
```

## Secret references

A key/value pair of a [reference content type](key-values.md#references) stores a reference to a secret, and a binding puts that text into the resolved document as a plain string. The platform never resolves it. The provider resolves it locally, with the application's own identity, and applies the secret in its place.

### The resolver contract

```csharp
public interface ISecretReferenceResolver
{
    bool CanResolve(string value);                                        // the whole, trimmed string value
    ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken);
}
```

- **Declining.** `CanResolve` returning `false` leaves the value to the next resolver, or as it is.
- **Failures.** `ResolveAsync` throws `SecretResolutionException`, with `IsTransient`, `VaultName` and `SecretName`, never a value.
- **Several resolvers.** `options.ReferenceResolver` takes one resolver. `SecretReferenceResolver.FirstOf(a, b, …)` combines several: the first that accepts a value resolves it.
- **Neutral.** The contract knows no vault. AWS, Google or HashiCorp resolvers can implement it.

### The platform's forms

`SecretReference.TryParse(value, out reference)` parses the forms that the platform accepts for a reference pair, for resolvers and diagnostics:

| `Form` | Rule |
| --- | --- |
| `Shorthand` | `kv://{vault}/{name}[/{version}]`: vault `[A-Za-z0-9-]{3,24}`, name `[A-Za-z0-9-]{1,127}`, version 32 hex characters. The scheme is lowercase, and there is no trailing slash. |
| `SecretUri` | `https://{host}/secrets/{name}[/{version}][/]`, with `/secrets/` in lowercase. The host is a DNS name, and so is its ASCII (IDN) form, without an empty label or a trailing dot: a Unicode host whose fullwidth delimiters or full stops map to `/`, `@`, `:` or `.` is not one. No user info, port (the default `443` written out included), query, fragment, `%` escape or dot segment. |
| `AppConfiguration` | A string that holds the JSON object `{"uri": "<secret URI>"}`, with exactly one string `uri`; other properties are ignored. |

- **Whole values.** The whole trimmed value must match. A reference inside a longer string is not recognized.
- **Members:** `Original`, `Form`, `VaultName` (the shorthand only), `VaultUri` (`https://{host}/`; `null` for the shorthand), `Name`, and `Version` (`null` for the latest). `ToString()` returns `Original`.
- **Stricter than the server**, which accepts a secret URI with a port, a query or user info. A value the server stored as a reference but this parser rejects is not a reference to the provider: the Key Vault resolver declines it, no warning names it, and unless another resolver accepts it, it is applied as written (see [Limits](#limits)).

### Substitution

- **When.** In `Resolved` mode, with a `ReferenceResolver`, after flattening and validation and before the swap. `Effective` mode resolves nothing.
- **What.** Every non-empty string value of the document is offered to `CanResolve`, trimmed; property names never are. A value that the resolver accepts is replaced by the resolved value.
- **Failure.** A failure fails the apply, with the class of the `SecretResolutionException` (see [Startup](#startup) and [Refresh](#refresh)). There is no fallback to the text of the reference.
  - An `OperationCanceledException` that the provider did not request, a timeout of the resolver's own, is transient.
  - A `CanResolve` that throws, a `null` value and any other exception are definitive. The message names the JSON path and the exception type; the resolver's message stays in the inner exception.
- **Unclaimed references.** A `kv://` shorthand that no resolver accepts fails the apply, because the shorthand is the platform's own and unambiguous. Any other value that no resolver accepts stays as it is. If it parses as another `SecretReference` form, a warning names its path and host.
- **No resolver.** Values that parse as a `SecretReference` stay as written, with one warning per applied document that lists their paths.
- **Memory cache.** A resolved value is kept per value text, with its resolution time, and reused until `SecretRefreshInterval`, also by a changed document. Failures are never cached. The provider shares the values among the documents it applies. `IStorytellerDocuments` keeps them in its cached documents only, so a document that is evicted or expires takes its secrets with it, and a changed document reuses the values of its own previous version.
- **Rotation.** On a `304`, references older than `SecretRefreshInterval` are resolved again, and the configuration reloads only when a value changed. A rotated secret therefore reaches the application without a change of the document, at the first poll after `SecretRefreshInterval` has passed. With `RefreshInterval = 0` that happens only on `TryRefreshAsync`. A rotated value that does not bind is rolled back once, and kept out until it rotates again (see [A document that does not bind](#a-document-that-does-not-bind)).
- **Secrecy.** Resolved values are never logged, written to disk, or put in an exception or the status. Logs name the path, the vault and the secret.
- **Scope.** Only Storyteller documents are substituted: the provider's and those of `IStorytellerDocuments`. A reference in an environment variable or `appsettings.json` stays literal; resolving it would make the provider read secrets for sources it does not own.

### The Key Vault resolver

```csharp
options.ReferenceResolver = new KeyVaultReferenceResolver(new DefaultAzureCredential());
```

`KeyVaultReferenceResolver(TokenCredential credential, KeyVaultReferenceResolverOptions? options = null)` resolves the three forms in Azure Key Vault.

| `KeyVaultReferenceResolverOptions` | Default | Meaning |
| --- | --- | --- |
| `DefaultVaultSuffix` | `vault.azure.net` | The suffix a `kv://{vault}/…` shorthand expands to, `https://{vault}.{suffix}/`, as on the server. It is always allowed. |
| `AllowedHostSuffixes` | `vault.azure.net` | The hosts a secret URI may name. Sovereign clouds are added here, and the host of a local emulator (see below). |
| `AllowedVaults` | empty: any vault | Vault names. When set, every other vault is declined. The vault of a secret URI is the first label of its host, so `myvault` admits both `kv://myvault/x` and `https://myvault.vault.azure.net/secrets/x`. |
| `SecretClientOptions` | – | Passed to every `SecretClient` that the resolver creates itself. Ignored when `SecretClientFactory` is set; the factory configures its own clients. |
| `SecretClientFactory` | – | `Func<Uri, SecretClient>`, called once per vault with `https://{host}/`, in place of the default factory: for tests and custom clients. |

- **Host match.** Case-insensitive on the IDN host: equal to a suffix, or ending with `.` and a suffix. `evilvault.azure.net` does not match `vault.azure.net`.
- **Options.** The suffixes and vault names are copied when the resolver is created. `SecretClientOptions` is kept by reference and passed to each `SecretClient` it creates, so a later change to it affects clients created afterwards. Suffixes are trimmed, a leading dot is removed, and an IDN suffix becomes punycode. An invalid suffix or an empty vault name throws `ArgumentException`. Vault names compare without case, as Key Vault does.
- **Declined values** go to the next resolver of a `FirstOf`. A `kv://` shorthand that no resolver accepts, for example one of a vault outside `AllowedVaults`, fails the apply (see [Substitution](#substitution)).
- **Clients.** One `SecretClient` per vault origin.
- **Read.** `GetSecretAsync(name, version)`; the latest version when the reference names none.
- **A local emulator.** A secret URI names a host on the default `https` port: a reference with a port is not a reference (see [The platform's forms](#the-platforms-forms)), and the shorthand expands to `https://{vault}.{DefaultVaultSuffix}/` without one. For an emulator on another port, store the reference without the port, add its host to `AllowedHostSuffixes`, and put the port back in the client. [Lowkey Vault](https://github.com/nagyesta/lowkey-vault), the emulator of the Aspire host, also needs what the platform's own Lowkey Vault clients use (see [Key/value pairs](key-values.md#local-development)): a dummy token, no challenge resource check, and its self-signed certificate accepted. Aspire maps the emulator's `8443` to a host port of its choice, so the application reads the endpoint from its configuration, here `KeyVault:Endpoint`, set by the app host with `.WithEnvironment("KeyVault__Endpoint", keyVault.GetEndpoint("https"))`. For local development only:

  ```csharp
  var emulator = new Uri(builder.Configuration["KeyVault:Endpoint"]!);   // such as https://localhost:51786
  var credential = new EmulatorTokenCredential();
  var clientOptions = new SecretClientOptions
  {
      DisableChallengeResourceVerification = true,
      Transport = new HttpClientTransport(new HttpClientHandler
      {
          ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
      }),
  };
  var vaultOptions = new KeyVaultReferenceResolverOptions
  {
      // https://localhost/secrets/db-password is read from the port of the emulator.
      AllowedHostSuffixes = { "localhost" },
      SecretClientFactory = vaultUri => new SecretClient(new UriBuilder(vaultUri) { Port = emulator.Port }.Uri, credential, clientOptions),
  };

  builder.Configuration.AddStoryteller(options =>
  {
      options.ReferenceResolver = new KeyVaultReferenceResolver(credential, vaultOptions);
  });

  // Lowkey Vault checks that a bearer token is present and ignores its value.
  public sealed class EmulatorTokenCredential : TokenCredential
  {
      public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
          => new("lowkey-vault-emulator", DateTimeOffset.MaxValue);

      public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
          => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
  }
  ```

| Failure | Class |
| --- | --- |
| `408`, `429`, `5xx`, no answer (status 0), `HttpRequestException`, `IOException`, `TimeoutException`, a cancellation the caller did not request; a token request that failed the same way (see Credentials below) | Transient |
| `401`, `403` (also a disabled secret), `404` and any other status; any other credential failure; a secret without a value | Definitive |

- **After the retries of the client.** A `SecretClient` retries network errors and timeouts itself (three times by default). When it gives up, it throws the failures of every attempt together, and the resolver classifies them together: transient when every attempt failed transiently. The message names the number of attempts and describes the last one.
- **Credentials.** Azure.Identity throws `AuthenticationFailedException` when a token request fails. The resolver classifies its cause:
  - Entra answered: MSAL's `MsalServiceException` and its subclasses, by their `StatusCode`. `408`, `429` and `5xx`, an Entra outage or throttling, are transient; `400`, `401` and the other answers, such as a wrong client secret, are definitive.
  - Entra did not answer: the network failures of the credential's retries, transient as above. Client-secret, certificate, workload identity and other federated credentials come out so when their authority is unreachable.
  - `CredentialUnavailableException` (a subclass of `AuthenticationFailedException`) is definitive: a credential that cannot work in this environment. A managed identity endpoint that does not answer comes out so too, because Azure.Identity reports it as it reports a machine without a managed identity. An outage of the managed identity endpoint therefore fails the apply as definitive: an `Optional` provider throws at startup instead of starting empty (see [Startup](#startup)).
  - Any other credential failure is definitive.
- **Cancellation.** When the caller's token is cancelled, any failure comes out as `OperationCanceledException`.

The message names the vault host and the secret, never the value. Key Vault's own error text stays in the inner exception.

### Trust model

- **Who can read what.** Anyone who can write the configuration document, or a key/value pair that it binds, can make the application read any secret that the application's identity can read, and put it under a key of their choice.
- **Ambiguity.** An ordinary string with the shape of a reference is resolved too. The platform does not report which values came from reference pairs, so the provider cannot tell them apart.
- **Least privilege.** Set `AllowedVaults` to the vaults the application needs, and give the application's identity read access only to the vaults or secrets it needs (`Key Vault Secrets User` at the narrowest scope).

## Documents on demand

`services.AddStorytellerDocuments(configure)` registers `IStorytellerDocuments`, a singleton for a job that reads configurations while it runs. It uses the environment contract, the transport, the reader and the apply pipeline of the provider, without its poller and its disk cache.

```csharp
builder.Services.AddStorytellerDocuments();   // the same configure delegate as AddStoryteller, if there is one

public sealed class NightlyExport
{
    private readonly IStorytellerDocuments _documents;

    public NightlyExport(IStorytellerDocuments documents)
    {
        _documents = documents;
    }

    public async Task RunAsync(string customer, CancellationToken cancellationToken)
    {
        // An annotation key in the organization, project and view of STORYTELLER_KEY.
        var document = await _documents.GetAsync($"uxe.{customer}.invoicing.production.nightly-export", cancellationToken);
        var settings = document.Get<ExportSettings>("export");
    }
}
```

- **Options.** `Endpoint`, `Key`, `Mode`, `Credential`, `ReferenceResolver`, `SecretRefreshInterval`, `ReadTimeout`, `Resilience`, `LoggerFactory` (the container's when unset), `TimeProvider`, `FileSystem` (for credential files) and `ConfigurePrimaryHandler` apply. `RefreshInterval`, `Optional`, `CacheDirectory`, `CacheProtector` and `Placement` are ignored, from code and from the environment, even when their variables hold invalid values.
- **Registration.** The options are read and validated when `AddStorytellerDocuments` is called, and throw `StorytellerConfigurationException`. The first registration wins. An application that uses the provider too passes the same `configure` delegate to both.
- **`Key`** is optional. When set, its organization, project and view are the default scope of annotation keys.
- **A federated credential** exchanges its token at the token endpoint of the project its own options name (`FederatedTokenOptions.Organization` and `Project`), or else of the project of `Key`. A federated credential that names no project, such as one from the environment, makes every read fail without `Key`, with a definitive error that names `STORYTELLER_KEY`, and no request is sent.

| `IStorytellerDocuments` | Does |
| --- | --- |
| `GetAsync(key, cancellationToken)` | Reads in the mode of the options |
| `GetAsync(key, mode, cancellationToken)` | Reads in the given mode |

`key` is a full key, or an annotation key in the default scope. An annotation key without a default scope throws `ArgumentException`.

| `StorytellerDocument` | Content |
| --- | --- |
| `Key`, `Mode` | The full key and the read mode |
| `ETag`, `Version`, `Hash`, `Modified`, `Labels`, `Values` | From the platform's answer |
| `Content` | The `JObject` after reference substitution: the document as the job uses it |
| `Configuration` | The flattened keys, in a `ConfigurationRoot` over a memory source |
| `Get<T>(section)` | Binds the whole document, or a section of it, with `ConfigurationBinder` |

- **Revalidation.** Every call sends `If-None-Match`, because the platform answers `no-cache` or `no-store`. A `304` returns the document from memory, with references older than `SecretRefreshInterval` resolved again.
- **Sharing.** Concurrent calls for one configuration, the same full key and mode, share one request. The request is bounded by `ReadTimeout`, and a caller's token ends only its own wait.
- **Memory cache.** At most 1024 documents, each with a sliding expiration of twice `SecretRefreshInterval`, because they hold resolved secrets. A `401`, `403`, `404` or withheld secrets evict the configuration; other failures keep it. Nothing is written to disk.
- **Resolved values** live in the cached documents only, not in a cache of their own: a document that is evicted, expires or is disposed takes its secrets with it. A changed document reuses the fresh values of its previous version; two configurations that name the same secret resolve it each.
- **A document per call.** What a caller changes in `Content`, `Values`, `Modified` or `Configuration` changes neither the cache nor another caller's document. A `304` returns an equal document, not the same instance.
- **Failures** throw `StorytellerConfigurationException` with its class, withheld secrets in `Resolved` mode included. There is no last-known-good: the job decides what to do without its configuration. A reference that cannot be resolved again on a `304` throws too, and the document stays cached for the next call.
- **`Get<T>`** throws `StorytellerConfigurationException` when there is nothing to bind, so it never returns `null`, and when a value does not convert. The message names the key, the type and the section, not the value.
- **`ToString()`** prints the key, the mode and the version, never the content.
- **Logging.** The category is `_42.Platform.Storyteller.Sdk.Configuring.StorytellerDocuments`. The warnings about unbound bindings and unresolved references come once per `200`, with one debug line per `200`.
- **Disposal.** The container disposes the service, and with it its connections and its memory cache. A call after that throws `ObjectDisposedException`.

## Limits

- **Reference ambiguity.** In `Resolved` mode, every string with the shape of a reference is offered to the resolver, also one that did not come from a reference pair (see [Trust model](#trust-model)).
- **Floating-point text.** Numbers arrive as `double`, so `1.50` becomes `1.5`. A value whose text matters is written as a JSON string.
- **No disk cache for secret readers without a protector.** The platform answers `no-store` to every resolved read of a caller that may read secrets. Such a provider caches nothing on disk and cannot start from the cache, unless it has a `CacheProtector`.
- **Polls of documents with bindings.** A `d` tag is the same on every instance, but its dependency manifest is kept per instance, so a poll that reaches another instance resolves there once and still answers `304`. A document that reads Key Vault or JSON-e keeps a `b` tag, which differs between instances unless `ConditionalReads:EntityTagKey` is set; such a poll gets a `200` that reloads nothing, because the keys and values are the same (see [Conditional reads](inheritance.md#conditional-reads)).
- **Azure Functions trigger bindings** (`%Name%` in trigger attributes) are resolved by the Functions host from its app settings, not from the worker's `IConfiguration`. Storyteller values cannot drive them.
- **Substitution covers Storyteller documents only.** References in environment variables, `appsettings.json` or other providers stay literal.
- **References the parser rejects are plain text.** The platform accepts a secret URI with a port, a query or user info, or an IP host, as a reference pair. `SecretReference` does not, so such a value is offered to the resolver as an ordinary string: the Key Vault resolver declines it, no warning names it, and unless another resolver accepts it, it is applied as written. Store references without these parts; for an emulator on another port, see [The Key Vault resolver](#the-key-vault-resolver).
- **Temporary roots must be disposed.** A root that is never disposed keeps its source polling.
- **A reload from elsewhere can hide a rejection.** A change token delivers a change raised during its callback once that callback returns. When the application calls `IConfigurationRoot.Reload()`, or another provider of the same root reloads, while a Storyteller document is notified, that document's rebind error can surface in the other reload, and the document is kept instead of rolled back.
- **A switch lasts for the process.** A restart starts from the configured key.
- **No start without a key.** A job without a fixed key uses `IStorytellerDocuments`.
