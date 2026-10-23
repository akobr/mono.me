# Cheap 304s for Configurations with Bindings

## Overview

A resolved configuration read with bindings now records every input its resolve reads (key/value pairs, annotations, Key Vault secrets), each with the storage version it saw, into a dependency manifest. When every input was recorded, the read is tagged `W/"d.…"`, a digest of the effective `r` tag, the runtime version and the recorded inputs, and the API keeps the manifest per caller for `ConditionalReads:ManifestLifetime`. A later read that sends the tag revalidates the versions in one Cosmos ReadMany and answers `304 Not Modified` without evaluating, looking up, decrypting, metering or auditing anything. A volatile resolve (JSON-e, a Key Vault read, a binding that does not record) keeps the `b` tag over the resolved body. This review covers [Cheap 304s for Configurations with Bindings](../2026-10-23%20Cheap%20304s%20for%20Configurations%20with%20Bindings.md) in full, for Linear 424-51: all five phases and the fixes from the code review.

## What Was Done

### 1. The dependency manifest (`Binding.Abstractions`)

As planned.
- **`BindingDependency`** (`Kind`, `Source`, `Key`, `Marker`) with `BindingDependency.Absent = "absent"`.
- **`BindingDependencies`**: a lock around a dictionary by identity. The same marker again changes nothing, another marker makes it volatile ("The kv input 'db.host' was read in two versions."), `MarkVolatile` keeps the first reason, `ToList` sorts by kind, source and key (ordinal).
- **It fails closed.** A new collector is volatile ("No resolve recorded its inputs.") until a `BindingScope` carries it: the `init` accessor of `BindingScope.Dependencies` calls the internal `Attach()`.
- **`IRecordingBinding`**, an empty marker interface next to `IDescribedBinding`.
- **Plumbing.** `Dependencies` on `BindingScope`, `BindingRequest` and `BindingFunctionRequest`; null records nothing.

### 2. Recording in the runtime (`Binding.Language`, `Binding.Object`)

As planned.
- `BindingEvaluator` passes `_scope?.Dependencies` into both requests and marks them volatile before it calls a registered source or function that is not `IRecordingBinding`. An unregistered name and the source call form that finds no source mark nothing.
- `BindingExecutor` and `ConfigBindingFunction` implement `IRecordingBinding`.
- `ConfigurationBindingResolver` implements it, marks a `jsone` envelope volatile right before the engine call, and marks a string executor that is not `IRecordingBinding` volatile when it binds an `@` string.

### 3. Storage markers (`Backend.Core`, `Backend.CosmosDb`)

As planned.
- `MarkedRead<T>(T? Value, string? Marker)` and `BindingDependencyKinds` (`kv`, `annotation`, `vault`) in `Backend.Core/src/Configuring`.
- **Key/values.** `IKeyValueService.LookupWithMarkerAsync` is a default member (marker null). `CosmosKeyValueService` implements it; `LookupAsync` returns its `Value`. An invalid key and a missing item are `absent`; an existing item, also deleted or a hidden secret, gives the ETag that `ReadAsync` already returned. Unprotect, `reads.binding`, `reveals` and `RecordBindingRead` stay where they were.
- **Annotations.** `IAnnotationService.GetAnnotationWithMarkerAsync` is a default member. `CosmosAnnotationService` implements it with the same single `LoadAsync` and returns `node.ETag` when the item exists, `absent` otherwise; `GetAnnotationAsync` returns its `Value`.
- `CountingKeyValueService` forwards and counts the new member.
- **The marker equality** that the design rests on (the ETag header of a point read equals the `_etag` in the body of a ReadMany) is asserted by `BindingDependencyTests` against the emulator.

### 4. The built-in inputs (`Binding.Core`, `Binding.Azure.KeyVault`)

As planned.
- `KeyValueBindingSource` memoizes `MarkedRead<KeyValueLookup>` and records after every await of the memo, before the kind filter; a null marker marks volatile.
- `AnnotationBindingFunction` records the target key and view right after the read.
- `KeyVaultBindingSource` marks volatile before it calls the vault, then records the secret version (or `absent` on a 404 and for a secret without a version). Without `IncludeSecrets` it records nothing.

