# Cheap 304s for Configurations with Bindings

Linear: [424-51 Cheap 304s for configurations with bindings](https://linear.app/424fun/issue/424-51/cheap-304s-for-configurations-with-bindings)

## Problem

424-47 (PR #107, merged) gave the configuration reads a weak `ETag` and a `304 Not Modified` for `If-None-Match`. A resolved read of a document **with bindings** still evaluates every binding before it compares the tag, because the tag is a digest of the resolved body. Each poll costs:

- the key/value lookups, with their `reads.binding` meters, and for secrets the `reveals` meter and a `BindingRead` audit count;
- the Key Vault `GetSecret` calls, which are not cached and count against vault throttling;
- the `@annotation` point reads;
- the JSON Logic and JSON-e work.

The 304 saves only the transfer. Most real configurations have bindings (secrets come from `@secret(...)` or a vault), and those are the ones that providers and agents poll.

The goal: a resolve records every external input it reads, with a cheap version marker, into a **dependency manifest**. The tag of a resolved read becomes a digest of the effective inputs and that manifest. A later conditional read finds the manifest of the tag it was sent, revalidates the markers in bulk without evaluating anything, and answers 304 when nothing moved.

The inputs to this spec are:

- the issue;
- a read-only map of the code, tests and docs at `origin/main` `7b1975c3` (seven parallel readers: binding runtime, object resolver and JSON-e, key/value backend, annotation and configuration backend, API, test infrastructure, documentation);
- the CodeRabbit coding plan attached to the issue (`2026-10-23 Cheap 304s for configurations with bindings.md`), used as a second opinion and a source of ideas, not as the decision. Section 13 lists where this spec differs from it.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The resolved read

- **Handler.** `Api.Functions/src/V1/ConfigurationHttp.cs:142-197` (`GetConfigurationResolved`):
  1. scope and access check, key parsing, `GetSecretRightsAsync(...).AsAmbient()`;
  2. `GetRawConfigurationAsync(fullKey)`, null answers 404;
  3. `HasBindings(effective)`: without bindings the read answers with the `r` tag and never calls the resolver;
  4. with bindings, `ResolveConfigurationAsync(fullKey, effective, rights.CanReadSecrets)`, then the `b` tag: HMAC-SHA256 over the resolved body, keyed by `ConditionalReads:EntityTagKey` or a per-process key;
  5. the headers (`X-2S-Secrets-Withheld`, `Cache-Control: no-store` or `private, no-cache`), then `OkOrNotModified`.
- **Tags.** `Api.Functions/src/ConditionalReads/ConfigurationEntityTags.cs`: `W/"<kind>.<digest>"`, 16 bytes of SHA-256 (or HMAC) base64url, length-prefixed inputs. The `r` tag hashes the full key, the rights code (`s`, `w`, `p`), `Version`, `Hash` and the compact JSON of the effective `Content`.
- **`If-None-Match`.** `ConditionalRequestExtensions.MatchesIfNoneMatch` parses the header strictly and returns a bool; nothing exposes the listed tags. `GetEntityTagKey` resolves `IOptions<ConditionalReadOptions>` from `FunctionContext.InstanceServices`, so the `ConfigurationHttp` constructor did not change in 424-47.
- **Resolve step.** `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:207-224`: the only production `BindingScope` (`Document` = a deep clone of the effective content for `@config`, `Context` = `ConfigurationBindingContext(key)`). It resolves `effective.Content` **in place** and returns the same instance. The scope never leaves the service.

### The binding runtime

- **Scope and requests.** `Binding.Abstractions/src/BindingScope.cs` has `Document` and `Context` only. Sources get a `BindingRequest` (`Path`, `IncludeSecrets`, `Context`), functions a `BindingFunctionRequest` (`Name`, `Arguments`, `IncludeSecrets`, `Document`, `Context`). Neither sees the scope.
- **Evaluator.** `Binding.Language/src/BindingEvaluator.cs` builds the only production `BindingRequest` (`:128`) and `BindingFunctionRequest` (`:161-168`). An unregistered source or function returns null and the string stays as stored (`:123-126`, `:135-146`). `BindingExecutor` builds a new evaluator per `@` string over the live registry dictionaries.
- **Registry.** `BindingExecutor` is filled once by the `AddConfigurationBindings` factory (`Binding.Core/src/EntryPoint.cs:17-46`). `IBindingRegistry` stays writable after startup. `GetCatalog()` lists the names, sorted.
- **Object resolver.** `Binding.Object/src/ConfigurationBindingResolver.cs` walks sequentially and passes the same scope to every string bind: top level, `$context`, and the post-pass over an engine result (`:210`, `:213`). The engine call is `:171`. JSON Logic is pure. JSON-e pushes `now = DateTime.Now` on every evaluation (`Binding.JsonE/src/EvaluationContext.cs:44`) and offers `$fromNow` and `fromNow()`.
- **Built-in inputs.**
  - `Binding.Core/src/KeyValueBindingSource.cs`: `@path`, `@public(...)`, `@secret(...)`. A static `ConditionalWeakTable` memo per read context, keyed `s:` or `p:` plus the lower-cased key, so one key can be looked up twice in one read. It calls `IKeyValueService.LookupAsync`.
  - `Binding.Core/src/AnnotationBindingFunction.cs:72-121`: `@annotation`, one `IAnnotationService.GetAnnotationAsync` per occurrence, of the configured annotation or of an ancestor (possibly in another partition).
  - `Binding.Language/src/ConfigBindingFunction.cs`: `@config` reads only the snapshot in `Document`.
  - `Binding.Azure.KeyVault/src/KeyVaultBindingSource.cs`: without `IncludeSecrets` it returns null and calls nothing; otherwise `GetSecretAsync(name)` (latest version), 404 is null.

### Storage markers

- **Key/values.** `Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs:515-546` (`LookupAsync`):
  - an invalid key is null without a read;
  - `ReadAsync` returns `(entity, etag)` and the ETag is dropped (`:523`);
  - null for a missing or soft-deleted item, and for a secret without `includeSecrets`;
  - then `UnprotectAsync`, the `reads.binding` meter, and for secrets `reveals` and `RecordBindingRead`.

  The catalog item is `kv.{lower-cased key}` in partition `{project}.kv.{view}` of the container `org.{organization}`. A soft delete and a restore keep `Version` and change `_etag`; a purge removes the item, and a new create starts again at version 1. An identical `SetAsync` writes nothing.
- **Annotations.** `CosmosAnnotationService.GetAnnotationAsync` (`Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs:60-64`) loads the annotation item only (`withConfiguration: false, withAudit: false`) and returns it when active. `AnnotationItemStore.LoadAsync` (`:99-135`) has the item's ETag in `node.ETag` (`:119`); the public `Annotation` has none. `AnnotationItemStore.ReadStatesAsync` (`:53-97`) already reads many items in one `ReadManyItemsStreamAsync` and parses the raw documents.
- **No interface returns a marker.** `IKeyValueService`, `IAnnotationService` and `IConfigurationService` have no default members, except `IConfigurationBindingResolver.ContainsBindings`.

### Caching and options

- `AddCosmosDbAnnotations` registers a shared `IMemoryCache` without `SizeLimit`; its users set entries without `Size`. `EnvelopeValueProtector` owns a separate size-limited `MemoryCache`.
- `ConditionalReadOptions` (section `ConditionalReads`) has `EntityTagKey` only.

### Tests that pin today's behaviour

- `Api.Functions/test/ConfigurationConditionalReadTests.cs`: `GetConfigurationResolved_WithBindings_TagsTheResolvedBody` (`b.` prefix, three resolves for three reads), `_TagsOverBindings_FollowTheConfiguredKey`, `Tags_SplitTheThreeSecretRights`. Its `ConfigurationProxy` throws for any member it does not know.
- `Api.Functions/test/MachineRestrictionHttpTests.cs:84-103` pins the call sequence `GetRawConfigurationAsync`, `HasBindings`, `ResolveConfigurationAsync`.
- Hand-written doubles: `IKeyValueService` (`Backend.CosmosDb/test/CountingKeyValueService.cs`, `Api.Functions/test/KeyValueTestDoubles.cs` `RecordingKeyValueService`), `IAnnotationService` (`Backend.Core/test/Transferring/FakeAnnotationService.cs`, the private fake in `Backend.CosmosDb/test/AnnotationBindingFunctionTests.cs`, `Api.Functions/test/AnnotationsHttpTests.cs` `RecordingAnnotations`), `IConfigurationService` (`Backend.Core/test/Transferring/TransferFakes.cs` `FakeConfigurationService`).
- `Backend.CosmosDb/test` uses the local Windows emulator and **deletes every database in it** at fixture start (`Startup.cs:82-95`); `Startup.UseContainer` switches to a Linux emulator container.

### Documentation

- `docs/Platform/Storyteller/inheritance.md:288-359` ("Conditional reads"): the `b` row, "What a 304 saves: the transfer only", the list of what a conditional read still evaluates, the HMAC key.
- `docs/Platform/Storyteller/key-values.md:462`: "A conditional read … looks the keys up too: its 304 saves the transfer, not the lookups"; `:114`, `:118` (audit and meters).
- `docs/Platform/Storyteller/binding.md:11`: "A document with bindings is resolved for every read"; `:14` (the abstractions), `:157`, `:178`, `:183`, `:319`, `:321-323` (extending).
- `docs/42for.net/platform/using-the-platform.md:47`: "A document with bindings is still evaluated on every poll".
- `Api.Functions/src/Definitions.cs:511` (`ConditionalResolvedRead`, OpenAPI text, copied into the SDKs).

## Proposed Changes

### 1. The dependency manifest (`Binding.Abstractions`)

New files in `Binding.Abstractions/src/`:

- **`BindingDependency`**, `public sealed record BindingDependency(string Kind, string Source, string Key, string Marker)`:
  - `Kind`, `Source` and `Key` identify an input, for example `kv`, the view and the normalized key;
  - `Marker` is an opaque version of what was read: the Cosmos `_etag` of the item, or `BindingDependency.Absent` (`"absent"`) when no item exists. A Cosmos `_etag` is a quoted string, so it never equals `absent`.
- **`BindingDependencies`**, the thread-safe collector of one resolve (`sealed class`, a lock around a dictionary):
  - `void Record(BindingDependency dependency)`: the first marker of an identity is kept; the same marker again is a no-op; **a different marker for the same identity makes the manifest volatile**. Two reads of one input in one resolve (the `s:` and `p:` lookups of a key) that see different versions cannot be told apart from a stale read.
  - `void MarkVolatile(string reason)`: the first reason is kept.
  - `bool IsVolatile`, `string? VolatileReason`.
  - `IReadOnlyList<BindingDependency> ToList()`: sorted by `Kind`, `Source`, `Key` (ordinal).
  - **It fails closed.** A new collector is volatile ("No resolve recorded its inputs.") until a `BindingScope` carries it: the `init` accessor of `BindingScope.Dependencies` attaches it (an internal `Attach()`). A configuration service, a decorator or a test double that never puts the collector on a scope therefore leaves it volatile, and the read keeps the `b` tag. It cannot produce an empty manifest that claims "no inputs".
- **`IRecordingBinding`**, an empty marker interface, like the optional `IDescribedBinding`: "this binding component records every external input it reads into `BindingDependencies`, or reads none, and passes the collector on". Sources, functions, the executor and the resolver implement it.
- **Plumbing.** An optional `BindingDependencies? Dependencies { get; init; }` on `BindingScope`, `BindingRequest` and `BindingFunctionRequest`. Null makes recording a no-op, so every existing caller and test is unchanged.

**Volatile** means: the resolved body may depend on something the markers do not capture. A volatile manifest is never cached, and its read keeps the `b` tag of 424-47 (section 7). The sources of volatility:

| Source | Where it is marked |
| --- | --- |
| No scope carries the collector (a configuration service that ignores it) | `BindingDependencies` itself, until attached |
| JSON-e, which can read the clock | `ConfigurationBindingResolver`, before the engine call of a `jsone` envelope |
| A registered source or function that does not implement `IRecordingBinding` (a third-party binding, the test sources) | `BindingEvaluator`, before it calls it |
| A string executor or a resolver that does not implement `IRecordingBinding` | `ConfigurationBindingResolver` (executor), `CosmosConfigurationService` (resolver) |
| A Key Vault read (v1) | `KeyVaultBindingSource` |
| A service that returns no marker (a decorator or double that keeps the default member of section 3) | `KeyValueBindingSource`, `AnnotationBindingFunction` |
| Two different markers for one input | `BindingDependencies.Record` |

So a new source is volatile until it opts in, and cannot cause a stale 304.

**Not volatile, and not recorded:**

- JSON Logic: its result depends only on the stored `$definition` and the resolved `$context`, whose reads record themselves.
- `@config`: it reads the snapshot of the effective content, which the tag covers (section 6).
- An unregistered source or function, and the source call form that finds no source: no read happens, and the result depends only on the registry, which the tag covers (section 6).
- A source that declines without reading: `KeyValueBindingSource` outside a configuration read, `KeyVaultBindingSource` without secrets.

### 2. Recording in the runtime (`Binding.Language`, `Binding.Object`)

- **`BindingEvaluator`.**
  - `ResolveSourceAsync` and `InvokeFunctionAsync` set `Dependencies = _scope?.Dependencies` on the request.
  - Before it calls a registered source or function that is not `IRecordingBinding`, it calls `MarkVolatile($"The source '{key}' does not record its reads.")` (or "function").
- **`BindingExecutor`** implements `IRecordingBinding`: it passes the scope to the evaluator.
- **`ConfigBindingFunction`** implements `IRecordingBinding` and records nothing.
- **`ConfigurationBindingResolver`** implements `IRecordingBinding`.
  - Before `ObjectBindingEngine.Evaluate` of a `jsone` envelope: `scope.Dependencies?.MarkVolatile("JSON-e can read the clock.")`. Marking before the call is enough: a failed evaluation caches nothing.
  - `BindStringAsync`: when the executor is not `IRecordingBinding`, `scope.Dependencies?.MarkVolatile(...)` before it binds.
  - The walk and the shared scope stay as they are, so `$context` and the post-pass record into the same collector.

### 3. Storage markers (`Backend.Core`, `Backend.CosmosDb`)

New `Backend.Core/src/Configuring/MarkedRead.cs`: `public sealed record MarkedRead<T>(T? Value, string? Marker) where T : class`. `Marker` is null when the service cannot tell, and the binding then marks the manifest volatile.

New `Backend.Core/src/Configuring/BindingDependencyKinds.cs`: `KeyValue = "kv"`, `Annotation = "annotation"`, `KeyVault = "vault"`.

**The marker rule**, the same on the read and the revalidation side: the `_etag` of the item when it exists, in any state (active, soft-deleted, a secret hidden from the caller); `absent` when there is no item. A soft delete, a restore, a protection change and any content write change the `_etag`; a purge or the TTL expiry of a deleted item makes it `absent`. A write that changes nothing (an identical `SetAsync`) keeps it. Anything else that rewrites an item without changing its value (a key re-wrap, a sibling's relation update on an annotation) costs one full resolve, never a stale 304.

- **Key/values.** `IKeyValueService` gets a default member:

  ```csharp
  // LookupAsync with the storage marker of the pair (see BindingDependency.Marker).
  async Task<MarkedRead<KeyValueLookup>> LookupWithMarkerAsync(string organization, string project, string view, string key, bool includeSecrets)
      => new(await LookupAsync(organization, project, view, key, includeSecrets), Marker: null);
  ```

  `CosmosKeyValueService` implements it, and `LookupAsync` becomes `(await LookupWithMarkerAsync(...)).Value`:
  - an invalid key: `(null, absent)` without a read;
  - no item: `(null, absent)`;
  - an item: the marker is the ETag that `ReadAsync` already returns; the value is null when deleted or a hidden secret, as today.

  The side effects (unprotect, `reads.binding`, `reveals`, `RecordBindingRead`) stay exactly where they are. `CountingKeyValueService` forwards and counts the new member. `RecordingKeyValueService` keeps the default.
- **Annotations.** `IAnnotationService` gets a default member `Task<MarkedRead<Annotation>> GetAnnotationWithMarkerAsync(FullKey fullKey)` that returns `(await GetAnnotationAsync(fullKey), null)`. `CosmosAnnotationService` implements it with the same single `LoadAsync(withConfiguration: false, withAudit: false)`: the value as `GetAnnotationAsync` returns it, the marker `node.ETag` when `node.Entity` is not null, `absent` otherwise. `GetAnnotationAsync` delegates to it. The public `Annotation` model does not change.

Default members, not required ones: a hand-written decorator or double that does not know markers stays correct (volatile) and keeps compiling. A `DispatchProxy` double receives the new members in `Invoke` (the default body does not run there), but no proxy stands behind a binding source or function.

### 4. The built-in inputs (`Binding.Core`, `Binding.Azure.KeyVault`)

- **`KeyValueBindingSource`** implements `IRecordingBinding`.
  - The memo holds `Lazy<Task<MarkedRead<KeyValueLookup>>>` from `LookupWithMarkerAsync`.
  - After each await of the memo (a hit too), when `request.Dependencies` is set: `Record(new(KeyValue, context.ConfigurationKey.ViewName, KeyValueRules.Normalize(key), marker))`, or `MarkVolatile` when the marker is null. Recording happens before the kind filter, so a pair that `public` or `secret` declines, and a secret hidden from the caller, record their marker too: a protection change still changes the tag.
- **`AnnotationBindingFunction`** implements `IRecordingBinding`. It calls `GetAnnotationWithMarkerAsync(targetKey)` and records `(Annotation, targetKey.ViewName, targetKey.Annotation.ToString(), marker)` right after the read, before the null checks.
- **`KeyVaultBindingSource`** implements `IRecordingBinding`.
  - Without `IncludeSecrets` it reads nothing and records nothing; the secret rights are part of the tag.
  - With it, it records `(KeyVault, bindingKey, secretName, Properties.Version)` from the secret it already fetched, or `absent` on a 404, and calls `MarkVolatile("A Key Vault secret has no cheap version check.")`. Reading the properties costs as much as `GetSecret`, so v1 does not revalidate vault entries (see Out of scope).

### 5. The resolve step and revalidation (`Backend.Core`, `Backend.CosmosDb`)

`IConfigurationService`:

- **`ResolveConfigurationAsync(FullKey key, Configuration effective, bool includeSecrets = false, BindingDependencies? dependencies = null)`**: an optional parameter. `CosmosConfigurationService` puts the collector on the `BindingScope` (which attaches it), and marks it volatile when the registered resolver is not `IRecordingBinding`. It still resolves in place. Without a resolver it returns the effective document and leaves the collector unattached, so volatile; the handler never gets there, because `HasBindings` is false without a resolver.
- **`Task<bool> AreDependenciesUnchangedAsync(FullKey key, IReadOnlyList<BindingDependency> dependencies)`**: true only when every dependency still has its marker. A required member.

**`CosmosConfigurationService.AreDependenciesUnchangedAsync`** delegates to a new internal `BindingDependencyValidator` (`Backend.CosmosDb/src/Configuring/`):

1. No dependencies: true, without a read.
2. Any kind other than `kv` and `annotation`: false.
3. It maps each dependency to an item of the organization container of `key`:
   - `kv`: id `CosmosKeyValueService.GetId(normalized key)`, partition `PartitionKeys.GetKeyValues(key.ProjectName, Source)`. An invalid key expects `absent` and is not read.
   - `annotation`: `AnnotationKey.TryParse(Key)` (false when it fails), `FullKey.Create(annotationKey, key.OrganizationName, key.ProjectName, Source)`, id `AnnotationItemIds.Annotation(...)`, partition `GetPartitionKey()`.
4. **One `ReadManyItemsStreamAsync`** over the distinct `(id, partition)` pairs, across the partitions. A non-success response is false (log at warning), never an exception, so a failing check falls back to a full resolve.
5. It parses only `id`, `PartitionKey` and `_etag` of each returned document; a pair that is not returned is `absent`.
6. True when every recorded marker equals the current one.

It never calls `LookupAsync`, `UnprotectAsync`, `GetRawConfigurationAsync`, a usage meter or the audit log. One ReadMany costs about one RU per item, and the key/value items are small.

`FakeConfigurationService` gets the new member and the new parameter as `NotSupportedException`, like its other reads.

### 6. The tags (`Api.Functions`)

`ConfigurationEntityTags` gets a fifth kind:

- **`d`**, `ForDependencies(EntityTagHeaderValue effective, string runtime, IReadOnlyList<BindingDependency> dependencies)`: SHA-256 over
  - the kind;
  - the `r` tag of the effective read, which already digests the full key, the secret rights, `Version`, `Hash` and the effective `Content`;
  - the runtime version (below);
  - the number of dependencies, then each `Kind`, `Source`, `Key` and `Marker`, in the sorted order of `ToList()`.

  Not keyed: the inputs are the stored document, which every reader gets from the effective read, and opaque storage versions, never a value. So a `d` tag is the same on every instance, with or without `ConditionalReads:EntityTagKey`.
- **The runtime version** has two parts:
  - the **code**, taken once per process: the module version ids (`Assembly.ManifestModule.ModuleVersionId`) of the assemblies that take part in a resolve, plus the informational version of the API. The informational version alone changes per commit, not per build, so a build of uncommitted changes would keep it; the MVID of a deterministic build changes whenever the compiled code does. The assemblies, by an anchor type each: Api.Functions, Binding.Abstractions, Binding.Language, Binding.JsonE, Binding.Object, Binding.Core, Binding.Azure.KeyVault, Backend.Core, Backend.CosmosDb and the `JsonLogic` package;
  - the **registry**, taken per read because it stays writable: the sorted names of the registered sources and functions from `IBindingCatalog.GetCatalog()`.

  A deployment of new code therefore costs each poller one 200. It replaces the issue's hand-bumped constant.
- **`b`** stays for volatile manifests: the body can change while the markers do not (the clock), so only a digest of the body is a valid tag. The HMAC key stays for it.

Because the `r` tag and the runtime version are inputs, a manifest recorded for another key, other secret rights, another effective document or other code never produces the tag a client sent.

### 7. The manifest cache and the read (`Api.Functions`)

**`DependencyManifests`** (new, `Api.Functions/src/ConditionalReads/`), a singleton registered by `AddConditionalReads`:

- its own `MemoryCache` with `SizeLimit = ManifestCacheSizeLimit` (the `EnvelopeValueProtector` pattern; the shared `IMemoryCache` has no limit), disposed with the container;
- an entry is a `DependencyManifest(EntityTagHeaderValue Effective, string Runtime, IReadOnlyList<BindingDependency> Dependencies)`: the `r` tag and the runtime version it was recorded under, and its dependencies. Size 1 + the number of dependencies;
- **keyed by the caller and the tag**: the actor kind and id and the channel of `SecretRights.Caller`, and the opaque tag (the quoted part, so a tag sent without `W/` finds it). A manifest recorded by one caller never answers another caller's cheap 304. So every caller is resolved, metered and audited (`BindingRead`) at least once per lifetime on the instance, the audit trail keeps naming every machine that receives secrets, and one caller cannot turn another's reads into 304s;
- `bool TryGet(string caller, EntityTagHeaderValue tag, out DependencyManifest manifest)`, `void Set(string caller, EntityTagHeaderValue tag, DependencyManifest manifest)`, `void Remove(string caller, EntityTagHeaderValue tag)`;
- an **absolute** expiration of `ManifestLifetime` from the resolve that recorded it, not a sliding one. A manifest answers 304s for at most that long; then the next read resolves again. A poller still skips all but one resolve per lifetime;
- `string GetRuntimeVersion()` (section 6).

The handler gets it through `FunctionContext.InstanceServices.GetService<DependencyManifests>()`, like `GetEntityTagKey` gets the options, so the `ConfigurationHttp` constructor stays as it is. Without it (tests), nothing is cached and the runtime version is its code part alone.

**`ConditionalReadOptions`**: `ManifestLifetime` (default 5 minutes) and `ManifestCacheSizeLimit` (default 100,000), both validated as positive on start, with a message that names the setting.

**`ConditionalRequestExtensions`**: `IReadOnlyList<EntityTagHeaderValue> GetIfNoneMatch()`, the strict parse that `MatchesIfNoneMatch` uses (empty when absent or invalid), which `MatchesIfNoneMatch` then reuses.

**`GetConfigurationResolved`**:

1. Scope check, access check, key parsing, secret rights, `GetRawConfigurationAsync` and the 404, as today.
2. `effectiveTag = ForResolved(fullKey, effective, rights)`, before anything is evaluated.
3. **No bindings:** the read answers with `effectiveTag`, as today.
4. **Bindings, cheap path.** `runtime = manifests.GetRuntimeVersion()`. Go through the distinct listed tags whose opaque tag starts with `"d.`, at most four of them; the rest are only compared after a resolve. A tag is a candidate when the caller has a manifest under it and the manifest's `Effective` equals `effectiveTag` and its `Runtime` equals `runtime`. Because the tag is a digest of exactly those inputs, this is the same check as recomputing it, in constant time, so a long `If-None-Match` costs no hashing over manifests. Only the first candidate is revalidated, so a request costs at most one ReadMany.
   - When `AreDependenciesUnchangedAsync(fullKey, manifest.Dependencies)` is true: the headers of step 7, `ETag` = the weak tag of the manifest (the one a 200 sends, also when the client sent the strong form), and `304`. No lookup, no decryption, no usage meter and no audit entry: nothing is revealed.
   - When it is false, the manifest is removed, and the read takes the full path.
5. **Bindings, full path.** `dependencies = new BindingDependencies()`, then `ResolveConfigurationAsync(fullKey, effective, rights.CanReadSecrets, dependencies)`.
   - Volatile: the `b` tag over the resolved body, as today. Log the reason at debug level.
   - Otherwise: `tag = ForDependencies(effectiveTag, runtime, dependencies.ToList())`, and `manifests.Set(caller, tag, new(effectiveTag, runtime, ...))`.
   - A binding error (`422`) propagates before anything is cached or any header is set.
6. **`If-None-Match: *`** never takes the cheap path: it names no tag, and a 304 must carry the tag a 200 would have. It resolves and answers 304 as today.
7. The headers, on the 200 and both 304s, and never on 400, 404 or 422: `X-2S-Secrets-Withheld` when withheld, `Cache-Control` (`no-store` with secret rights, otherwise `private, no-cache`), `ETag`. Then `OkOrNotModified`.

A tag whose manifest is not in this instance (another instance, an eviction, the lifetime) takes the full path. Its `d` tag comes out the same when nothing changed, so the read still answers 304, after evaluating, as in 424-47.

**The order rule.** The effective inputs come from step 1, before the resolve. Each marker is taken by the read that produced the value, during the resolve. A write that races the read can only make the stored tag older than the data, which the next poll's revalidation catches (one more 200); it can never pin an old body under a newer marker.

**Billing and audit.** A cheap 304 is not a billed read: it records no `reads.binding` and no `reveals`, and writes no `BindingRead` count, because nothing was looked up or revealed. Only the RUs of the ReadMany are spent. The full resolve of each caller, once per `ManifestLifetime` on an instance, still meters and audits, so the hourly `BindingRead` counts keep every machine that receives secrets, with a lower `Count`.

**What markers do not see.**
- **A revoked customer key.** The `_etag` of a pair does not change when its BYOK key is revoked, and a cheap 304 decrypts nothing. A read keeps answering 304 until its manifest expires; the next full resolve fails with `424 CustomerKeyUnavailable` once the plaintext cache no longer serves the value. The worst case after a revocation is therefore `KeyValues:Protection:Byok:CacheDuration` plus `ManifestLifetime` (5 + 5 minutes by default), not the plaintext cache alone. A 304 reveals nothing the client did not already hold.
- **Write times of inputs the caller cannot read.** The marker of a secret hidden from the caller, and of an ancestor annotation, is part of the tag. A caller without secret rights, or a machine restricted to its annotation, sees a 200 with the same body when such an input is written. It learns that an input changed, never its value; the key/value listing already shows the versions of pairs to every reader.

### 8. OpenAPI and descriptions

- `Definitions.Descriptions.ConditionalResolvedRead` becomes: "A document without bindings is not evaluated. A document with bindings is revalidated from the versions of the key/value pairs and annotations it read, without evaluating it, when the serving instance recorded them for the sent ETag; otherwise it is evaluated and may still answer 304. JSON-e, and Key Vault secrets for a caller that may read secrets, are evaluated for every read." No other shape changes: the tag stays opaque.
- The SDKs are regenerated (section 11), because the text is copied into them.
- The `Program.cs` comment on `AddConditionalReads` names the manifests.

### 9. Tests

- **`Binding.Language/test`**
  - `BindingDependenciesTests`: idempotent record, a conflict makes it volatile, the first reason is kept, `ToList` is sorted, concurrent records; a collector is volatile until a scope carries it.
  - `BindingEvaluatorTests` / `SourceCallFormTests`: the collector reaches source and function requests, function arguments that read a source record too; a non-recording source or function marks volatile, a recording one does not; an unknown source or function marks nothing; without a collector nothing changes.
  - `ConfigBindingFunctionTests`: `@config` with a collector leaves it attached and not volatile.
  - `BindingExecutorTests`: `TryBinding` with a scope forwards the collector.
- **`Binding.Object/test`** (`ConfigurationBindingResolverTests`, with a recording variant of `FakeStringBinding`)
  - a `jsone` envelope marks volatile, a `jlogic` one does not;
  - a non-recording executor marks volatile when it binds a string, and not for a document without `@` strings;
  - `$context` and post-pass strings reach the same collector.
- **`Backend.CosmosDb/test`, no Cosmos**
  - `ConfigurationResolutionTests`: the collector is on the scope and attached; a non-recording resolver marks it volatile; without a resolver it stays unattached; without a collector nothing changes.
  - `AnnotationBindingFunctionTests`: an annotation records its marker, a missing one `absent`, an ancestor its own key and view; a service without markers marks volatile.
  - new `KeyValueBindingSourceRecordingTests` (a fake `IKeyValueService` that returns chosen `MarkedRead`s, and one that keeps the default member): a null marker marks volatile; `@secret` on a public pair, `@public` on a secret and a secret hidden from the caller still record their marker; a memo hit records again; outside a configuration read nothing is recorded.
  - new `KeyVaultBindingSourceTests` (a `SecretClient` subclass and a fake `IAzureClientFactory<SecretClient>`; secrets from `SecretModelFactory`): with secrets, a hit records the version and marks volatile, a 404 records `absent` and marks volatile; without secrets nothing is called, recorded or marked.
- **`Backend.CosmosDb/test`, Cosmos** (a new `BindingDependencyTests`, plus `KeyValueBindingTests`)
  - recording through a real resolve: a hit records the item `_etag`, a missing key `absent`, a deleted key and a hidden secret their `_etag`; the recorded ETag equals the `_etag` that ReadMany returns;
  - revalidation of a manifest with a key/value hit, a missing key, a deleted key, a hidden secret, `@annotation` of the configured key and of an ancestor in another partition: true with **zero lookups** and no change to `reads.binding`, `reveals` or the `BindingRead` audit;
  - each makes it false: an update, a soft delete, a restore, a purge followed by a recreate, a protection change, an annotation update; an unknown kind is false.
- **`Api.Functions/test`** (`ConfigurationConditionalReadTests`; the proxy puts the passed collector on a scope and records into it, or marks it volatile, answers `AreDependenciesUnchangedAsync`, and a shared service provider holds `DependencyManifests`)
  - a recorded manifest gives a `d` tag; the same tag, weak or strong, is a 304 **without** a resolve, with the weak tag and the same headers;
  - a changed marker gives a resolve and a 200 with a new tag, and drops the old manifest;
  - a volatile manifest gives a `b` tag, is not cached, and a later read with it resolves again; a proxy that ignores the collector gets a `b` tag;
  - a cache miss resolves and answers 304 when the `d` tag is the same;
  - a manifest of other rights, another key, another effective document or another caller never answers the cheap 304;
  - a long `If-None-Match` revalidates at most once; `*` resolves;
  - a binding error caches nothing and sends no tag;
  - `DependencyManifests`: the lifetime (through `MemoryCacheOptions.Clock`) and the size limit, the runtime version follows the registered names;
  - the options validation of the new settings;
  - the existing `b` tests are rewritten for `d` or volatile.
- `MachineRestrictionHttpTests` keeps its call sequence (no `If-None-Match`, so no revalidation; its proxy ignores the collector, so the read is volatile).
- **The Cosmos-backed classes never run against the shared local emulator**: their fixture deletes every database in it, and other worktrees and sessions use it. They run against the Linux emulator container through a temporary local switch, reverted before every commit:
  1. `Startup.UseContainer = true`;
  2. an https connection string to the mapped port with the well-known emulator key, instead of `DbContainer.GetConnectionString()`, which is http for the default image;
  3. a fresh container (no reused one), and a current image if it reports an expired evaluation;
  4. small filtered batches, not the full suite.

  The review lists every batch and its result, and which tests did not run if the container does not start.

### 10. Documentation

- **`docs/Platform/Storyteller/inheritance.md`, "Conditional reads"**: the `d` row in the tags table; the resolved-read table with three cases (no bindings, recorded inputs, volatile); the manifest, its per-caller cache, the lifetime and the cache miss; the order rule; billing and audit; `d` not keyed; the limits (`d` follows the effective cache like the others; the per-instance cache; the BYOK worst case; write times of inputs the caller cannot read).
- **`docs/Platform/Storyteller/key-values.md`**: `:462` (a conditional read revalidates the versions of the keys and looks nothing up); `:114` and `:118` (a cheap 304 is not metered or audited, and each caller is resolved at least once per lifetime); `:76` (BYOK revocation: plaintext cache plus manifest lifetime); the service table (`LookupWithMarkerAsync`).
- **`docs/Platform/Storyteller/binding.md`**: `:11`; the abstractions list; recording and volatility in "Extending the System" (a new source or function is volatile until it implements `IRecordingBinding` and records); `@config`, `@annotation`, the key/value source and `KeyVaultBindingSource`; the dependency-injection paragraph (the collector on the scope).
- **`docs/42for.net/platform/using-the-platform.md`, "What a service reads"**: a document whose bindings read key/value pairs or annotation values is revalidated without evaluating it; JSON-e, and Key Vault secrets for a caller that receives them, are evaluated on every poll.

Specs and earlier reviews are not changed.

### 11. Phases and commits

1. `docs(platform): plan cheap 304s for configurations with bindings` (this spec)
2. **Phase 1**, `feat(platform): record the inputs of a binding resolve`: sections 1 and 2 and their tests.
3. **Phase 2**, `feat(platform): record and revalidate the storage markers of bindings`: sections 3 to 5 and their tests.
4. **Phase 3**, `feat(platform): answer conditional resolved reads from dependency manifests`: sections 6 to 8 (without the regeneration) and their tests.
5. **Phase 4**, `docs(platform): document the dependency manifests of conditional reads`: section 10.
6. **Phase 5**, `chore(platform): regenerate the SDKs from OpenAPI 0.9.N`.
7. `docs(platform): review cheap 304s for configurations with bindings`.

Each phase compiles and passes its tests on its own: phase 1 adds only optional members, phase 2 only default and optional ones plus the required `AreDependenciesUnchangedAsync` with its two implementations. Every commit body names `Refs: 424-51`.

### 12. Verification

```bash
dotnet build src/Platform/Storyteller/Api.Functions/src
dotnet test src/Platform/Storyteller/Binding.Language/test
dotnet test src/Platform/Storyteller/Binding.Object/test
dotnet test src/Platform/Storyteller/Backend.Core/test
dotnet test src/Platform/Storyteller/Api.Functions/test              # Debug (DEV_AUTH)
dotnet test src/Platform/Storyteller/Api.Functions/test -c Release
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test --filter "FullyQualifiedName~ConfigurationResolutionTests|FullyQualifiedName~AnnotationBindingFunctionTests|FullyQualifiedName~KeyValueBindingSourceRecordingTests|FullyQualifiedName~KeyVaultBindingSourceTests"
# Cosmos, in the emulator container only (section 9), batch by batch:
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test --filter "FullyQualifiedName~BindingDependencyTests|FullyQualifiedName~KeyValueBindingTests"
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test --filter "FullyQualifiedName~KeyValueAuditAndUsageTests|FullyQualifiedName~CosmosKeyValueServiceTests|FullyQualifiedName~ClientEncryptionAndReferenceTests"
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test --filter "FullyQualifiedName~CosmosConfigurationServiceTests"
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test --filter "FullyQualifiedName~CosmosAnnotationServiceTests|FullyQualifiedName~CosmosAnnotationLifecycleTests|FullyQualifiedName~CosmosAnnotationStatusTests|FullyQualifiedName~CosmosAnnotationAuditTests"
# Phase 5:
dotnet build src/Platform/Storyteller/Sdk.NSwag/src
dotnet build src/Platform/Cli/src
cd src/Platform/Storyteller/sdk.typescript/src && npm test && npm run build
```

### Out of scope

- **Revalidating Key Vault entries.** A time-window policy (revalidate at most every N seconds) or the secret properties. The recorded versions are kept in the manifest for it.
- **JSON-e without the clock.** A patch of the vendored `EvaluationContext` could flag a read of `now`, so only JSON-e that reads the clock is volatile.
- **A shared manifest store** (Cosmos or Redis) for many instances. With a per-instance cache, a read on another instance resolves and still answers 304 when nothing changed, because `d` tags are the same on every instance.
- **Tying the manifest lifetime to the BYOK plaintext cache.** A manifest with a BYOK read could expire with `Byok:CacheDuration`; v1 documents the sum instead.
- **Push notification of changes** (424-49) and **SDK helpers for `If-None-Match`**.
- **A replaced source under the same name.** The runtime version follows the names of the registered bindings, not their types; the registry is filled once at startup.

### 13. Differences from the CodeRabbit coding plan

The plan's choices that this spec keeps:

- a thread-safe collector on the scope, forwarded through the requests, with a conflicting marker making the manifest volatile;
- an opt-in marker interface, so a binding that does not record is volatile;
- markers from the reads that produced the values, the `_etag` and not `Version`, a marker for a hidden secret too;
- Key Vault recorded and volatile in v1; JSON-e volatile; JSON Logic and `@config` pure;
- an in-process cache of non-volatile manifests, nothing cached after a 422, a post-resolve 304 when the sent tag equals the new one;
- no usage meter and no audit entry for a cheap 304.

It differs in these points:

1. **424-47 is merged.** The plan assumed its conditional-read code was missing; this spec extends it.
2. **No new resolve method and no clone.** The effective inputs are captured as the `r` tag before the resolve, so the in-place resolve and `GetResolvedConfigurationAsync` stay as they are; the collector is an optional parameter.
3. **A volatile manifest keeps the `b` tag.** The plan tags every read from its manifest; for JSON-e, whose body changes with the clock while the markers do not, that tag would answer stale 304s.
4. **The marker is the `_etag` whenever the item exists**, `absent` only without an item. A soft delete changes the `_etag` anyway, so the revalidation needs no deletion field, and one rule serves both sides.
5. **One ReadMany** over all dependencies, across partitions, instead of a query per key/value partition plus annotation reads.
6. **An unregistered source or function is not volatile.** It reads nothing; the registered names are part of the tag instead.
7. **The runtime version is the compiled code plus the registered names**, not a hand-bumped constant.
8. **The guard fails closed at every layer**: the collector is volatile until a scope carries it, and the executor and the resolver must opt in like sources and functions. A component that ignores the collector would otherwise produce an empty, non-volatile manifest.
9. **An absolute manifest lifetime, a dedicated size-limited cache, and manifests kept per caller**, instead of a sliding expiration in the shared `IMemoryCache`. The cache entry keeps the inputs of its tag, so a candidate is checked in constant time.
10. **Revalidation is a member of `IConfigurationService`** and runs at most once per read; `*` never takes the cheap path.
11. **The SDKs are regenerated**, because the OpenAPI description changes.
12. **Non-Cosmos tests for the key/value source and the Key Vault source**, whose opt-in turns the evaluator's guard off.
