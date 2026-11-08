# Phase 1 of Hosting: who am I for any annotation, and a catalog and informer for any descendants

Linear: [424-214](https://linear.app/424fun/issue/424-214/hosting-who-am-i-for-any-annotation-and-a-catalog-and-informer-for-any).

## Overview

Executes phase 1, typed snapshots and who am I (sections 1 and 2), of [Hosting: who am I for any annotation, and a catalog and informer for any descendants](../2026-11-08%20Hosting%20-%20who%20am%20I%20for%20any%20annotation,%20and%20a%20catalog%20and%20informer%20for%20any%20descendants.md). Every annotation type now has a sealed snapshot with its names from the key and its relation names from the body, who am I hands them out for the key and its ancestors, and `Find<T>()` gives them typed. No public signature changed.

## What Was Done

### 1. Typed snapshots of every annotation type

- `57606b5b` adds `StorytellerResponsibility` (`UnitNames`), `StorytellerUnit` (`ResponsibilityName`, `UnitName`, `UnitType`, `UnitDefinition`), `StorytellerSubject` (`ContextNames`, `ResponsibilityNames`), `StorytellerUsage` (`SubjectName`, `ResponsibilityName`, `ContextNames`), `StorytellerContext` (`SubjectName`, `ContextName`, `ResponsibilityNames`) and `StorytellerUnitOfExecution` (`SubjectName`, `ResponsibilityName`, `ContextName`, `UnitName`) in `src/Libraries/Platform/Storyteller/Hosting/src/`, as the spec's table has them.
- Each follows `StorytellerExecution`: a public parameterless constructor, `required init` names, relation lists copied by `CopyNames` (`null` gives an empty list), an internal `[SetsRequiredMembers]` constructor from a base snapshot, and a `WithDeletedAt` override that keeps the subtype, the names and the lists.
- `Reading/AnnotationMapper.Map` switches on the key's type for all seven types. The names come from the key; the relation lists and the unit's strings come from the body only when it is the SDK's type of the key, so a body without the discriminator, with another one, or with one in another case gives empty lists and `null` strings.
- `Reading/SnapshotTypes` maps each `AnnotationType` to its snapshot type and back, and `Require<T>()` throws `ArgumentException` for `StorytellerAnnotation`, which names no type. The catalog of phase 2 uses it too.

### 2. Who am I

- `StorytellerExecutionContext` keeps its members and their types. Its comments say that a read puts the typed snapshots there.
- `Find<TAnnotation>()` returns the annotation of the snapshot type, the key's own or an ancestor, by `as`: `null` when it was not read or is a test double's base snapshot. `Find<StorytellerAnnotation>()` throws `ArgumentException`.
- `ExecutionContextService` did not change: a background `404` calls the virtual `WithDeletedAt`, which keeps the subtype.

### Tests

- `test/Snapshots/TypedSnapshotTests.cs` (new): each type's members from a body that contradicts the key's names, the discriminator fallbacks (none, another type, `responsibility` in lower case, an unknown one), bodies without relations, `SnapshotTypes` both ways, deleted copies of every type, copies and `null` initializers of what an application builds, and the public constructors and setters by reflection.
- `test/Executions/ExecutionContextTests.cs`: an `rst` key is a `StorytellerResponsibility` with its `UnitNames`; a `uxe` key's ancestors are typed with their relations; a background `404` keeps the responsibility's type and `UnitNames`; a test double's base usage is returned by `Usage` and not by `Find<StorytellerUsage>()`.
- The Hosting tests went from 218 to 272, all passing.

### Deviations

None from the spec.
