# Hosting for .NET

The package `42.Platform.Storyteller.Hosting` adds the catalog to the [configuration provider](configuration-provider.md). A process reads who it is: the annotation of its key, its ancestors, and whether its subscription is in effect. A worker that serves every customer of a responsibility learns which subjects use it in a context, and keeps the resolved document and the named options of each of them in memory. Hosting uses the settings, credentials and transport of the provider, so one set of `STORYTELLER_*` variables drives both.

| Service | Does |
| --- | --- |
| `IExecutionContext` | Who am I: the annotation of `STORYTELLER_KEY` and its ancestors |
| `ISubscriptionCatalog` | One execution by its key, and the informer: the subscriptions of a responsibility in a context, as a stream of changes |
| `IStorytellerSubscriptions` | A hosted informer that keeps the document of every subscription in memory, with `IOptionsMonitor<T>.Get(subject)` over them |

The code is in `src/Platform/Storyteller/Hosting/src/`: the public types at its root, the registration (`Container/` and the extension classes), who am I (`Executions/`), the catalog (`Catalog/`), the reads (`Reading/`), the polling informer (`Informer/`), and the subscriptions with their named options (`Subscriptions/`).

The design and its decisions are in [the spec](specs/2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md). An Aspire AppHost passes the variables with [the Aspire integration](aspire.md).

## Package

| Package | Namespace | Contents |
| --- | --- | --- |
| `42.Platform.Storyteller.Hosting` | `_42.Platform.Storyteller.Sdk.Hosting` | `IExecutionContext`, `ISubscriptionCatalog`, `IStorytellerSubscriptions`, their snapshots, events and options |

- **Registration needs no `using`.** `services.AddStoryteller(…)`, `IStorytellerHostingBuilder`, `AddSubscriptions` and `AddSubjectOptions` are in `Microsoft.Extensions.DependencyInjection`; `builder.AddStoryteller(…)` on an `IHostApplicationBuilder` is in `Microsoft.Extensions.Hosting`.
- **The services and snapshots** need `using _42.Platform.Storyteller.Sdk.Hosting;`.
- **The options and errors** are the provider's: `StorytellerConfigurationOptions`, `StorytellerConfigurationException`, `StorytellerReadMode` and `StorytellerDocument`, in `_42.Platform.Storyteller.Sdk.Configuring`.
- **`AnnotationType` and `AnnotationStatus`** are in `_42.Platform.Storyteller`, from `Abstractions.Annotations`, which the package brings.
- **`AuditStamp`.** `Created` and `Modified` are the SDK's `AuditStamp`, in `_42.Platform.Storyteller.Sdk`. `_42.Platform.Storyteller` has an `AuditStamp` too, so a file that imports both namespaces names it qualified: `_42.Platform.Storyteller.Sdk.AuditStamp`.
- **Configuration comes with Hosting.** Hosting builds on internals of `42.Platform.Storyteller.Configuration`, and depends on the Configuration package built from the same commit. The two do not share a version number: each version counts the commits that changed its own project, so a Hosting `0.10.6-alpha` can depend on a Configuration `0.10.2-alpha`. Let Hosting bring Configuration, and do not reference or update `42.Platform.Storyteller.Configuration` on its own to another version than the one Hosting depends on. The dependency is not pinned exactly yet, so NuGet accepts a newer Configuration under Hosting, which can fail at run time with `MissingMethodException` or `TypeLoadException`.

## A service of one subject

A process that knows its key, here an execution, gets its configuration and who it is with one call:

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;

var builder = WebApplication.CreateBuilder(args);

// STORYTELLER_ENDPOINT, STORYTELLER_KEY and the variables of the machine's credential come from the environment.
// The provider loads the configuration of the key into IConfiguration; who am I, the catalog and the documents
// go into the container.
builder.AddStoryteller();

builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection("invoicing"));

var app = builder.Build();

// Whether the subscription of this process is in effect: its execution and its usage.
app.MapGet("/subscription", async (IExecutionContext self, CancellationToken cancellationToken) =>
{
    var context = await self.GetAsync(cancellationToken);
    return context.GetSubscriptionStatus(DateTimeOffset.UtcNow).ToString();
});