### 5. The resolve step and revalidation (`Backend.Core`, `Backend.CosmosDb`)

As planned.
- `IConfigurationService.ResolveConfigurationAsync` has the optional `BindingDependencies? dependencies`; `CosmosConfigurationService` puts it on the scope and marks it volatile for a resolver that is not `IRecordingBinding`. Without a resolver it returns the effective document and leaves the collector unattached.
- `AreDependenciesUnchangedAsync` is a required member. `CosmosConfigurationService` delegates to the internal `BindingDependencyValidator`: no dependencies is true without a read; an unknown kind or an annotation key that does not parse is false; an invalid key/value key expects `absent` without a read; everything else is one `ReadManyItemsStreamAsync` over distinct `(id, partition)` pairs, parsing `id`, `PartitionKey` and `_etag`.
- **Deviation, from the code review.** A failed response, a `CosmosException`, an `OperationCanceledException` and a `JsonException` are all "changed" (logged at warning), so a failing check resolves instead of failing the read. The spec named only a non-success response.
- `FakeConfigurationService` throws `NotSupportedException` for the new member and accepts the new parameter.

### 6. The tags (`Api.Functions`)

As planned.
- `ConfigurationEntityTags.ForDependencies(effective, runtime, dependencies)` hashes the kind, the opaque `r` tag, the runtime version, the count, then each input's four fields. `IsDependencies` tells a `d` tag by its prefix. The class comment lists `d`.
- **The runtime version** is in a new static `BindingRuntimeVersion`: the informational version of the API and the MVIDs of ten anchor assemblies (Api.Functions, Binding.Abstractions, Binding.Language, Binding.JsonE, Binding.Object, Binding.Core, Binding.Azure.KeyVault, Backend.Core, Backend.CosmosDb, `JsonLogic`) once per process, plus the sorted names from `IBindingCatalog.GetCatalog()` per read. `DependencyManifests.GetRuntimeVersion()` delegates to it; without the manifests (tests) the handler uses the code part alone. The spec kept the computation inside `DependencyManifests`.

### 7. The manifest cache and the read (`Api.Functions`)

As planned, with these details settled during the implementation:
- **`DependencyManifest`** carries its own `Tag` next to `Effective`, `Runtime` and `Dependencies`, so the cheap 304 returns the weak tag without hashing again, and `Set(caller, manifest)` takes the key from it.
- **`DependencyManifests`**: its own `MemoryCache` with `SizeLimit`, entries of size 1 + inputs, absolute expiration of `ManifestLifetime`, keys of `{actor kind}:{actor id}:{channel}` and the opaque tag. `CallerOf(SecretRights)` builds the caller. An internal constructor takes a `TimeProvider` for the expirations (from the code review).
- **Options.** `ManifestLifetime` (5 minutes) and `ManifestCacheSizeLimit` (100,000), validated as positive on start by a second `Validate` whose message names both settings. `AddConditionalReads` registers `DependencyManifests` through a factory with the optional `IBindingCatalog`.
- **`GetIfNoneMatch()`** is the strict parse that `MatchesIfNoneMatch` now uses; `GetDependencyManifests()` resolves the singleton from `FunctionContext.InstanceServices`.
- **`GetConfigurationResolved`** follows the seven steps of the spec. `FindUnchangedManifestAsync` takes at most `MaxManifestCandidates` (4) distinct `d` tags, compares `Effective` and `Runtime` of the caller's manifest in constant time, revalidates only the first candidate, and removes it when it changed. The headers moved into `SetResolvedHeaders`, called on the 200 and both 304s after the decision, so a 422 still carries none.

### 8. OpenAPI and descriptions

As planned. `Definitions.Descriptions.ConditionalResolvedRead` has the spec's text, and the `Program.cs` comment names the manifests. The regenerated document differs from 0.9.48 only in that description and `info.version`.

