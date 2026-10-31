# Saving a Subject or Usage annotation answers 500 for its relation arrays

Linear: [424-129](https://linear.app/424fun/issue/424-129/saving-a-subjectusage-annotation-fails-with-500-ireadonlyset). Related: [424-128](https://linear.app/424fun/issue/424-128/bulk-create-annotations-in-admin-ui-fails-with-400-no-annotation).

## Problem

Saving an annotation from the Admin UI fails against the Functions API, for example a new value on the Subject `sbt.wing`. `POST v1/{organization}/{project}/{view}/annotations/{key}` answers `500` with a 4.78 kB body:

> The collection type 'System.Collections.Generic.IReadOnlySet\`1[System.String]' is abstract, an interface, or is read only, and could not be instantiated and populated. Path: $.ContextNames | LineNumber: 0 | BytePositionInLine: 17.

1. System.Text.Json cannot create an `IReadOnlySet<string>`. Every body that has one of the relation arrays fails, even with `[]`: `ContextNames`, `ResponsibilityNames` or `UnitNames` of a Responsibility, Subject, Usage, Context or Execution.
2. The serializer throws `NotSupportedException`, and `SetAnnotation` catches only `JsonException`. The exception reaches the middleware, which answers `500` with the whole exception, stack trace included.
3. The exception comes before the check of the required members. So a Usage body without `SubjectKey` but with `ContextNames` also answers `500`, where it should answer the `400` that names `SubjectKey`.

The server ignores the values of these arrays anyway: it owns the relations.

## Current State

- **The models.** `Abstractions.Annotations/src/Model/` has six init-only `IReadOnlySet<string>` properties, each with an `= new HashSet<string>(0)` initializer:
  - `Subject.ContextNames` and `Subject.ResponsibilityNames` (`Subject.cs:8,10`)
  - `Usage.ContextNames` (`Usage.cs:15`)
  - `Responsibility.UnitNames` (`Responsibility.cs:7`)
  - `Context.ResponsibilityNames` (`Context.cs:11`)
  - `Execution.UnitNames` (`Execution.cs:21`)

  The interfaces (`ISubject`, `IUsage`, `IResponsibility`, `IContext`, `IExecution`) declare them too. None has a serialization attribute. `Unit` and `UnitOfExecution` have no sets.
- **The serializer.** .NET 10 (runtime 10.0.8, System.Text.Json 10.0.12) still has no built-in support for `IReadOnlySet<T>`. It uses its generic `IEnumerable<T>` converter, which can create only types that a `List<T>` can be assigned to. For anything else it throws the `NotSupportedException` above. A scratch project confirmed this, with options equal to the API's:
  - `[]`, `["a"]`, and any letter case of the member name → `NotSupportedException`;
  - an absent member → the initializer's empty set;
  - `null` → the non-nullable property becomes `null`;
  - `"x"`, `{}` or `5` → a `JsonException` (already `400`).
- **The Functions handler.** `Api.Functions/src/V1/AnnotationsHttp.cs`:
  - `SetAnnotation` (`:309-389`) copies the body (`:325`) and parses it with `JsonDocument.Parse` (`:332`). It checks for an object root and a known `AnnotationType`, then deserializes the root into `AnnotationTypes.GetRuntimeType(type)` with the static `AnnotationBodyOptions` (`:24-30`, `:346`). Only `JsonException` is caught (`:355-358`), as `400 "Invalid annotation object in body: {message}"`.
  - `ExceptionHandlingMiddleware` (`Api.Functions/src/ErrorHandling/ExceptionHandlingMiddleware.cs:39-60`) has no mapping for `NotSupportedException`. It answers `500` with `ErrorResponse.Error` set to the exception (an `ExceptionWrapper` with the stack trace). That is the 4.78 kB response.
  - The bulk `SetAnnotations` (`:403-457`) reads each element through `DeserializeAnnotationElement` (`:993-1004`) into the base `Annotation`, or into `Unit` when it carries a trigger. Neither has a set, so the relation arrays of a bulk body are skipped as unknown members, and the bulk route is not affected. It also catches only `JsonException` (`:427`).
- **Who sends the arrays.**
  - The OpenAPI document marks every type-specific member as required, the relation arrays included (`Api.Functions/src/OpenApi/Filters/AnnotationDocumentFilter.cs:66`). So the generated SDKs type them as required (`sdk.typescript/src/generated/model/subject.ts`; `Required.DisallowNull` in `Sdk.NSwag`).
  - The Admin UI builds the save body by spreading the loaded annotation (`ui.admin/src/app/forms/annotation.schema.ts:121-147`), so the arrays go back as they came, always arrays.
  - The bulk actions of the UI (disable, add and remove labels: `useAnnotationActions.ts:307-323`) spread catalog entries into `saveMany` (`:110-140`), which calls `SetAnnotation` per key. They fail the same way for those five types.
- **The server ignores the values.**
  - `AnnotationFactory.Normalize` (`Backend.Core/src/Annotating/AnnotationFactory.cs:28-48`) rebuilds the annotation from its key and copies only the editable members. Create (`CosmosAnnotationService.cs:351`), update (`:460`) and import (`CosmosAnnotationService.Import.cs:147`) go through it.
  - On update, `AnnotationRelations.CopyFrom` keeps the stored arrays (`Backend.CosmosDb/src/Annotating/AnnotationRelations.cs:49-61`).
  - The relations are compared ordinally (`AnnotationRelations.cs:36,44-45`).
- **Other readers of the models.**
  - Api.Web's `SetAnnotation` (`Api.Web/src/Controllers/AnnotationsController.cs:410-451`) deserializes the runtime type a second time with inline options (`:422-429`). It would hit the same exception, but it fails earlier: `[FromBody]` has already read `Request.Body`, and without a `JsonStringEnumConverter` a string `AnnotationType` is a `400`. Aspire, the UI and the SDKs use the Functions API only.
  - `sform story set --import` reads with Newtonsoft (`Cli/src/Commands/Storyteller/StorytellerSetCommand.cs:109`). Newtonsoft 13.0.4 reads `IReadOnlySet<string>` and ignores System.Text.Json attributes.
  - The view import (`Backend.Core/src/Transferring/ViewAnnotationDocument.cs:360-374`) ignores the relation members by reflection, and reads the editable ones into its own DTO.
  - Sdk.NSwag, Mcp and Hosting use the SDK's DTOs (`ICollection<string>`). Backend.CosmosDb stores entities (`IReadOnlyCollection<string>`). No source-generated System.Text.Json context covers the models.
- **OpenAPI.** The Functions OpenAPI extension picks the schema by the property type (`IEnumerable` → array of the element type). It reads no System.Text.Json attribute. NJsonSchema 11.5.2 (NSwag in Api.Web) and `JsonSchemaExporter` also emit the same array schema with or without a converter on the property (checked in a scratch project). Committed document: `sdk.typescript/open.api.v0.10.15.json` and its copy in `Sdk.NSwag/`.
- **Tests.**
  - The Functions fixtures `UsageBody` and `SubjectJson` (`Api.Functions/test/AnnotationsHttpTests.cs:25-32`) carry no relation arrays. That is why no test caught this.
  - `RecordingAnnotations.SetAnnotationAsync` (`:1090`) does not keep the annotation it was given.
  - There is no test project for Abstractions.Annotations. `Backend.Core/test` (xUnit, Shouldly) holds `AnnotationModelTests`.
- **Docs.**
  - `docs/Platform/Storyteller/annotation-lifecycle.md:68` says that a write ignores the relation arrays of its body. Today that is false for five types.
  - `:158` lists the `400`s of `PUT`/`POST …/{key}`.

## Proposed Changes

### 1. A converter for the relation arrays on the shared models

New `Abstractions.Annotations/src/Model/RelationNamesJsonConverter.cs`, namespace `_42.Platform.Storyteller`:

```csharp
// The relation arrays of the models (ContextNames, ResponsibilityNames, UnitNames) as JSON arrays of strings.
internal sealed class RelationNamesJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override bool HandleNull => true;

    public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options);
}
```

- **Read.**
  - `null` gives an empty set, so the non-nullable property is never `null`. `HandleNull` makes the serializer call the converter for a `null` token.
  - An array of strings gives a `HashSet<string>` with `StringComparer.Ordinal`: a duplicate collapses, and the case is kept (`"a"` and `"A"` are two names), as in `AnnotationRelations`.
  - Any other token throws `JsonException` without a message, so the serializer writes its own message with the path (`$.ContextNames`), as `MachineScopeJsonConverter` does. That covers a string, a number, `true` or an object in place of the array, and an element that is not a string: `null`, a number, an object or a nested array.
- **Write.** A JSON array of the strings, the same bytes as today (checked for 0, 1 and 3 names, escaped strings, default and Web options). A `null` set is written as `null`. The serializer passes `null` to `Write` because `HandleNull` is on, unless the options ignore nulls.
- **Where.** `[JsonConverter(typeof(RelationNamesJsonConverter))]` (System.Text.Json's attribute) on the six properties of section "Current State". The declared types, the initializers and the interfaces stay. The converter is `internal`: the serializer needs only its public parameterless constructor, and it is no API of the package.

The attribute sits on the shared model, so it covers every set of options that reads one: `AnnotationBodyOptions`, the MVC options of the Functions host, the inline options of Api.Web, and tests. Newtonsoft ignores it.

**Alternatives rejected:**

- A concrete `HashSet<string>` property type: it changes the public contract of the models and their interfaces.
- `[JsonObjectCreationHandling(Populate)]`: the serializer then refuses every body of the type with `InvalidOperationException`, even without the member.
- A type-info modifier that sets `CreateObject` for `IReadOnlySet<string>`: the serializer refuses it, because its converter does not support `CreateObject`.
- A converter in `AnnotationBodyOptions` only: every other set of options would need it too.
- Skipping the members in the Functions body read: it fixes one path and leaves the models unreadable for System.Text.Json.

**The write body after the change:**

| Relation array in the body | Before | After |
|---|---|---|
| absent | the empty initializer | unchanged |
| `[]`, `["a"]`, `["a","a"]` | `500` | accepted; the server ignores the values |
| `null` | accepted, the property is `null` | accepted, an empty set |
| `"x"`, `5`, `true`, `{}` | `400` | `400`, the message names the path |
| `[1]`, `[null]`, `[["a"]]` | `500` | `400`, the message names the path |

### 2. The Functions body read refuses an unreadable member with 400

In `Api.Functions/src/V1/AnnotationsHttp.cs`:

```csharp
// The annotation of a body, read into the given runtime type.
internal static object? DeserializeBody(JsonElement element, Type runtimeType)
```

- It calls `element.Deserialize(runtimeType, AnnotationBodyOptions)`.
- It turns a `NotSupportedException` into `JsonException("A member of the annotation has a type the API cannot read.", exception)`.
- `SetAnnotation` (`:346`) and `DeserializeAnnotationElement` (`:996`) call it.

So no body answers `500` for a member type the serializer cannot read:

- `SetAnnotation`'s existing catch answers `400 "Invalid annotation object in body: A member of the annotation has a type the API cannot read."`.
- The bulk route keeps the exception as its invalid element. It answers the same `400` after the access check, so the order of `403` before `400` stays.

The fixed message keeps the serializer's CLR type names and the stack trace out of the response.

Such an exception is a defect of a model, not of the body: no body could pass. So both handlers log the inner `NotSupportedException` at `Error`, and only in that case, before they answer. The order of the checks in `SetAnnotation` stays:

1. the size limit;
2. the object root;
3. the `AnnotationType`;
4. the deserialization;
5. the route key;
6. the service.

### 3. Api.Web

No code change. Its deserialization of the runtime type picks up the attribute. The route fails before it for other reasons (Current State), and those are out of scope.

### 4. Admin UI

The comment of `toAnnotationBody` (`ui.admin/src/app/forms/annotation.schema.ts:116-120`) says SetAnnotation requires `ContextNames`. It doesn't. The comment will name the members it does require, and say that the relation arrays come along from `original` and that the server ignores them. Nothing else in the UI or its mock changes.

### 5. Tests

**`Backend.Core/test/RelationNamesJsonTests.cs`** (Shouldly). A theory over the six properties, each with its type and member:

- an array is read into the set;
- duplicates collapse, and the case is kept;
- `null` gives an empty set, not `null`;
- `[]` and an absent member give an empty set;
- the member after the array is still read;
- every wrong token and every wrong element throws `JsonException` whose `Path` is `$.{Member}`;
- serialization writes the arrays as before, and a round trip keeps the names;
- a `null` set is written as `null`.

**`Api.Functions/test/AnnotationsHttpTests.cs`:**

- `RecordingAnnotations` gets `LastSet`, the annotation `SetAnnotationAsync` was given.
- A theory over the five types and `POST`/`PUT`. The body is an annotation as the API reads it, serialized with the API's JSON options: filled relation arrays, `Values`, `Labels` and both stamps, as the Admin UI sends it back. Each case answers `200`, and the service receives the concrete type with the names.
- `"ContextNames": null` answers `200`, and the service receives an empty set.
- Malformed arrays (`"prod"`, `{}`, `[1]`, `[null]`) answer `400` with `$.ContextNames` in the message, and the service is not called.
- A Usage body without `SubjectKey` but with `ContextNames` answers the `400` that names `SubjectKey`.
- A bulk body whose elements carry relation arrays is created. This is a regression guard.
- `DeserializeBody` with a test record that has an `IReadOnlySet<string>` without the converter throws `JsonException` with the fixed message and the `NotSupportedException` inside.

**OpenAPI.** The document is rendered with `OpenApiExportTests` into a temporary file and compared with `open.api.v0.10.15.json`; everything but `info.version` is expected to be equal. The SDKs are not regenerated.

### 6. Documentation

`docs/Platform/Storyteller/annotation-lifecycle.md`:

- `:68`: a write ignores the values of the relation arrays. When they are present they must be arrays of strings, and `null` counts as empty; otherwise the write answers `400`.
- `:158`: the `400` for a member of the wrong type or a missing type-specific member (`Invalid annotation object in body: …`, with the JSON path).

Historical specs and reviews under `docs/**/specs/` stay as they are.

### Out of scope

- Api.Web's `SetAnnotation`: it reads the consumed body a second time, has no enum converter for names, and has no exception mapping.
- The exception details in `SetAnnotation`'s second catch (`exception.ToErrorResponse()`, a `400` with the stack trace) and in the middleware's `500`. Both are wider than this route.
- `JsonDocument.Parse` in `SetAnnotation` uses default options, so `AllowTrailingCommas` of `AnnotationBodyOptions` has no effect there.