app.Run();
```

`builder.AddStoryteller(configure)` is the same as the two calls:

```csharp
builder.Configuration.AddStoryteller(configure);   // the provider, as in the provider's documentation
builder.Services.AddStoryteller(configure);        // Hosting
```

## A worker for every subject

A worker that holds a responsibility finds its subjects through the catalog. Its key is the responsibility's, and it subscribes to one context:

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// STORYTELLER_KEY=@house.main.default.rst.invoicing: the organization, project and view, and the responsibility.
builder.Services.AddStoryteller()
    .AddSubscriptions("production", options => options.WaitUntilReadyOnStart = true)
    .AddSubjectOptions<InvoicingOptions>("invoicing");

builder.Services.AddHostedService<InvoicingWorker>();

builder.Build().Run();

public sealed class InvoicingWorker : BackgroundService
{
    private readonly IStorytellerSubscriptions _subscriptions;
    private readonly IOptionsMonitor<InvoicingOptions> _options;

    public InvoicingWorker(IStorytellerSubscriptions subscriptions, IOptionsMonitor<InvoicingOptions> options)
    {
        _subscriptions = subscriptions;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Added for every current subscription first, then the changes as they happen.
        await foreach (var change in _subscriptions.WatchAsync(stoppingToken))
        {
            var subject = change.Subscription.Subject;

            if (change.Kind == StorytellerChangeKind.Removed)
            {
                StopTenant(subject);
                continue;
            }

            // The options bound to the subject's document, and the usage's values: the subject's plan.
            var options = _options.Get(subject);
            var plan = change.Subscription.Usage.Values.GetValueOrDefault("plan") as string;
            RunTenant(subject, options, plan);
        }
    }

    private void RunTenant(string subject, InvoicingOptions options, string? plan)
    {
        // Start the work of the subject, or apply its new options.
    }

    private void StopTenant(string subject)
    {
        // Stop the work of the subject.
    }
}
```

- `AddSubscriptions("production")` takes the responsibility from the key. `AddSubscriptions("invoicing", "production")` names it.
- `WaitUntilReadyOnStart` holds the host's start until the first poll and the first reads of the documents completed.
- A worker that also wants the configuration of its own key in `IConfiguration` calls `builder.AddStoryteller()` instead of `builder.Services.AddStoryteller()`, and adds to the builder it returns.

## Registration

`services.AddStoryteller(configure)` registers:

- `IExecutionContext` and `ISubscriptionCatalog`;
- `IStorytellerDocuments`, when nothing registered it before; an application that called `AddStorytellerDocuments` first keeps its own;
- the provider's refresher and logging, with `AddStorytellerConfiguration()`. Its registrations are all `TryAdd`, so this is harmless without a provider.

It returns an `IStorytellerHostingBuilder`, whose `Services` is the collection, for `AddSubscriptions` and `AddSubjectOptions`.

### Options

The options are the provider's `StorytellerConfigurationOptions`, so one delegate serves the provider, the documents and Hosting. As for the provider, every option the delegate left `null` is read from its `STORYTELLER_*` variable (see [Options and variables](configuration-provider.md#options-and-variables)).

| Option | In Hosting |
| --- | --- |
| `Endpoint`, `Credential` | Every read |
| `Key` | Optional at registration. Who am I needs it. Its organization, project and view are the scope of annotation keys, of the informer and of the subscriptions. |
| `Mode` | The mode of `IStorytellerDocuments`, and the default mode of the subscriptions' documents |
| `ReferenceResolver`, `SecretRefreshInterval` | The documents: the application's `IStorytellerDocuments` and the subscriptions' |
| `ReadTimeout` | Each request: a point read, a page of a list, a document. It runs on the system clock. |
| `Resilience` | The retries and the circuit breaker of each transport |
| `TimeProvider` | The poll timer, the age of who am I, the status times and the memory cache of the documents |
| `LoggerFactory` | The logs; the container's `ILoggerFactory` when unset |
| `FileSystem`, `ConfigurePrimaryHandler` | As for the provider |
| `RefreshInterval`, `Optional`, `CacheDirectory`, `CacheProtector`, `Placement` | Ignored, from code and from the environment |

- **Read at the call.** The options are read and validated when `AddStoryteller` is called. A missing endpoint or an invalid key throws `StorytellerConfigurationException` there, so a misconfigured process fails at its start.
- **The first registration wins.** A later `services.AddStoryteller(…)` returns a builder over the first one, without reading its options.
- **One credential.** Who am I, the catalog, `IStorytellerDocuments` and the subscriptions share one bound credential, and with it one token cache. The provider reads its own settings and binds its own copy, and so does an `IStorytellerDocuments` that `AddStorytellerDocuments` registered before.
- **Separate transports.** The catalog (who am I, `GetExecutionAsync`, the informer), the application's `IStorytellerDocuments` and the subscriptions' documents each have their own `HttpClient` and circuit breaker, built as the provider's is (see [Transport](configuration-provider.md#transport)). A failing tenant cannot open the breaker of the application's own reads.

### One call

`IHostApplicationBuilder.AddStoryteller(configure)` calls `builder.Configuration.AddStoryteller(configure)` and then `builder.Services.AddStoryteller(configure)`. The delegate runs once for each. The provider requires a key, so this call does too. The variables come from the environment, or from an [Aspire AppHost](aspire.md).

## Who am I

`IExecutionContext` reads the annotation of the options' `Key` and its ancestors.

