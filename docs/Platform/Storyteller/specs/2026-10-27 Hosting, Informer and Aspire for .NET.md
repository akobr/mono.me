# Hosting, Informer and Aspire for .NET

Linear: [424-91 Hosting, FeatureManagement and Aspire packages, and the subscription informer for .NET](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription)

## Problem

PR #127 (424-85) shipped the configuration half of the .NET story: `42.Platform.Storyteller.Configuration` (the `IConfiguration` provider and `IStorytellerDocuments`) and `.KeyVault`. It left out the catalog half that the study's reference .NET story needs (`study-usage.md` §3.8, §5.1, §5.3, §5.4):

1. **Who am I.** A process knows its key (`STORYTELLER_KEY`), but nothing reads the annotation behind it: the title, labels and `values` of the execution, its subject, responsibility, usage and context, or whether the subscription is active right now.
2. **The subscription informer.** A worker that serves every customer of a responsibility cannot learn which subjects use it in its context, or who was added, changed or removed. Without it, one process per customer is the only option.
3. **Documents and options per subject.** Nothing keeps the resolved documents of those subjects in memory, and `IOptionsMonitor<T>.Get("northwind")` has no source.
4. **Aspire.** An AppHost cannot pass the `STORYTELLER_*` variables to the projects and executables it starts.

The issue also names a FeatureManagement bridge and asks to decide which of the four pieces belong to the 1.0 wedge.

### The product owner's answers (comment on the issue)

| Question of the issue | Answer |
|---|---|
| Does the informer wait for change events/watch (424-49) and bulk read (G5/C4), or start with polling the list? | **Start with polling the list, but make it easy to exchange in the near future.** |
| FeatureManagement vs OpenFeature/OFREP (C11) first? | **Keep FeatureManagement and OpenFeature/OFREP for later, out of scope.** |
| Is Aspire worth it before published NuGet packages (C10)? | **Yes.** |

### Acceptance criteria

The criteria of the issue, verbatim, and where this spec deviates:

| Criterion | Where | Deviation |
|---|---|---|
| "Decide which of the four pieces (Hosting, informer, FeatureManagement, Aspire) belong to the 1.0 wedge; split into separate issues." | Section 1, section 14 | The in-scope pieces are phases of one PR, not issues; the deferred pieces get backlog issues. |
| "Hosting: `IExecutionContext` and `ISubscriptionCatalog` (status, entitlements) on top of the PR #127 provider and credentials." | Sections 4-7 | No typed entitlements API: the usage annotation (its labels and `values`) is exposed as catalog facts for the process and for every subscription; `IsEntitled` waits for the billing module (section 14). |
| "Informer: list-then-refresh of executions per responsibility/context, feeding `IStorytellerDocuments` and named options." | Sections 7-8 | The informer's documents are exposed by `IStorytellerSubscriptions`, not by `IStorytellerDocuments`, whose revalidate-on-every-call contract jobs depend on (8.6). |
| "Aspire: hosting resource + client integration passing `STORYTELLER_*`." | Section 10, 4.4 | No separate Aspire client package: the hosting resource passes the plain `STORYTELLER_*` variables, and the "one call" of a client integration is `IHostApplicationBuilder.AddStoryteller()` in the Hosting package. Aspire-specific health checks and an `Aspire:*` settings section are deferred. |

The deviations are called out in the PR and the issue comment for the product owner to confirm.

The inputs to this spec are:

