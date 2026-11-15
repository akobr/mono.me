# Phase 1 of Hosting: time zone, chain status and managed identity federation

Linear: [424-256](https://linear.app/424fun/issue/424-256/hosting-time-zone-chain-status-and-managed-identity-federation).

## Overview

Executes phase 1, the time zone (sections 1.1 to 1.3, with their parts of sections 4 and 5), of [Hosting: time zone, chain status and managed identity federation](../2026-11-12%20Hosting%20-%20time%20zone,%20chain%20status%20and%20managed%20identity%20federation.md). Every snapshot now carries the `TimeZone` of its annotation as stored. Who am I gives the chain of its key, the key's annotation and the ancestors that were read with the nearest first, and `GetEffectiveTimeZone()` gives the first zone that is set along it. Everything is additive: no existing member, message or `ToString()` text changed. A review of the change confirmed two findings, neither a defect: both are rules that only a context built by hand can reach. One added a sentence to `hosting.md`, and the code did not change. The change is `6f8d685a`.

## What Was Done

### 1.1 `TimeZone` on the snapshots

- `StorytellerAnnotation.TimeZone` (`string?`, `init`) is new in `src/Libraries/Platform/Storyteller/Hosting/src/StorytellerAnnotation.cs`: the zone as stored, neither checked nor converted.
- The copy constructor copies it. `WithDeletedAt` and the seven typed snapshots go through that constructor, so they keep the zone without a change of their own.
- `Reading/AnnotationMapper.Map` sets it from the SDK's `Annotation.TimeZone`. A `null`, empty or white-space value becomes `null`; any other one is kept as it is, untrimmed.
- `GetStatus(now)` and `ToString()` did not change.
- **Left open by the spec.** The mapper's rule of a blank zone is in the comment above the class, next to its other rules, not inside the object initializer.

### 1.2 The chain of a key

- `StorytellerExecutionContext.Chain` (`IReadOnlyList<StorytellerAnnotation>`) is new: the key's annotation first, then the entries of `Ancestors` in the fixed order of the private `ChainOrder` (`uxe`, `exe`, `cnt`, `usg`, `unt`, `sbt`, `rst`). That gives the spec's table for every key type.
- `BuildChain` takes `Annotation` and `Ancestors` only. A context built with an initializer has its chain, and an ancestor that is unreadable or missing is not in it. The list is read-only (`List<T>.AsReadOnly()`) and never `null`.
- **Cached, which the spec left open.** An initializer sets `Annotation` and `Ancestors` after the constructor ran, so the chain is built at the first access, not in the constructor. `Interlocked.CompareExchange` publishes it: calls at once build equal chains, the first one stays, and every caller gets the same list.
- **The deleted copy.** The private copy constructor does not take the cached list. The copy that `WithKeyGoneAt` makes after a background `404` builds its own chain, headed by its deleted annotation, also when the chain of the previous snapshot was read before.
- **An entry of `Ancestors` under the key's own type**, which only a context built by hand can hold, is left out of `Chain` (`type != Annotation.Type` in `BuildChain`). The private `Find` gives the key's annotation for that type, so the chain holds what the members give, and one annotation per type, as the spec's table has it. A read cannot produce the state: `ExecutionContextService.ReadAsync` fills `Ancestors` from `GetAllAncestorKeys()`, which leaves the key itself out and gives no key of its type. `AnAncestorOfTheTypeOfTheKey_IsNotInTheChain` pins it.
- **Not filtered.** An entry of a type that is no ancestor of the key's type, again only in a context built by hand, stays in the chain at its place of the order. `ChainOrder` is the chain of the longest key, so it lists `UnitOfExecution` although no key has one as an ancestor.
- **Fixed after review.** The rule of the key's own type was only in the comments of the code and of the test. The "Test doubles" bullet of "The chain" in `hosting.md` now states it. The code and the tests did not change.

### 1.3 The effective time zone

- `StorytellerExecutionContext.GetEffectiveTimeZone()` is new: it walks `Chain` and returns the first `TimeZone` that is set, as stored, or `null` when none is.
- It reads nothing. An ancestor in `Unreadable` is not in the chain, so it is skipped, and a machine restricted to its execution without a zone of its own gets `null`.
- **A blank zone counts as not set in the walk too** (`string.IsNullOrWhiteSpace`). A read never carries one, as the mapper gives `null`, so for a read the check equals a null check. A snapshot built by hand may carry one, as the `init` accessor keeps what it is given: the walk skips it, and the snapshot's `TimeZone` stays as given. The zone that is returned is not trimmed. `TheEffectiveTimeZone_SkipsABlankOne_AndIsAsStored` pins it.
- **Reviewed, not changed.** The review asked whether that check goes beyond the spec's "the first `TimeZone` set". Section 1.1 says a blank value is none, and a null-only check would return the `""` of a snapshot built by hand, so the caller's `?? "UTC"` would not apply. The code and `hosting.md` stay as they are.

### 4. Documentation

- `docs/Platform/Storyteller/hosting.md`:
  - the introduction links this spec next to the two earlier ones;
  - the table of who am I's snapshot has `Chain` and `GetEffectiveTimeZone()`;
  - "The chain" is new: the table of the chain order, only what was read, read-only and never `null`, test doubles;
  - "The time zone" is new: as stored, it reads nothing, the caller's fallback, not the configuration's rule (with the link to `inheritance.md#what-an-ancestor-changes`), and the status does not use it, with a sample that takes the zone through `TimeZoneInfo.TryFindSystemTimeZoneById`;
  - the table of `StorytellerAnnotation` has `TimeZone`.
- `docs/Libraries/Platform/Storyteller/Hosting/package-readme.md`: one line for the time zone.
- **Placement, which the spec left open.** The two sections sit between "Refresh" and "The status of the subscription", so the status section of phase 2 can refer to the chain.
- **Not documented.** The difference from khosmos (section 1.3) is about the migration, not about Hosting. That the admin UI gives a new annotation the project's default zone, where the walk then stops, is open question 2 and undecided.
- **Left to their phases.** The status scope, `FindInactiveAnnotation`, the catalog, "Cost and limits", "Scopes and restricted machines" and the package readme's line of the chain status belong to phases 2 and 3, and the documents of the federation to phase 4. What a restricted machine gets is in "The time zone".
- No other document outside `specs/` lists the members of Hosting's snapshots, so nothing else changed.

### Tests

- `test/Snapshots/SnapshotTests.cs`: a zone through a point read is kept as stored (an IANA id, a Windows id, one with spaces around it, an unknown one); an absent one is `null`, and so is an empty or white-space one; a zone changes neither `GetStatus` nor `ToString()`; `TimeZone` is a public `init` member.
- `test/Snapshots/TypedSnapshotTests.cs`: the mapper with a `null`, an empty, a white-space and a set `TimeZone` on the SDK's object; the snapshot of each of the seven types and its deleted copy keep the zone.
- `test/Executions/ExecutionContextTests.cs`:
  - `Chain` for the seven key types: the key's annotation first, then the ancestors in the order of the spec's table, and the same list on every access;
  - unreadable ancestors (`403 AnnotationRestricted`) and missing ones (`404`) are left out, for the five key types that have ancestors;
  - a context built with an initializer, with and without ancestors: its list is read-only and does not follow the dictionary it was given;
  - an entry of the key's own type;
  - the chain of the deleted copy after a background `404`, with the chain of the previous snapshot read before, which stays as it was;
  - `GetEffectiveTimeZone` along the chain of each key type: every annotation sets a zone of its own, then they lose it one by one, the nearest first, until none is set;
  - an unreadable ancestor is skipped and no request is made; a machine restricted to its execution; a blank zone of a snapshot built by hand.
- **A JSON `null`** is not tested through a read: the generated `Annotation.TimeZone` is declared `Required.DisallowNull`, which leaves a JSON `null` to the SDK's deserialization, not to the mapper. `null` is tested on the SDK's object, and an absent member through a point read.
- **Helpers.** No new fake. In `ExecutionContextTests`, `RouteUnitOfExecution` now calls the new `RouteKey` (a key of any type and its ancestors, all active), `RouteZones` routes a chain with a zone per place, and `Snapshot` builds a base snapshot for a context built by hand.
- The Hosting tests went from 348 to 403, all passing: 32 new cases in `ExecutionContextTests`, 9 in `SnapshotTests` and 14 in `TypedSnapshotTests`. A rebuild of Hosting and its tests gives no warning in their files.

### Deviations

- No member, signature or rule differs from the spec.
- The spec left open what the sections above decide, with their reasons: the cached chain, the entry of the key's own type, the blank zone in the walk, where the mapper's rule is described, and the place of the two sections in `hosting.md`.
- Beyond the spec: in `hosting.md` the link in the introduction, the sample and the bullet that the status does not use the zone; in the tests the status and `ToString()` with a zone, the public `init` accessor, and that `GetEffectiveTimeZone` makes no request.