### 9. Tests

| Project | Result |
| --- | --- |
| `Binding.Language/test` | 127 pass. New: `BindingDependenciesTests` (7), `BindingRecordingTests` (9, one a theory of 3) |
| `Binding.Object/test` | 219 pass. New: `ConfigurationBindingRecordingTests` (7) |
| `Backend.Core/test` | 867 pass |
| `Api.Functions/test`, Debug and Release | 2246 pass in both builds |
| `Backend.CosmosDb/test`, without Cosmos | 44 pass: `ConfigurationResolutionTests`, `AnnotationBindingFunctionTests`, and the new `KeyValueBindingSourceRecordingTests` (8) and `KeyVaultBindingSourceTests` (4) |
| `Backend.CosmosDb/test`, Cosmos, in the emulator container | every batch passed, see below |
| `sdk.typescript` | 130 pass, 1 skipped; `npm run build` passes |
| `Sdk.NSwag/src`, `Cli/src` | build |

**New in `Api.Functions/test`** (`ConfigurationConditionalReadTests`): the proxy puts the collector on a scope and records `db.host` in the version `Marker`, marks it volatile, or ignores it; it answers `AreDependenciesUnchangedAsync` by comparing with `Marker`. The tests cover a cheap 304 for the weak and the strong form without a resolve and with the same headers (also the withheld header), a changed input (a resolve, a 200 and a dropped manifest), a volatile and an ignored collector (`b`), another instance (a resolve and still a 304 with the same `d` tag), other rights on the same caller, another caller, another document and another key, another binding registry, a long `If-None-Match` (one revalidation), `*`, the `d` tag inputs, the manifest lifetime (with a stepping clock: a read at minute 3 does not extend a 5-minute lifetime), caller isolation, the strong form, the size limit, the runtime version, and the options validation. The rewritten 424-47 tests: `_RecordedInputs_TagTheReadByTheirMarkers`, `_VolatileInputs_TagTheResolvedBody`, `_TagsOverBindings_FollowTheConfiguredKey` (now volatile).

**Mutation check.** Switching `DependencyManifests.Set` to a sliding expiration makes the lifetime test fail.

**Cosmos.** The fixture deletes every database of the emulator it targets, and the local Windows emulator is shared, so the Cosmos classes ran only against the Linux emulator container, through the temporary switch that section 9 describes (`UseContainer = true`, an `https` connection string to the mapped port, no reuse), reverted before every commit:

| Batch | Result |
| --- | --- |
| `BindingDependencyTests` (9, new), `KeyValueBindingTests` | 15 pass |
| `KeyValueAuditAndUsageTests`, `CosmosKeyValueServiceTests`, `ClientEncryptionAndReferenceTests` | 44 pass |
| `CosmosConfigurationServiceTests` | 34 pass. The first attempt failed in the fixture with a `408` from the emulator that was still starting; the retry passed |
| `CosmosAnnotationServiceTests`, `CosmosAnnotationLifecycleTests`, `CosmosAnnotationStatusTests`, `CosmosAnnotationAuditTests` | 51 pass |
| After the code-review fixes: `BindingDependencyTests`, `KeyValueBindingTests`, `KeyValueAuditAndUsageTests` | 18 pass |
| `CosmosViewTransferTests`, `CosmosViewImportTests`, `CosmosConfigurationCompositionTests`, `CosmosConfigurationTemplateServiceTests` | 79 pass |

`BindingDependencyTests` resolve a document with a key/value hit, a hidden secret, a deleted and a missing key, `@annotation` of a usage and of its subject (another partition), and assert the recorded markers against the raw item ETags, a revalidation without a lookup, no change to `reads.binding`, `reveals` or the `BindingRead` audit, and that an update, a soft delete, a restore, a purge and a recreate, a protection change and an annotation update each fail the revalidation, while a write that changes nothing does not.