- the issue and the product owner's answers in its comments;
- a read-only map of the code, tests and docs at `feat/424-96` `b49c6eb3` (five parallel readers: the configuration provider, the C# SDK and the 424-96 credential contract, the server semantics of executions and their listing, Aspire 13.1.2 and the repository conventions, the studies);
- four adversarial reviews of the first draft against the code (the provider internals, the server semantics, Aspire 13.1.2, scope and product). They probed the SDK's JSON handling and the API's serializers in scratch projects, and compiled and ran an Aspire 13.1.2 mirror of section 10, including three live AppHost runs with the cached DCP;
- the studies `study-usage.md` and `study-evaluation.md` attached to the issue (sections 3.1, 3.2, 3.5, 3.6, 3.8, 5.1-5.5, 16.1, 18, 19, 20.5 and evaluation sections 4, 8, 9);
- the CodeRabbit coding plan attached to the issue (`2026-10-26 Hosting, Aspire packages, and the subscription informer.md`), used as a second opinion and a source of ideas, not as the decision. Section 15 lists where this spec differs from it.

The branch is based on `feat/424-96` (PR #135), not on `main`: the canonical `STORYTELLER_*` names, `STORYTELLER_KEY` and the public credential contract that this work reads exist only there.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The configuration provider (PR #127)

- **Public surface** (namespace `_42.Platform.Storyteller.Sdk.Configuring`): `IConfigurationBuilder.AddStoryteller(…)`, `IServiceCollection.AddStorytellerConfiguration()`, `IServiceCollection.AddStorytellerDocuments(configure)`, `IStorytellerDocuments`, `StorytellerDocument` (sealed, **internal constructor**), `StorytellerConfigurationOptions`, `StorytellerConfigurationException` (`Key`, `StatusCode`, `ErrorCode`, `IsTransient`), `StorytellerReadMode`. Everything else is internal. `InternalsVisibleTo`: only `42.Platform.Storyteller.Configuration.UnitTests` (`Configuration/src/Configuration.csproj`).
- **Settings** (`Configuration/src/Settings/SettingsReader.cs`): options win over the environment. `Endpoint` or `STORYTELLER_ENDPOINT` (deprecated fallback `STORYTELLER_API_URL`), `Key` or `STORYTELLER_KEY` parsed with `FullKey.Parse`, `StorytellerCredential.FromEnvironment(…)` bound with `StorytellerCredentialBinding` (organization and project of the key). Every `Bind` returns a copy with its own token cache. `ReadForDocuments` makes the key optional (the default scope of annotation keys) and skips the provider-only settings. The test seams `GetEnvironmentVariable`, `PrimaryHandler` and `NextRandom` on the options are internal.
- **Transport** (`Configuration/src/Transport/TransportFactory.cs`): built by hand, not through `AddStorytellerSdk`, because the provider runs before the container exists and Aspire service defaults would stack a second resilience handler (comment at lines 9-11). `StorytellerCredentialHandler` → `ResilienceHandler(StorytellerResilience.CreatePipeline)` (one circuit breaker per transport) → `SocketsHttpHandler` (no redirects, client certificate). It builds a `ConfigurationsApiClient` only; `StorytellerTransport` is `(HttpClient, IConfigurationsApiClient)`.
- **Reading** (`Configuration/src/Reading/DocumentReader.cs`): conditional reads through the SDK's hand-written twins (`ApiSdk.Conditional.cs`). Only `IsTransientStatus` is internal (true for 408, 424, 429 and 5xx except 501 and 505); `Classify`, `Describe` and the error parsing are private and phrased for configurations. `ReadTimeout` is applied by `RunScope` (system clock) in `StorytellerDocuments`.
- **`IStorytellerDocuments`** (`Configuration/src/Documents/StorytellerDocuments.cs`): every `GetAsync(key)` sends one conditional GET with the cached ETag; a 304 answers from a `MemoryCache` (size limit 1024, sliding expiry 2 × `SecretRefreshInterval`, on the options' clock); a definitive failure (401, 403, 404, withheld secrets) evicts and throws; there is no last-known-good. Single flight per identity; a caller's token ends only its own wait. Each call returns a new `StorytellerDocument` over a shared immutable `DocumentEntry`; a 304 returns the cached entry (or `WithETag`, which shares `Data`), a 200 or a secret refresh builds new `Data`. The engine detects changes with `ReferenceEquals(Data)` and a private `HasSameData` (`Engine/StorytellerConfigurationEngine.cs:415-427`).
- **Out of scope of PR #127** (`docs/Platform/Storyteller/specs/2026-10-23 Configuration Provider for .NET.md`, lines 903-917): "The `Hosting` package of the study: `IExecutionContext`, `ISubscriptionCatalog` and the informer; unit handlers and the health publisher; OpenTelemetry resource attributes", "`FeatureManagement`, the Aspire hosting and client integrations, named options per subject and tenant middleware", "bulk and flat reads (C4, C5)".

### The C# SDK (424-96)

- `Sdk.NSwag/src/Credentials/StorytellerVariables.cs`, public: `ENDPOINT` (`STORYTELLER_ENDPOINT`), `API_URL` (deprecated), `KEY` (`STORYTELLER_KEY`), and the list `Credentials` (`API_KEY`, `CLIENT_CERTIFICATE[_PASSWORD]`, `CLIENT_ID`, `CLIENT_SECRET`, `TOKEN_URL`, `TOKEN_SCOPE`, `TOKEN_FILE`, `SUBJECT_TOKEN_TYPE` and deprecated aliases). The provider-only names (`STORYTELLER_MODE`, `_REFRESH_INTERVAL`, `_OPTIONAL`, `_CACHE_DIR`) are internal in `Configuration/src/Settings/ConfigurationVariables.cs`.
- 424-96 defines `STORYTELLER_KEY` as the "full key `@{organization}.{project}.{view}.{annotation key}` of the annotation a provider reads" — any annotation type, not only executions (`docs/Platform/Storyteller/configuration-provider.md:80`).
- Generated clients (`Sdk.NSwag/src/ApiSdk.g.cs`):
  - `IAnnotationsApiClient.GetAnnotationAsync(organization, project, view, key, includeInactive, includeDeleted, ct)` returns the polymorphic `Annotation` (an `Execution` keeps all its members). A 404 without a body (missing, purged, or deleted without `includeDeleted`) is a non-generic `ApiException` with status 404; a 404 with a body is `ApiException<ErrorResponse>` (`AnnotationInactive` when inactive without `includeInactive`).
  - `GetExecutionsAsync(…, responsibilityNameQuery, subjectNameQuery, contextNameQuery, continuationToken, includeInactive, includeDeleted, ct)` and `GetDescendantsAsync(organization, project, view, key, descendants, continuationToken, includeInactive, includeDeleted, ct)`.
  - Models: `Annotation` (`AnnotationKey`, `Name`, `IsDisabled`, `ValidFrom`, `ExpiresAt`, `Title`, `Description`, `Labels`, `Values`, `Created`, `Modified`, `Deletion`; no type property, the type comes from the discriminator) and `Execution : Annotation` (`SubjectName`, `ResponsibilityName`, `ContextName`, `UsageKey`, `UnitNames`, …, all `Required.DisallowNull`, so a missing member deserializes as null). `AuditStamp` and `Values` are mutable.
  - `ConfigurationsApiClient` sets `DateParseHandling.None` (`ApiSdk.partial.cs:53-55`); `AnnotationsApiClient` does not, so a date-like string in `Values` comes back as a local `DateTime` (offset lost, host time zone).
- The SDK does not reference `Abstractions.Annotations`. `AnnotationStatusExtensions.GetStatus(isDisabled, validFrom, expiresAt, now)`, `AnnotationType`, `AnnotationStatus`, `FullKey` and `AnnotationKey` live there, in the namespace `_42.Platform.Storyteller`; `Configuration.csproj` references both projects.

### Executions and usages on the API

- **Keys and graph.** `exe.{subject}.{responsibility}.{context}` (`Abstractions.Annotations/src/AnnotationKey.cs:135`); the full key is `@{organization}.{project}.{view}.exe.S.R.C`. Ancestors: `rst.R`, `sbt.S`, `usg.S.R`, `cnt.S.C` (`AnnotationKeyExtensions.GetAllAncestorKeys`); descendants: `uxe.S.R.C.U` only. The usage `usg.S.R` is the study's entitlement record ("this subject may use this capability, on these terms, in this validity window", §16.1). A context belongs to one subject (`cnt.S.C`). Executions and usages of R live in the Cosmos partition `{project}.rst.R` (`Backend.CosmosDb/src/PartitionKeys.cs`).
- **The list endpoints.**
  - `GET …/executions` (`Api.Functions/src/V1/AnnotationsHttp.cs:941-1077`): an exact `responsibilityNameQuery` becomes a single-partition query. `subjectNameQuery`, `contextNameQuery` and the regex/contains forms are **silently ignored** for executions (a known bug, `docs/Platform/Storyteller/specs/reviews/2026-10-12 Phase 2 of Soft-delete and audit of annotations.md:154`). It calls `CheckAccessToProjectAsync`, so a machine restricted to an annotation gets `403 AnnotationRestricted`.
  - `GET …/annotations/rst.R/executions` and `…/rst.R/usages` (`GetDescendants`, `:166-293`): the same single partition with a key prefix, and `CheckAccessToAnnotationAsync(rst.R)`, so they also serve a machine restricted to `rst.R`. They answer 200 with an empty list when `rst.R` does not exist. Items are written by the declared type `Annotation`: `AnnotationType`, `Modified`, `Values`, `Labels`, `ValidFrom`, `ExpiresAt` and `IsDisabled` are present, the `Execution` members are not.
  - Both: page size fixed at 1000 (`CosmosConstants.MaxItemCountPerPage`, counted before the in-memory status filter), no `ORDER BY`, and "a page can hold fewer items than the page size, or none, and still have a continuation token" (`docs/Platform/Storyteller/annotation-lifecycle.md:48`). Only active annotations by default; `includeInactive` and `includeDeleted` add the others.
- **Paging of annotation lists does not work.** `CosmosAnnotationService.GetAnnotationsAsync` answers `ContinuationToken.ToString()` = `"{Type}|{cosmos token}"` (`Backend.CosmosDb/src/ContinuationToken.cs:22`); `RequestProcessModel` parses the incoming token with `ContinuationToken.TryParse`, which requires base64 (`:41`) and exactly two `|`-separated parts (`:46-48`), so the parse fails. Even a token that parsed would be lost: `GetAnnotationsAsync<TAnnotation, TEntity>` sets `model.InputContinuationToken = null` (`CosmosAnnotationService.cs:634`) before it passes `model.InputContinuationToken?.CosmosToken` to Cosmos (`:672`). Every annotation list restarts at its first page, so a client that follows the token loops for ever as soon as Cosmos answers a second page (more than 1000 items before the status filter, or a page cut short by Cosmos). The first-page sentinel `"first"` (`ContinuationToken.cs:7`) would reach Cosmos as a literal token once the token is passed. The configuration and deleted lists use raw Cosmos tokens and work.
- **List items carry lossy `Values`.** List pages are read through the Cosmos LINQ path with Newtonsoft (`CosmosAnnotationService.cs:671-738`): arrays in `Values` become `JArray`, which the API's System.Text.Json writes as arrays of empty arrays (`[[],[]]`, it has a converter for `JObject` only, `Backend.Core/src/Json/JObjectConverter.cs`), decimals lose their trailing zeros and date-like strings take the server's offset. Point reads (`GetAnnotation`) go through System.Text.Json (`AnnotationItemStore.cs:118`) and keep the stored text.
- **Point reads.**

  | State | `GetAnnotation(includeInactive=true, includeDeleted=true)` |
  |---|---|
  | active or inactive | 200 |
  | deleted | 200 with `Deletion` |
  | missing or purged | 404 without a body |

- **Status.** `AnnotationStatusExtensions.GetStatus`: `Disabled` wins, then `ValidFrom > now` is `Scheduled`, then `ExpiresAt <= now` is `Expired`, else `Active`; the active interval is `[ValidFrom, ExpiresAt)`. The status is per annotation: nothing propagates from ancestors, so an execution stays active when its usage expires. It changes without a write when a boundary passes.
- **`Modified`** changes on create, on an update that changes something, and on restore. It does **not** change on a configuration write on the annotation or an ancestor (configurations are separate items, `{view}.cnf.{key}`), on a `UnitNames` change, on a soft delete, or on a status transition. A configuration change is visible only through the ETag of a configuration read.
- **Machine access.** Listing and reading annotations need `Annotation.Read`; configuration reads need `Configuration.Read` (and `Secrets.Read` for secrets). The restriction is checked before the scopes: a machine restricted to `exe.S.R.C` reads its execution, its descendants and its configuration (ancestors merged), but an ancestor's annotation answers `403 AnnotationRestricted`. `AccessDenied` means a missing scope or role.
- **Subscriptions, entitlements, billing:** no code. `docs/Platform/Storyteller/specs/2026-10-01 Billing and Subscriptions for Annotations.md` is a proposal without a review.

### Aspire and the repository

- `Directory.Packages.props` pins `Aspire.Hosting.AppHost`, `.Azure.*`, `.JavaScript`, `.Orleans`, `.Redis` and `Aspire.StackExchange.Redis` at 13.1.2, but not the core `Aspire.Hosting` nor `Aspire.Hosting.Testing` (the offline cache has `aspire.hosting` 13.1.2 and `aspire.hosting.testing` 9.4.2 only). `Aspire.Hosting` 13.1.2 depends on Newtonsoft.Json, Microsoft.Extensions.Http, Polly.Core, gRPC and KubernetesClient, and brings `MessagePack` 2.5.192 (restore audit warnings NU1902/NU1903, as in the AppHost today).
- No class in `src` implements `IResource`, `IResourceWithConnectionString` or `IResourceWithEnvironment`. `src/Platform/Aspire.Host/src/AppHost.cs` runs the platform locally and wires no `STORYTELLER_*`.
- In 13.1.2, the stock `WithReference(IResourceBuilder<IResourceWithConnectionString>, connectionName)` writes `ConnectionStrings__{name}` and, for connection properties, `{NAME}_{PROPERTY}` with the resource name as prefix. `ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig().BuildAsync(context, logger)` evaluates a resource's environment without DCP; an exception thrown in an environment callback leaves `BuildAsync`, a value that cannot be resolved lands in `result.Exception`. `WithInitialState`, `CustomResourceSnapshot`, `KnownResourceStates.Running` and `ExcludeFromManifest` exist and are not experimental.
- Conventions: projects under `<project>/src` and `<project>/test`, picked up by the traversal `src/Platform/Directory.Build.proj` and listed in `42.mono.slnx`; versions in `Directory.Packages.props` (alphabetical); `src/Platform/version.json` (`0.10-alpha`) inherited; package readme at `docs/<path without src>/package-readme.md`; tests with xunit 2.9.3, Shouldly, `FakeTimeProvider` and a `StubHttpHandler` (no Moq, no FluentAssertions); explanatory `//` comments, no XML docs; public extension classes in framework namespaces. No CI job builds or tests .NET.

## Proposed Changes

### 1. Scope of the 1.0 wedge

| Piece | Decision | Where |
|---|---|---|
| Hosting: who am I (`IExecutionContext`) | In | Section 6 |
| Hosting: `ISubscriptionCatalog` (one execution, the polling informer) | In | Section 7 |
| Subscriptions: documents and named options per subject, fed by the informer | In | Section 8 |
| Usage as the subscription's status and entitlement facts | In, as catalog facts (no typed `IsEntitled`) | Sections 5-8 |
| Aspire hosting integration (`AddStoryteller`, `WithReference`, `STORYTELLER_KEY`) | In (answer 3) | Section 10 |
| "One call" of a client integration | In, as `IHostApplicationBuilder.AddStoryteller()` in Hosting | Section 4.4 |
| Aspire client package (health checks, `Aspire:*` settings) | Deferred | Section 14 |
| FeatureManagement bridge, OpenFeature/OFREP | Out (answer 2). A process with one key already gets `feature_management` from its document through `IConfiguration`; documented | Section 14 (backlog issue) |
| Unit handlers, health publisher, OpenTelemetry resource detector, tenant middleware | Out | Section 14 |
| Watch transport (424-49), bulk read (C4) | Out (answer 1); the informer's change source is an internal seam | Section 7.5 |

### 2. Paging of annotation lists (server)

The informer follows continuation tokens, so paging must work first.

- `Backend.CosmosDb/src/ContinuationToken.cs`:
  - `ToString()` returns the base64 of `"{Type}|{CosmosToken}"`, the form `TryParse` already expects.
  - `TryParse` splits on the first separator only (`Split(TOKEN_SEPARATOR, 2)`), so a Cosmos token that contains `|` survives.
- `Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs`, `GetAnnotationsAsync<TAnnotation, TEntity>`: take the Cosmos token of the input before the input is cleared, and pass it to the query; the first-page sentinel maps to null:

  ```csharp
  var cosmosToken = model.InputContinuationToken is { IsFirstPage: false } input ? input.CosmosToken : null;
  model.InputContinuationToken = null;
  …
  repository.Container.GetItemLinqQueryable<TEntity>(continuationToken: cosmosToken, …)
  ```

- Every annotation list (`…/annotations`, the seven per-type lists, `…/{key}/{descendants}`) goes through this method, so all of them page from now on. Tokens issued before never worked, so no client depends on their text. Standard base64 travels in the query string: the NSwag client escapes it (`Uri.EscapeDataString`), the TypeScript client uses `URLSearchParams`.
- **Page-size seam.** `CosmosAnnotationService` reads its page size from an internal property (default `CosmosConstants.MaxItemCountPerPage`), so a test can page through three items.
- **Tests.**
  - Pure, in `Backend.CosmosDb/test`: a token's text parses back to the same type and Cosmos token; the first-page token round-trips; a Cosmos token with `|` round-trips; garbage and the old unencoded form do not parse.
  - With the Cosmos container (the project's `CosmosDbCollection`): with a page size of 2, listing five executions through the descendants route and through `…/executions`, and the multi-type `…/annotations` across a type boundary, returns every item once and ends.
- **Not fixed here:** an unparsable token still restarts silently (a 400 would be clearer); the lossy `Values` of list items (Current State) get a bug issue (section 14), and the informer does not depend on them (7.3).

### 3. Date-like strings in annotation values (SDK)

`Sdk.NSwag/src/ApiSdk.partial.cs` gives `AnnotationsApiClient` the same `UpdateJsonSerializerSettings` as `ConfigurationsApiClient`: `DateParseHandling.None`. A date-like string in `Values` stays the string that was stored, as it does in configuration values; typed members (`ValidFrom`, `ExpiresAt`, `AuditStamp.At`) still parse. `sform` shows annotation values through this client, so it now prints such values as stored instead of as a local `DateTime`. A test in `Sdk.NSwag/test` covers an offset date in `Values` and a typed `ValidFrom`.

### 4. The Hosting package

#### 4.1 Project

`src/Platform/Storyteller/Hosting/src/Hosting.csproj`: assembly `42.Platform.Storyteller.Hosting`, root namespace `_42.Platform.Storyteller.Sdk.Hosting` (next to `Sdk.Configuring`), `net10.0`, `IsPackable=true`, description and tags like the Configuration package, `InternalsVisibleTo` for its tests. References: `Configuration/src/Configuration.csproj` (which brings `Sdk.NSwag` and `Abstractions.Annotations`), `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions` (all pinned already). Inherits `src/Platform/version.json`. Test project `Hosting/test/Hosting.UnitTests.csproj` (`42.Platform.Storyteller.Hosting.UnitTests`). Both go into `42.mono.slnx` after the Configuration.KeyVault entries.

Hosting's public types use `AnnotationType` and `AnnotationStatus` from `_42.Platform.Storyteller` and the SDK's `AuditStamp` from `_42.Platform.Storyteller.Sdk`; both namespaces define an `AuditStamp`, so code that imports both names it qualified. The docs say so.

#### 4.2 Reuse of the provider's internals

`Configuration.csproj` gains `InternalsVisibleTo` for `42.Platform.Storyteller.Hosting` and `42.Platform.Storyteller.Hosting.UnitTests`. Hosting reads its settings with `SettingsReader.ReadForDocuments` and builds on these internals, refactored where they are private or phrased for configurations:

| Internal | Change in Configuration |
|---|---|
| `TransportFactory` | Split into `CreateHttpClient(settings)` (the credential, resilience and primary handlers) and `CreateSdkConfiguration(settings)`; `Create` keeps building the configuration transport from them. Hosting builds `new AnnotationsApiClient(httpClient, sdkConfiguration)` in its own disposable `CatalogTransport`, so the catalog has its own circuit breaker. |
| Failure classification | Moved out of `DocumentReader` into an internal `ReadFailures` classifier: status, error code (typed body or parsed), `StorytellerAuthenticationException`, timeouts, open circuits, `HttpRequestException`. It takes the key text and a describer per kind of read, so configuration reads keep their exact messages and catalog reads get their own ("needs Annotation.Read", "a machine restricted to an annotation reads that annotation and its descendants only"). `DocumentReader` uses it; its tests stay green unchanged. |
| `RunScope` | Reused for `ReadTimeout`: per page of a list, per point read. |
| `SwitchableLoggerFactory` | Wraps Hosting's loggers, so a log after the host disposed its factory does not throw. |
| `StorytellerDocuments` | Gains `internal Task<DocumentEntry> GetEntryAsync(FullKey key, StorytellerReadMode mode, CancellationToken)` and `internal void Evict(FullKey key, StorytellerReadMode mode)`. |
| `HasSameData` | Moved from the engine to an internal static helper on `DocumentEntry`; the engine calls it there. |
| `StorytellerDocument` | Its internal constructor is used to hand out a new document per caller over a shared `DocumentEntry`. |

The alternatives were worse: rebuilding settings and transport from public SDK parts duplicates the precedence and binding rules that 424-96 just unified, and a public "connection" type in the Configuration package would freeze an API for one consumer. The cost is that Hosting and Configuration must ship the same version (section 16).

#### 4.3 Registration

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class StorytellerHostingServiceCollectionExtensions
{
    // IExecutionContext, ISubscriptionCatalog, IStorytellerDocuments when it is not registered yet, and the provider's
    // refresher and logging (AddStorytellerConfiguration). The options are the provider's (STORYTELLER_* when unset),
    // read and validated here; the provider-only options are ignored. The first registration wins.
    public static IStorytellerHostingBuilder AddStoryteller(this IServiceCollection services, Action<StorytellerConfigurationOptions>? configure = null);
}

public interface IStorytellerHostingBuilder
{
    IServiceCollection Services { get; }
}
```

- The options type is `StorytellerConfigurationOptions`, so one delegate serves the provider, the documents and Hosting. `Key` is optional at registration; `IExecutionContext` needs it when used, the catalog and the subscriptions take their organization, project and view from it.
- Settings are read once at registration, so a bad endpoint or key fails at start, and the catalog, who am I and the subscriptions' documents share one bound credential (one token cache). The provider and an earlier `AddStorytellerDocuments` read their own settings and bind their own copies.
- `IStorytellerDocuments` is registered with `TryAdd` over the same settings; an application that registered it before keeps its own. Either way it is a separate instance from the subscriptions' documents (8.1).
- `AddStorytellerConfiguration()` is called (all `TryAdd`, harmless without a provider), so `builder.Configuration.AddStoryteller()` plus `builder.Services.AddStoryteller()`, the study's shape (§5.2), has the provider's logs and refresher.
- Hosting's log categories are under `_42.Platform.Storyteller.Sdk.Hosting`; the reads of documents keep the provider's category `…Sdk.Configuring.StorytellerDocuments`.

#### 4.4 One call

```csharp
namespace Microsoft.Extensions.Hosting;

public static class StorytellerHostApplicationBuilderExtensions
{
    // builder.Configuration.AddStoryteller(configure), then builder.Services.AddStoryteller(configure): the provider,
    // its refresher and logs, who am I, the catalog and the documents, from one delegate.
    public static IStorytellerHostingBuilder AddStoryteller(this IHostApplicationBuilder builder, Action<StorytellerConfigurationOptions>? configure = null);
}
```

This is the useful part of the study's "client integration" (§5.4, "one call that reads the connection string and adds the configuration provider"), for any host, with or without Aspire; the variables come from `WithReference` (section 10) or from the environment. The provider requires a key, so this call does too.

### 5. Catalog snapshots

Immutable snapshots of what the API answered, in the Hosting namespace:

```csharp
// An annotation as the catalog read it. Its status is evaluated against a time, as it changes when ValidFrom or
// ExpiresAt passes.
public class StorytellerAnnotation
{
    public required string Key { get; init; }              // full key, type code lower-cased, as the documents' keys
    public required string AnnotationKey { get; init; }
    public required AnnotationType Type { get; init; }     // from the key
    public required string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Labels { get; }           // never null
    public IReadOnlyDictionary<string, object?> Values { get; }   // a deep copy per access; never null
    public bool IsDisabled { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public AuditStamp? Created { get; }                     // the SDK's AuditStamp, a copy per access
    public AuditStamp? Modified { get; }
    public DateTimeOffset? DeletedAt { get; init; }        // set only on a deleted annotation (who am I)
    public AnnotationStatus GetStatus(DateTimeOffset now); // AnnotationStatusExtensions.GetStatus
}

public sealed class StorytellerExecution : StorytellerAnnotation
{
    public required string SubjectName { get; init; }      // from the key, never from the body
    public required string ResponsibilityName { get; init; }
    public required string ContextName { get; init; }
    public IReadOnlyList<string> UnitNames { get; }        // the units of execution under it
}
```

- Built only from point reads (`GetAnnotation`), never from list items, whose `Values` are lossy (Current State). One internal mapper; the type and names come from the parsed key.
- `Values` holds what the SDK deserialized (with section 3, date-like strings stay strings); `JToken` values are deep-copied per access, as `StorytellerDocument.Values` does, and audit stamps are copied, so a caller cannot change a cached snapshot.
- `ToString()` gives the key and the type, never values.

### 6. Who am I: `IExecutionContext`

```csharp
public interface IExecutionContext
{
    // The annotation of the options' Key (STORYTELLER_KEY) and its ancestors. The first call reads; later calls return
    // the cached snapshot at once and refresh it in the background when it is older than the refresh interval.
    Task<StorytellerExecutionContext> GetAsync(CancellationToken cancellationToken = default);

    // Reads now, ancestors that were unreadable included; throws on failure and keeps the previous snapshot.
    Task<StorytellerExecutionContext> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class StorytellerExecutionContext
{
    public required string Key { get; init; }
    public required StorytellerAnnotation Annotation { get; init; }   // of the key; a StorytellerExecution for an exe key
    public IReadOnlyDictionary<AnnotationType, StorytellerAnnotation> Ancestors { get; init; }   // those that were read
    public IReadOnlyList<string> Unreadable { get; init; }            // ancestor keys the credential may not read
    public required DateTimeOffset ReadAt { get; init; }

    public StorytellerAnnotation? Subject { get; }          // the annotation of that type: the key's own or an ancestor
    public StorytellerAnnotation? Responsibility { get; }
    public StorytellerAnnotation? Usage { get; }
    public StorytellerAnnotation? Context { get; }
    public StorytellerExecution? Execution { get; }         // the key's own for exe, its parent for uxe

    // The study's "is it active" (3.8), from the key's annotation, its execution and its usage.
    public StorytellerSubscriptionStatus GetSubscriptionStatus(DateTimeOffset now);
}

public enum StorytellerSubscriptionStatus { Active, Disabled, Scheduled, Expired, Deleted, Unknown }

public sealed class StorytellerExecutionContextOptions
{
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);   // 0: only RefreshAsync reads again
}
```

- **Any key type.** An execution (the common case), a unit of execution (a job), or a responsibility (a worker that "holds the responsibility and discovers the subjects through the catalog", study §3.1).
- **Reads.** The key's annotation with `includeInactive=true` and `includeDeleted=true` (a disabled, expired or deleted subscription is an answer, not a failure), then its ancestors (`GetAllAncestorKeys`) concurrently with `includeInactive=true`. Each read is bounded by `ReadTimeout` and goes through the catalog transport.
- **Failures.**
  - The key's own annotation: a 404 without a body throws `StorytellerConfigurationException` (404, definitive) that names the key and says it does not exist; other failures throw with the catalog's classification (4.2).
  - An ancestor: a 403 with `AnnotationRestricted` (a machine restricted to its execution) leaves it out and adds its key to `Unreadable`, one information log per read of the context; such keys are not read again until `RefreshAsync`. A 404 leaves it out (an ancestor deleted after the key was read; the deletion cascades). Any other failure, `AccessDenied` included, fails the read as a whole.
- **No key.** `GetAsync` throws `StorytellerConfigurationException` naming `STORYTELLER_KEY` and `StorytellerConfigurationOptions.Key`.
- **Refresh.** Stale while revalidate (study §3.5, "a refresh never blocks a caller"): only the first `GetAsync` waits; later ones return the cached snapshot and start one background read (single flight) after `RefreshInterval`. A background read that fails transiently keeps the snapshot and logs a warning; one that finds the key deleted or gone (404) replaces the snapshot with one whose `Annotation.DeletedAt` is set (the time of the read for a 404), so the status turns `Deleted` instead of staying `Active` for ever. Knobs: `services.Configure<StorytellerExecutionContextOptions>(…)`.
- **`GetSubscriptionStatus(now)`:** `Deleted` when the key's annotation or its execution is deleted; else the first status that is not `Active` among the key's annotation, its execution and its usage; `Unknown` when all that were read are active but the usage was unreadable; else `Active`. For a key without an execution in its chain (`rst`, `sbt`, `unt`, …) it is the status of the key's annotation. A machine restricted to its execution therefore gets `Unknown` while its execution is active, and `Annotation.GetStatus(now)` for the execution alone; whether such machines may read their ancestors' metadata is a product question (section 14).
- **`SwitchKeyAsync`** of the provider's refresher does not move `IExecutionContext`, which follows the options' key; documented.
- **Entitlements.** `Usage.Values` and `Usage.Labels` are the material plans and seats are built from (§16.1). A typed `IsEntitled` is deferred (section 14).

### 7. `ISubscriptionCatalog`

```csharp
public interface ISubscriptionCatalog
{
    // One execution, in any status; null when it does not exist or is deleted. key: a full key, or an annotation key in
    // the organization, project and view of the options' Key.
    Task<StorytellerExecution?> GetExecutionAsync(string key, CancellationToken cancellationToken = default);

    // The informer: Added for every subscription of the first complete poll, then Added, Changed and Removed after every
    // poll. Each enumeration runs its own loop. Ends with OperationCanceledException when the token is cancelled.
    IAsyncEnumerable<StorytellerExecutionChange> WatchExecutionsAsync(string responsibility, string context, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default);
}

public sealed record StorytellerExecutionChange(
    StorytellerChangeKind Kind,
    StorytellerExecution Execution,
    StorytellerAnnotation Usage,
    StorytellerRemoval? Removal = null);

public enum StorytellerChangeKind { Added, Changed, Removed }

// Which annotation ended the subscription, and why.
public sealed record StorytellerRemoval(string AnnotationKey, StorytellerRemovalReason Reason);

public enum StorytellerRemovalReason { Deleted, Disabled, Scheduled, Expired, Missing }
```

#### 7.1 A subscription

The study's subscription informer answers "which subjects use my responsibility in my context" (§3.8), and its "is it active" is "the status of the usage and of the execution". A **subscription** of responsibility R in context C is therefore an execution `exe.S.R.C` that is active **and** whose usage `usg.S.R` is active. Events carry both annotations, so every tenant's usage facts (plans, seats) travel with it.

#### 7.2 Scope and names

- Organization, project and view come from the options' `Key` (any full key of the view, for example `@house.main.default.rst.invoicing` for a worker). Without a key, the call throws `StorytellerConfigurationException` naming `STORYTELLER_KEY`.
- `responsibility` and `context` are names (one key segment each), compared ordinally. An empty name or one with a `.` throws `ArgumentException`.
- `pollInterval` defaults to 30 s, with the provider's jitter (`interval × (0.9 + 0.2·r)`) on the options' `TimeProvider`; `TimeSpan.Zero` polls once. Each poll is scheduled after the previous one completed. The provider's `RefreshInterval` is not reused: a worker may poll its tenants every few minutes and its own document every 30 s.

#### 7.3 One poll

1. **Two lists**, each complete or failed: `…/annotations/rst.{R}/executions` and `…/annotations/rst.{R}/usages`, active only. They are the same single partition as `…/executions?responsibilityNameQuery=`, and they also serve a machine restricted to `rst.R`, which a worker's least privilege asks for. Follow `ContinuationToken` until it is absent; an empty page with a token is legal; a repeated key (pages have no order) keeps the last item. A token seen twice, or more than 1000 pages, fails the list with a message that names the server fix of section 2. Any failed page fails the poll.
2. **Join.** Keep the executions whose key has context C (the server ignores `contextNameQuery`) and whose usage `usg.S.R` is in the usage list. These are the candidates.
3. **Fingerprints.** For each candidate execution and its usage, the list item's `Modified` (instant, actor kind and id), or, when `Modified` is absent, a hash of the list item's members. List `Values` are lossy but deterministic, so the hash is stable between polls; the snapshots never come from list items.
4. **Diff** against the held subscriptions:
   - a candidate not held → read the execution and the usage (point reads, `includeInactive=true`); both active → `Added` with both snapshots;
   - a held candidate whose execution or usage fingerprint changed → read what changed; still active → `Changed` with the new snapshots;
   - a held subscription that is not a candidate → read both (`includeInactive=true`, `includeDeleted=true`) and confirm: the first of execution, usage that is deleted, inactive or gone gives `Removed` with a `StorytellerRemoval` (`Deleted`, `Disabled`, `Scheduled`, `Expired`, or `Missing` for a 404); both active (the list missed it) → no event, a debug log.
   - A point read that fails leaves that subscription as it was, without an event, and it is diffed again at the next poll; a 403 on it logs a warning that names the restriction.
5. **Order** within a poll: `Removed`, `Changed`, `Added`, each by key. `Removed` carries the last held snapshots.

Reads run at most 4 at once. Before the first complete poll there is nothing to emit; a failed poll emits nothing, keeps the held set, logs a warning (the failure's message, never a body) and polls again at the next interval.

The lists are the server's view of "active now", so a subscription whose `ExpiresAt` passes leaves at the first poll after it (study §3.5, pull on an interval).

**Empty responsibility.** The descendants route answers an empty list for a responsibility that does not exist, so the first poll reads `rst.R` once (`includeInactive=true`): a 404 fails it as a definitive `StorytellerConfigurationException` ("the responsibility R does not exist"). A first poll whose list holds executions of R but none in C logs one warning naming the contexts it saw.

#### 7.4 `GetExecutionAsync`

A point read with `includeInactive=true` and `includeDeleted=true`. A 404 without a body or a deleted execution returns null. A key that does not name an execution throws `ArgumentException`. Other failures throw `StorytellerConfigurationException` with the catalog's classification.

#### 7.5 The seam for a watch transport

The product owner asked for polling that is easy to exchange. The internal seam is a **change source**, not a list:

```csharp
internal interface ISubscriptionChangeSource
{
    // Batches of subscription changes, each with the keys whose documents may have changed. The polling source yields one
    // batch per poll and marks every held key dirty; a watch source would yield a batch per event and mark only the
    // keys an event names.
    IAsyncEnumerable<SubscriptionBatch> ReadAsync(SubscriptionScope scope, CancellationToken cancellationToken);
}

internal sealed record SubscriptionBatch(IReadOnlyList<StorytellerExecutionChange> Changes, IReadOnlyCollection<string> DirtyKeys);
```

- `PollingSubscriptionSource` implements 7.3 (lists, join, diff, confirmation) over an internal `IAnnotationPages` (the two lists and the point reads), so tests drive it without HTTP when they want to.
- `WatchExecutionsAsync` yields the `Changes` of each batch; the subscriptions (section 8) also read the documents of `DirtyKeys`.
- A watch (424-49, events "for every descendant the invalidator touched", resumed from a token) or a bulk read (C4) replaces the source; the public types do not change.

### 8. Subscriptions: the informer, the documents and named options

```csharp
public static class StorytellerHostingBuilderExtensions
{
    // A hosted informer for one responsibility in one context that keeps the resolved document of every subscription in
    // memory. The first overload takes the responsibility from the options' Key. One per container; a second call throws.
    public static IStorytellerHostingBuilder AddSubscriptions(this IStorytellerHostingBuilder builder, string context, Action<StorytellerSubscriptionOptions>? configure = null);
    public static IStorytellerHostingBuilder AddSubscriptions(this IStorytellerHostingBuilder builder, string responsibility, string context, Action<StorytellerSubscriptionOptions>? configure = null);

    // IOptionsMonitor<TOptions>.Get(subject) and IOptionsSnapshot<TOptions>.Get(subject) bind the document of that
    // subject, or its section; OnChange is raised for the subject. Needs AddSubscriptions.
    public static IStorytellerHostingBuilder AddSubjectOptions<TOptions>(this IStorytellerHostingBuilder builder, string? section = null)
        where TOptions : class;
}

public sealed class StorytellerSubscriptionOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);   // 0: one poll
    public StorytellerReadMode? Mode { get; set; }                            // default: the settings' mode (Resolved)
    public int MaxConcurrentReads { get; set; } = 4;                          // 1 to 64
    public bool WaitUntilReadyOnStart { get; set; }                           // the host's start waits for WaitUntilReadyAsync
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public interface IStorytellerSubscriptions
{
    string Responsibility { get; }
    string Context { get; }
    StorytellerSubscriptionsStatus Status { get; }

    // Completes when the first poll completed and every subscription in it had its first document read. Throws the
    // failure of the latest poll while the subscriptions were never ready and that failure is definitive (400, 401, 403,
    // 404), so a misconfigured worker does not wait for ever; the loop keeps polling.
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<StorytellerSubscription> GetAll();          // the subscriptions that have a document, by subject
    StorytellerSubscription? Find(string subject);

    // Added for every current subscription first, then the changes as they happen. Several watchers may run at once.
    IAsyncEnumerable<StorytellerSubscriptionChange> WatchAsync(CancellationToken cancellationToken = default);
}

public sealed class StorytellerSubscription
{
    public required StorytellerExecution Execution { get; init; }
    public required StorytellerAnnotation Usage { get; init; }
    public string Subject => Execution.SubjectName;
    public StorytellerDocument Document { get; }               // a new document per access over the last good entry
    public StorytellerConfigurationException? LastError { get; init; }
    public DateTimeOffset? LastErrorAt { get; init; }
    public required DateTimeOffset LastSucceededAt { get; init; }
}

public sealed record StorytellerSubscriptionChange(StorytellerChangeKind Kind, StorytellerSubscription Subscription, StorytellerRemoval? Removal = null);

// For health checks and diagnostics, like the provider's StorytellerConfigurationStatus.
public sealed record StorytellerSubscriptionsStatus
{
    public bool IsReady { get; init; }
    public DateTimeOffset? LastPollSucceededAt { get; init; }
    public StorytellerConfigurationException? LastPollError { get; init; }
    public int ConsecutivePollFailures { get; init; }
    public int Subscriptions { get; init; }
    public IReadOnlyList<StorytellerPendingSubscription> Pending { get; init; }   // without a first good document
    public int Failing { get; init; }                                             // with a LastError
    public bool IsDegraded { get; }                                               // a failed poll, pending or failing
}

public sealed record StorytellerPendingSubscription(string Key, StorytellerConfigurationException? LastError);
```

#### 8.1 The loop

A `BackgroundService` owns the polling change source (7.5) and a **private** `StorytellerDocuments` built from the Hosting settings: its own transport, circuit breaker and cache, so failing tenants cannot open the breaker of the application's own reads. For each batch:

1. apply the subscription changes;
2. read the documents of the dirty keys (every held subscription when polling), at most `MaxConcurrentReads` at once, through `GetEntryAsync` in the subscriptions' mode;
3. publish the changes to the watchers and the named options, after the reads, so each change carries its document.

The first poll starts with the host. With `WaitUntilReadyOnStart`, the service's start waits for `WaitUntilReadyAsync` up to `StartTimeout` and fails the host's start after it. Stopping cancels the loop and disposes the private documents service, which cancels the reads in flight.

#### 8.2 Rules

- **Added** when a subscription has its first good document. One whose first reads fail is pending: no event, not in `GetAll`, listed in `Status.Pending` with its last error, a warning per failed poll; it becomes `Added` when a read succeeds.
- **Changed** when the execution or the usage changed (7.3), or the document's data changed: the new entry's `Data` is not the same instance as the held one and `HasSameData` says it differs (ordinal keys and values). A new ETag with the same data, or a new version that changed only the configuration's labels or values, raises nothing; a 304 whose secret substitution refreshed to new values does. The held entry is always replaced by the newest good one, so `ETag` and `Version` stay current.
- **A failed read** keeps the last good document (static stability, study §3.6), sets `LastError`/`LastErrorAt` and logs a warning, for transient and definitive failures alike (a withheld secret, a 403 after a policy change, a 422 binding error). The next poll reads again; a good read clears the error.
- **Removed** when the source removes the subscription, with its `StorytellerRemoval`; the entry is evicted from the private cache (`Evict`), so its secrets do not linger.
- **Ready** after the first complete poll and one read attempt per subscription in it, whether the reads succeeded or not, so one broken tenant does not block a worker's start.
- **`Find(subject)`** looks up by subject name, ordinally. Within one responsibility and context a subject has exactly one execution, so names are unique.
- **`Document`** builds a new `StorytellerDocument` over the held entry on every access, so what one caller changes in it reaches neither the cache nor another caller (the contract of `StorytellerDocument`).

#### 8.3 Responsibility and key

`AddSubscriptions(context)` takes R from the options' key (its own responsibility, or the one in its chain: `rst.R`, `usg.S.R`, `exe.S.R.C`, `uxe.S.R.C.U`, `unt.R.U`); a key without one throws at registration. `AddSubscriptions(responsibility, context)` throws at registration when the key's chain names a different responsibility, and both throw when there is no key.

#### 8.4 Named options

- `AddSubjectOptions<TOptions>(section)` registers an `IConfigureNamedOptions<TOptions>` that, for a name that `Find` resolves, binds the subscription's document (or its section) with `ConfigurationBinder.Bind`, and an `IValidateOptions<TOptions>` that fails, for any other name than `Options.DefaultName`, with "no subscription of subject S in R/C (not ready yet, pending, or not subscribed)". A worker never runs a tenant on default options by mistake. Values shared by all subjects come from `ConfigureAll<TOptions>`; `Configure<TOptions>(config)` applies to the default name only.
- `AddSubjectOptions` also registers a closed-generic `IOptionsMonitor<TOptions>`: it wraps the framework's `OptionsMonitor<TOptions>(factory, sources, cache)`, and on every `Added` and `Changed` of subject S it removes S from the `IOptionsMonitorCache<TOptions>` and raises `OnChange` with `(Get(S), S)`, so code written for one tenant reacts per subject (study §3.5, "one change notification per applied document"). On `Removed` it removes S from the cache and raises nothing (`Get(S)` would fail validation); removal is a `WatchAsync` event. Other subjects keep their cached instance.
- `AddSubjectOptions` without `AddSubscriptions` throws `InvalidOperationException` at the first resolution, naming the missing call.

#### 8.5 Two watch APIs

`ISubscriptionCatalog.WatchExecutionsAsync` is the study's informer: cold (its own loop and lists), annotations only, `Added` when listed and read. `IStorytellerSubscriptions.WatchAsync` is hot (the hosted loop), with documents, `Added` when the first document is good. The docs say which to use: a worker with `AddSubscriptions` uses `WatchAsync`; `WatchExecutionsAsync` is for tools and processes that do not keep documents.

#### 8.6 Why not `IStorytellerDocuments`

The study's §5.3 writes "`IStorytellerDocuments.GetAsync(executionKey)`: an in-memory cache of resolved documents per key, refreshed with the informer". The `IStorytellerDocuments` of PR #127 revalidates on every call, and jobs depend on that. Changing it under `AddSubscriptions` would make the same call fresh in one application and up to an interval old in another. The informer's documents are therefore exposed by `IStorytellerSubscriptions`, and `IStorytellerDocuments` keeps its contract.

### 9. Errors, logging and cost

- **One exception type.** Catalog failures throw `StorytellerConfigurationException` (`Key`, `StatusCode`, `ErrorCode`, `IsTransient`), classified by `ReadFailures` (4.2). Messages name the key and the fix, never a body or a value.
- **Logs** never contain values or documents: keys, statuses, error codes, counts.
- **Scopes.** Who am I and the catalog need `Annotation.Read`; the subscriptions also need `Configuration.Read` (and `Secrets.Read` for secrets). A machine that `sform` created with `Configuration.Read` only runs the provider but fails who am I. The informer needs an unrestricted machine or one restricted to `rst.R`.
- **Cost per poll** with N subscriptions: two list requests per 1000 executions and usages of R (all contexts, as the server cannot filter the context), N conditional configuration reads (304s while unchanged), and point reads only for changes. The documents' cache keeps an entry for 2 × `SecretRefreshInterval` (2 h by default) after its last read: with a poll interval longer than that, every poll reads in full (200). The cache holds 1024 entries; beyond 1024 subscriptions evicted documents are read in full. The docs state these limits and point at the bulk read (C4) and the watch (424-49).

### 10. The Aspire hosting integration

#### 10.1 Project

`src/Platform/Storyteller/Aspire.Hosting/src/Aspire.Hosting.csproj`: assembly `42.Platform.Storyteller.Aspire.Hosting`, root namespace `_42.Platform.Storyteller.AspireHosting` (a root namespace with an `Aspire` segment would hide `Aspire.Hosting.*` inside the project's own namespaces, CS0234), `IsPackable=true`, `InternalsVisibleTo` for its tests. The public types live in Aspire's namespaces, as Aspire's own integrations do: the resource in `Aspire.Hosting.ApplicationModel`, the extensions in `Aspire.Hosting`. Usings are explicit (the core package adds no implicit usings). The only reference is `Aspire.Hosting`, added to `Directory.Packages.props` at 13.1.2 before `Aspire.Hosting.AppHost`. The package does not reference the SDK, so an AppHost is not coupled to the SDK's version and does not pull in its client encryption, resilience and file-system dependencies; the variable names are internal constants that a test compares with `StorytellerVariables` (10.6).

Test project `Aspire.Hosting/test/Aspire.Hosting.UnitTests.csproj` references `Aspire.Hosting`, the src project, `Sdk.NSwag` and `Abstractions.Annotations` (for the comparisons). Both projects go into `42.mono.slnx` after the `Api.Web` entries.

#### 10.2 The resource

```csharp
namespace Aspire.Hosting.ApplicationModel;

// A Storyteller API that runs outside the AppHost, or a project of the AppHost (the local platform). Not a process: it
// starts Running, and holds what WithReference passes on.
public sealed class StorytellerResource : Resource
{
    public ReferenceExpression Endpoint { get; }
    public string? Organization { get; }   // the view of short keys (10.4), set by WithView
    public string? Project { get; }
    public string? View { get; }
}
```

```csharp
namespace Aspire.Hosting;

public static class StorytellerResourceBuilderExtensions
{
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, string endpoint);
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, IResourceBuilder<ParameterResource> endpoint);
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, ReferenceExpression endpoint);
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, in ReferenceExpression.ExpressionInterpolatedStringHandler endpoint);

    public static IResourceBuilder<StorytellerResource> WithView(this IResourceBuilder<StorytellerResource> builder, string organization, string project, string view = "default");

    // Machine credentials. Secrets are parameters only, so they never sit in the AppHost's code.
    public static IResourceBuilder<StorytellerResource> WithApiKey(this IResourceBuilder<StorytellerResource> builder, IResourceBuilder<ParameterResource> apiKey);
    public static IResourceBuilder<StorytellerResource> WithClientCredentials(this IResourceBuilder<StorytellerResource> builder, IResourceBuilder<ParameterResource> clientId, IResourceBuilder<ParameterResource> clientSecret, string? tokenUrl = null, string? tokenScope = null);
    public static IResourceBuilder<StorytellerResource> WithClientCertificate(this IResourceBuilder<StorytellerResource> builder, string path, IResourceBuilder<ParameterResource>? password = null);

    // The STORYTELLER_* variables of the resource, for a project, an executable or a container.
    public static IResourceBuilder<TDestination> WithReference<TDestination>(this IResourceBuilder<TDestination> builder, IResourceBuilder<StorytellerResource> storyteller)
        where TDestination : IResourceWithEnvironment;

    // STORYTELLER_KEY: a full key, or an annotation key in the view of the referenced Storyteller resource.
    public static IResourceBuilder<TDestination> WithStorytellerKey<TDestination>(this IResourceBuilder<TDestination> builder, string key)
        where TDestination : IResourceWithEnvironment;

    // STORYTELLER_KEY of the execution exe.{subject}.{responsibility}.{context}.
    public static IResourceBuilder<TDestination> WithExecution<TDestination>(this IResourceBuilder<TDestination> builder, string subject, string responsibility, string context)
        where TDestination : IResourceWithEnvironment;
}
```

- **Endpoint.** A URL, a parameter, a `ReferenceExpression`, or an interpolated string such as `$"{api.GetEndpoint("http")}/api"` for the platform's own `Api.Functions` project (the handler overload, so a non-constant interpolated string does not bind to `string`). A literal URL is checked with the SDK's rule, copied: absolute, `https`, or `http` only to a loopback address or `localhost`; the process checks again.
- **No connection string.** The resource does not implement `IResourceWithConnectionString`: nothing reads a `ConnectionStrings__storyteller`, and with it Aspire's own `WithReference(resource, "name")` would bind silently and pass no `STORYTELLER_*`. Without it, that call does not compile. The resource is excluded from the manifest (`ExcludeFromManifest`); in publish mode the destinations' variables hold the endpoint and parameter expressions themselves.
- **Credentials.** One kind per resource, as the SDK reads them: an API key, client credentials, or a certificate; an API key with a certificate is the SDK's `CertificateAndApiKey`. Another combination throws `InvalidOperationException` when the second is added. The token-file and federated kinds are not offered: processes started by an AppHost have no projected or CI token. A certificate path is made absolute against the AppHost directory when it is added. Credentials belong to the resource, so projects that need different machines (for example one restricted to each execution) use one Storyteller resource each.
- **Dashboard and lifecycle.** `WithInitialState`: resource type `Storyteller`, state `Running`, the endpoint as a property (the URL, or the expression's text for a parameter or an endpoint reference). `WaitFor(storyteller)` proceeds (probed with DCP); to order starts after the local API, the destination waits for that project resource.

#### 10.3 `WithReference`

- Adds an environment callback that writes `STORYTELLER_ENDPOINT` (the endpoint expression) and the credential variables that are set: `STORYTELLER_API_KEY`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE`, `STORYTELLER_CLIENT_CERTIFICATE`, `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`. Parameters stay parameters: Aspire resolves them at start and the dashboard masks secrets; in publish mode they are references.
- Adds a reference relationship (the dashboard draws it) and an internal annotation that remembers the resource for `WithStorytellerKey`. A second `WithReference` to a Storyteller resource replaces the first (one set of `STORYTELLER_*` per process).
- **Containers.** A certificate path on the host does not exist in a container, so `WithReference` from a `ContainerResource` to a resource with a certificate throws `InvalidOperationException` (use an API key or client credentials). An `http` endpoint that Aspire rewrites to `host.docker.internal` is refused by the SDK in the container (only loopback may be `http`); the docs say so.
- The same variables reach .NET projects and Node.js, Python or Java executables: they are the contract every SDK and `sform` read (424-96).

