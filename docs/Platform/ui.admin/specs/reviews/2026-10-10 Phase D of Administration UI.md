# Phase D of Administration UI

## Overview

Phase D delivers the configuration screens of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), sections 9 to 11.3:

- **Editor additions:** schema linting, binding decorations and `DiffEditor` on top of the editor foundation of phase C.
- **Schema-driven form:** `SchemaForm`, JSON Forms with our own Nuxt UI renderer set, including inherited values and bindings.
- **Configuration detail:** Edit, Effective, Resolved, Versions and Schema, reachable from a new Configurations page and from the annotation detail.
- **Saving:** JSON Patch saves guarded by `test` operations, with 409 schema errors (Save anyway) and the 412 conflict dialog.
- **History:** versions and diffs, which templates and schemas reuse in phase E.
- **View comparison:** the Compare views page.
- **Mock API:** the configuration endpoints, with real effective merging, combined schemas, bindings, patch semantics and diffs.

## What Was Done

### 9. Editors

- **Schema linting** (`components/editor/setup/schema-lint.ts`):
  - **Choice:** an Ajv-based CodeMirror linter. The spec's fallback is used as the primary implementation instead of `codemirror-json-schema` (see Deviations).
  - **Validation:** the document is parsed and validated with `utils/schema-validation.ts`.
  - **Mapping errors to text:** each error's JSON Pointer is mapped to a text range with `jsonc-parser` (`findNodeAtLocation`) for JSON, or the `yaml` CST node ranges for YAML. Missing values fall back to the nearest parent.
  - **Binding values** and everything below them are not reported.
- **`utils/schema-validation.ts`:**
  - **Ajv instances:** draft-07 by default, or 2019-09 / 2020-12 when `$schema` says so. The options are `allErrors`, non-strict and `ajv-formats`, and the `x-secret` and `x-multiline` keywords are accepted.
  - **Compilation:** each schema object is compiled once (`WeakMap`); an invalid schema yields no client errors.
  - **Error pointers:** `required` errors point at the missing property.
  - **Server errors:** `parseServerErrors` maps the API's 409 messages (NJsonSchema style `#/endpoints[0].url: NumberTooBig`) to pointers and readable messages ("Number too big").
- **Binding decorations** (`setup/bindings.ts`):
  - What is marked: `"@…"` strings, YAML `@…` scalars, and the `$jsonlogic`, `$jsone` and `$definition` keys.
  - How: a subtle background through a `ViewPlugin`, plus an "fx" gutter marker on their lines.
- **`DiffEditor`:**
  - **Layouts:** `MergeView` side by side at `lg` and above, `unifiedMergeView` below, both read-only.
  - **Collapsing:** `collapseUnchanged: { margin: 3, minSize: 6 }`, the server's hunk rules.
  - **Header:** "+N −M · K changes" from the server `DiffStats`, previous/next buttons that jump to each hunk's `NewStart`, a Collapse unchanged switch, and Auto | Split | Unified.
  - **Theme:** the colors use the theme variables.
- **Lazy loading:** `DiffEditor`, `SchemaForm` and CodeMirror are loaded with `defineAsyncComponent`, and the production build keeps them in their own chunks.

### 10. Schema-driven configuration form

- **`SchemaForm`:**
  - **Rendering:** `@jsonforms/vue` `JsonForms` with `Generate.uiSchema(schema)` and `validationMode: 'NoValidation'`.
  - **Errors:** our Ajv pass (binding-aware) and the server's 409 errors are passed as `additionalErrors`, so both appear on the same controls.
  - **Inheritance:** a provide/inject context gives the renderers the effective document and the actions Override (copy the inherited value into the own content) and Reset to inherited (remove the own value).
- **Renderers** (`components/configuration/renderers/`, registry in `index.ts`):

  | Renderer | Rank | Schema |
  | --- | --- | --- |
  | `LayoutRenderer` | 1 | VerticalLayout, HorizontalLayout, Group |
  | `TextControl` | 2 | strings |
  | `TextControl` | 3 | special strings: multiline, secret with reveal, URL with open |
  | `NumberControl` | 2 | `UInputNumber` with minimum, maximum and multipleOf as step |
  | `BooleanControl` | 2 | `USwitch` |
  | `EnumControl` | 3 | enum: `USelect`, or `USelectMenu` with search above 8 options |
  | `EnumControl` | 5 | oneOf-const enum: titles shown, const stored |
  | `DateTimeControl` | 4 | date, time and date-time |
  | `PrimitiveArrayControl` | 4 | `UInputTags`, or a multiple `USelectMenu` for enums |
  | `ArrayLayout` | 4 | arrays of objects: collapsible cards with add, remove and move |
  | `ObjectRenderer` | 2 | fieldset with title, description and collapse |
  | `MapControl` | 4 | `additionalProperties` without `properties`: key/value rows, values rendered with the `additionalProperties` schema |
  | `CombinatorRenderer` | 4 | oneOf/anyOf of objects: segmented branch picker, branch fitting the data preselected |
  | `FallbackJsonControl` | 1 | any other control |
  | `FallbackJsonControl` | 50 | `not`, `if`, `patternProperties`, tuples and `prefixItems` |

