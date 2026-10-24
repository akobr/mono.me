# Properties of annotation Unit are not properly propagated

Line numbers refer to the tree this plan was written against (`origin/main` at `7b1975c3`). No application code is changed by this document.

Linear issue [424-90](https://linear.app/424fun/issue/424-90/properties-of-annotation-unit-are-not-properly-propagated). A coding note attached to that issue was used as a second opinion. The decisions below were checked against the code.

## Problem

`Unit` stores `UnitType` and `UnitDefinition`. `IUnit` does not expose them, so code that only has the interface cannot read the trigger of a unit.

Both properties accept any string. The admin UI still cannot edit them. The form, the JSON view, and the YAML view share a draft that omits them, and the form prints the stored values as read-only text under Relations.

Bulk create in the Functions API reads the body as `List<Annotation>`. System.Text.Json then drops both properties. A later normalize step stores the defaults `event` and `unknown` instead of the values the caller sent.

## Current State

### Model

`Unit` (`src/Platform/Storyteller/Abstractions.Annotations/src/Model/Unit.cs`, lines 10–13) declares both properties as `required string` with `get; init;`. The comments name time, message, file, and event, and a definition such as a CRON expression, a message template, or a file pattern.

`IUnit` (`src/Platform/Storyteller/Abstractions.Annotations/src/Model/IUnit.cs`) exposes only `ResponsibilityKey` and `ResponsibilityName`. `Unit` is the only implementation. `IUnitOfExecution` and `UnitOfExecution` are a different type: they point at a unit and do not own its trigger.

`PartitionKeys.GetPartitionKey` (`Backend.CosmosDb/src/PartitionKeys.cs`, line 48) is the only match on `IUnit`. It uses the responsibility, not the trigger.

### Normalization and storage

`AnnotationFactory.Normalize` (`Backend.Core/src/Annotating/AnnotationFactory.cs`, lines 45–47) copies `UnitType` and `UnitDefinition` only when the input is a concrete `Unit`. Any other input, including a base `Annotation` whose `AnnotationType` is `Unit`, keeps the defaults from `Create` (`event` and `unknown`, lines 80–82).

`AnnotationFactory.CreateDefault` does not copy the trigger onto an automatic ancestor. That stays as it is.

Cosmos mapping (`Backend.CosmosDb/src/AnnotationMappings.cs`, lines 308–309 and 332–333) already stores and reads both properties on `Unit`. `AnnotationAuditDiff` already records a `UnitType` value change and a `UnitDefinition` change without the text. View import and export already round-trip both properties for units. Those paths are unchanged.

### Functions API

`AnnotationsHttp.SetAnnotation` (lines 319–353) reads one object and deserializes it with `AnnotationTypes.GetRuntimeType` and `AnnotationBodyOptions` (case-insensitive names, `JsonStringEnumConverter`). A unit body keeps both properties. A body that is not valid JSON for that runtime type is `400` with `Invalid annotation object in body: …`.

`AnnotationsHttp.SetAnnotations` (lines 403–451) does not do that. It calls `ReadJsonBodyAsync<List<Annotation>>`. There is no polymorphic converter on `Annotation`, so each element becomes a base `Annotation` and the unit properties are ignored. The handler then:

1. Reads keys from the parsed list and calls `CheckAccessToAnnotationsAsync` with `MachineScope.AnnotationWrite` and `MachineScope.AnnotationManage` before it returns a body `400`. A body that fails JSON parsing names no key.
2. Rejects a missing body and a null entry with the messages already covered by `AnnotationsHttpTests`.
3. Overwrites `ProjectName` and `ViewName` from the route.
4. Calls `CreateAnnotationsAsync`.

`CreateCoreAsync` normalizes the model. A base `Annotation` therefore lands as a unit with the defaults.

The OpenAPI request body stays `List<Annotation>`. The generated C# and TypeScript SDKs already describe both properties on the `Unit` schema. They are not regenerated.

`Api.Web` bulk create returns a job id and does not store annotations. It is out of scope. The issue names `Api.Functions` and `ui.admin`.

### Admin UI

`toAnnotationDraft` (`src/Platform/ui.admin/src/app/forms/annotation.schema.ts`, lines 78–95) copies the shared fields only. `annotationFormSchema` extends `SetAnnotationBody` and strips unknown keys, so `UnitType` and `UnitDefinition` do not survive validation. `toAnnotationBody` (lines 102–121) spreads the loaded annotation and overwrites the shared fields, which keeps the old trigger even when the user edits JSON or YAML.

`AnnotationForm.vue` (lines 118–132 and 259–280) shows the two values as read-only text under Relations. `DocumentEditor` edits `values` from that same form, so JSON and YAML never contain the trigger.

`withRelations` (`app/utils/annotation-relations.ts`, lines 37–39) adds `UnitType: event` and `UnitDefinition: unknown` when a unit body omits them. The mock catalog and the create dialog depend on that. The legacy catalog units, including `unt.invoicing.pdf`, are created that way.

`AnnotationCreateDialog.vue` and `LabelsInput.vue` already use `UInputMenu` with `create-item` for a suggested value plus a custom string.

The public guide (`docs/42for.net/platform/annotations.md`) lists `time`, `message`, `file`, and `event`. Showcase fixtures use `time`, `message`, `file`, and `event`. The issue asks the UI to suggest Time, Message, and Volume. Those stored values are `time`, `message`, and `volume`. Existing `file` and `event` values stay valid.

## Proposed Changes

### Decisions

1. Store the suggestions as `time`, `message`, and `volume`. The menu shows Time, Message, and Volume. Help text matches the current value without regard to case. Any other string, including `event`, `file`, and `timer`, stays valid and uses generic help.
2. On bulk create, deserialize an element as `Unit` only when its `AnnotationType` is `Unit` and the element contains `UnitType` or `UnitDefinition`. Otherwise keep base `Annotation`, so a caller that omits both properties still gets the defaults and does not get a `400`. The OpenAPI body stays `List<Annotation>`.
3. Do not reject a Time definition that is not a Cron expression. When the selected type is Time and the definition does not have 5 or 6 whitespace-separated fields, show a hint. Save stays enabled.

A consequence of decision 2: an element that opts into unit properties is deserialized as `Unit`, so `ResponsibilityKey` and `ResponsibilityName` are required, as they are on the single-write path. An element that omits both unit properties does not take that path.

### Phase 1: Contract and bulk create

1. Add `string UnitType { get; }` and `string UnitDefinition { get; }` to `IUnit`. Document that any string is stored, that `time`, `message`, and `volume` are the common values, and what a definition usually contains. Do not change `Unit`, `IUnitOfExecution`, or `UnitOfExecution`.
2. In `AnnotationFactory.Normalize`, match the input as `IUnit` instead of `Unit`. The object built from the key stays a `Unit`, and the copy still uses `with`. Defaults for a base `Annotation` and for automatic ancestors stay `event` and `unknown`.
3. In `SetAnnotations`, read `List<JsonElement>` with the same options and the same limit as today. Collect `AnnotationKey` values with a case-insensitive name before returning a body error, then keep the current access check, missing-body check, null-entry check, route overwrite, size cap, and restricted-machine filter.
   - An object whose type is `Unit` and that has `UnitType` or `UnitDefinition` (any case) is deserialized as `Unit` with `AnnotationBodyOptions`.
   - Every other element is deserialized as `Annotation` with those options.
   - `JsonException` becomes `400` with `Invalid annotation object in body: …`, the same text as the single write.
   - The OpenAPI attributes stay `List<Annotation>`. No package reference and no SDK regeneration.
4. Tests:
   - `AnnotationModelTests`: normalization copies a non-default trigger from an `IUnit` that is not a `Unit`.
   - `AnnotationsHttpTests`: a bulk element with custom `UnitType` and `UnitDefinition` is passed to `CreateAnnotationsAsync` as a `Unit` with those values; a unit element without either property is a base `Annotation`; a unit element that has only one of the two properties is `400`.

### Phase 2: Admin UI

1. Add `src/Platform/ui.admin/src/app/utils/unit-types.ts`.
   - Suggestions `time`, `message`, and `volume`, each with a label, a short description, a definition placeholder, and definition help.
   - Time: a Cron expression, for example `*/15 * * * *`. The annotation time zone controls the schedule.
   - Message: a message name rule, a message template, or a source name.
   - Volume: a watched location or a source of entities (files, Azure storage, PostgreSQL, Cosmos DB, or a virtual source), plus an optional path filter.
   - A lookup that ignores case and returns generic help for any other value.
   - `cronFieldHint`: a sentence when a Time definition does not have 5 or 6 fields; no sentence when it does.
   - `isUnitAnnotation`: the key code is `unt`, or `AnnotationType` is `Unit`. That is the same key rule as `withRelations`.
2. In `annotation.schema.ts`:
   - `toAnnotationDraft` includes `UnitType` and `UnitDefinition` only for a unit.
   - The Zod schema accepts both as optional strings and requires both to be non-blank when the annotation is a unit. It does not limit the vocabulary.
   - `toAnnotationBody` writes the trimmed draft values over `original` for a unit and leaves other types as they are.
   - The fields stay off the read-only identity list, so JSON and YAML may change them.
   - `typed-schema.ts`, `json-yaml.ts`, and `DocumentEditor.vue` stay as they are.
3. In `AnnotationForm.vue`, show a Unit section only for a unit.
   - `UnitType` is a `UInputMenu` with the suggestions, `create-item`, and the current value even when it is not a suggestion.
   - `UnitDefinition` is a `UTextarea`. Placeholder and help follow the selected type. The Cron hint sits under the field for Time and does not block save.
   - Remove the read-only trigger lines from Relations. Keep the key links and the name chips.
4. Tests:
   - `annotation-form.test.ts`: a unit draft has both fields; a non-unit draft does not; custom values pass and blank unit values fail; `toAnnotationBody` writes the edited values; a JSON document and a YAML document that change the values produce a body with the new values.
   - `test/unit/utils/unit-types.test.ts`: lookup, generic help, and the Cron hint.
   - `annotations.spec.ts`: on `unt.invoicing.pdf`, set a custom type and definition, save, reopen, and see the values in JSON and YAML. A subject does not show the unit fields.
5. User docs:
   - `docs/42for.net/platform/annotations.md`: name `volume` next to the existing examples. Keep `file` and `event`.
   - `docs/42for.net/platform/admin-ui.md`: the form, JSON, and YAML edit the unit type and definition; the menu suggests Time, Message, and Volume; any other text is saved; a Time definition that does not have 5 or 6 fields shows a hint and can still be saved.

Do not edit generated SDK files. Do not add a UI dependency.
