# Phase 2 of Hosting: time zone, chain status and managed identity federation

Linear: [424-256](https://linear.app/424fun/issue/424-256/hosting-time-zone-chain-status-and-managed-identity-federation).

## Overview

Executes phase 2, the chain status in who am I and the lists (sections 2.1 to 2.4, and 2.5 for `ListAsync` and `ListWithAncestorsAsync`, with their parts of sections 4 and 5), of [Hosting: time zone, chain status and managed identity federation](../2026-11-12%20Hosting%20-%20time%20zone,%20chain%20status%20and%20managed%20identity%20federation.md). A status can now count every ancestor of a key: `GetSubscriptionStatus(now, StorytellerStatusScope.Chain)`, with `FindInactiveAnnotation` for the annotation that decides it. `IStorytellerCatalog` reads any key with its ancestors (`GetWithAncestorsAsync`) and the descendants of a target with theirs (`ListWithAncestorsAsync`), and a target with `RequiringActiveAncestors()` lists only the descendants under active ancestors. Everything is additive: `GetSubscriptionStatus(now)`, the requests of `ListAsync` for a target without the requirement, and every existing message and `ToString()` text stay as they were, and a test double of the catalog compiles unchanged. `WatchAsync` does not honour the requirement yet: that is phase 3. A review of the change confirmed seven findings. One changed the code, in a rule that only a context built by hand can reach. The other six changed comments, `hosting.md` and tests. The change is `0eedc95e`.

## What Was Done

### 2.1 The scope of a status

- `StorytellerStatusScope` is a new public enum in its own file, `src/Libraries/Platform/Storyteller/Hosting/src/StorytellerStatusScope.cs`: `Subscription = 0` and `Chain`, with the comments of the spec.
- The comments of `StorytellerSubscriptionStatus` now hold for both scopes: what `Deleted` and `Unknown` mean in each. Its members did not change.

### 2.2 Who am I

- `StorytellerExecutionContext.GetSubscriptionStatus(now, scope)` and `FindInactiveAnnotation(now, scope)` are new. `GetSubscriptionStatus(now)` stays and means `Subscription`.
- **One routine.** The private `Decide` returns the status and the annotation that decides it, and the three public members call it, so they cannot disagree. `Counted` gives the annotations that a scope counts, in its order: `Chain` for the chain scope, and for the subscription scope the key's annotation, its execution and its usage, or the key's annotation alone for a key without an execution in its chain. `HasUnreadable` tells `Unknown`.
- **With `Chain`:** `Deleted` for a deleted key or execution, else the status of the first annotation of `Chain` that is not active, else `Unknown` when an ancestor is in `Unreadable`, else `Active`. A key of every type counts its ancestors.
- **`FindInactiveAnnotation`** gives the first annotation that is not active in the order of the scope. For `Deleted` it is the deleted one, the key's own first and else its execution. It is `null` for `Active` and `Unknown`. It returns the snapshot that the context holds, and reads nothing.
- **The subscription scope did not change.** `Decide` gives `GetSubscriptionStatus(now)` the answer of the previous code for every input, and the existing tests of the status pass unchanged.
- **Left open by the spec: `Unknown` with `Chain`.** An entry of `Unreadable` counts when it is a full key of a type that the context holds no annotation of. That is the rule that `IsUnreadable` already has for the subscription scope. For a context from a read it equals the spec's "an ancestor of the chain is in `Unreadable`": a read puts there only the full keys of the ancestors that it could not read. A context built by hand can differ. An annotation key, a text that is no key, or the full key of a type that is the key's own or in `Ancestors` does not count, which is narrower than the spec's sentence. The full key of a type that is no ancestor of the key counts, which is wider.
- **Left open by the spec: `Deleted` with `Chain`.** Only the key's annotation and its execution are asked for a deletion, as the spec says ("as today"). A deleted ancestor of another type, which only a context built by hand can hold, does not give `Deleted`: a read leaves such an ancestor out with its `404`.
- **Left open by the spec: a scope that is not defined** throws `ArgumentOutOfRangeException` from both members, with the parameter name `scope` and the message "Unknown status scope.". It is checked first, before the deletion.
- **Fixed after review: a deleted execution that is a base snapshot.** `Decide` asked the typed `Execution` for the deletion in both scopes, while the chain scope counts whatever `Ancestors` holds. A context built by hand for a `uxe` key, with a deleted base `StorytellerAnnotation` as its execution, was `Active` in the chain scope, though the same snapshot disabled would have been `Disabled`. With `Chain` the deletion is now asked of the execution that the chain holds (`Find(AnnotationType.Execution)`). `Subscription` still asks the typed one, so `GetSubscriptionStatus(now)` is unchanged. A read cannot reach the state: it leaves a deleted ancestor out, and the mapper builds a typed `StorytellerExecution`.
- **Fixed after review: the rule of `Unknown` was only in a test.** The comment of the private `HasUnreadable` and a new "Test doubles" bullet of "The scope of a status" in `hosting.md` now state it. The code did not change.

### 2.3 Any key with its ancestors

- `Reading/AncestorReader` (internal, static) and `Reading/AncestorRead` (internal record: the ancestor's `FullKey`, its snapshot or none, and the failure of its read, with `IsRestricted`) are new. The reader has `KeysOf` (the full keys of the ancestors, roots first), `ReadAsync` (one ancestor: a `403` `AnnotationRestricted` makes it restricted, a `404` missing, and any other failure throws), `ReadAllAsync` (the ancestors of a key at once, every read ending before the first failure is thrown), `ContextOf` (the `StorytellerExecutionContext` of a key from its reads) and `AreActive` (for 2.5).
- **`ExecutionContextService`** reads its ancestors through `ReadAllAsync` and builds its context with `ContextOf`. Its own `ReadAncestorAsync` and its private `AncestorRead` record are gone. It keeps the deleted key, the cache, the refresh, the skipped ancestors, its stopping token and its log line. Its existing tests pass unchanged.
- **`IStorytellerCatalog.GetWithAncestorsAsync(key)`** is new. `StorytellerCatalog` parses the key as `GetAsync` does, returns `null` for a missing or deleted key without reading an ancestor, then reads the ancestors at once and builds the context. `ReadAt` is when the read started, on the settings' `TimeProvider`.
- **The default interface implementation** calls the new internal static `Catalog/CatalogDefaults`. It reads the key with the double's `GetAsync`, then each ancestor with `GetAsync` by its full key. It knows no restrictions: nothing lands in `Unreadable`, an ancestor that `GetAsync` answers `null` for is left out, and one it throws for fails the call. `ReadAt` is `DateTimeOffset.UtcNow`, as the interface has no `TimeProvider`.
- **Left open by the spec: the shape of the reader.** `AncestorReader` holds no state and takes the caller's point read as a delegate, as `ListPaging` does, so each caller passes its own tokens. Who am I passes its stopping token, and the catalog passes none.
- **Left open by the spec: "the key and its ancestors at once".** The key is read first, and then its ancestors at once, as who am I does. The ancestors of one key do not go through the limiter of the lists.
- **Who am I's log line** of restricted ancestors now takes them from the built context: the entries of `Unreadable` that this read asked for. Its order and its text are as before. `GetWithAncestorsAsync` logs nothing about them: the line stays who am I's.
- **Left open by the spec: the defaults ask one at a time.** They wait for each `GetAsync` before the next one, so a test double need not answer calls at once.
- **Beyond the spec: a snapshot without a full key.** The defaults take the ancestors' keys from the `Key` of the snapshot that the double answered, because the argument may be an annotation key and a list has no key argument. A snapshot whose `Key` is not a full key throws `InvalidOperationException`: "The catalog answered a snapshot whose Key, '…', is not a full key, @{organization}.{project}.{view}.{annotation key}, so the keys of its ancestors are not known. Give the snapshots of a test double their full keys." A context without ancestors would be a wrong answer. `StorytellerAnnotation.Key` is documented as the full key, so only a malformed double meets it.
- **Fixed after review.** The comment of `GetWithAncestorsAsync` in `IStorytellerCatalog` did not name that failure. It now says where the full keys come from and what a snapshot without one throws. A new test covers the list's path, which had none.

### 2.4 A target with its ancestors

- **`IStorytellerCatalog.ListWithAncestorsAsync(target)`** is new. In `StorytellerCatalog` both lists share one private `ListAsync(scope, limiter, withAncestors, …)`, which returns the descendants it keeps (`Listed`: the full key and the snapshot) and the reads of their ancestors (`Listing`).
- **Reads.** It lists and reads the descendants as `ListAsync` did. Then `ReadAncestorsAsync` reads each distinct ancestor of the descendants it kept once, through the list's limiter of 4. A context per descendant comes from `AncestorReader.ContextOf`, by key. Descendants that share an ancestor get the same snapshot of it.
- **The target's own ancestor** is an ancestor of every descendant. The read of the list's existence check serves for it, so it is not read again, and the cost is the spec's: 1 + 10 + 10 + 20 for its example.
- **A restricted ancestor** goes to `Unreadable` of each context that has it, and a missing one is left out. Any other failure of an ancestor's read throws, once every read ended.
- **`ReadAt`** is when the call started, on the settings' `TimeProvider`, the same in every context.
- **Without the requirement of 2.5, `ListAsync` sends the requests it sent before.** The existence check goes through `AncestorReader.ReadAsync`, which makes the same request: a `404` still throws the definitive failure of a missing ancestor, and a `403` `AnnotationRestricted` still goes on to the list.
- **The default interface implementation** (`CatalogDefaults.ListWithAncestorsAsync`) builds on the double's `ListAsync` and `GetAsync`. It asks for each distinct ancestor once, and orders the contexts by key itself, as a double may list in any order.
- **Left open by the spec: the order of the reads.** The ancestors are read after the descendants, and only those of the descendants that the list kept by their own status.

### 2.5 Targets that require active ancestors

Done for `ListAsync` and `ListWithAncestorsAsync`. `WatchAsync`, `StorytellerRemoval` and its documentation are phase 3.

- **`StorytellerTarget<TAnnotation>.RequiringActiveAncestors()`** and **`RequireActiveAncestors`** are new. The copy constructor copies the flag, so it combines with `IncludingInactive()` and the filters in any order. `ToString()` ends with ", under active ancestors" for such a target, and is unchanged for any other.
- **`Informer/DescendantScope`** carries the flag (`RequireActiveAncestors`), and builds the failure of a restricted ancestor (`CannotReadAncestor`).
- **In the lists**, a descendant is kept when every ancestor of its chain is active (`AncestorReader.AreActive`). A missing ancestor leaves its descendants out. With `IncludingInactive()` the inactive descendants under active ancestors are kept too.
- **A restricted ancestor** fails the call with a definitive `StorytellerConfigurationException`: the ancestor's key, the status and the error code of the read's exception (`403`, `AnnotationRestricted`), and that exception as the inner one.
- **Left open by the spec: the wording of the message.** "The machine may not read {ancestor}, so the catalog cannot tell which of the {descendants} of {target's ancestor} are under active ancestors. Requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it." The descendants are named as the other messages of a list name them, such as "the executions of @acme.billing.default.rst.invoicing".
- **Left open by the spec: when a restricted ancestor fails the call.** Whenever it is among the ancestors of a descendant that the list kept by its own status, also when another inactive ancestor would drop that descendant anyway. With several restricted ancestors the first by ordinal full key is named, so the failure does not depend on the order in which the reads ended. Any other failure of a read, such as a `500`, is thrown first.
- **Left open by the spec: the time.** `now` is taken once, after the descendants' reads, and tells the status of the descendants and of their ancestors. Time that passes while the ancestors are read does not count.
- **The target's own ancestor counts.** This follows from "every ancestor of its chain": the executions of a disabled responsibility are none with the requirement.
- **The default implementation of `ListWithAncestorsAsync`** keeps the descendants under active ancestors itself, as the `ListAsync` of a test double may not know the requirement. It takes `now` from `DateTimeOffset.UtcNow`, after `ListAsync` returned.
- **Until phase 3, `WatchAsync` does not honour the requirement.** `DescendantScope` carries the flag, so the watch's log texts show ", under active ancestors" for such a target, but the watch still yields every descendant. The comments and `hosting.md` name the lists only.

### 4. Documentation

- `docs/Platform/Storyteller/hosting.md`:
  - the table of who am I's snapshot has `GetSubscriptionStatus(now, scope)` and `FindInactiveAnnotation(now, scope)`;
  - "The scope of a status" is new: the table of both scopes, the cases of `Chain`, what decides, a restricted machine, test doubles, and `FindInactiveAnnotation` with a sample that logs the annotation;
  - "The status of the subscription" links the `Chain` scope where it says that an execution stays active under a disabled subject;
  - the catalog: the members' table and the sample, the bullets of the targets (`RequiringActiveAncestors()`, `RequireActiveAncestors`, `ToString()`), step 5 of "Lists", and the new sections "With the ancestors", "Requiring active ancestors" and "Test doubles of the catalog";
  - "Scopes and restricted machines": the row of `IStorytellerCatalog`, the ancestors that a restricted machine may not read, and that requiring active ancestors needs a machine that may read them;
  - "Cost and limits": the reads of `GetWithAncestorsAsync` and of the lists with ancestors, with the spec's example;
  - one phrase each in the introduction's table, in the link to this spec and in the "Test doubles" bullet of the snapshots.
- `docs/Libraries/Platform/Storyteller/Hosting/package-readme.md`: one line for the chain status.
- The comments of `IStorytellerCatalog`, of each new member and of `StorytellerTarget<TAnnotation>` say the same.
- **Placement, which the spec left open.** "The scope of a status" sits after "The status of the subscription", and the three new sections of the catalog after "Lists".
- **Beyond the spec.** "Test doubles of the catalog" describes the default implementations: what they read, that they know no restrictions, their times, and the failure of a snapshot without a full key. The spec asks only for the restrictions.
- **Fixed after review.**
  - "Requiring active ancestors": the bullet "Without it" followed the one of `IncludingInactive()`, so it read as "without `IncludingInactive()`", and it described the change, not the API. It now names `RequiringActiveAncestors()` and says what each list reads without it.
  - "Test doubles of the catalog": a default member is a member of the interface, not of the double, so a call on a variable of the double's own type does not compile. The section now says to call them through `IStorytellerCatalog`.
  - The comment at the top of `IStorytellerCatalog` said that "the ancestors above" a restricted machine's annotation are unreadable to it. Nothing is above a responsibility, yet the subjects and contexts of its executions are unreadable. It now says "any other ancestor of what it reads".
- **Left to their phases.** The removal by an ancestor and the watch belong to phase 3, and the documents of the federation to phase 4. No document mentions a watch that requires active ancestors.
- **Not changed.** The line of the root `CLAUDE.md` about `IStorytellerCatalog` ("get, list, and a polling informer") still holds. No other document outside `specs/` lists the members of the catalog or of who am I.

### Tests

- `test/Executions/ExecutionContextTests.cs`:
  - with `Chain`: a disabled subject, a scheduled context and an expired responsibility decide the status, and are the annotation found, while the subscription scope stays `Active`;
  - a key of every type counts its ancestors, and a key without ancestors has the status of its own annotation;
  - the nearest inactive annotation wins, in the chain order, and the status follows `ValidFrom` of the subject at the times it is given;
  - an unreadable ancestor is `Unknown` while the rest is active, for five pairs of a key and an ancestor, and does not hide an inactive one; a machine restricted to its execution is `Unknown` in both scopes; a missing ancestor does not count;
  - a deleted key is `Deleted` in both scopes before an inactive ancestor; a deleted execution of a unit of execution; a unit of execution deleted with its execution finds its own annotation;
  - the subscription scope equals the status without a scope, for twelve pairs of a key and a disabled annotation, and finds the execution before the usage;
  - a scope that is not defined is refused by both members;
  - `FindInactiveAnnotation` gives the snapshot of the context and makes no request;
  - a context built by hand has its chain status.
  - **After the review:** a deleted execution that is a base snapshot (`Deleted` with `Chain`, not counted with `Subscription`), which fails without the fix; and a theory of six entries of `Unreadable` in a context built by hand, with the full key of a type that is no ancestor of the key, which no test pinned before. The comment above `ADeletedExecution_OfAUnitOfExecution_IsDeletedInBothScopes_AndIsTheAnnotationFound` named a background read as a source of a deleted execution next to a key that is not. A background `404` deletes the key's own annotation, so the comment now names only a context built by hand.
- `test/Catalog/CatalogTests.cs`:
  - `GetWithAncestorsAsync`: a key of four types read on every call, the flags of its requests, a missing or deleted key (`null`, no ancestor read), a restricted ancestor (`Unreadable`), a missing ancestor (left out, purged or deleted), a `500` of an ancestor (thrown after every ancestor was asked), a failure of the key itself, a full key in another scope, an annotation key without the options' `Key`, keys that do not parse, and `ReadAt`;
  - `ListWithAncestorsAsync`: one read per distinct ancestor, counted, the target's own included; the list and the filters of `ListAsync`; the chain status and the time zone of each descendant; a restricted ancestor in each context that has it; a missing ancestor; any other failure; an ancestor that does not exist; no descendants; a full key in another scope; `ReadAt`; at most four reads of ancestors at once;
  - `RequiringActiveAncestors` on `ListAsync`: a disabled subject, an expired usage, a context that is not valid yet and a disabled responsibility drop their executions; a missing ancestor; a restricted ancestor fails definitively, with its message, key, status, error code and inner exception; no ancestor but the target's own is read without the requirement; the combination with `IncludingInactive()`; the time of the descendants;
  - `RequiringActiveAncestors` on `ListWithAncestorsAsync`: the descendants under active ancestors, and the failure of a restricted one.
- `test/Catalog/TargetTests.cs`: a new target with the flag, the combination with `IncludingInactive()` and the filters in any order, `ToString()`, and `DescendantScope` carrying the flag.
- `test/Catalog/CatalogDefaultsTests.cs` is new: the default implementations over a double that implements the four older members, called through `IStorytellerCatalog`. For one key: the key and each ancestor by its full key, an annotation key, a key that `GetAsync` does not find, an ancestor that it does not find, one that it throws for, and a snapshot without a full key. For a list: each distinct ancestor asked for once and the contexts by key, a target that requires active ancestors, an ancestor that `GetAsync` throws for, a descendant without a full key, no descendants, and a `null` target.
- **Helpers.** `FakeCatalog` gained `PutWithAncestors` (an annotation and every ancestor that the catalog does not hold yet). In `ExecutionContextTests`, `Snapshot` takes `isDisabled`, and `DeletedExecution` builds a typed deleted execution. `DoubleCatalog` of `CatalogDefaultsTests` is not a parallel fake of the API: it is the application's test double that the defaults are for.
- **Time.** The tests use the fixture's time provider and held reads. No test sleeps. `CatalogDefaultsTests` compares `ReadAt` with the system clock around the call, as the defaults have no `TimeProvider`.
- **Left open by the spec.** "Every read ends before the first failure is thrown" is checked by the count of requests: every ancestor was asked. The limiter test holds the reads of the ancestors, so it needs no real latency.
- The Hosting tests went from 403 to 511, all passing: 54 new cases in `ExecutionContextTests`, 38 in `CatalogTests`, 4 in `TargetTests` and the 12 of `CatalogDefaultsTests`. 100 came with the implementation and 8 with the fixes after the review. A rebuild of Hosting and its tests gives no warning in their files. `DotNet.SimpleExecutor` of the playground, the only other project that references Hosting, builds unchanged, and its 35 tests pass.

### Deviations

- No member or signature differs from the spec, and for a context that the library read no rule does.
- For a context built by hand, two rules of 2.2 differ from the spec's sentences, both described above: which entries of `Unreadable` give `Unknown` with `Chain`, and that `Deleted` with `Chain` counts a base snapshot of the execution, where "as today" would ask the typed one only.
- The spec left open what the sections above decide, with their reasons: the undefined scope, the shape of `AncestorReader`, the order of the reads of a key and of a list, the time of the ancestors' status, when a restricted ancestor fails a list and the wording of its message, the order of the defaults' calls, and the place of the new sections in `hosting.md`.
- Beyond the spec: the `InvalidOperationException` of the defaults for a snapshot without a full key, the section "Test doubles of the catalog", and in the tests a failure of the key itself, keys that do not parse, a key without the options' `Key`, a full key in another scope and an empty list.

### After the review of the pull request

- **The target's own ancestors.** A list of a target that requires active ancestors asked only for the ancestors of the descendants it kept, so a machine that may not read them got an empty list while the target had no descendants, and a `403` once it had one. `ListAsync` and `ListWithAncestorsAsync` now also ask for the ancestors of the target's own ancestor, which are among those of every descendant. They cost a read of their own only while the target has no descendants. An ancestor that only a descendant names, such as the subject of a responsibility's execution, is still asked for once such a descendant is listed: its key is not known before. `ListAsync_RequiringActiveAncestors_OfNoDescendants_ARestrictedAncestorOfTheTarget_FailsToo` pins it, and `hosting.md` says both.