- **`ControlField`** frames every control:
  - label and required mark;
  - Markdown help (`MarkdownText`);
  - errors;
  - the "inherited" chip with the inherited value as placeholder, and Override / Reset to inherited;
  - a deprecated badge;
  - the "fx" literal → expression toggle.
- **Binding values:** `ControlField` renders `BindingExpressionInput` instead of the control. That component edits a monospace `@…` value, or an envelope as JSON, and has "Convert to value".

### 11.2 Configuration detail

- **`useConfiguration(key)`:** queries the effective document (`GetConfiguration`, which carries `Version`), the own content (`GetConfigurationVersion(Version)`) and the combined schema. A schema without properties counts as "no schema", which hides the Form mode.
- **`ConfigurationEditor` (Edit):**
  - **Modes:** Form | JSON | YAML. Form is the default when a schema applies; `?format=` overrides it.
  - **Text modes:** schema linting and binding decorations.
  - **Footer:** the change count of the pending patch, Revert, and Save (`⌘S`).
  - **Unsaved changes:** the guard of phase C.
  - **Saving:**
    - `utils/json-patch.ts` `buildPatch` runs `compare(original, edited)` and adds a `test` with the original value before every `replace` and `remove`.
    - `PatchConfiguration` sends `application/json-patch+json`.
    - On success it updates the catalog's configuration summary (the version column) and invalidates `/configuration/{key}` and `/configurations`.
  - **409 schema errors:**
    - `SchemaErrorsPanel` lists them, and the same errors are attached to the form controls.
    - Contributors get Save anyway. It asks for confirmation naming the failing rules, then patches with `force=true`.
  - **412 `PatchTestFailed`:**
    - `ConflictDialog` loads the fresh own content and shows theirs and yours in a `DiffEditor`.
    - **Reload, discard mine** takes theirs.
    - **Overwrite** re-patches from the fresh base, so the new tests guard the values just loaded.
    - **Copy mine** copies to the clipboard.
  - **No configuration (404):** "Create empty", or "Start from template" (`GetTemplate` content), through `SetConfiguration`.
  - **Delete own:** type the key to confirm. Inherited values remain.
- **`ConfigurationEffective`:** the merged document, read-only, with the format switch, binding decorations and the hash.
- **`ConfigurationResolved`:**
  - `gcTime: 0` and `staleTime: 0`, so nothing is cached.
  - A "may contain secrets" banner, worded per role (ContributorWithSecrets).
  - The content is blurred until **Reveal**.
  - A 422 shows the binding error's message and hint.
- **`CombinedSchemaView`:** the applied layers in order (type, descendant type, annotation, with versions), linking to their schema pages (phase E), and the merged schema read-only.
- **Routes:**
  - **Configurations page:** `/[org]/[project]/[view]/configurations` is a virtualized grid of the configuration summaries (E6), filtered with the same filter language as the annotations. `configurations/[key]` is a `DetailPanel` with Edit (index), Effective, Resolved, Versions and Schema as child routes, and an "Annotation ›" link.
  - **Annotation detail tabs:** Configuration (with the `vN` badge from the catalog; Edit, Effective and Resolved as sub-tabs in `?cfg=`), Versions and Schema, plus "Open configuration" in the menu.
  - **Navigation:** Configurations and Compare views in the sidebar, and `g c`.

### 11.3 Versions and diffs

- **`VersionSource`** describes where a versioned document gets its history: `queryKey`, `list`, `content`, `diff`, `current`. Configurations implement it here; templates and the three schema kinds implement it in phase E.
- **`VersionsView`:**
  - **Addressing:** `?v=` views one version, and `?v=&from=` compares two.
  - **Compare pickers:** two selects with a Compare button; this is the spec's "Compare with…".
  - **Timeline:** `VersionTimeline`, newest first, marks the current and expired versions and has View and Compare with previous.
- **`VersionCompare`:** loads the server diff and both documents. Both are serialized identically (2 spaces), so the editor diff and the server hunks agree. When a document is gone (expired), it explains why and shows the server hunks in `HunkList`, a unified read-only view with old and new line numbers.
- **View compare** (`[view]/compare.vue`):
  - Pick a key (configured keys first) and another view of the project.
  - `GetConfigurationViewDiff` gives the stats and hunks.
  - Both effective documents go into the `DiffEditor`; a missing configuration counts as `{}`.
  - The selection is kept in `?key=&to=`.

### Mock API