| Member | Does |
| --- | --- |
| `GetAsync(cancellationToken)` | The first call reads and waits. Later calls return the snapshot at once, and refresh it in the background when it is older than `RefreshInterval`. |
| `RefreshAsync(cancellationToken)` | Reads now, the ancestors that were unreadable included. Throws on failure, and the previous snapshot stays. |

The key may be of any type. The common ones:

| Key | Process | Ancestors read |
| --- | --- | --- |
| `exe.{subject}.{responsibility}.{context}` | A service of one subject | `rst.{responsibility}`, `sbt.{subject}`, `usg.{subject}.{responsibility}`, `cnt.{subject}.{context}` |
| `uxe.{subject}.{responsibility}.{context}.{unit}` | A job | The same, `unt.{responsibility}.{unit}` and `exe.{subject}.{responsibility}.{context}` |
| `rst.{responsibility}` | A worker that discovers its subjects through the catalog | None |

### The snapshot

`StorytellerExecutionContext` is immutable.

| Member | Content |
| --- | --- |
| `Key` | The full key, with the type code lower-cased |
| `Annotation` | The annotation of the key; a `StorytellerExecution` for an `exe` key |
| `Ancestors` | The ancestors that were read, by their `AnnotationType` |
| `Unreadable` | The full keys of the ancestors the machine may not read |
| `ReadAt` | When the read started, on the options' `TimeProvider` |
| `Subject`, `Responsibility`, `Usage`, `Context` | The annotation of that type: the key's own, or an ancestor; `null` when it was not read |
| `Execution` | The key's own for an `exe` key, its parent for a `uxe` key |
| `GetSubscriptionStatus(now)` | Whether the subscription is in effect; see below |

### Reads

- **The key** is read with `includeInactive` and `includeDeleted`: a disabled, scheduled, expired or deleted annotation is an answer, not a failure.
- **The ancestors** are read at once, with `includeInactive`.
- **Each read** is bounded by `ReadTimeout`, and goes through the catalog's transport.

### Failures

