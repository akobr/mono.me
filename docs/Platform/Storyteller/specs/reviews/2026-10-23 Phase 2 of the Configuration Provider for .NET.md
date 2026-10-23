# Phase 2 of the Configuration Provider for .NET

Linear: [424-85](https://linear.app/424fun/issue/424-85/create-config-provider-for-net-and-c).

## Overview

Phase 2 covers sections 5-12 of [Configuration Provider for .NET](../2026-10-23%20Configuration%20Provider%20for%20.NET.md): the new package `42.Platform.Storyteller.Configuration` with `AddStoryteller`, its options and environment contract, the transport, the reader, flattening, the last-known-good cache, the engine with refresh, rollback and key switching, and the container integration. It also adds the public resolver contract of section 13 (`ISecretReferenceResolver`, `SecretResolutionException`, `SecretReferenceResolver.FirstOf` and the `ReferenceResolver` option), without substitution, which is Phase 3. The phase is commit `39d55013`. The final code review of the whole branch found four defects in this phase's code; their fixes are listed under "Review fixes".

| Project | Passed (final state of the branch) |
|---|---|
| `Configuration/test` (new) | 541, 2 skipped (the Unix file-mode tests, on Windows) |
| `Sdk.NSwag/test` | 199 |

## What Was Done

### 5. Packages and projects

- `Configuration/src` (assembly `42.Platform.Storyteller.Configuration`, namespace `_42.Platform.Storyteller.Sdk.Configuring`) and `Configuration/test`, in `42.mono.slnx`, with the references of the spec. `IsPackable`, `Description` and `PackageTags` as in `KeyValues.ClientEncryption`, and `InternalsVisibleTo` for the tests.
- `Directory.Packages.props`: `Microsoft.Extensions.Configuration.EnvironmentVariables` 10.0.12, and, for the tests, `Microsoft.Extensions.Options.DataAnnotations` 10.0.12 (the `ValidateDataAnnotations` rollback tests). `Microsoft.Extensions.TimeProvider.Testing` came in Phase 1.
- The test project also references `Microsoft.Extensions.Hosting`, for the host-lifecycle tests, and `Libraries/Testing/System.IO.Abstractions.Trace`, to trace the file operations of the cache.

### 6. Options and the environment contract

- `StorytellerConfigurationOptions`, `StorytellerReadMode` and `SourcePlacement` as specified. The test seams are internal properties of the options: `GetEnvironmentVariable`, `PrimaryHandler` and `NextRandom`.
- `Settings/SettingsReader.cs` fills what the callback left `null` from the environment through `VariableReader`, validates once, and binds the credential to the validated endpoint, the organization and project of `Key`, `ConfigurePrimaryHandler`, `TimeProvider` and `Resilience`. `SettingValues` parses durations, booleans and modes. Messages name the variable or the property and never the value; an invalid endpoint is not echoed, because it may carry user info.
- **Rules the spec did not give:**
  - a blank string set in code counts as unset;
  - `RefreshInterval` is at most 45 days, the longest interval whose jittered delay a system timer takes;
  - `SecretRefreshInterval` and `ReadTimeout` must be positive, or `Timeout.InfiniteTimeSpan`;
  - the endpoint has no query or fragment, because paths are appended to it;
  - `CacheDirectory` becomes a full path when the source is added, so a later change of the working directory does not move the cache;
  - `Resilience` is copied and validated by building its pipeline once.
- `Settings/LocalUrl.cs` is a copy of the SDK's `LocalUrlRule` (**deviation**: the spec has one rule, but the SDK keeps it internal, so the provider has its own copy).

### 7. Transport

- `Transport/TransportFactory.cs` builds the stack of the spec: `StorytellerCredentialHandler`, a `ResilienceHandler` with a pipeline per transport (it holds the breaker's state), and a `SocketsHttpHandler` with the certificate, `AllowAutoRedirect = false` and `PooledConnectionLifetime` of 5 minutes, both set again after `ConfigurePrimaryHandler`. `HttpClient.Timeout` is infinite.
- The `ConfigurationsApiClient` gets `AccessTokenFactory = null`, and `X-ARR-ClientCert` only for local `http`. A test's primary handler is not disposed with the transport.

### 8. Reading a document

- `Reading/DocumentIdentity.cs` (the identity of the spec, the type code lower-cased), `Reading/DocumentReader.cs` and `Reading/ReadOutcome.cs` (`Changed`, `NotModified`, `Withheld`, `Definitive`, `Transient`).
- **Classes the spec did not list:**
  - a `2xx` whose body does not parse is transient: a proxy or a captive portal in between;
  - a `3xx` is definitive: the redirect is not followed, and the message says to set the final URL;
  - an exception the reader does not know, such as one from a credential of the application, is transient: no verdict of the platform;
  - a `304` to a read that sent no tag the provider can apply is transient (`StorytellerConfigurationEngine.UnexpectedNotModified`).
- An `ErrorResponse` that the generated client left as text (the undeclared `424` and `503`) is parsed for the message, the hint and the error code.
- `Reading/UnboundBindings.cs` finds the JSON paths of strings that start with `@`; the warning lists up to 20.

### 9. Flattening and validation

- `Flattening/DocumentFlattener.cs`, `JsonPaths.cs` and `InvalidDocumentException.cs`, with the rules of the spec. **Beyond the spec:** a content that is not a JSON object is invalid, and a `null` content (a version-0 configuration) has no keys. Every property name is checked, also under a `null` or an empty container, so validity does not depend on the values.
- `Configuration/test/Vectors/flattening.json` pins the study example and every rule (`name`, `document`, `keys` or `error`).

### 10. Last-known-good cache

- `Caching/LastKnownGoodCache.cs`, `CacheIdentity.cs`, `CacheEntry.cs`, `CacheControlDirectives.cs` and `CredentialFingerprint.cs`, with the `no-store` rules, the protection, the trust rules and the file handling of the spec. Files have the extension `.cache` and a format version. A protected file starts with the marker `2S-STORYTELLER-CACHE-PROTECTED/1`.
- **`Effective` mode** writes an entry unless the answer has `no-store`. The spec gives the rule for `Resolved` mode only, where a missing `Cache-Control` counts as `no-store`.
- **Deviation: the fingerprint of a credential.** The SDK keeps the fields of its credentials internal, so the fingerprint is read from the public surface: the type of the credential, and a SHA-256 over its `ToString()` (every SDK kind prints its principal there and no secret), the thumbprint of its certificate, and the machine segment of an API key. The machine of an `ApiKey` authorization counts only when `GetAuthorizationAsync` completes synchronously: a token request can be the very request that fails when the cache is needed. The docs ask a credential of the application that fetches its key asynchronously to name its principal in `ToString()`.
- Cache writes are serialized with the deletions of `304`s, per identity and by snapshot sequence, so an answer that a later `304` said not to store is not written.

### 11. Source, provider and engine

- `StorytellerConfigurationSource`, `StorytellerConfigurationProvider` and `Engine/StorytellerConfigurationEngine.cs`, with `ConfigurationSnapshot`, `EngineLifetime` (the epoch), `RunScope` (`ReadTimeout` and the caller's token) and `SwitchTarget`. The state lives in the engine as specified: one poller and one cache writer per source however often it is built, a copy of the data per provider, and publishing by sequence number.
- The startup table of the spec, except the resolver rows, which came with substitution in Phase 3. The fallback to the cache runs within what is left of `ReadTimeout`.
- `AddStoryteller` in the namespace `Microsoft.Extensions.Configuration`, with the two placements: `builder.Sources.Insert` in front of the last unprefixed environment source, otherwise `builder.Add`.
- **Beyond the spec:** a `200` with a new tag but the same keys and values reloads nothing.

### 12. Refresh, refresher and key switching

- The apply pipeline, the rollback with the rejected `ETag`, the one-shot timer with jitter, `TryRefreshAsync` with one shared queued run, `SwitchKeyAsync` and `StorytellerConfigurationStatus`, as specified.
- **Deviation: a notification lane.** The spec runs steps 5 and 6 on the thread of the run, after it releases the run gate. They run on a lane instead (a `SemaphoreSlim`), one pass at a time. A pass publishes the newest snapshot to every provider, reloads each, then confirms and caches it or rolls it back. Two runs that swap one after the other therefore never notify at the same time, and a reload error belongs to the snapshot of its pass.
- **Calls from change callbacks.** A run started from a change callback (an `AsyncLocal` marks the notification) takes the lane only if it is free. Otherwise it completes at its swap, and the pass that holds the lane notifies its snapshot next. So `TryRefreshAsync` and `SwitchKeyAsync` with `.GetAwaiter().GetResult()` in an `IOptionsMonitor.OnChange` callback do not deadlock. A switch from a callback whose document then does not bind does not throw; the rejection shows in the status.
- **Known limit, in the docs:** a reload raised elsewhere during a notification (`IConfigurationRoot.Reload()` by the application, or another provider of the root) can coalesce with it, so a rebind error can surface in that other reload and the document is kept instead of rolled back.
- **Container:** `AddStorytellerConfiguration` registers `IStorytellerConfigurationRefresherProvider` (`Container/StorytellerEngines.cs` walks `IConfigurationRoot.Providers`, chained configurations included), the single `IStorytellerConfigurationRefresher`, and `StorytellerLoggingHandover`, an `IHostedLifecycleService`. `Logging/SwitchableLoggerFactory.cs` lets the engine log to the options' factory, the null factory, or the container's after the hand-over. At the hand-over a degraded start is logged.

**Tests** (`Configuration/test`, namespace `_42.Platform.Storyteller.Sdk.Configuring.UnitTests`): every item of the Phase 2 list in spec section 17, with `FakeTimeProvider`, `MockFileSystem`, a recording stub handler, a recording logger factory, a test protector and a test credential. The Unix file modes are `UnixFact` tests, skipped on Windows.

## Reviews

All of these were fixed before `39d55013`.

- **Three reviewers** read the code through the lenses of concurrency and lifecycle, the cache with security and HTTP, and conformance with tests. They found 13 defects, three of them reported twice:
  - a `Load()` that failed left its provider attached, so the engine never stopped;
  - the fingerprint of the cache called the credential, so a slow token source used up `ReadTimeout` before the cache entry was read;
  - a document that did not bind could stay applied and be cached after a change of metadata only;
  - a property renamed only in case was never applied;
  - a `RefreshInterval` over 45 days broke the system timer after the startup had committed;
  - an oversized `format` number in a cache file crashed the startup;
  - the error of a rejection carried the value that did not convert, through its inner exception and `Status.ToString()`;
  - an older run could write a plain entry that a newer `304` had deleted;
  - an infinite `SecretRefreshInterval` was always due;
  - the cache fallback got a fresh `ReadTimeout`.
- **A verifier** found two more:
  - a rollback with two roots restored the rejected data and cached it;
  - the API-key credentials of one application type shared a fingerprint.
- **A last verification** found a defect of high severity. While a root still handles one reload, `ChangeToken` coalesces the next one and runs its rebind on the first thread. So a document that failed options validation was confirmed and cached, and `IOptionsMonitor.CurrentValue` kept throwing.
  - The per-root tracking that the earlier fixes had added was replaced by the notification lane of section 12.
  - That also removed four smaller issues: a same-content `200` after a rollback, a stale confirmed snapshot after a restart, a rejection counted twice, and an early `Load()` marking.
  - Every case has a regression test.

## Review fixes

The final review of the branch found these in Phase 2 code. All are fixed and tested, in `240d75be`:

1. **`505` was transient in the reader** but definitive in the SDK's pipeline, which does not retry it. `DocumentReader.IsTransientStatus` now treats `501` and `505` alike, as definitive. The comments of `ReadOutcome` and the failure-class table of `configuration-provider.md` say so. `424` stays transient and is not retried, as the spec intends.
2. **A `2xx` whose body did not parse** was reported as "Reading … failed (200)." The message now says that the answer did not parse as a configuration, and to check that the endpoint is the API base, `/api` included, and that nothing in between rewrites the response. It quotes neither the body nor the SDK's message, which contains the body, and a resolved body may hold secrets.
3. **The `no-store` notice was lost at startup.** The startup runs before `AddStorytellerConfiguration()` hands over the container's logger factory, so the notice went to the null logger, and the once-per-configuration flag was set anyway. `LastKnownGoodCache.ReportNotCached` now sets the flag only when the logger takes Information, and `StorytellerConfigurationEngine.UseLoggerFactory` reports the notice of the applied answer at the hand-over, as it already did for the other warnings. The docs list the notice in the hand-over. Tests: `TheNoStoreNotice_WaitsForALogger_ThenComesOnce` and `ANoStoreAnswerAtTheStartup_IsReportedOnceTheHostStarts_AndOnlyOnce`.
4. **`SettingsReader.CopyResilience`** copied the resilience options property by property. It now uses the SDK's public `Clone()`. Negative `MaxRetryAttempts` and `AttemptTimeout`, which the pipeline used to skip, now fail when the source is added (Phase 1 fix 3).
