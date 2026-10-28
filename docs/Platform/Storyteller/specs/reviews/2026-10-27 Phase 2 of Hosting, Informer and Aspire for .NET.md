# Phase 2 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 2 covers sections 3-6 and 7.4 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md). The C# SDK keeps date-like strings in annotation values. The new package `42.Platform.Storyteller.Hosting` reuses the provider's settings, transport, failure classification and documents through internals of the Configuration package, and registers who am I (`IExecutionContext`), the catalog's `GetExecutionAsync` and the immutable snapshots of what the catalog read. A follow-up fixes `sform history`, which the SDK change affected, and the scope error of `GetExecutionAsync`. The final review of the whole branch changed the construction of the snapshots; that is listed under "Final review".

| Commit | Content |
|---|---|
| `b7378229` | Section 3: `DateParseHandling.None` for the annotations client |
| `b40a0e0d` | Sections 4-6 and 7.4: the Hosting package, the refactors of Configuration, who am I, `GetExecutionAsync` |
| `43fe7b25` | Follow-up: audited instants in `sform history`, a catalog key without scope, more status tests |
| `29d18041` | Final review: snapshots that applications can build for test doubles |

| Project | Passed (final state of the branch) |
|---|---|
| `Sdk.NSwag/test` | 286 |
| `Configuration/test` | 548, 2 skipped (unchanged by the refactors) |
| `Hosting/test` (new), the classes of this phase | `RegistrationTests` 39, `SnapshotTests` 21, `ExecutionContextTests` 29, `GetExecutionTests` 20 |
| `Cli/test` | 818 |

## What Was Done

### 3. Date-like strings in annotation values (SDK)

- `Sdk.NSwag/src/ApiSdk.partial.cs` gives `AnnotationsApiClient` the `UpdateJsonSerializerSettings` of `ConfigurationsApiClient`: `DateParseHandling.None`. Typed members (`ValidFrom`, `ExpiresAt`, `AuditStamp.At`) still parse.
- `Sdk.NSwag/test/AnnotationsApiClientTests.cs`: a date-like string with an offset in `Values` stays the stored string; typed dates still parse.
- **Follow-up in `sform`** (`43fe7b25`). The audit entries of `sform history` carry `ValidFrom` and `ExpiresAt` changes as values, which now arrive as text. `Cli/src/Output/AuditConsoleExtensions.cs` parses them and prints them as timestamps with their offset again; value changes stay as stored. A test in `AuditConsoleExtensionsTests` covers both.

### 4. The Hosting package

**4.1 Project.** `Hosting/src/Hosting.csproj` and `Hosting/test/Hosting.UnitTests.csproj` as specified: assembly `42.Platform.Storyteller.Hosting`, root namespace `_42.Platform.Storyteller.Sdk.Hosting`, packable, `InternalsVisibleTo` for its tests, references to the Configuration project and the three `Microsoft.Extensions.*` packages, both in `42.mono.slnx`. The test helpers are copied from `Configuration/test`.

**4.2 Reuse of the provider's internals.** `Configuration.csproj` gives `InternalsVisibleTo` to Hosting and its tests.

- `TransportFactory` is split into `CreateHttpClient(settings)` and `CreateSdkConfiguration(settings)`; `Create` builds the configuration transport from them. Hosting's `CatalogTransport` builds an `AnnotationsApiClient` over its own `HttpClient`, so the catalog has its own circuit breaker.
- The failure classification moved from `DocumentReader` to `Reading/ReadFailures.cs`, with a describer per kind of read. `DocumentReader` keeps its messages, and its tests pass unchanged. Hosting's `CatalogReader` describes its reads: "needs Annotation.Read", "a machine restricted to an annotation reads that annotation and its descendants only", "does not exist".
- `StorytellerDocuments` gained `internal GetEntryAsync(FullKey, StorytellerReadMode, CancellationToken)` and `internal Evict(FullKey, StorytellerReadMode)`; `HasSameData` moved from the engine to `DocumentEntry`.
- `RunScope` bounds every catalog read by `ReadTimeout`, and `SwitchableLoggerFactory` wraps Hosting's loggers.