- **The key does not exist.** A `404` throws `StorytellerConfigurationException` with status `404`, definitive, that names the key. Any other failure throws with its class (see [Errors and logging](#errors-and-logging)).
- **A restricted ancestor.** A `403` with `AnnotationRestricted`, which a machine restricted to its execution gets for its ancestors, leaves the ancestor out and adds its key to `Unreadable`. One information log names them, per read. They are not read again until `RefreshAsync`.
- **A missing ancestor.** A `404` leaves it out, in neither `Ancestors` nor `Unreadable`: it was deleted after the key was read.
- **Any other failure of an ancestor**, `AccessDenied` included, fails the read as a whole.
- **No key.** `GetAsync` and `RefreshAsync` throw `StorytellerConfigurationException` naming `STORYTELLER_KEY` and `StorytellerConfigurationOptions.Key`.

### Refresh

Who am I is stale while revalidate: a refresh never blocks a caller.

- **The first read.** Concurrent first calls share one read. A failed first read is not kept: the next call reads again.
- **Later calls** return the snapshot. The first call after `RefreshInterval` has passed, counted from the snapshot's `ReadAt` or from the start of the last background read, starts one background read. One runs at a time, so a failing platform is not asked on every call.
- **A background read that fails** keeps the snapshot and logs a warning.
- **A key that is gone.** A deleted key is read with its deletion, so `Annotation.DeletedAt` is the time it was deleted. A background read that gets a `404`, a key that was purged, keeps the previous snapshot with `DeletedAt` set to the time of the read. Either way the status turns `Deleted` instead of staying `Active` for ever. A first read or `RefreshAsync` that gets a `404` throws.
- **The token of `GetAsync`** ends the caller's wait, not the first read that other callers share.
- **Disposal.** The container disposes the service, which cancels the reads in flight. A call after that throws `ObjectDisposedException`.

`StorytellerExecutionContextOptions.RefreshInterval` is 5 minutes. Zero, or `Timeout.InfiniteTimeSpan`, leaves the reads to `RefreshAsync`. A negative value fails the options' validation.

```csharp
builder.Services.Configure<StorytellerExecutionContextOptions>(options => options.RefreshInterval = TimeSpan.FromMinutes(1));
```

`SwitchKeyAsync` of the provider's refresher does not move who am I. It follows the options' key.

### The status of the subscription

`GetSubscriptionStatus(now)` answers the study's "is it active" from the key's annotation, its execution and its usage:

| Case | Status |
| --- | --- |
| The key's annotation or its execution is deleted, or gone | `Deleted` |
| A key of another type than `exe` and `uxe` (`rst`, `sbt`, `usg`, `cnt`, `unt`) | The status of its own annotation |
| The first of the key's annotation, its execution and its usage that is not active | `Disabled`, `Scheduled` or `Expired` |
| All that were read are active, but the usage, or the execution of a `uxe` key, was unreadable | `Unknown` |
| Otherwise | `Active` |

- **Per annotation.** The status of an annotation is its own: `Disabled` wins, then a `ValidFrom` after `now` is `Scheduled`, then an `ExpiresAt` at or before `now` is `Expired`. Only the key's annotation, its execution and its usage count: an execution stays active when its subject is disabled.
- **The clock.** The status changes without a write when `ValidFrom` or `ExpiresAt` passes, so it is evaluated against a time.
- **A restricted machine.** A machine restricted to its execution cannot read its usage, so its status is `Unknown` while its execution is active. `context.Annotation.GetStatus(now)` gives the status of the execution alone.

### Entitlements

The usage `usg.{subject}.{responsibility}` records that a subject may use a capability, on its terms, in its validity window. Its `Labels` and `Values` are the material that plans and seats are built from: `context.Usage?.Values`. There is no typed entitlement API.

## Snapshots

The catalog hands out immutable snapshots of what the API answered: `StorytellerAnnotation`, and `StorytellerExecution` for an execution.

| `StorytellerAnnotation` | Content |
| --- | --- |
| `Key` | The full key, with the type code lower-cased, as the documents' keys have it |
| `AnnotationKey` | The annotation key |
| `Type`, `Name` | From the key: its `AnnotationType` and its last segment |
| `Title`, `Description` | From the answer |
| `Labels` | Never `null` |
| `Values` | Strings (date-like ones as stored), numbers, booleans and `null`s as Newtonsoft.Json reads them, and `JObject` or `JArray` for objects and arrays. Never `null`. A deep copy per access. |
| `IsDisabled`, `ValidFrom`, `ExpiresAt` | What the status is evaluated from |
| `Created`, `Modified` | The SDK's `AuditStamp`, a copy per access |
| `DeletedAt` | Set only on a deleted annotation, which only who am I reads |
| `GetStatus(now)` | The `AnnotationStatus` at `now` |

| `StorytellerExecution` adds | Content |
| --- | --- |
| `SubjectName`, `ResponsibilityName`, `ContextName` | From the key, never from the answer |
| `UnitNames` | The units of execution under it, by their unit names |

- **Point reads only.** Snapshots come from reads of one annotation, never from the items of a list, whose `Values` lose array items, the trailing zeros of decimals and the offsets of date-like strings.
- **Copies.** A snapshot is shared by every caller, so what a caller could change (`Values`, `Created`, `Modified`) is a copy per access.
- **`ToString()`** prints the key and the type, never values.

## The catalog

`ISubscriptionCatalog` reads executions through the platform's annotations.

### One execution

`GetExecutionAsync(key, cancellationToken)` reads one execution, in any status.

- `key` is a full key, or an annotation key in the organization, project and view of the options' `Key`. An annotation key without that `Key` throws `StorytellerConfigurationException` naming `STORYTELLER_KEY`.
- A key that does not name an execution, `exe.{subject}.{responsibility}.{context}`, throws `ArgumentException`.
- It returns `null` when the execution does not exist or is deleted.
- Other failures throw `StorytellerConfigurationException`.

### The informer

`WatchExecutionsAsync(responsibility, context, pollInterval, cancellationToken)` streams the subscriptions of a responsibility in a context:

```csharp
await foreach (var change in catalog.WatchExecutionsAsync("invoicing", "production", TimeSpan.FromMinutes(1), cancellationToken))
{
    Console.WriteLine($"{change.Kind} {change.Execution.SubjectName} {change.Removal?.Reason}");
}
```

A **subscription** of responsibility R in context C is an execution `exe.{subject}.R.C` that is active, and whose usage `usg.{subject}.R` is active. Each event carries both annotations, so the usage's facts (plans, seats) travel with the subject.

| `StorytellerExecutionChange` | Content |
| --- | --- |
| `Kind` | `Added`, `Changed` or `Removed` |
| `Execution`, `Usage` | The snapshots: of point reads for `Added` and `Changed`, those held before for `Removed` |
| `Removal` | For `Removed`: which annotation ended the subscription, by its annotation key, and why |

| `StorytellerRemovalReason` | The annotation that ended it |
| --- | --- |
| `Deleted` | Is soft-deleted. Restoring it adds the subscription again. |
| `Disabled` | Has `IsDisabled` set |
| `Scheduled` | Has a `ValidFrom` after now |
| `Expired` | Has an `ExpiresAt` at or before now |
| `Missing` | Answered `404`: purged, or gone without a trace of a deletion |

The execution is checked before the usage.

- **The scope.** The organization, project and view come from the options' `Key`, which may be any full key of the view, for example `@house.main.default.rst.invoicing` for a worker. Without a `Key`, the call throws `StorytellerConfigurationException` naming `STORYTELLER_KEY`.
- **Names.** `responsibility` and `context` are names, one key segment each, compared ordinally. An empty name, or one with a `.`, throws `ArgumentException`. The arguments are checked at the call, not at the first `MoveNextAsync`.
- **The interval.** `pollInterval` is 30 s when `null`, with a jitter of ±10 % (`interval × (0.9 + 0.2 × r)`) on the options' `TimeProvider`, counted from the end of a poll. `TimeSpan.Zero` polls once, and a negative interval throws `ArgumentOutOfRangeException`. The provider's `RefreshInterval` is not used: a worker may poll its tenants less often than its own document.
- **Each enumeration** runs its own loop and holds its own subscriptions, so it starts with `Added` for every subscription of its first complete poll.
- **Cancellation** ends the enumeration with `OperationCanceledException`.

### One poll

1. **The responsibility.** Until a poll has completed, the poll first reads `rst.{responsibility}`. The lists of a responsibility that does not exist are empty, which would read as no subscriptions for ever, so a `404` fails the poll with a definitive `StorytellerConfigurationException`: "The responsibility R does not exist".
2. **Two lists**, `…/annotations/rst.{responsibility}/executions` and `…/annotations/rst.{responsibility}/usages`, of active annotations only. Each list follows its continuation tokens until the platform answers none. An empty page with a token is legal. A key listed twice keeps the item listed last. A token answered twice, or more than 1000 pages, fails the list with a message that asks to update the API: a Storyteller API without the paging fix answers the first page again.
3. **Join.** The candidates are the executions in context C whose usage is listed. The platform does not filter the context of the list, so all contexts of the responsibility are listed and filtered here.
4. **Fingerprints.** Each list item is fingerprinted by its `Modified` stamp (instant, actor kind and id), or, when it has none, by a hash of its members.
5. **Diff** against the held subscriptions:
   - a candidate that is not held: the execution and the usage are read; both active gives `Added`;
   - a held candidate whose execution or usage fingerprint changed: what changed is read; both still active gives `Changed`;
   - a held subscription that is no candidate any more: both are read, deleted ones included. The first that is deleted, inactive or gone gives `Removed` with its reason. Both active means the list missed it: it stays, without an event.
6. **Order.** `Removed`, then `Changed`, then `Added`, each by key.

- **Reads** of a poll, the lists, the annotations and the confirmations, run at most 4 at once, or `MaxConcurrentReads` of the [subscriptions](#subscription-options).
- **A point read that fails** leaves that subscription as it was, without an event, and the next poll diffs it again. A failure other than a `404` logs a warning with the failure's message; for a `403`, it names the restriction or the scope the machine lacks.
- **A failed poll** emits nothing, keeps what is held, logs a warning with the failure's message, and polls again at the next interval. With a zero interval, the failure of the one poll is thrown.
- **The wrong context.** A first poll that lists executions of the responsibility, but none in the context, logs one warning that names the contexts it saw, up to ten.
- **Expiry.** The lists are the platform's view of "active now", so a subscription whose `ExpiresAt` passes leaves at the first poll after it.
- **What `Changed` sees.** `Modified` moves on a create, an update that changes something, and a restore. It does not move on a configuration write, on a change of `UnitNames`, or on a status that changes because a date passed. The documents of the [subscriptions](#subscriptions) are read on every poll, so they follow configuration writes anyway.

## Subscriptions

`AddSubscriptions` adds a hosted informer for one responsibility in one context. It keeps the resolved document of every subscription in memory, and serves them through `IStorytellerSubscriptions`.

```csharp
builder.Services.AddStoryteller()
    .AddSubscriptions("production", options =>
    {
        options.PollInterval = TimeSpan.FromMinutes(1);
        options.MaxConcurrentReads = 8;
    });
```

| Overload | Responsibility |
| --- | --- |
| `AddSubscriptions(context, configure)` | The one in the chain of the options' `Key`: its own (`rst.R`), or the one of its unit (`unt.R.U`), usage (`usg.S.R`), execution (`exe.S.R.C`) or unit of execution (`uxe.S.R.C.U`). A key without one (`sbt`, `cnt`) throws. |
| `AddSubscriptions(responsibility, context, configure)` | The named one. A key whose chain names another responsibility throws. |

- **At registration.** Both throw `StorytellerConfigurationException` without a key and for an invalid option. The context-only overload also throws it for a key whose chain names no responsibility, and the named overload for a key whose chain names another responsibility; a key without one (`sbt`, `cnt`) takes the named responsibility. Both throw `ArgumentException` for a name that is empty or has a `.`, and `InvalidOperationException` for a second call in the container, or on a collection without `services.AddStoryteller(…)`. One container holds the subscriptions of one responsibility in one context.
- **The scope** is the organization, project and view of the key.

### Subscription options

`StorytellerSubscriptionOptions` are read and validated at registration; a later change of the object changes nothing.

| Option | Default | Rule |
| --- | --- | --- |
| `PollInterval` | 30 s | `0` polls once; otherwise 1 s to 45 days. With a jitter of ±10 %, counted from the end of a poll. Every poll reads the document of every subscription again, so this is also how old a document may get. |
| `Mode` | `null` | The read mode of the documents. `null` takes the options' `Mode` (`STORYTELLER_MODE`, `Resolved` when unset). |
| `MaxConcurrentReads` | 4 | 1 to 64. The reads of one poll at once: the lists and annotations, then the documents. |
| `WaitUntilReadyOnStart` | `false` | The host's start waits until the subscriptions are ready. |
| `StartTimeout` | 60 s | Positive, or `Timeout.InfiniteTimeSpan`. Used with `WaitUntilReadyOnStart` only. |

### The loop

A `BackgroundService` owns the polling informer of [One poll](#one-poll) and a document service of its own, built from the Hosting settings, with its own transport, circuit breaker and memory cache. After each poll it:

1. applies the changes of the informer;
2. reads the document of every held subscription, at most `MaxConcurrentReads` at once, in the subscriptions' mode;
3. publishes: a new `GetAll()` and `Status`, then the events for the watchers and the named options, so each event carries its document.

The documents are read as [`IStorytellerDocuments`](configuration-provider.md#documents-on-demand) reads them: a conditional read with the cached `ETag`, a `304` from memory, and secret references resolved with the options' `ReferenceResolver`.

### Rules

- **Added** when a subscription has its first good document. Until then it is **pending**: no event, not in `GetAll()`, listed in `Status.Pending` with its last error, and a warning per failed read. It is added when a read succeeds.
- **Changed** when the execution or the usage changed, or the data of the document changed. A `304`, a new `ETag` with the same data, or a new version that changed only the configuration's labels or values raises nothing. A `304` whose secret references resolved to new values raises `Changed`. The held document is always the newest good one, so its `ETag` and `Version` stay current.
- **A failed read** keeps the last good document, sets `LastError` and `LastErrorAt`, and logs a warning, whatever its class: a transient failure, withheld secrets, a `403` after a policy change, a `422` of a binding. The next poll reads again, and a good read clears the error. The error shows in `GetAll()`, `Find` and `Status`, without an event.
- **Removed** when the informer removes the subscription, with its `StorytellerRemoval`. Its document is dropped from the memory cache, so its secrets do not linger. A pending subscription that leaves raises nothing.
- **Ready** after the first complete poll and one read of the document of every subscription in it, whether the reads succeeded or not, so one broken tenant does not block a worker's start.
- **A failed poll** keeps every subscription and its document, and counts in the status.

### `IStorytellerSubscriptions`

| Member | Does |
| --- | --- |
| `Responsibility`, `Context` | What the subscriptions watch |
| `Status` | A `StorytellerSubscriptionsStatus`, a new one after every poll |
| `WaitUntilReadyAsync(cancellationToken)` | Completes when the subscriptions are ready |
| `GetAll()` | The subscriptions that have a good document, by subject, ordinally |
| `Find(subject)` | One by its subject name, ordinally; `null` when it is not subscribed, not ready yet, or pending |
| `WatchAsync(cancellationToken)` | `Added` for every current subscription, then the changes as they happen |

**`WaitUntilReadyAsync`**

- It completes when the subscriptions are ready, and at once after that.
- While they were never ready, it throws the failure of the latest poll when that failure is definitive, such as `400`, `401`, `403` or `404`, or a responsibility that does not exist. A misconfigured worker does not wait for ever; the loop keeps polling.
- It throws `OperationCanceledException` when the token is cancelled, or when the host stopped the informer first. It throws the failure of an informer that broke, and, with a zero `PollInterval`, the failure of the one poll.

**`WatchAsync`**

- Several watchers may run at once. Each starts with `Added` for every current subscription, and then gets every change once.
- Each event is a `StorytellerSubscriptionChange`: `Kind`, the `Subscription` (for `Removed`, the one held before) and `Removal`.
- It ends when the informer stops; with `OperationCanceledException` when its token is cancelled; and with the informer's `StorytellerConfigurationException` when the informer broke, which it logs as an error.

| `StorytellerSubscription` | Content |
| --- | --- |
| `Subject` | The subject name of the execution |
| `Execution`, `Usage` | The snapshots of the informer |
| `Document` | A new `StorytellerDocument` per access, over the last good read |
| `LastError`, `LastErrorAt` | The failure of the latest read, while the last good document stays; `null` after a good read |
| `LastSucceededAt` | When a read of the document last succeeded |

- **Immutable.** The subscriptions hand out a new `StorytellerSubscription` when something of it changed.
- **`Document`** is a new instance on every access, so what one caller changes in it reaches neither the subscriptions nor another caller.
- **`ToString()`** leaves the document out, as it may hold resolved secrets.

### Status

`StorytellerSubscriptionsStatus` is for health checks and diagnostics, like the provider's `StorytellerConfigurationStatus`.

| Member | Meaning |
| --- | --- |
| `IsReady` | The first poll completed, and every subscription in it had its first document read |
| `LastPollSucceededAt` | When a poll last completed |
| `LastPollError` | The failure of the latest poll; `null` when it succeeded. Also the failure of an informer that broke. |
| `ConsecutivePollFailures` | Failed polls since the last one that succeeded |
| `Subscriptions` | The number of subscriptions that have a good document |
| `Pending` | The list of subscriptions without a first good document, each a `StorytellerPendingSubscription` with its full key and last error |
| `Failing` | The number of subscriptions whose latest read failed, while they keep their last good document |
| `IsDegraded` | The latest poll failed, or a subscription is pending or failing |

`ToString()` prints the errors by their messages, which name keys and statuses only. A health check of ASP.NET Core over it:

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class SubscriptionsHealthCheck : IHealthCheck
{
    private readonly IStorytellerSubscriptions _subscriptions;

    public SubscriptionsHealthCheck(IStorytellerSubscriptions subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = _subscriptions.Status;
        var result = !status.IsReady ? HealthCheckResult.Unhealthy(status.ToString())
            : status.IsDegraded ? HealthCheckResult.Degraded(status.ToString())
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
```

### Start and stop

- **Start.** The first poll starts with the host. With `WaitUntilReadyOnStart`, the start waits for `WaitUntilReadyAsync` up to `StartTimeout`, on the system clock. A definitive failure of the poll fails the start at once; the timeout fails it with a transient `StorytellerConfigurationException` that names the timeout and, when a poll failed, its failure.
- **Stop.** Stopping cancels the loop and disposes the document service, which cancels the reads in flight and drops the cached secrets. The watchers end.
- **A broken informer.** An unexpected exception of the loop is logged as an error. The subscriptions keep what they held, the status reports the failure as `LastPollError`, so it is degraded, and the watchers end with it. No poll follows.

## Named options

`AddSubjectOptions<TOptions>(section)` binds options per subject:

```csharp
builder.Services.AddStoryteller()
    .AddSubscriptions("production")
    .AddSubjectOptions<InvoicingOptions>("invoicing");

// Later, for the subject northwind:
var options = serviceProvider.GetRequiredService<IOptionsMonitor<InvoicingOptions>>().Get("northwind");
```

- **Binding.** `IOptionsMonitor<TOptions>.Get(subject)` and `IOptionsSnapshot<TOptions>.Get(subject)` bind the document of that subject's subscription, or its `section`, with `ConfigurationBinder`. Without a section, the whole document is bound.
- **Other names fail.** Any name other than the default one that has no subscription fails validation with `OptionsValidationException`: "no subscription of the subject S in R/C (not ready yet, pending, or not subscribed)". A worker never runs a tenant on default options by mistake.
- **The default name** is left alone: `Configure<TOptions>(configuration)` and `IOptions<TOptions>.Value` work as usual.
- **Shared values.** Values that all subjects share come from `ConfigureAll<TOptions>`. The order of registration decides, as for any configure action: `ConfigureAll` before `AddSubjectOptions` gives defaults that the documents override, `ConfigureAll` after it overrides the documents, and `PostConfigureAll` runs after both in any order.
- **Changes.** On every `Added` and `Changed` of a subject, its options are dropped from the cache and `OnChange` is raised with `(Get(subject), subject)`. On `Removed` they are dropped, and nothing is raised, as `Get(subject)` would fail validation; removal is an event of `WatchAsync`. The other subjects keep their cached instance.
- **In step with the watchers.** The options of a subject are dropped before a watcher or a waiter hears of its change, so a watcher that calls `Get(subject)` on `Changed` gets options bound to the new document.
- **A value that does not convert** throws `StorytellerConfigurationException` from `Get`, naming the key, the type and the section, never the value. After a change, `OnChange` is then not raised for the subject, and a warning is logged.
- **Calls.** `AddSubjectOptions` needs `AddSubscriptions`, in any order: without it, the first resolution of the options throws `InvalidOperationException`. A second `AddSubjectOptions` of the same type throws `InvalidOperationException`. An empty section throws `ArgumentException`.

### Which to use

| | `ISubscriptionCatalog.WatchExecutionsAsync` | `IStorytellerSubscriptions.WatchAsync` |
| --- | --- | --- |
| Loop | Cold: each enumeration polls on its own | Hot: the one hosted loop |
| Content | The execution and the usage | The same, with the document |
| `Added` | When listed and read | When the first document is good |
| For | Tools and processes that do not keep documents | A worker with `AddSubscriptions` |

The subscriptions' documents are not served by `IStorytellerDocuments`. Its `GetAsync` revalidates on every call, and jobs depend on that; under `AddSubscriptions` the same call would be up to a poll interval old. The application's `IStorytellerDocuments` and the subscriptions' documents are separate instances, with separate caches.

## Scopes and restricted machines

| Service | Scopes of the machine | Restricted machine |
| --- | --- | --- |
| Who am I | `Annotation.Read` | Restricted to its key: reads the key; its ancestors are `Unreadable` |
| `GetExecutionAsync` | `Annotation.Read` | Reads the executions at or under the key it is restricted to |
| The informer | `Annotation.Read` | Restricted to `rst.{responsibility}`, or unrestricted |
| The subscriptions | `Annotation.Read` and `Configuration.Read`; `Secrets.Read` for secrets | Restricted to `rst.{responsibility}`, or unrestricted |

- **A runtime reader** of `sform account machine create` has `Annotation.Read` and `Configuration.Read`, so it runs who am I and the provider. A machine without `Annotation.Read` runs the provider, but who am I fails with `403` `AccessDenied`.
- **A worker's machine.** `sform account machine create -a rst.invoicing` restricts a machine to the responsibility. It lists and reads the executions and usages of `rst.invoicing`, and their configurations. Add `--scope secrets-read` when the documents need their secrets.
- **A machine restricted to its execution** gets who am I without ancestors and the status `Unknown`, and cannot run the informer. The informer's first read, of `rst.{responsibility}`, answers `403` `AnnotationRestricted`, so every poll fails with "The machine may not read …" before any list is requested. The failure is definitive: `WaitUntilReadyAsync`, and a start with `WaitUntilReadyOnStart`, throw it.

See [Annotation restriction of machines](authentication.md#annotation-restriction-of-machines) and [Scopes of machines](authentication.md#scopes-of-machines).

## Errors and logging

- **One exception type.** Failures throw `StorytellerConfigurationException`, with `Key`, `StatusCode`, `ErrorCode`, `IsTransient` and the inner exception, classified as the provider's are (see [Failure classes](configuration-provider.md#failure-classes)). Messages name the key and the fix, never an answer's body or a value.
- **The catalog's words.** A `403` with `AnnotationRestricted` says that a machine restricted to an annotation reads that annotation and its descendants only; another `403` that the machine needs `Annotation.Read`; a `404` that the annotation does not exist.
- **Logs** contain keys, statuses, error codes and counts, never values or documents.

| Category | Logs |
| --- | --- |
| `_42.Platform.Storyteller.Sdk.Hosting.ExecutionContext` | Information: the ancestors the machine may not read. Warning: a failed background read, a key that is gone. Error: an unexpected failure of a background read. |
| `_42.Platform.Storyteller.Sdk.Hosting.Informer` | Warning: a failed poll, a failed point read, executions only in other contexts. Debug: a subscription the list missed, an annotation gone since the list. The informer of `WatchExecutionsAsync` and that of the subscriptions log here. |
| `_42.Platform.Storyteller.Sdk.Hosting.Subscriptions` | Information: the counts of a poll that changed something. Warning: a failed read of a document, a listener or named options that failed. Error: an informer that broke. |
| `_42.Platform.Storyteller.Sdk.Configuring.StorytellerDocuments` | The reads of the documents, as for `IStorytellerDocuments` |

## Cost and limits

**Cost of a poll** with N subscriptions:

- one request per page of each list, executions and usages of the responsibility in every context, as the platform cannot filter the context. A page reads up to 1000 annotations;
- one read of the responsibility, until a poll has completed;
- point reads for changes only: two per added or removed subscription, one or two per changed one;
- for the subscriptions, N conditional reads of the documents, `304` while unchanged.

**Limits:**

- **Polling.** A change reaches the process at the next poll, up to the poll interval and its jitter later. The bulk read of many documents and a watch of change events are follow-ups; the informer polls behind an internal seam that either can replace.
- **The memory cache of documents.** An entry stays for twice `SecretRefreshInterval` (2 hours by default) after its last read. With a poll interval longer than that, every poll reads every document in full. The cache holds 1024 documents; beyond 1024 subscriptions, the documents that were evicted are read in full.
- **No last-known-good on disk.** A worker restarted during an outage serves no tenant until the API answers.
- **One responsibility and one context** per container.
- **Clocks.** The lists use the platform's clock, `GetStatus(now)` the process's. A skew shows until the next poll.
- **`UnitNames`** of an informer's execution is read again only when the execution's `Modified` moves, which a new unit of execution does not do.
- **No typed entitlements.** The usage's `Labels` and `Values` are the facts; there is no `IsEntitled`.
- **Restricted machines.** See [Scopes and restricted machines](#scopes-and-restricted-machines).

## Feature flags

There is no FeatureManagement or OpenFeature bridge. Microsoft.FeatureManagement reads its flags from `IConfiguration`, so a process with one key gets them from a `feature_management` section of its document, and they follow every applied document. Flags per subject of a worker are not covered.