**Not run:** the other Cosmos classes (access, invitations, certificates, schemas, maintenance, policies, listings, views) and `KeyValueEndToEndTests` (Lowkey Vault); the change does not touch their paths. No Functions host ran locally: the handler is covered by the unit tests above and the services by the emulator.

### 10. Documentation

As planned.
- **`inheritance.md`, "Conditional reads"**: the `d` row; the resolved-read table with three cases; a new `### The dependency manifest` (markers, volatility, the `d` tag and runtime version, the cache, the conditional read, the cache miss, the order rule, billing and audit); `d` among the unkeyed tags; the strong form in the headers table; four new limits (the per-instance cache, the BYOK window, write times of inputs the caller cannot read, Key Vault and JSON-e).
- **`binding.md`**: the overview, the abstractions, the evaluator, the object walker, the key/value and Key Vault sources, `@annotation`, the dependency-injection paragraph, a new `## Dependency manifests` with the table of recorded inputs, and how a custom binding takes part.
- **`key-values.md`**: the BYOK revocation window, the `BindingRead` counts, the meters, `LookupWithMarkerAsync` in the service table, and the binding bullet about conditional reads.
- **`using-the-platform.md`**: what a polling service saves.

### 11. Phases and commits

As planned, plus one commit from the code review:

1. `c8e937cd` `docs(platform): plan cheap 304s for configurations with bindings`
2. `6846668e` `feat(platform): record the inputs of a binding resolve`
3. `edd34fa6` `feat(platform): record and revalidate the storage markers of bindings`
4. `cb78cca6` `feat(platform): answer conditional resolved reads from dependency manifests`
5. `926b7cb5` `docs(platform): document the dependency manifests of conditional reads`
6. `9e310ec9` `fix(platform): fail closed on a failed revalidation and pin the manifest lifetime`
7. `185be2bc` `chore(platform): regenerate the SDKs from OpenAPI 0.9.50`
8. `docs(platform): review cheap 304s for configurations with bindings` (this review)

### 12. Verification

The commands of the spec, with the results in section 9. `Backend.CosmosDb/test` also needed `Moq` for the Key Vault and validator tests; it is in `Directory.Packages.props` already.

### Reviews

- **Before the spec was committed.** Four skeptics read the draft (stale 304s, security, fit with the code, tests and docs) and one verifier checked each one's findings. What went into the spec:
  - a collector that fails closed until a scope carries it, because a configuration service that ignores the optional parameter would otherwise produce an empty, non-volatile manifest;
  - the weak comparison and the weak tag on a 304 for a strong-form `If-None-Match`;
  - manifests that keep their inputs and are kept per caller, so a long `If-None-Match` costs no hashing and every caller is still resolved and audited once per lifetime;
  - the runtime version from the MVIDs of the compiled code, because the informational version changes per commit, not per build;
  - the honest BYOK window (plaintext cache plus manifest lifetime) and the write-time exposure of inputs the caller cannot read;
  - the Key Vault wording of the OpenAPI text, the non-Cosmos tests of the two sources, the positive annotation revalidation across partitions, the container switch, and the extra Cosmos and SDK checks.
- **After the implementation.** Four reviewers (correctness, robustness, security, tests and conventions) and one verifier each.
  - Confirmed and fixed: a new SA1202 in `ConfigurationConditionalReadTests` (the test source moved to the end and became `PlainSource`), and a lifetime test that a sliding expiration would also pass (now a stepping clock through the new internal `TimeProvider` seam).
  - Refuted as defects but adopted as cheap hardening: the wider catch in the validator with tests for a failed and a throwing ReadMany; a same-caller rights test (the old one changed the caller too); a test that a manifest of another binding registry is not revalidated.
  - Refuted: an untested cap of four candidates (the loop revalidates at most once anyway) and a "binding error caches nothing" test (the cache is set only after the resolve returns).

### Merge notes

- **The SDKs are at OpenAPI 0.9.50.** A branch that regenerates after this one starts from it.
- **Open PRs.** Only #60 (dependency bumps) is open; it does not touch these files.
