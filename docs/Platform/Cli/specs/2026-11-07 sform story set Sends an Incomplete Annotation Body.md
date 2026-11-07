# sform story set Sends an Incomplete Annotation Body

Found by the plan review of the playground for Storyteller ([424-205](https://linear.app/424fun/issue/424-205/create-a-playground-for-storyteller), PR #168), whose sample data uses `sform story import` because of it.

## Problem

`sform story set <key>` cannot write an annotation of any type. It fails in two layers.

**1. The client refuses to serialize the body.** `ToSdkAnnotation` never sets `AnnotationKey`, which the SDK's model marks `Required.Always`, so the generated client throws while it writes the JSON:

```text
Newtonsoft.Json.JsonSerializationException: Cannot write a null value for property 'AnnotationKey'. Property requires a value.
```

`StorytellerSetCommand` catches only `ApiException`, so the command ends in the top-level catch of `Program`, as a crash.

**2. The server would refuse the body next.** With `AnnotationKey` set, a body still has only the members of the base model. `SetAnnotation` reads the body into the runtime type of its `AnnotationType`, whose type-specific members are `required`, so every type but `rst` and `sbt` answers `400`: `Invalid annotation object in body: … missing required properties … SubjectKey`.

No test found this. `StorytellerSetCommandTests` mocks `IAnnotationsApiClient`, so nothing serializes the body.

## Current State

- **`src/Platform/Cli/src/Model/AnnotationExtensions.cs`:** `ToSdkAnnotation` picks the SDK's type by `AnnotationType` and copies the base members, without `AnnotationKey`.
- **`src/Platform/Cli/src/Commands/Storyteller/StorytellerSetCommand.cs`:** builds a base `Annotation` from the key, or reads a file with `--import` into the runtime type of the key (so a `Unit` of a file holds its `UnitType` and `UnitDefinition`), adds `-c` and `-l`, and calls `SetAnnotationAsync`.
- **The API (`AnnotationsHttp.SetAnnotation`):** reads the body with System.Text.Json into `AnnotationTypes.GetRuntimeType(type)`. A `required` member that is missing is a `400`. The key-derived members of the body are not used after that: `AnnotationFactory.Normalize` takes everything the key decides from the key. `UnitType` and `UnitDefinition` are the only type-specific members a write stores.
- **`AnnotationFactory` (`Backend.Core`)** holds `DefaultUnitType` (`event`) and `DefaultUnitDefinition` (`unknown`), which an automatic ancestor and an import without those members get. `sform` does not reference `Backend.Core`.
- **`AnnotationKeyExtensions` (`Abstractions.Annotations`)**, which `sform` references, derives every ancestor key and name from a key.
- **What a write means.** `POST` and `PUT` replace the editable content of an existing key (`UpdateCoreAsync`): what the body does not hold is cleared. An import does the same.

## Proposed Changes

### 1. The body

`ToSdkAnnotation` parses `AnnotationKey` and fills the SDK's model of the key's type:

| Type | Members added |
| --- | --- |
| All | `AnnotationKey` |
| `unt` | `ResponsibilityKey`, `ResponsibilityName`, `UnitType`, `UnitDefinition` |
| `usg` | `SubjectKey`, `SubjectName`, `ResponsibilityKey`, `ResponsibilityName` |
| `cnt` | `SubjectKey`, `SubjectName` |
| `exe` | `SubjectKey`, `SubjectName`, `ResponsibilityKey`, `ResponsibilityName`, `ContextKey`, `ContextName`, `UsageKey` |
| `uxe` | The members of `exe`, `UnitKey`, `UnitName`, `ExecutionKey` |

- The names and keys come from `AnnotationKeyExtensions`, so they always agree with the key, as `AnnotationFactory.Normalize` makes them on the server.
- The relation arrays (`UnitNames`, `ContextNames`, `ResponsibilityNames`) stay out: the server owns them, and ignores them in a write.
- The type is taken from the key. The fallback to a base `Annotation` goes: it cannot happen, and a base model would only be refused by the API.

### 2. A unit's own members

`UnitType` and `UnitDefinition` come from the `Unit` of a `--import` file. A plain `sform story set unt.r.u`, or a file without them, sends the defaults that a create and an import use.

The defaults move to `Abstractions.Annotations`, as public constants of `Unit`, and `AnnotationFactory`'s constants refer to them. So `sform` and the server cannot drift apart, without a reference from `sform` to `Backend.Core`.

### 3. The meaning of a write stays

`sform story set` keeps the meaning of the API's write: the body replaces the editable content of an existing key. Without `--import`, that body holds only `-c` and `-l`, so on an existing key the title, the description, the other values and labels, the validity window and the disabled flag are cleared, and a unit gets the default `UnitType` and `UnitDefinition`.

Until now no write reached the server, so nobody has met this. Reading the key first and merging is a change of the command's meaning, not of its body, and stays out of this fix. `annotation-lifecycle.md` states the behaviour, and the review raises it with the owner.

### 4. Tests

`StorytellerSetBodyTests`, in `src/Platform/Cli/test/Commands/`, runs `StorytellerSetCommand` with the real `AnnotationsApiClient` over `ScriptedHttpHandler`, so the body is serialized as in production:

- for each of the seven types, exit code `0`, and the body reads into the runtime type of its key with the options of the API (`AnnotationsHttp.AnnotationBodyOptions`): the check that answers `400` on the server;
- the key-derived members of each type;
- a unit with defaults, and a unit whose `UnitType` and `UnitDefinition` come from a `--import` file;
- the title, labels and values of a file, `-c` and `-l` in the body.

### 5. Documentation

The row of `sform story set` in `docs/Platform/Storyteller/annotation-lifecycle.md` states what the body holds and what a write to an existing key clears.

### 6. Verification

- The new tests fail before the fix with the error above.
- `dotnet test --project src/Platform/Cli/test`, and the `Backend.Core` tests for the moved constants.
- The bodies that `sform` sends, for each type, through the real `AnnotationsHttp.SetAnnotation` of the API's test project, once, outside the committed tests.
- The built `sform` against a throwaway fake of the API. The local platform is not started: it holds the developer's data.

### Not in scope

- Merging into an existing annotation (section 3).
- The playground's data: `sform story import` also brings the configurations, so it stays the way to load them.
