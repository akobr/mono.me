# Phase E of Administration UI

## Overview

Phase E adds the Templates and Schemas sections of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 14.

- **Templates:** the seven annotation types with their template and how many configurations use it. The template editor saves with guarded JSON Patch, and has history.
- **Schemas:** type, annotation and descendant-type schemas from the companion spec's E7 listing. A schema editor validates against the JSON Schema meta-schema next to a live form preview. Saving replaces the whole document and lists the configurations the change would break (409), with Save anyway. All three kinds have history.

Both sections reuse the phase D building blocks: `DocumentEditor`, `SchemaForm`, `VersionsView`, `ConflictDialog`, the binding decorations and the path-prefix invalidation.

## What Was Done

### 14. Templates

- **`templates.vue`:**
  - Lists the 7 annotation types, ordered by merge order, with the template version, author and update time (`GetTemplates`, E8), or "No template".
  - Each row shows how many configurations of that type the view has ("Applies to N configurations", from the catalog's configuration summaries).
  - The detail is a nested route (`templates/[type].vue`) in a `DetailPanel`.
- **`TemplateDetail`:**
  - **Tabs:** Edit and Versions (`?tab=versions`).
  - **Edit:** a JSON/YAML `DocumentEditor` with binding decorations and the version badge.
  - **Saving:**
    - Edits go through `PatchTemplate` with the same `test`-guarded patch as configurations (`buildPatch`). A 412 opens the `ConflictDialog` (Reload / Overwrite / Copy).
    - A missing template (404) offers "Create template", which saves with `SetTemplate`.
    - After a save or delete, the template path and every configuration of the view are invalidated (`invalidateAllConfigurations`), because effective documents include the template.
  - **Delete:** type the type code to confirm.
  - **Versions:** `VersionsView` with a template `VersionSource` (`GetTemplateVersions`, `GetTemplateVersion`, `GetTemplateVersionDiffCustom`).

### 14. Schemas

- **`utils/schema-targets.ts`:** one adapter per schema kind, `SchemaTarget` = type | annotation | descendant. `schemaApi(scope, target)` returns:
  - the title, the description and the resource path (query key base);
  - `get`, `set` (PUT, with `force`) and `remove`;
  - a `VersionSource` (`GetSchemaVersion*`, `GetAnnotationSchemaVersion*`, `GetDescendantTypeSchemaVersion*`);
  - `appliesTo(key, ancestors)`, which counts the affected configurations.

  `schemaRoute` and `summaryTarget` map routes and E7 listing rows; type codes are used as in the API.
- **`schemas.vue`:**
  - Three sections: type schemas (all 7 rows, with "No schema" where none exists), annotation schemas and descendant-type schemas.
  - Each row links to its editor; a descendant-type row also links to the annotation it belongs to.
  - "New schema" (`SchemaCreateDialog`) picks the kind, the type and/or the key (autocompleted from the catalog) and opens the editor.
- **Routes:** `schemas/type/[type]`, `schemas/annotation/[key]` (`[key]/index.vue`) and `schemas/annotation/[key]/type/[type]`.
  - All three are nested in the list page and render `SchemaDetailPanel` (title, link to the annotation) with `SchemaDetail`.
  - The annotation schema route uses `[key]/index.vue` instead of a `[key].vue` parent, so the descendant-type route is its sibling and not its child.
- **`SchemaDetail`:**
  - **Tabs:** Edit and Versions (`?tab=versions`). It shows the description and "Applies to N configurations".
  - **Layout:** at `xl` it is a split view, with the `DocumentEditor` on the left and `SchemaFormPreview` on the right. Below `xl` the two panes are tabs (Schema | Preview).
  - **Validation:** the document is checked against its meta-schema (`validateSchemaDocument`: draft-07, or 2019-09 / 2020-12 by `$schema`). Issues are listed under the editor and block saving.
  - **Saving:** `Set*Schema` replaces the whole document, then the schema and configuration-schema prefixes are invalidated (combined schemas included).
  - **409:** an alert with `NonCompliantTable` lists configuration, view and errors, each linking to the configuration, with **Save anyway**. Save anyway confirms with the count, then saves with `force=true`.
  - **Missing schema (404):** opens the editor with a starter document (`$schema` draft-07, `type: object`) and a "Create" button.
  - **Delete:** type `delete` to confirm.
- **`SchemaFormPreview`:** renders `SchemaForm` live from the schema being edited, with local sample data (kept across edits until "Reset sample", never saved, shown in a collapsible "Sample data"). It shows nothing while the schema is invalid or not an object schema.
- **Combined schema:** the phase D `CombinedSchemaView` (annotation detail and configuration detail) links each applied layer to these editors.

### Navigation

- Templates and Schemas are in the view section of the sidebar and in the command palette, with the shortcuts `g t` and `g s`.

### Mock API

- **`mock/template-schema-handlers.ts`:** generic versioned endpoints on the phase D document store, covering get, set (POST/PUT), patch (templates), delete, versions, a version and the version diff. Also:
  - the template listing and the schema listing (`Type`, `Annotation`, `DescendantType`);
  - templates for `exe` and `uxe`;
  - type schemas for `exe`, `uxe` and `cnt`;
  - an annotation schema for `exe.northwind.invoicing.prod` and a descendant-type schema below `sbt.northwind`.
- **409 on a schema change:** setting a schema evaluates every configuration of the view against the combined schema as if the new document were stored. It returns 409 with `Errors[]` (AnnotationKey, ViewName, NJsonSchema-style messages) unless `force=true`.
- The read-only template handler of phase D moved into this module.

### Deviations from the spec

1. **Detail tabs:** Template and schema details use a `?tab=versions` query instead of separate `versions` child routes. The annotation schema detail and the descendant-type schema detail would otherwise nest in each other's routes.
2. **Split view breakpoint:** the editor and preview split at `xl` (1280 px) instead of the general `lg`, because both sides need width inside the detail panel; below that they are tabs.

## Tests

`npm test` runs 123 tests in 16 files, and all pass. New in phase E, `test/unit/configuration/templates-schemas.test.ts`:

- schema targets: routes and API paths per kind, mapping of the E7 listing rows, `appliesTo` for descendant-type schemas;
- `validateSchemaDocument`: valid schema, invalid type keyword, 2020-12 keywords, non-object;
- `SchemaDetail` against the mock API (`msw/node`): a schema change breaking configurations lists them, with "/retries Number too big" and Save anyway, and the stored version stays unchanged; meta-schema errors block saving;
- `TemplateDetail`: an edit is saved as a guarded JSON Patch and becomes version 4.

These also pass:
- `vue-tsc --build`;
- oxlint and ESLint, with no findings;
- `npm run build`.

Screenshots of the schemas list with the execution schema editor and live form preview (1600 px, light) and of the templates list with the execution template (dark) were checked, with no console errors.

## Follow-ups

- Phase F: members, invitations, machines, certificates, the views page and project settings.
