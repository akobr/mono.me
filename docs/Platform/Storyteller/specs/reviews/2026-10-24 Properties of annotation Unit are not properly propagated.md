# Properties of annotation Unit are not properly propagated

## Overview

Bulk create now keeps `UnitType` and `UnitDefinition` when a unit element carries them, and the admin form, JSON, and YAML can edit both. This review covers both phases of [Properties of annotation Unit are not properly propagated](../2026-10-24%20Properties%20of%20annotation%20Unit%20are%20not%20properly%20propagated.md). Linear: [424-90](https://linear.app/424fun/issue/424-90/properties-of-annotation-unit-are-not-properly-propagated).

| Commit | Content |
|---|---|
| `bd43b86d` | The spec |
| `ba9c1b36` | Phase 1: `IUnit` and bulk create |
| `403cdbab` | Phase 2: the admin form and the user docs |

## What Was Done

### Decisions

The three decisions in the spec are in place. Suggestions are stored as `time`, `message`, and `volume`. A bulk element becomes a `Unit` only when its type is Unit and it has `UnitType` or `UnitDefinition`. A Time definition that is not 5 or 6 fields shows a hint and can still be saved. The OpenAPI body stays `List<Annotation>`. The C# and TypeScript SDKs were not regenerated. `Api.Web` bulk create is still a stub and was left alone.

### Phase 1: Contract and bulk create

1. `IUnit` now has `UnitType` and `UnitDefinition`. The comments say any string is stored, name `time`, `message`, and `volume`, and describe a definition as a Cron expression, a message name rule, template, or source, or a volume source or path filter. `Unit`, `IUnitOfExecution`, and `UnitOfExecution` are unchanged.
2. `AnnotationFactory.Normalize` copies the trigger when the input is an `IUnit`, not only a `Unit`. The object built from the key is still a `Unit`. A base `Annotation`, including one whose type is Unit and that omitted both properties, still gets `event` and `unknown`. Automatic ancestors are unchanged.
3. `SetAnnotations` reads `List<JsonElement>`. Keys are collected with a case-insensitive name before a body `400`. The access check, the missing-body check, the null-entry check, the size cap, and the restricted-machine filter are unchanged.
   - An element whose type is Unit and that has `UnitType` or `UnitDefinition` is deserialized as `Unit`. Every other element is deserialized as `Annotation`. Both use `AnnotationBodyOptions`.
   - `JsonException` is `400` with `Invalid annotation object in body: …`.
   - The route still overwrites `ProjectName` and `ViewName`. The copy is `unit with { ProjectName, ViewName }` cast back to `Annotation`. A `with` on the base reference would have dropped the `Unit`.
   - `AnnotationBodyOptions` now allows a trailing comma. The first parse already did, and the `JsonElement` keeps that comma, so the second parse rejected `SetMany_PropertyAndEnumNamesInAnyCase`. The single-write path still parses with `JsonDocument.Parse` defaults, so a trailing comma there is still rejected.
4. `Normalize_CopiesTheTriggerFromIUnit` uses a private `TriggerOnlyUnit : Annotation, IUnit` (`volume` / `incoming/*.csv`). `AnnotationModelTests` passed 14. The three new `SetMany` tests cover a concrete unit (`time`, `*/15 * * * *`, project and view from the route), a unit element without either property staying a base `Annotation`, and one trigger property returning `400` with no create call. The `SetMany` filter passed 12.

### Phase 2: Admin UI

1. `app/utils/unit-types.ts` holds the three suggestions, generic help, a case-insensitive lookup, `cronFieldHint`, `isUnitAnnotation` (type Unit or key code `unt`), and a menu that keeps the current value when it is not a suggestion. The menu list is typed as plain strings so a custom value such as `event` typechecks.
2. `annotationFormSchema` accepts optional `UnitType` and `UnitDefinition` and requires both to be non-blank for a unit. `toAnnotationDraft` adds them only for a unit. `toAnnotationBody` writes the trimmed draft over the loaded annotation for a unit and leaves other types unchanged. The fields are not in the read-only identity list. `typed-schema.ts`, `json-yaml.ts`, and `DocumentEditor.vue` are unchanged.
3. `AnnotationForm.vue` shows a Unit section only for a unit. The type is a `UInputMenu` with `create-item`. The definition is a `UTextarea` whose placeholder and help follow the selected type. The Cron hint is under the field for Time and does not block save. Relations no longer prints the trigger. Key links and name chips stay.
4. `annotation-form.test.ts` covers a unit draft, a non-unit draft, a custom value, blank unit values, the written body, and a JSON document and a YAML document. The form type is imported as `AnnotationDraft` so it does not clash with the Vue component. `unit-types.test.ts` covers lookup, generic help, the menu, the Cron hint, and `isUnitAnnotation`. Those two files passed 19 tests. `vue-tsc --build` and `oxlint && eslint` passed. `annotations.spec.ts` on `unt.invoicing.pdf` sets `queue` and `orders.>`, saves, and checks JSON, YAML, and a reload. A subject does not show the unit fields. Desktop and mobile: 19 passed, 3 skipped.
5. `docs/42for.net/platform/annotations.md` names `volume` and keeps `file` and `event`. `docs/42for.net/platform/admin-ui.md` says Form, JSON, and YAML edit the type and the definition, the menu suggests Time, Message, and Volume, any other text is saved, and the Time hint does not block save.

The preview of the legacy mock on `unt.invoicing.pdf` showed the Unit section with `event` and `unknown`, generic help, and Relations limited to the responsibility. Typing `queue` offered Create "queue". Saving from that preview needs a real key press on the menu item. Playwright already did that click, the save, the JSON and YAML checks, and the reload, on desktop and on mobile.
