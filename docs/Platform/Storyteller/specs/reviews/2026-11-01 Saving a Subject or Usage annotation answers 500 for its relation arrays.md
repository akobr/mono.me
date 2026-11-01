# Saving a Subject or Usage annotation answers 500 for its relation arrays

Linear: [424-129](https://linear.app/424fun/issue/424-129/saving-a-subjectusage-annotation-fails-with-500-ireadonlyset).

## Overview

This review covers all of [Saving a Subject or Usage annotation answers 500 for its relation arrays](../2026-11-01%20Saving%20a%20Subject%20or%20Usage%20annotation%20answers%20500%20for%20its%20relation%20arrays.md), delivered in one branch.

- The relation arrays of the shared models (`ContextNames`, `ResponsibilityNames`, `UnitNames`) can be read with System.Text.Json now. Before, a `SetAnnotation` body of a Responsibility, Subject, Usage, Context or Execution that carried one of them answered `500`, even with `[]`. Every save from the Admin UI and every bulk action of the UI on those types failed that way.
- The Functions body read answers `400` for a member type the serializer cannot read, instead of a `500` with the whole exception. Such a failure is also logged as an error.
- The OpenAPI document and the SDKs do not change.

| Commit | Content |
|---|---|
| `11e3a545` | The spec |
| `f697d704` | Section 1: `RelationNamesJsonConverter` on the six properties, and the converter tests in Backend.Core |
| `3c7d11ca` | Section 2: `DeserializeBody` and the logging in the Functions handler, and the handler tests |
| `35950c61` | Sections 4 and 6: the lifecycle doc and the comment of the Admin UI |
| `8d2c05cc` | Doc fixes from the review of the branch |
| (this commit) | This review |

## What Was Done

### 1. The converter of the relation arrays

`Abstractions.Annotations/src/Model/RelationNamesJsonConverter.cs`, `internal sealed class RelationNamesJsonConverter : JsonConverter<IReadOnlySet<string>>`, follows the spec:

- `HandleNull` is on. `null` reads as an empty set.
- An array of strings reads into a `HashSet<string>` with `StringComparer.Ordinal`.
- Any other token, or an element that is not a string, throws `JsonException` without a message. The serializer then writes its own message with the path, for example `The JSON value could not be converted to System.Collections.Generic.IReadOnlySet`1[System.String]. Path: $.ContextNames | LineNumber: 0 | BytePositionInLine: 75.` That is the same form as the serializer's other type errors, CLR type name included, and the same choice as `MachineScopeJsonConverter`.
- `Write` writes the names as an array, and a `null` set as `null`.

The attribute `[JsonConverter(typeof(RelationNamesJsonConverter))]` is on `Subject.ContextNames`, `Subject.ResponsibilityNames`, `Usage.ContextNames`, `Responsibility.UnitNames`, `Context.ResponsibilityNames` and `Execution.UnitNames`. The model files have no `using Newtonsoft.Json;`, so the attribute is System.Text.Json's without qualification. The types, the initializers and the interfaces did not change.

### 2. The Functions body read

`Api.Functions/src/V1/AnnotationsHttp.cs`:

- `internal static object? DeserializeBody(JsonElement element, Type runtimeType)` reads with `AnnotationBodyOptions` and turns a `NotSupportedException` into `JsonException("A member of the annotation has a type the API cannot read.", exception)`. It sits after the public functions and before the private helpers (StyleCop SA1202).
- `SetAnnotation` and `DeserializeAnnotationElement` (the bulk elements) call it. Their existing `catch (JsonException)` blocks now answer that case too:
  - `SetAnnotation` answers `400 "Invalid annotation object in body: A member of the annotation has a type the API cannot read."`.
  - The bulk create answers the same `400` after the access check, as before.
- Both catch blocks call the private `LogUnreadableMember(JsonException)`, the last member of the file. It logs at `Error` only when the inner exception is a `NotSupportedException`, and it logs that inner exception. A body that is just invalid is not logged.
- The order of the checks in `SetAnnotation` did not change.

### 3. Api.Web

No code change, as planned. Its second deserialization of the runtime type picks up the attribute. The route still fails before that (see "Left for later").

### 4. Admin UI

The comment of `toAnnotationBody` (`ui.admin/src/app/forms/annotation.schema.ts:116-120`) names `SubjectKey` and `ResponsibilityName` as required members. It also says that the relation arrays come along from `original` and that the server ignores their values. Prettier accepts the file. Nothing else in the UI changed.

### 5. Tests

- **Backend.Core.** `RelationNamesJsonTests` has 109 cases, over the six properties:
  - an array is read;
  - duplicates collapse, and the case is kept;
  - `null` gives an empty set;
  - `[]` and an absent member give an empty set;
  - the member after the array is still read;
  - a member name in another case uses the converter;
  - nine wrong values (`"prod"`, `5`, `true`, `{}`, `[1]`, `[null]`, `[{}]`, `[["prod"]]`, `["prod",false]`) give `JsonException` with `Path` `$.{Member}`;
  - the names are written as an array, with escaped strings, and survive a round trip;
  - one name or none is written as the serializer writes a list;
  - a `null` set is written as `null`, or left out with `WhenWritingNull`.

  Two cases are not in the spec: the member in another case, and a guard. The guard, `EverySetOfTheModels_HasTheConverter`, finds every `IReadOnlySet<string>` property of the models by reflection, checks that the list is the six, and checks that each has the converter. The tests go through the models, because the converter is internal.
- **Api.Functions.** 18 new cases in `AnnotationsHttpTests`. `RecordingAnnotations` got `LastSet`, and `Http` takes an optional logger.
  - `Set_AnAnnotationAsItWasRead_IsStoredWithItsType` covers the five types by `POST` and `PUT`. The body is an annotation as a read returns it: relation arrays, `Labels`, `Values`, both stamps, serialized with the API's options. Each case answers `200`, and the service receives the concrete type with the names.
  - A `null` relation array is stored as an empty set.
  - `"prod"`, `{}`, `[1]` and `[null]` answer `400` with `Path: $.ContextNames`. Nothing is called, and nothing is logged.
  - A Usage body without `SubjectKey` but with `ContextNames` answers the `400` that names `SubjectKey`.
  - A bulk create of the five types as they were read creates them all.
  - `DeserializeBody` with a stand-in record (an `IReadOnlySet<string>` without the converter) gives the fixed message with the `NotSupportedException` inside.
- **Checks that the tests fail without the fix.**
  - Without the attribute on `Usage.ContextNames`, 14 Backend.Core cases fail.
  - With the old `Subject.cs`, 5 Functions cases fail.
- **OpenAPI.** `OpenApiExportTests` rendered the document into a temporary file (`STORYTELLER_OPENAPI_EXPORT`). Next to `open.api.v0.10.15.json`, two things differ: `info.version`, and the 404 description of the configuration patch. That description comes from `47d09516`, which is already on `main` and not exported yet. The six relation arrays are still required arrays of strings. The SDKs were not regenerated.

### 6. Documentation

`docs/Platform/Storyteller/annotation-lifecycle.md`:

- **`:68`.** A client can send an annotation back as it read it. `PUT`/`POST …/{key}` checks the type of a member it sends: a relation array is an array of strings or `null` (as empty), and anything else answers `400`. The bulk create skips the relation arrays.
- **`:158`.** The route row lists `400 "Invalid annotation object in body: …"` in two cases:
  - a missing type-specific member, where the message names the missing members;
  - a member of the wrong type, where the message names its JSON path.

Both sentences differ from spec section 6, after the review. The spec stated the type check for every write, but the bulk create never reads the relation arrays. The spec also gave the path for the missing-member case, but the serializer's message for missing required members has no path.

## The review of the branch

Four reviewers looked at the branch: the converter against the serializer's semantics, the handler, the tests, and the contract with the docs. A skeptic per finding then tried to refute it. Seven findings came back.

**Confirmed and fixed (both in the docs, `8d2c05cc`):**

- The lifecycle page stated the `400` for a malformed relation array for every write. The bulk create skips the arrays and answers `200`.
- The route row said that the `400` for a missing type-specific member names a JSON path. It names the members.

**Refuted:**

- **The converter is internal.** A source-generated `JsonSerializerContext` in another assembly drops it and warns `SYSLIB1220`. There is no regression: source-generated contexts could not read the arrays before either, and no such context covers the models in the repo (see "Left for later").
- **Two `500`s for an unpaired UTF-16 escape (`"\uD800"`).** One is in the `AnnotationType` of `SetAnnotation`, the other in a member name, the `AnnotationType` or the `AnnotationKey` of a bulk element. Both are real, and both predate this change. The raw `JsonElement` reads before `DeserializeBody` throw `InvalidOperationException`. They are recorded under "Left for later".
- **The `Error` log of `LogUnreadableMember` is not tested.** No model has an unreadable member any more, so the branch cannot be reached through the handler. The wrapping and the no-log case are tested.
- **The review was missing.** It is this document.

## Compared with the CodeRabbit coding plan

The issue carried a CodeRabbit coding plan, and it was used as a second opinion. The core is the same:

- one converter, applied with `[JsonConverter]` on the six properties;
- `null` as an empty set, an ordinal set, and `JsonException` for other tokens and elements;
- `400` instead of `500` for `NotSupportedException` in both Functions paths;
- no change to Api.Web.

Where this change differs:

- **The messages.** The plan said that the serializer adds the JSON path to the converter's `JsonException`. It does that only when the exception has no message, so the converter throws one without a message. With a message of its own, the client would not learn the member.
- **One boundary instead of two catches.** The plan added a `NotSupportedException` catch next to each `JsonException` catch. `DeserializeBody` wraps it once, so both paths keep their existing catch. It also logs the defect, which the plan did not, because no body can pass such a model.
- **The place of the converter.** The plan used a new `Json/` folder. The converter is in `Model/`, in the root namespace like every file of the project.
- **The converter tests.** The plan put them in `Api.Functions/test` when Abstractions.Annotations has no test project. They are in `Backend.Core/test`, which references the models, and they go through the models because the converter is internal. The guard over all set properties is new.
- **The import path and the follow-ups.** The plan asked to run `ViewAnnotationDocumentTests` and to record the Api.Web problems as follow-ups. The tests pass (they are part of the 1221 below). The problems are under "Left for later"; no issues were opened.

## Validation

- **`dotnet build 42.mono.slnx`: 0 errors.** The first run failed in `Crumble/Playground/Examples` with a missing source-generated type (`IChainedCrumbsExecutor`), as in earlier branches. That project built alone, and the second full build passed.
- **`dotnet test`:**

  | Project | Passed |
  |---|---|
  | Backend.Core | 1221 |
  | Api.Functions | 2660 |
  | Mcp | 728 |
  | Hosting | 218 |

- **Backend.CosmosDb was not run.** The change touches no entity and no Cosmos serializer: the entities have their own types, and `AnnotationItemSerializer` reads entities.
- **No end-to-end save.** The handler tests send the body the UI sends to the real handler, but no Functions host, emulator or Admin UI was started to repeat the save of the issue.

## Left for later

- **Api.Web's `SetAnnotation`** (`Api.Web/src/Controllers/AnnotationsController.cs:410-451`):
  - it reads `Request.Body` a second time after `[FromBody]` consumed it;
  - it has no `JsonStringEnumConverter`, so a string `AnnotationType` answers `400`;
  - it deserializes outside any `try`.
- **Unpaired surrogate escapes answer `500`.**
  - In `SetAnnotation`, `typeElement.ToString()` throws `InvalidOperationException` for `"AnnotationType":"\uD800"`.
  - In the bulk create, `TryGetProperty`, `IsUnitElement` and `AnnotationKeyOf` throw for such an escape in a member name, the `AnnotationType` or the `AnnotationKey`. This happens before the access check, so a caller without access gets `500` instead of `403`.
  - A small `TryGetString` helper and tests would close both.
- **Exception details leave the API.** `SetAnnotation`'s second catch (`exception.ToErrorResponse()`) answers `400` with the stack trace, and the middleware's `500` carries the exception.
- **`RelationNamesJsonConverter` is internal.** A source-generated context over the models in another assembly cannot use it. Making it `public` would allow that, if a consumer of the package needs it.
- **The Admin UI mock keeps the relation arrays of a save body** (`app/mock/annotation-handlers.ts`, `storeAnnotation`). The server keeps the stored ones.
- **`AllowTrailingCommas` has no effect on `SetAnnotation`.** `JsonDocument.Parse` there uses default options.