#### 10.4 `WithStorytellerKey` and `WithExecution`

- Each registers its own environment callback for `STORYTELLER_KEY`; the last call wins, and `WithReference` is not needed for a full key.
- A key that starts with `@` is used as written. Any other key is an annotation key, prefixed with `@{organization}.{project}.{view}.` of the Storyteller resource the destination references, in the callback, so the order of calls does not matter.
- Both forms are checked with the rules of `FullKey` (an internal copy: five to eight segments, a known type code, the segment count of the type); a short key is checked after the prefix. A short key without a referenced resource that has a view throws `InvalidOperationException` from the callback, naming `WithView` and `WithReference` (Aspire fails that resource's start).
- `WithExecution(subject, responsibility, context)` checks the names (one segment each) and calls `WithStorytellerKey($"exe.{subject}.{responsibility}.{context}")`. The study's `.WithExecution("exe.northwind.invoicing.dev")` is `WithStorytellerKey`; a worker that holds a responsibility uses `WithStorytellerKey("rst.invoicing")`.

#### 10.5 What stays out

No client package (4.4), no change to `src/Platform/Aspire.Host/src/AppHost.cs` (it has no project that reads Storyteller; the docs show the wiring with `Api.Functions`), no token-file or federated credentials.

#### 10.6 Tests

Through `ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig().BuildAsync(executionContext, NullLogger.Instance, timeout token)`, without DCP, for a project (`AddProject(name, path, o => o.ExcludeLaunchProfile = true)`, which needs no file), an executable and a container; assertions look at the `STORYTELLER_*` and `ConnectionStrings__*` names only, as Aspire adds `OTEL_*` and others:

- each endpoint form and each credential kind give exactly their variables, and no `ConnectionStrings__*`;
- run mode resolves parameters (`Parameters:{name}`); publish mode gives their manifest expressions, so secrets are references; the endpoint-reference form is tested in publish mode (an unallocated endpoint waits in run mode);
- the internal names equal `StorytellerVariables.ENDPOINT`, `KEY` and the names of `StorytellerVariables.Credentials`, minus a listed "not offered" set (`TOKEN_FILE`, `SUBJECT_TOKEN_TYPE`, the deprecated aliases), so a new SDK credential name fails until it is mapped or listed;
- the key validator agrees with `FullKey.TryParse` over a table of keys;
- full and short keys, `WithExecution`, the last key wins, the error without a view (`Should.ThrowAsync`, the callback's exception leaves `BuildAsync`);
- invalid credential combinations, the endpoint rule, the certificate path made absolute, the container with a certificate;
- the initial state, the exclusion from the manifest, and that Aspire's `WithReference(resource, "name")` does not compile is a documented choice, not a test.

### 11. Tests of the Hosting package

`Hosting/test` follows `Configuration/test`: xunit, Shouldly, `FakeTimeProvider`, a `StubHttpHandler` passed through the internal `PrimaryHandler` seam, a dictionary environment through `GetEnvironmentVariable`. Helpers are copied from `Configuration/test` (`StubHttpHandler`, `RecordedRequest`, `RecordingLoggerFactory`, `TestCredential`, `CountingTimeProvider`, `GatedResponse`) rather than shared through a new project. Traps to respect:

- resilience as in `EngineFixture` (`MaxRetryAttempts = 0`, `MinimumThroughput = 1000`), or a queued 5xx is retried into the next answers and the breaker opens;
- `ReadTimeout` runs on the system clock (small real values in timeout tests);
- the documents' `MemoryCache` follows the fake clock (advancing past 2 × `SecretRefreshInterval` drops entries);
- concurrent reads (ancestors, documents, confirmations) answer by URL path through `StubHttpHandler.Otherwise`, not from a FIFO queue;
- advance the clock only after the loop armed its timer (`CountingTimeProvider`).

Cases:

- **Registration:** settings read at registration (a bad endpoint throws there); `IStorytellerDocuments` registered once; `AddStorytellerConfiguration` wired; one bound credential (`TestCredential.Bindings.Count == 1`) after who am I, the catalog and the subscriptions resolved; `IHostApplicationBuilder.AddStoryteller`; `AddSubscriptions` twice, without a key, with a responsibility that contradicts the key, and with the key's responsibility; `AddSubjectOptions` without `AddSubscriptions`.
- **Snapshots:** names from the key; deep copies of `Values` and audit stamps; `ToString` without values.
- **Who am I:** an exe key reads the key and its four ancestors (routes and flags asserted); a uxe key fills `Execution`; an rst key fills only `Responsibility`; `AnnotationRestricted` on an ancestor fills `Unreadable`, logs once and is not read again until `RefreshAsync`; `AccessDenied` on an ancestor fails; a 404 key throws 404 definitive; no key throws naming `STORYTELLER_KEY`; `GetSubscriptionStatus` at `ValidFrom`, at `ExpiresAt`, with a disabled usage, with an unreadable usage (`Unknown`), deleted; stale while revalidate (no wait after the first read, one background read); a transient background failure keeps the snapshot; a background 404 turns it `Deleted`; `RefreshAsync` throws on failure.
- **Catalog:** `GetExecutionAsync` maps a 404 and a deleted execution to null, refuses a non-execution key; the lists follow tokens through an empty page, keep the last of a repeated key, fail on a repeated token and after 1000 pages, fail the poll on a failed page.
- **Informer:** the first poll adds all with point-read snapshots (list `Values` never used); the context filter; an execution without an active usage is not a subscription; `Changed` on the execution's and on the usage's `Modified`, and by hash without `Modified`; each removal reason for the execution and for the usage; a removal refuted by an active read; a failed point read retried at the next poll; no `Removed` for a subscription never added; a failed poll emits nothing and keeps the set; event order; a missing responsibility; the warning for an empty context; interval `0`; jitter bounds; cancellation throws `OperationCanceledException`.
- **Subscriptions:** `Added` only with a good document; pending with its error in `Status`; a 304 is not a change; a new ETag with the same data is not a change; changed data, an execution-only change and a usage-only change are `Changed`; a failed read keeps the document and sets `LastError`, a good read clears it; `Removed` evicts the entry; `WaitUntilReadyAsync` with a failing tenant completes, and with a definitive list failure throws; `WaitUntilReadyOnStart` and its timeout; `WatchAsync` replays then streams, for two watchers; `MaxConcurrentReads` is honoured; `Document` is a new instance per access.
- **Named options:** `Get("northwind")` binds the subject's document and section, for `IOptionsMonitor` and `IOptionsSnapshot`, for two `TOptions` types; a change of one subject removes and notifies only that subject; an unknown subject fails validation; the default name is untouched.

### 12. Documentation

- `docs/Platform/Storyteller/hosting.md` (new): who am I, the catalog, the informer and its events, subscriptions and named options, scopes and restricted machines, cost and limits, the FeatureManagement note, the namespaces to import, examples for a single-subject service and a multi-subject worker.
- `docs/Platform/Storyteller/aspire.md` (new): the resource, credentials as parameters, keys, the local platform with `Api.Functions`, containers, what reaches non-.NET processes.
- Package readmes: `docs/Platform/Storyteller/Hosting/package-readme.md` and `docs/Platform/Storyteller/Aspire.Hosting/package-readme.md`, in the shape of the Configuration readme.
- `docs/Platform/Storyteller/configuration-provider.md`: links to the new pages where it says what the provider does not do (jobs without a fixed key, many subjects, feature flags).
- `docs/Platform/Storyteller/annotation-lifecycle.md`: paging now works (it already tells clients to follow the token).
- `docs/42for.net/platform/using-the-platform.md`: the two packages next to the provider.
- `CLAUDE.md`: `Configuration/`, `Configuration.KeyVault/`, `Hosting/` and `Aspire.Hosting/` in the list of Storyteller projects.

### 13. Phases and commits

| Phase | Content | Commits |
|---|---|---|
| 0 | This spec | `docs(platform): plan the hosting, informer and Aspire packages for .NET` |
| 1 | Section 2 | `fix(platform): page through annotation lists with their continuation token` |
| 2 | Sections 3-6 and 7.4 | `fix(platform): keep date-like strings in the annotation values of the C# SDK`; `feat(platform): read who am I and executions in a hosting package for .NET` |
| 3 | Sections 7.1-7.5 | `feat(platform): watch the subscriptions of a responsibility with a polling informer` |
| 4 | Section 8 | `feat(platform): keep the documents and options of every subscription in memory` |
| 5 | Section 10 | `feat(platform): pass the STORYTELLER_* variables from an Aspire AppHost` |
| 6 | Section 12 | `docs(platform): document the hosting and Aspire packages` |

One review file per phase in `docs/Platform/Storyteller/specs/reviews/`, committed after its phase.

### 14. Out of scope, and follow-up issues

Backlog issues created with the PR:

- **FeatureManagement and OpenFeature/OFREP** (answer 2): a per-subject `IFeatureDefinitionProvider` over the subscriptions' documents, and an OpenFeature provider. Single-key processes already work through `IConfiguration`.
- **Lossy `Values` in annotation lists** (bug): arrays, decimals and date offsets in list responses (Current State). The informer avoids them; the admin UI's lists and other clients do not.
- **Ancestor metadata for restricted machines** (product question): a machine restricted to its execution cannot read its usage, so its subscription status is `Unknown`.

Recorded only:

- typed entitlements (`IsEntitled`, plans, seats, quotas), until the billing module (`2026-10-01 Billing and Subscriptions for Annotations.md`) or an agreed shape of usage values;
- unit handlers, the health publisher, the OpenTelemetry resource detector, tenant middleware (study §5.1, §5.3, §5.5), and a ready-made `IHealthCheck` (`StorytellerSubscriptionsStatus` is its input);
- the Aspire client package (health, tracing, `Aspire:*` settings) and a sample worker in `src/Platform/Aspire.Host`;
- change events and watch (424-49), the bulk read (C4): the informer polls behind the seam of 7.5;
- server-side name filters of the execution list (the known bug), a 400 for an unparsable continuation token;
- status inherited from ancestors other than the usage;
- a last-known-good on disk for the subscriptions (a worker restarted during an outage serves no tenant until the API answers);
- several responsibilities per process (`AddSubscriptions` once per container; a keyed overload can be added without breaking);
- publishing the packages (a separate, later task per the 424-96 answers).

### 15. Differences from the CodeRabbit plan

1. **No Linear issues for the in-scope pieces.** The plan creates three in-scope and seven deferred issues. Here the in-scope pieces are phases of one PR; three backlog issues cover the deferrals that need a decision or a fix (section 14), the rest are recorded.
2. **PR #127 is merged and `IStorytellerDocuments` exists.** The plan assumed neither. Hosting reuses the provider's settings, transport, classification and documents (4.2) and keeps the contract of `IStorytellerDocuments` (8.6).
3. **`STORYTELLER_KEY` and the canonical names already exist** (424-96). The plan defines `STORYTELLER_KEY` and reads `STORYTELLER_API_URL` and `ConnectionStrings:{name}`; here the names are the SDK's (`STORYTELLER_ENDPOINT`), and there is no connection string at all (10.2).
4. **A subscription is execution and usage.** The plan follows executions only; the study's "is it active" includes the usage, which also carries the tenant's entitlement facts.
5. **The lists are the descendants routes**, not `GetExecutionsAsync`: they also serve a machine restricted to the responsibility, and the server ignores the context filter anyway.
6. **Active-only lists with point reads** for snapshots and removal reasons, rather than inactive executions in every list and list items as snapshots (list `Values` are lossy).
7. **Server fixes for paging** (section 2), which the plan's "follow tokens until null" needs, and a fix of date parsing in the SDK (section 3).
8. **Who am I reads the ancestors**, as the study's "who am I" row asks, survives a machine that may not read them (`Unreadable`, `Unknown`), and refreshes without blocking.
9. **The change source is the seam** (7.5), so a watch also spares the document reads, not only the list.
10. **Named options raise `OnChange` per subject** through a closed-generic monitor, and unknown subjects fail validation.
11. **The Aspire resource writes fixed `STORYTELLER_*` names** through its own `WithReference`, without a connection string, with `WithView`/`WithStorytellerKey` for short keys.

### 16. Risks

- **Internals shared between packages.** Hosting calls internals of Configuration (4.2). Both are versioned by `src/Platform/version.json` and built from one commit, but NuGet allows a newer Configuration under an older Hosting. When the packages are published, Hosting's dependency on Configuration must be pinned to the exact version, or the shared code moved into a public, versioned surface. The Configuration package already depends on the non-packable `Abstractions.Annotations`, and Hosting's public API uses its enums: publishing must ship it too.
- **Polling cost** (section 9). Fine for the wedge's tens to hundreds of tenants; the bulk read and the watch are the follow-ups.
- **Restricted machines.** A machine restricted to its execution gets who am I without ancestors and `Unknown` status, and cannot run the informer; a worker needs `rst.R` or an unrestricted machine.
- **Clocks.** List membership uses the server's clock, `GetStatus(now)` the process's; a skew shows until the next poll.
- **Aspire churn.** The integration targets 13.1.2; `ExecutionConfigurationBuilder` replaced an obsolete API in 13.x, and the next major may move more. Restoring `Aspire.Hosting` 13.1.2 warns about `MessagePack` 2.5.192 (NU1902/NU1903), as the AppHost does today.
- **The paging fix** changes what every annotation list returns after its first page: lists longer than a page were broken, now they page. The Cosmos-backed tests need the emulator container.
- **The SDK date fix** changes how `sform` prints date-like annotation values (as stored).