**4.3 Registration.** `services.AddStoryteller(configure)` in `Microsoft.Extensions.DependencyInjection` reads the settings with `SettingsReader.ReadForDocuments` once, at the call, and registers `HostingSettings` (which also marks the container as set up), `CatalogTransport`, `CatalogReader`, `IExecutionContext`, `ISubscriptionCatalog` and, with `TryAdd`, `IStorytellerDocuments` over the same settings, so they share one bound credential. It calls `AddStorytellerConfiguration()`. The first registration wins: a later call returns a builder over it without reading its options. `StorytellerExecutionContextOptions.RefreshInterval` is validated as zero, positive or infinite.

**4.4 One call.** `IHostApplicationBuilder.AddStoryteller(configure)` in `Microsoft.Extensions.Hosting` calls `builder.Configuration.AddStoryteller(configure)` and then `builder.Services.AddStoryteller(configure)`.

### 5. Catalog snapshots

- `StorytellerAnnotation` and `StorytellerExecution` as specified. One internal `AnnotationMapper` builds them from point reads; the type and the names come from the parsed key, with the type code lower-cased. `Values` and the audit stamps are copied, and copied again per access; `ToString()` prints the key and the type.
- A private copy constructor and `WithDeletedAt` let who am I turn a snapshot deleted when its key is gone.

### 6. Who am I: `IExecutionContext`

- `Executions/ExecutionContextService.cs` reads the key with `includeInactive` and `includeDeleted`, and its ancestors concurrently with `includeInactive`, through the catalog's transport.
- Failures as specified: a missing key throws a definitive `404` that names the key and reads no ancestor; a restricted ancestor goes to `Unreadable` with one information log per read and is not read again until `RefreshAsync`; a missing ancestor is left out; any other ancestor failure fails the read; no key throws naming `STORYTELLER_KEY` and `StorytellerConfigurationOptions.Key`.
- Stale while revalidate: concurrent first calls share one read, and a failed first read is not kept; later calls return the snapshot, and the first call after `RefreshInterval`, counted from `ReadAt` or from the start of the last background read, starts one background read. A transient background failure keeps the snapshot and warns; a background `404` keeps the snapshot with `DeletedAt` set to the time of the read. A caller's token ends its own wait only. Disposal cancels the reads in flight, and a call after it throws `ObjectDisposedException`.
- `StorytellerExecutionContext.GetSubscriptionStatus(now)` follows the spec. **Beyond the spec** (`43fe7b25`): a unit of execution whose execution is unreadable is `Unknown` while the rest is active, as an execution with an unreadable usage is; tests cover it, and an inactive execution of a `uxe` key.

### 7.4 `GetExecutionAsync`

- A point read with `includeInactive` and `includeDeleted`; a `404` without a body or a deleted execution returns `null`; a key that does not name an execution throws `ArgumentException`.
- **Follow-up** (`43fe7b25`): an annotation key without `STORYTELLER_KEY` threw `ArgumentException`; it throws `StorytellerConfigurationException` naming the variable and the option, as spec 7.2 and who am I do. A key that does not parse still throws `ArgumentException`. The tests assert the whole messages.

## Final review

- **Snapshots that could not be built outside the package** (`29d18041`). `StorytellerAnnotation`, `StorytellerExecution` and `StorytellerExecutionContext` had public constructors and `required` `init` members, but `Labels`, `Values`, `Created`, `Modified` and `UnitNames` had `internal init`, so an application could not build a usage with its entitlement facts for a fake `IExecutionContext` or `ISubscriptionCatalog`. Their `init` accessors are public now and copy their input: `Values` deeply, the stamps through JSON, the lists into read-only copies. `Ancestors` and `Unreadable` of the context copy too, so a snapshot stays immutable when a test changes its own objects afterwards. The mapper hands the SDK's objects to the initializers instead of copying them twice. `SnapshotTests` checks by reflection that the accessors are public (the test project sees the internals), that what an initializer is given is copied, and that `null` gives empty collections. `hosting.md` has a "Test doubles" bullet. The alternative, internal constructors, would have kept the surface smaller, and opening it later would have broken nobody. Public construction was chosen because the interfaces are public, an application's tests fake them, and the spec's `required … { get; init; }` members already assumed it.
