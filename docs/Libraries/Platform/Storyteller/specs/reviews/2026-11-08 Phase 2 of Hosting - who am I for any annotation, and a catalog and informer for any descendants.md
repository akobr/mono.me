# Phase 2 of Hosting: who am I for any annotation, and a catalog and informer for any descendants

Linear: [424-214](https://linear.app/424fun/issue/424-214/hosting-who-am-i-for-any-annotation-and-a-catalog-and-informer-for-any).

## Overview

Executes phase 2, the catalog and the descendant informer (sections 3 to 9), of [Hosting: who am I for any annotation, and a catalog and informer for any descendants](../2026-11-08%20Hosting%20-%20who%20am%20I%20for%20any%20annotation,%20and%20a%20catalog%20and%20informer%20for%20any%20descendants.md). `IStorytellerCatalog` gets any annotation, and lists and watches the descendants of any annotation by a `StorytellerTarget`, checked against the API's table of descendants. The subscriptions keep their informer; the two share the paging of lists. A multi-agent review of the change found two defects, fixed in `89f552ed`.

## What Was Done

### 3. The target

- `80a1101a` adds `StorytellerTarget` (the factories `UsagesOf`, `ContextsOf`, `UnitsOf`, `ExecutionsOf`, `UnitsOfExecutionOf`) and the sealed `StorytellerTarget<TAnnotation>` with `Of`, `Type`, the four filters, `IncludeInactive`, `With…`, `IncludingInactive()` and `ToString()`, as the spec has them. They are two files, `StorytellerTarget.cs` and `StorytellerTarget{TAnnotation}.cs`, as StyleCop wants one type per file.
- The constructor parses a full key or an annotation key, lower-cases the type code (`CatalogKeys.Normalize` gained an `AnnotationKey` overload), and checks the combination against `Reading/AnnotationTypeFacts`, which holds the table of `AnnotationsHttp.GetDescendants`, the route segments, the words of the messages and the parts of each type's key (`Reading/KeyParts`). A filter checks its name with `SubscriptionScope.ValidateName`, that the descendants' keys have that part, and that the ancestor does not name it already.

### 4. The catalog

- `IStorytellerCatalog` and `StorytellerAnnotationChange<TAnnotation>` are new public types; `Catalog/StorytellerCatalog` implements the catalog over the shared `CatalogReader`, with at most 4 reads at once and the informer's logging category.
- `GetAsync` and `GetExecutionAsync` share `CatalogReader.FindAsync`: any status, deleted ones included, `null` for a `404` or a deleted annotation.
- `ListAsync` reads the ancestor, every page, and each listed descendant that passes the filters, and returns the accepted snapshots by key; `Informer/DescendantScope` resolves the target in the options' view and decides what is listed, what passes and what is accepted.
- **Fixed after review.** `GetAsync<T>` of a type the application derived from `StorytellerAnnotation` skipped the type check, read, and threw `InvalidCastException`. It now throws `ArgumentException` before any read, as `Find<T>()` does; only `StorytellerAnnotation` itself takes any key.

### 5. The reads and the shared paging

- `IAnnotationPages.ReadDescendantsPageAsync(ancestor, type, includeInactive, token)` is new; `CatalogReader` sends `includeInactive=true` only when asked, so the informer's requests are unchanged. Both list reads go through one `ReadListPageAsync`; the end of the message of a `403 AnnotationRestricted` is the caller's, so the informer's stays word for word.
- `Informer/ListPaging` holds the loop that was `PollingSubscriptionSource.ReadListAsync`, with its messages; `PollingSubscriptionSource.MaxPages` is `ListPaging.MaxPages`. Nothing else of the subscriptions' informer changed, and its tests pass unchanged.

### 6. The descendant informer

- `Informer/DescendantPollingSource` and `DescendantBatch` poll as the spec says: the ancestor until a poll completed, the list, the candidates with their fingerprints, the diff with reads (`Added`, `Changed`, `Removed` with the reason of a confirmation read), and the order Removed, Changed, Added by key. It reuses the interval rule, the jitter and the cancellation handling of `PollingSubscriptionSource`.
- With `IncludeInactive`, a confirmation read removes only a missing or a deleted annotation.
- **Fixed after review.** The API refuses a machine restricted below the ancestor both the ancestor's read and its list, with `403 AnnotationRestricted`, so the read's message hid the list's, which says what listing needs. The ancestor check of `ListAsync` and of the informer now goes on to the list on that 403.

### 7. Registration

- `AddStoryteller` registers `IStorytellerCatalog` with its own `TryAddSingleton`, next to `ISubscriptionCatalog`.

### 8. Freshness of relation arrays

- Not fixed in the backend, as planned. The interface's comment and `hosting.md` (phase 3) say that a new child raises no event of its parent, and to watch the children's type. A test locks it.

### 9. Machine restrictions

- The reader's generic list message names the ancestor and says that listing needs a machine restricted to it or to an annotation above it, or an unrestricted one.

### Tests

- `test/Catalog/TargetTests.cs`: all 35 combinations of seven ancestor types and five descendant types against the API's table, normalization of keys, keys that do not parse, the messages of unsupported combinations and of filters, immutability, `ToString()`, and ordinal filters.
- `test/Catalog/CatalogTests.cs`: `GetAsync` of any type and status, typed, with a key of another type, with a derived type, missing, deleted, failing and without the options' key; `ListAsync` with point-read fidelity over lossy list values, the requests and their flags across pages, filters that read nothing else, inactive and deleted ones, a descendant read inactive or gone since the list, a failure thrown only after every read ended, a missing ancestor, a restricted machine, a list that does not end, a full key of another scope, no options' key, and at most four reads at once.
- `test/Catalog/WatchTests.cs`: the first poll and its requests, filters, `Changed` by `Modified` and by hash, a new descendant, each removal reason, expiry without a write, the order of a poll's changes, an annotation the list missed (with a confirmation that fails, too), inactive ones included, a failed read of a change, a descendant listed but read inactive, a failed poll, the zero interval, a missing ancestor, a restricted machine, independent enumerations, the checks at the call, cancellation, and the freshness of relation arrays.
- `test/Registration/RegistrationTests.cs`: one singleton, one registered before is kept, and an `ISubscriptionCatalog` registered before does not hide it.
- `FakeCatalog` serves the descendants of any annotation by the graph, with `includeInactive`, list items without the subtype's members as the API serializes them, `Relate` for a relation change without `Modified`, and holds of the reads of one key; `ScriptedPages` refuses the new member.
- The Hosting tests went from 272 to 344, all passing.

### After the review of the pull request

- **A definitive failure before any poll completed ends a watch.** `WatchAsync` threw only the failure of a zero interval, so a mistyped ancestor, a `401` or a `403` logged a warning every poll and never ended, looking like an empty target. It now throws a definitive failure until a poll has completed; transient failures, and any failure after a poll completed, are still logged and polled again. This differs from `WatchExecutionsAsync`, which keeps its behaviour.
- **An ancestor deleted after the first poll** was thought to fail every later poll. The API checks access only, not existence, before it lists, so the list answers what is not deleted under it, and the cascade removes the held descendants. A test locks it.
- **Shared reads.** `Informer/PollReads` holds the read limiter (three copies before) and the removal reason (two), used by both informers and the catalog. The poll loops stay apart: their batches, join and logs differ, and the subscriptions' tests guard theirs.
- `hosting.md` names the cost of a list over a busy ancestor and says to target the narrowest one; the concurrency of 4 stays fixed.
- `ListAsync` computes a descendant's full key once, and `Find<T>()` names its parameter with `nameof`.
- Not changed: keys differing only in the case of the type code (the platform stores lower-cased codes, and the catalog lower-cases its own), the ordinal filters (documented as such), and the playground's units listed once under `--watch` (documented).

### Deviations

- The ancestor check also lets a `403 AnnotationRestricted` through to the list, which the spec did not foresee.
- `AnnotationTypeFacts`, `KeyParts` and the split of the target into two files are implementation details the spec did not name.