- **`mock/documents.ts`:** a versioned document store shared by configurations, templates and schemas.
  - **Base data:** deterministic histories. About a third of the annotations has a configuration with 1–12 versions; versions older than 30 days are expired and have no content.
  - **Edits:** copied into `db.documents` with new versions; deletions are marked.
- **`mock/configuration-handlers.ts`:**
  - **Reads:** listing, get (effective), versions, a version, version diff and view diff (`mock/diff.ts`, an LCS line diff with the server's hunk rules).
  - **Writes:** set (deep merge), patch, delete.
  - **Effective document:** ancestors in merge order, then the type template, then the own content.
  - **Combined schema:** type, descendant-type and annotation layers; `mock/schemas.ts` holds an execution schema that exercises every renderer.
  - **Resolved:** `@key/path` references are evaluated, and a 422 is returned when they cannot be.
  - **Patch:** `fast-json-patch` with validation; a failed `test` gives 412 `PatchTestFailed`. Ajv schema checks give 409 with NJsonSchema-style messages, unless `force=true`.
  - **Template:** read-only here.
- **`withDb` writes back only when the handler changed the data.** Before this, a read that finished late could overwrite a newer write. The editor test that simulates a concurrent save found the problem.

### Fixes to earlier phases

- **`formatDateTime`** combined `dateStyle`/`timeStyle` with `timeZoneName`, which `Intl` rejects, so `RelativeTime` tooltips threw. It now lists the fields one by one, and a test covers it.
- **`structuredClone`** rejects Vue reactive proxies (query data). `utils/clone.ts` `cloneJson` replaces it for configuration documents.

### Deviations from the spec

1. **Schema linting:** Ajv-based linting is the primary implementation instead of `codemirror-json-schema`. That package pulls in Shiki and markdown-it for hover rendering and has a known YAML completion issue. As a result, the text editors have schema **validation** but no schema **completion or hover**.
2. **Binding detection** happens in `ControlField`, not in a rank-100 `BindingControl`. JSON Forms testers receive the schema and the UI schema, not the data, so a tester cannot see that a value is a binding.
3. **Reset to inherited** is offered for every own value whose path also exists in the effective document. Knowing what the value "would inherit" without the own layer needs the ancestors' own documents, which the spec defers as well.
4. **Date controls:** date, time and date-time use the native `date`, `time` and `datetime-local` inputs inside `UInput`, instead of `UInputDate`/`UInputTime` (`@internationalized/date` objects). Values stay RFC 3339 strings.
5. **Renderer files:** one `TextControl` serves the string, multiline, secret and URL renderers with separate ranks, and one `EnumControl` serves enum and oneOf-enum.
6. **Configuration tab in the annotation detail:** Edit, Effective and Resolved are sub-tabs (`?cfg=`) instead of separate child routes. Versions and Schema are child routes.

## Tests

`npm test` runs 116 tests in 15 files, and all pass. New in phase D:

| File | Covers |
| --- | --- |
| `test/unit/configuration/documents.test.ts` | JSON Pointer (escaping, JSON Forms paths, immutable set and remove); `buildPatch` (`test` before replace and remove, no-op); schema validation (pointers, required, formats, binding values skipped, `$schema` drafts, broken schemas); 409 path and kind mapping; `cloneJson` on reactive data; the mock diff's hunk merging |
| `test/unit/configuration/schema-form.test.ts` | Every renderer kind renders (text with Markdown help, multiline, secret, URL, number with required mark, switch, date, object array, map, object, fallback, deprecated); emptied values are removed; inherited placeholder and Override; Reset to inherited; binding values as expressions without schema errors; client and server errors attached to controls |
| `test/unit/configuration/configuration-editor.test.ts` | Against the e2e mock API served by `msw/node`: load own content, save a guarded patch as a new version; a 409 with Save anyway; a concurrent save → 412 → conflict dialog with theirs |
| `test/unit/utils/annotations.test.ts` | + date formatting in a zone with fallback, relative time |

These also pass:
- `vue-tsc --build`;
- oxlint and ESLint, with no findings;
- `npm run build`.

**Screenshots checked in mock mode, with no console errors:**
- the configurations grid;
- the Edit form, showing inherited chips, Override, Reset, the binding input, select, oneOf enum and password field;
- a save creating v2, with the catalog version column updated;
- the side-by-side diff of v2 and v4 (dark), with "+5 −4 · 1 change";
- the Resolved tab revealed, with bindings evaluated.

## Follow-ups

- Schema completion and hover in the text editors, if `codemirror-json-schema` becomes lighter or the Ajv linter grows a completion source.
- "Would inherit" values for Reset to inherited, once the ancestors' own documents are loaded.
- Phase E: the template and schema sections, which reuse `VersionsView`, `DocumentEditor` and `SchemaForm`.
