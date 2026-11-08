# Hosting: who am I for any annotation, and a catalog and informer for any descendants

Linear: [424-214](https://linear.app/424fun/issue/424-214/hosting-who-am-i-for-any-annotation-and-a-catalog-and-informer-for-any). It covers [424-208](https://linear.app/424fun/issue/424-208/arch-hosting-cannot-list-the-units-of-a-responsibility-catalog-only), the narrow case of the units of a responsibility, found in the [playground](../../../../Platform/Storyteller/specs/2026-11-06%20Playground%20for%20Storyteller.md) (424-205).

## Problem

1. **Who am I is typed for an execution only.** `IExecutionContext` reads a key of any type with its ancestors, but only `exe` gets a typed snapshot, `StorytellerExecution` with its `UnitNames`. Every other type is a plain `StorytellerAnnotation`, without the names of its relations or its own members (a unit's `UnitType` and `UnitDefinition`).
2. **The catalog knows executions only.** `ISubscriptionCatalog` has `GetExecutionAsync` and `WatchExecutionsAsync(responsibility, context)`. It cannot read, list or watch the units of a responsibility, the usages, contexts or executions of a subject, or the units of execution of an execution. There is no typed way to say what to target.
3. **The playground shows units through executions.** `DotNet.SimpleExecutor` takes the units of a responsibility from the `UnitNames` of its executions, so a unit without a unit of execution under a listed execution does not show.
4. **Freshness and restrictions are not stated in general.** Relation arrays do not move `Modified`, so a new descendant may raise no event on its parent. What a machine restricted to an annotation can target is documented for the informer only.

## Current State

- **Snapshots.** `StorytellerAnnotation` (`src/Libraries/Platform/Storyteller/Hosting/src/StorytellerAnnotation.cs`) is public, with a public constructor and `init` members that copy what they are given, a `private protected` copy constructor and an `internal virtual WithDeletedAt`. `StorytellerExecution` is the only subtype: `SubjectName`, `ResponsibilityName` and `ContextName` from the key (`required init`), `UnitNames` copied from an initializer, an internal `[SetsRequiredMembers]` constructor and a `WithDeletedAt` override.
- **The mapper.** `Reading/AnnotationMapper.Map(FullKey, Annotation)` takes the type and the names from the key that was read and the rest from the body; it builds a `StorytellerExecution` for an `exe` key, with `(annotation as Execution)?.UnitNames`, and a base `StorytellerAnnotation` for any other key.
- **Who am I.** `StorytellerExecutionContext` has `Annotation`, `Ancestors` (by `AnnotationType`), and `Subject`, `Responsibility`, `Usage` and `Context` typed `StorytellerAnnotation?`, and `Execution` typed `StorytellerExecution?` (`Find(type) as StorytellerExecution`). The test `SnapshotTests.ASnapshotThatAnApplicationBuilt_CopiesWhatItWasGiven` builds a context with a base `StorytellerAnnotation` as its usage, as a test double of an application does, and expects `context.Usage` to be that instance.
- **The catalog.** `Catalog/SubscriptionCatalog` implements `ISubscriptionCatalog`. `GetExecutionAsync` parses the key with `CatalogKeys.Parse` (a full key, or an annotation key in the scope of the options' `Key`), reads it with `includeInactive` and `includeDeleted`, and answers `null` for a missing or deleted execution.
- **The reads.** `Reading/CatalogReader` reads one annotation (`ReadAsync`) and one page of `…/annotations/rst.{R}/executions` or `/usages` (`ReadPageAsync(FullKey, AnnotationList, token)`), without `includeInactive`. It implements `IAnnotationPages`, the seam of the informer, which the tests script with `ScriptedPages`.
- **The informer.** `Informer/PollingSubscriptionSource` checks the responsibility until a poll completed, reads every page of both lists (`ReadListAsync`: empty pages with a token, the last item of a key wins, a repeated token or more than `MaxPages` (1000) pages fail), joins executions of the context with their usages, diffs by `ListFingerprint` (the `Modified` stamp, or a hash of the item), and point-reads what changed. It yields `SubscriptionBatch`es, which `SubscriptionCatalog.WatchExecutionsAsync` and the hosted `SubscriptionsService` consume.
- **The API.** `GET …/annotations/{key}/{descendants}` (`AnnotationsHttp.GetDescendants`) takes `usages`, `contexts`, `executions`, `units`, `units-of-execution` or `all`, with `continuationToken`, `includeInactive` and `includeDeleted`, and no filter of subject, context or unit. It refuses the combinations that are not in the graph: a responsibility has no contexts, a subject no units, a unit only units of execution, a usage and a context only executions and units of execution, an execution only units of execution, and a unit of execution nothing.
- **List items.** The Functions API serializes `AnnotationsResponse.Annotations` with System.Text.Json by its declared element type, the base `Annotation`: an item has its `AnnotationType` discriminator, but none of the subtype's members, so no relation arrays. Its values are lossy (424-108).
- **Relation arrays.** `Backend.CosmosDb/src/Annotating/AnnotationRelations.cs` keeps the arrays of the parents: `rst.UnitNames` (units), `sbt.ResponsibilityNames` (usages), `sbt.ContextNames` (contexts), `usg.ContextNames` and `cnt.ResponsibilityNames` (executions), `exe.UnitNames` (units of execution). `Add` and `Remove` write the parent without stamping `Modified`.
- **The playground.** `src/Platform/Storyteller/Playground/DotNet.SimpleExecutor/src/ExecutorCommand.cs` injects `IExecutionContext`, `ISubscriptionCatalog` and `IStorytellerDocuments`, and renders unit documents from `execution.UnitNames`, once per run. Its tests run against a loopback `FakeApi` that maps paths to JSON.

## Proposed Changes

Nothing public changes its signature: every change is additive, so applications, their test doubles of `ISubscriptionCatalog` and `IExecutionContext`, and the hosted subscriptions keep working.

### 1. Typed snapshots of every annotation type

Six new public sealed classes in `Hosting/src/`, each deriving from `StorytellerAnnotation` the way `StorytellerExecution` does:

| Type | Key | From the key (`required init`) | From the body (`init`, copied) |
| --- | --- | --- | --- |
| `StorytellerResponsibility` | `rst.{R}` | | `UnitNames` |
| `StorytellerUnit` | `unt.{R}.{U}` | `ResponsibilityName`, `UnitName` | `UnitType`, `UnitDefinition` (`string?`) |
| `StorytellerSubject` | `sbt.{S}` | | `ContextNames`, `ResponsibilityNames` |
| `StorytellerUsage` | `usg.{S}.{R}` | `SubjectName`, `ResponsibilityName` | `ContextNames` |
| `StorytellerContext` | `cnt.{S}.{C}` | `SubjectName`, `ContextName` | `ResponsibilityNames` |
| `StorytellerExecution` | `exe.{S}.{R}.{C}` | `SubjectName`, `ResponsibilityName`, `ContextName` | `UnitNames` (unchanged) |
| `StorytellerUnitOfExecution` | `uxe.{S}.{R}.{C}.{U}` | `SubjectName`, `ResponsibilityName`, `ContextName`, `UnitName` | |

- A responsibility's and a subject's own name is `Name`; they get no name members of their own.
- The relation lists are never `null`: an initializer's list is copied with `StorytellerAnnotation.CopyNames`, `null` gives an empty list, in the order the body has them.
- Each class has a public parameterless constructor for test doubles, an internal `[SetsRequiredMembers]` constructor from a base snapshot, the names and the copied lists (as `StorytellerExecution`), and a `WithDeletedAt` override that keeps the subtype, its names and its lists.
- The body keys of the SDK models (`SubjectKey`, `UsageKey`, …) are not exposed: the full key and the names carry them.

**The mapper** (`Reading/AnnotationMapper.Map`) switches on the type of the key that was read, for all seven types. The names come from the key; the relation lists and the unit's strings come from the body only when the body is the matching SDK subtype (`(annotation as Responsibility)?.UnitNames`, …). A body without the discriminator, or with another one, gives the key's subtype with empty lists and `null` strings, as an execution does today; nothing throws.

**The type map.** An internal `Reading/SnapshotTypes` maps each `AnnotationType` to its snapshot type and back. The catalog and who am I use it.

### 2. Who am I

- `StorytellerExecutionContext` keeps its members and their types: `Annotation`, `Subject`, `Responsibility`, `Usage` and `Context` stay `StorytellerAnnotation`, so a test double that puts a base `StorytellerAnnotation` there keeps working. A read puts the typed snapshot there, so `context.Annotation is StorytellerResponsibility responsibility` works for any key.
- New: `TAnnotation? Find<TAnnotation>() where TAnnotation : StorytellerAnnotation`, the annotation of that snapshot type, the key's own or an ancestor: `context.Find<StorytellerResponsibility>()?.UnitNames`, `context.Find<StorytellerUnit>()?.UnitType`. It is `null` when that annotation was not read, or when it is not of the type (a test double's base snapshot). `Find<StorytellerAnnotation>()` names no type and throws `ArgumentException`.
- Nothing else changes: the ancestors, `Unreadable`, the refresh and `GetSubscriptionStatus` stay. A background `404` keeps the subtype through its `WithDeletedAt`.

### 3. The target

A target says what to list or watch: the descendants of one type under one annotation, with optional filters.

```csharp
StorytellerTarget.UnitsOf("rst.invoicing")
StorytellerTarget.ExecutionsOf("sbt.northwind").WithContext("production")
StorytellerTarget.UnitsOfExecutionOf("@house.main.default.exe.northwind.invoicing.production").IncludingInactive()
```

- **`StorytellerTarget`** (static) has one factory per descendant type: `UsagesOf`, `ContextsOf`, `UnitsOf`, `ExecutionsOf` and `UnitsOfExecutionOf`, each taking the key of the ancestor and returning a `StorytellerTarget<TAnnotation>` of the matching snapshot type.
- **`StorytellerTarget<TAnnotation>`** (sealed, immutable, `where TAnnotation : StorytellerAnnotation`) has `Of` (the ancestor's key, a full key or an annotation key, its type code lower-cased), `Type` (the `AnnotationType` of the descendants), `Subject`, `Responsibility`, `Context` and `Unit` (the filters, `null` when unset), and `IncludeInactive`. `WithSubject`, `WithResponsibility`, `WithContext`, `WithUnit` and `IncludingInactive()` return a new target. `ToString()` describes it, as messages and logs name it: "the executions of sbt.northwind in the context production".
- **The key** is a full key, or an annotation key that the catalog puts in the organization, project and view of the options' `Key` at the call, as `GetExecutionAsync` does.
- **Validation at construction**, with `ArgumentException`:
  - the key must parse, as a full key or an annotation key;
  - the descendant type must be one the API lists under the key's type, the table of `AnnotationsHttp.GetDescendants`:

    | Of | Usages | Contexts | Units | Executions | Units of execution |
    | --- | --- | --- | --- | --- | --- |
    | `rst` | yes | | yes | yes | yes |
    | `sbt` | yes | yes | | yes | yes |
    | `unt` | | | | | yes |
    | `usg`, `cnt` | | | | yes | yes |
    | `exe` | | | | | yes |
    | `uxe` | | | | | |

  - a filter is one key segment, not empty and without a dot;
  - a filter must name a part of the descendants' key that the ancestor does not fix: `WithContext` on units, or `WithResponsibility` on the executions of `rst.invoicing`, throws.
- **The route segment** of each type is the API's: `usages`, `contexts`, `units`, `executions`, `units-of-execution`.
- **Status.** Active annotations only by default. `IncludingInactive()` adds disabled, scheduled and expired ones (the API's `includeInactive`). Deleted ones are never listed.
- **Filters apply in the process.** The API has no filter of the descendants, so a filter narrows what the list returned; the list costs the same.
- No fluent `catalog.Descendants(of).Units()` builder: the factories read the same, with one way to say it.

### 4. The catalog: `IStorytellerCatalog`

A new public interface, beside `ISubscriptionCatalog`, which stays as it is. A new member on `ISubscriptionCatalog` would break the applications that implement it for their tests.

```csharp
public interface IStorytellerCatalog
{
    Task<StorytellerAnnotation?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<TAnnotation?> GetAsync<TAnnotation>(string key, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;
}
```

`Catalog/StorytellerCatalog` implements it over the same `CatalogReader`, settings and transport.

**`GetAsync(key)`** reads one annotation of any type, as `GetExecutionAsync` reads an execution: in any status, with `includeDeleted`, and `null` when it is missing or deleted. The key is parsed with `CatalogKeys.Parse`. `GetAsync<TAnnotation>(key)` checks first that the key's type is that of `TAnnotation` (any type for `StorytellerAnnotation`), and throws `ArgumentException` otherwise. `SubscriptionCatalog.GetExecutionAsync` keeps its checks and messages and calls the same read (`CatalogReader.FindAsync`).

**`ListAsync(target)`**:

1. resolves the ancestor's full key, and throws `StorytellerConfigurationException` naming `STORYTELLER_KEY` for an annotation key without the options' `Key`;
2. reads the ancestor: the descendants of an annotation that does not exist are empty, so a `404` throws a definitive `StorytellerConfigurationException` "The annotation K does not exist …";
3. reads every page of `…/annotations/{ancestor}/{segment}`, with `includeInactive=true` when the target includes inactive annotations, through the paging of the informer (section 5);
4. keeps the items whose key parses, is of the target's type, descends from the ancestor and passes the filters;
5. point-reads each of them, at most 4 at once, with `includeInactive`: a `404` drops it (gone since the list), so does a deleted one, and an inactive one unless the target includes them;
6. returns the snapshots of the point reads, by key ordinally. Any other failure of a read or a page throws its `StorytellerConfigurationException` once every read ended.

Cost: one read of the ancestor, one request per page, and one point read per item, as list values are lossy.

**`WatchAsync(target, pollInterval)`** streams `StorytellerAnnotationChange<TAnnotation>`, a new public record `(StorytellerChangeKind Kind, TAnnotation Annotation, StorytellerRemoval? Removal = null)`: `Added` and `Changed` carry the snapshot of a point read, `Removed` the snapshot held before, with the `StorytellerRemoval` of the annotation itself. The interval follows the rule of `WatchExecutionsAsync` (30 s when `null`, zero polls once, otherwise 1 s to 45 days, ±10 % jitter); the arguments and the scope are checked at the call. Section 6 describes the polls.

### 5. The reads: `CatalogReader` and the shared paging

- `IAnnotationPages` gets `ReadDescendantsPageAsync(FullKey ancestor, string descendants, bool includeInactive, string? continuationToken, CancellationToken)`. `CatalogReader` implements it with `GetDescendantsAsync`, `includeInactive: true` or `null`; `ReadPageAsync(responsibility, list, …)` of the informer keeps its requests (no `includeInactive`) and its messages, the hint of a `403 AnnotationRestricted` included. The generic list says that listing needs a machine restricted to the ancestor or to an annotation it descends from, or an unrestricted one.
- `CatalogReader.FindAsync(FullKey, CancellationToken)`: the read of `GetExecutionAsync` and `GetAsync`, `null` for a `404` or a deleted annotation.
- **`Informer/ListPaging`** (internal static): the loop of `PollingSubscriptionSource.ReadListAsync`, moved as it is: it follows the tokens through empty pages, keeps the item listed last per annotation key, and fails a repeated token or more than `MaxPages` pages with the same message ("Listing the executions of K did not end: …"). `PollingSubscriptionSource` calls it; `PollingSubscriptionSource.MaxPages` stays, as `ListPaging.MaxPages`. Nothing else of it changes, nor of `SubscriptionBatch` and `SubscriptionsService`: the join of executions with usages stays the specialised informer behind `WatchExecutionsAsync` and the subscriptions, as its join, pair fingerprints and removal order are not those of one list.
- `ScriptedPages` of the tests implements the new member.

### 6. The descendant informer

`Informer/DescendantPollingSource` (internal) polls one resolved target (`Informer/DescendantScope`: the ancestor's full key, the type, the segment, the filters, `IncludeInactive`), with the same constructor arguments as `PollingSubscriptionSource` (pages, interval, concurrent reads, clock, random, logger). Each enumeration is cold: its own held annotations and its own limiter of 4 reads.

One poll:

1. **The ancestor.** Until a poll completed, it reads the ancestor; a `404` fails the poll with a definitive "The annotation K does not exist …".
2. **The list**, every page, through `ListPaging`.
3. **Candidates**: the items of the type that descend from the ancestor and pass the filters, each with its `ListFingerprint`.
4. **Diff** against what it holds:
   - a candidate not held is read; it is `Added` when it is accepted (not deleted, and active unless the target includes inactive ones);
   - a held candidate whose fingerprint changed is read again; accepted, it is `Changed`; otherwise it stays as it was, fingerprint included, and the next poll reads it again;
   - a held annotation that is not listed is read with deleted ones: missing gives `Removed` with `Missing`, deleted with `Deleted`, inactive (when the target does not include them) with its status; otherwise the list missed it and it stays. A failure other than a `404` keeps it and logs a warning.
5. **Order**: `Removed`, then `Changed`, then `Added`, each by key.

A failed poll emits nothing, keeps what is held, logs a warning ("Polling the units of K failed …") and polls again; with a zero interval, `WatchAsync` throws its failure. Cancellation ends the enumeration with `OperationCanceledException`, a failure seen after a stop included. Logs go to the informer's category, `_42.Platform.Storyteller.Sdk.Hosting.Informer`.

### 7. Registration

`AddStoryteller` adds `IStorytellerCatalog` with its own `TryAddSingleton`, over the same settings, bound credential, reader and transport. An application that registered its own `ISubscriptionCatalog` keeps it and still gets the default `IStorytellerCatalog`, and one that registered its own `IStorytellerCatalog` keeps that.

### 8. Freshness of relation arrays

Relation arrays do not move `Modified`, and list items do not carry them, so neither a list fingerprint nor a stamp can tell that a parent gained a child. The fix belongs to the backend (stamp the parent, or serialize the subtypes in lists), which this change does not touch. Hosting documents it:

- a new descendant raises no `Changed` of its parent in any watch; the parent's lists (`UnitNames`, …) are as fresh as its last point read;
- to follow the children, watch the descendant type itself: `WatchAsync(StorytellerTarget.UnitsOf("rst.invoicing"))` raises `Added` for a new unit;
- who am I reads the relation lists again at every refresh.

A test locks it: a relation-only change with the same `Modified` raises no `Changed`, and the watch of the children raises `Added`.

### 9. Machine restrictions

A machine restricted to an annotation reads that annotation and its descendants. It can target that annotation and any annotation under it, as the ancestor of a target or with `GetAsync`. It cannot target an ancestor of its annotation, nor the other descendants of that ancestor: the request answers `403 AnnotationRestricted`, which the catalog throws as `StorytellerConfigurationException` with a message that says so. `hosting.md` states this in its table of scopes.

### 10. The playground's executor

- `ExecutorCommand` injects `IStorytellerCatalog`.
- **The units of a responsibility.** For an `rst.R` key with `all` or `units`, the run lists `StorytellerTarget.UnitsOf("rst.R")` once, before the executions, in one shot and with `--watch`, and writes each unit's document in key order. The set of written units stays, so the units that executions name are not written twice. A unit that no execution names now shows.
- **`units` needs no context for `rst.R`**: without `--context` it writes the units and ends, as a `unt` key does. `all` still needs `--context` for the executions.
- **A failed list** prints `! the units of rst.R: {message}` on standard error, the run goes on with the executions, and it exits with `1`.
- `usg`, `unt`, `exe` and `uxe` keys keep their behaviour: listing `rst.R/units` would need the machine of the responsibility.
- **Who am I** prints the typed members of the key's annotation: the units of a responsibility, the contexts and responsibilities of a subject, the contexts of a usage, the responsibilities of a context, the units of an execution, the type of a unit.
- `FakeApi` gets the units list of `rst.playground`, a standalone unit `unt.playground.archive` with its document, and typed bodies with relation arrays; `ExecutorTests` cover the standalone unit, the shared unit written once, the selections that list no units, `units` without a context, a failed list, and the typed who am I.

### 11. Tests

In `src/Libraries/Platform/Storyteller/Hosting/test/`:

- **Snapshots**: for every type, names from the key over conflicting body members, relation lists and unit strings from the body, empty lists and `null` strings for a missing or other discriminator, copies of what an initializer gives, `null` initializers, `WithDeletedAt` keeping the subtype; the reflection test of public constructors and setters covers the new members.
- **Who am I**: an `rst` key gives `StorytellerResponsibility` with its `UnitNames`; a `uxe` key gives typed ancestors (`Find<StorytellerUnit>()`, `Find<StorytellerUsage>()`, …); a background `404` keeps the subtype; `Find<StorytellerAnnotation>()` throws.
- **Targets**: every unsupported combination, filters that do not apply or are fixed by the ancestor, dotted or empty filters, invalid keys, the segments, `ToString()`, immutability.
- **The catalog** over HTTP with `FakeCatalog`, which serves the descendants of any annotation by the graph, with paging, `includeInactive`, lossy values, and list items without the subtype members, as the API lists them: requests and flags, point-read fidelity, filters, inactive ones, a full key of another scope, an annotation key without the options' key, a missing ancestor, missing and deleted results, `GetAsync<T>` with the wrong type, `403 AnnotationRestricted`.
- **The watch**: `Added` of the first poll, `Changed` on a new `Modified`, every removal reason, expiry without a write, a failed change read again, a failed poll, the zero interval, independent enumerations, cancellation, the freshness test of section 8.
- **Registration**: `IStorytellerCatalog` is a singleton, one registered before is kept, and one `ISubscriptionCatalog` registered before does not hide it.
- The existing informer, list, subscriptions, catalog and options tests stay as they are and pass.

### 12. Documentation

- `docs/Platform/Storyteller/hosting.md`: the services table, the snapshot table per type with the fallback of the discriminator, `Find<T>()`, `IStorytellerCatalog` with targets, the table of combinations, filters and status, list cost, the watch and its comparison with the other two, freshness, restrictions, errors and logs.
- `docs/Platform/Storyteller/playground.md`: the units of an `rst` key come from the catalog, standalone units show, `units` without a context, the typed who am I; the limit "Units come from executions" goes.
- `docs/Libraries/Platform/Storyteller/Hosting/package-readme.md`: a line on `IStorytellerCatalog`; the package description in `Hosting.csproj` and `CLAUDE.md` name the catalog of any descendants.

## Phases

1. **Typed snapshots and who am I**: sections 1 and 2, with their tests.
2. **The catalog and the descendant informer**: sections 3 to 9, with their tests.
3. **The playground and the documentation**: sections 10 and 12.

## Decisions

- **Property types stay.** Narrowing `Subject`, `Usage`, … to the subtypes would break binary compatibility and the test doubles that put base snapshots there. `Find<T>()` gives typed access without either.
- **A new interface, `IStorytellerCatalog`.** Not new members on `ISubscriptionCatalog`, which applications implement for tests. Registered on its own, so it does not depend on which `ISubscriptionCatalog` is registered.
- **The subscriptions keep their informer.** The join of executions with usages is not one list; it shares the paging only. Rewriting it over the generic informer would risk the behaviour that the hosted subscriptions and their tests depend on, for no gain to their users.
- **Freshness is documented, not fixed.** The fix is in the backend; polling every held parent would cost a read per annotation per poll.
- **Point reads for lists.** `ListAsync` costs a read per item, as list values are lossy and lack the relations.
- The CodeRabbit coding plan of the issue was a second opinion. This spec follows it on the separate interface, the kept property types, the paging-only sharing, documenting freshness and the inclusion option, and differs in adding `Find<T>()`, checking the ancestor in `ListAsync`, rejecting filters the ancestor fixes, the context-free `units` of an `rst` key in the playground, and the typed who am I there.

## Out of scope

- Stamping the parent's `Modified` when its relation arrays change, or serializing the subtypes in lists (backend).
- A bulk read, or a watch of change events (424-49), in place of polling.
- A fluent builder over the catalog (`catalog.Descendants(of).Units()`).
