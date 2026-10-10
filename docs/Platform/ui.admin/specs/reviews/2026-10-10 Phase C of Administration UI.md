# Phase C of Administration UI

## Overview

Phase C adds the annotation screens of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), sections 6 to 8 and 11.1:

- **Catalog:** the catalog store, with seven per-type streams in parallel and an IndexedDB snapshot.
- **Grid and tree:** a virtualized grid and a tree with two perspectives.
- **Filtering and search:** the GitHub-style filter language, and annotation search in the command palette.
- **Detail panel:** breadcrumb, lineage, and an Annotation tab with Form, JSON and YAML editing (labels, values, time-zone dates).
- **Changes:** create, delete, and the bulk actions.

The CodeMirror editor foundation (`CodeEditor`, `DocumentEditor`, `FormatToggle`) was planned for phase D, but it is built here, because the JSON and YAML modes of the Annotation tab need it. Phase D adds schema linting, diffs and binding decorations to it.

## What Was Done

### 6. Data layer: the catalog

- **`stores/catalog.ts`:**
  - Pages the seven per-type endpoints (`GetResponsibilities` … `GetUnitsOfExecution`) concurrently with the SDK's `paginate`.
  - When any type fails, it pages `GetAnnotations` as the fallback.
  - The grid renders after the first page arrives, and a progress line shows "Loaded N annotations…".
- **Data structure:**
  - Annotations are kept in a raw `Map` behind a `shallowRef`, mutated in place and published through a `revision` counter, so 20,000 objects are never deeply reactive.
  - Derived values: `annotations`, `childrenByKey` (from the key structure), `labelCounts` (most used first), and `configured`.
  - `configured` comes from the companion spec's E6 listing (`GetConfigurations`, paged), loaded alongside. `configurations` keeps the summaries for the version column.
  - Parsed keys and ancestors are cached per key for the filter.
- **Snapshot:**
  - After a full load, the annotations are written to IndexedDB (`idb-keyval`, store `2s-admin/catalog`), keyed by user ID and scope.
  - The next visit shows the snapshot at once with "Refreshing…", while the fresh load collects into its own map and replaces it when done.
  - Sign-out clears the store (an `onSignOut` hook).
- **Local changes:**
  - `upsert`, `remove` and `setConfiguration` update the catalog locally.
  - Reloading the same scope does not reload unless forced (the Refresh button).
  - Leaving a scope aborts the in-flight streams.
- **`queries/scope.ts`:** `useAnnotation(key)` binds the context to the generated `useGetAnnotation`, with `refetchOnWindowFocus` for detail queries.
- **`queries/invalidation.ts`:** path-prefix helpers for annotations, configurations, templates, schemas and access, used by phases C to F.

### 7. Keys, status and tree projection

- **`utils/annotation-key.ts`:** re-exports the SDK key helpers and adds per-type metadata: label, plural, lucide icon, `text-st-*` and `bg-st-*` classes, merge order, and the key parts each type needs.
- **`utils/annotation-status.ts`:** disabled, then scheduled, then expired, otherwise active, computed against `now` in UTC; invalid dates are ignored.
- **`utils/tree-projection.ts`:**
  - Both perspectives use the parent rules of section 7.
  - A parent missing from the catalog becomes a "(missing)" node, attached to its own parent the same way, so a whole missing chain shows up.
  - With a filter, matching nodes keep their ancestor chain, and the kept ancestors are marked `matched: false`, which renders them dimmed.
  - `expandableKeys` supports "Expand all".

### 8. Grid, tree, filtering and search

- **`AnnotationGrid`:**
  - Nuxt UI `UTable` (TanStack Table v8) with `virtualize`; rows are 36 px (compact) or 44 px (comfortable) from the preferences.
  - Columns: select, type (sorted by merge order), name, key (`KeyText`), title, labels (3 + "+n" chips with a stable color per label), status, configuration version, and the hidden-by-default valid from, expires, description and documentation.
  - Column interaction:
    - visibility and sizes are persisted in `preferences.grids.annotations`;
    - select, type and name are pinned left;
    - resizing uses handles in `ColumnHeader`;
    - Shift-click sorts on several columns.
  - The selected row has an opaque highlight, so pinned sticky cells do not show scrolled content through it.
- **Tree mode:**
  - Spec: TanStack sub-rows. Implementation: the page flattens the projected tree itself, with its own expanded set, and passes the flat rows to the table.
  - Reason: `UTable` renders an extra "expanded" row for every expanded row, which breaks virtualization.
  - The type cell indents and holds the expand toggle and the child count.
  - While a filter is active, the whole matching tree is open.
  - Sorting applies to the grid mode only.
- **`AnnotationList`:** the mobile renderer below `md`, with two-line cards in a TanStack Virtual list. The tree mode is a desktop feature.
- **`AnnotationFilterBar`:**
  - The filter text is debounced into `?q=`.
  - Chips show the parsed terms, can be removed, and are colored for negation and errors.
  - A help popover lists the syntax and clickable examples.
  - The bar also holds:
    - the Grid | Tree switch (`?mode=`) and the perspective (`?by=`);
    - expand and collapse all;
    - the column visibility menu;
    - the "shown of total" count.
  - `/` focuses the box.
- **`utils/filter-parser.ts`:**
  - Grammar: the grammar of section 8, including quotes, comma OR and `-` negation. Type aliases are accepted (`exe`, `execution`, …).
  - Errors: invalid values and unknown qualifiers produce messages. A qualifier term with an error is ignored, so a half-typed query does not hide everything; an unknown qualifier is searched as text.
  - `key:` matches a prefix, or a glob with `*`.
  - `subject:` and `responsibility:` also match the subject or responsibility itself.
  - `removeTerm` and `setQualifier` edit the query.
- **Keyboard and selection:**
  - `j`/`k`/`↑`/`↓` move the active row (scrolled into view), `Enter` opens the detail and `Esc` closes it.
  - Focus returns to the originating row when the detail closes.
  - The URL keeps `q`, `mode` and `by` when opening a row.
- **Bulk actions** (`AnnotationBulkBar`, Contributors and above):
  - Enable, disable, and add or remove a label use `SetAnnotations` in batches of 100.
  - Delete uses `DeleteAnnotation` with a concurrency of 4 and needs the count typed to confirm. It reports failed keys.
  - A progress bar shows while a bulk action runs.
  - The logic lives in `useAnnotationActions`, together with `chunk` and `runLimited`.
- **Command palette:**
  - Annotations are searched with Fuse over name, key and title, showing the best 20. Items are only built while the palette is open.
  - Recently opened keys are shown (`preferences.recentKeys`, the last 10).
  - Actions: "New annotation", "Switch project", pin, shortcuts and sign out.
- **`AnnotationCreateDialog`:**
  - Pick a type, then fill the names it needs, with suggestions from the catalog: contexts are limited to the chosen subject, and units to the chosen responsibility.
  - New names are accepted, and the key preview is live.
  - Validation: "already exists", and name rules from the SDK key helpers.
  - The open question of section 8 is settled: **the backend creates missing ancestors** (`CosmosAnnotationService.TryCheckAndCreate*`). The dialog therefore lists them, sends one `SetAnnotation`, and refreshes the catalog in the background to pick up the created ancestors.

### 9. Editors (moved forward from phase D)

- **`CodeEditor`:**
  - Our own CodeMirror 6 wrapper: line numbers, folding, bracket matching and closing, search, history, `⌘S`.
  - The theme reads Nuxt UI CSS variables, and the syntax colors are `--cm-*` tokens in `main.css` for light and dark, so the editor follows the color mode without reconfiguring.
  - Language, read-only, lint source and extra extensions sit in compartments.
  - `goToLine` is exposed.
- **`DocumentEditor`:**
  - Value level: it keeps a text buffer, emits the parsed value whenever the text parses, and keeps the last valid value otherwise.
  - Parse errors are inline diagnostics and are also listed under the editor.
  - A `validate` callback adds semantic issues.
  - **Format switching:** a switch, from its own toggle or from the parent, serializes the last valid value. It is refused with "Fix the syntax error at line N first", and a parent-driven switch is reverted.
  - Loading: CodeMirror is loaded with `defineAsyncComponent`. The production build puts it in its own chunk (`CodeEditor-*.js`, 419 kB minified), and the grid route does not load it.
- **`utils/json-yaml.ts`:**
  - JSON uses 2 spaces. YAML uses the core schema, unique keys, `lineWidth: 0` and plain strings and keys.
  - V8 error messages no longer contain a position, so JSON syntax errors are located with `jsonc-parser` (strict options).
  - Integers beyond 2^53 produce a warning.
- **`FormatToggle`:** Form | JSON | YAML.

### 11.1 Annotation detail

- **Detail route:** `annotations/[key].vue` is a nested route of the grid page, so the grid keeps its scroll position, selection and filter.
  - It renders `DetailPanel` (`components/shell`):
    - at `lg` and above, a resizable `UDashboardPanel` (25–70 %, 40 % by default);
    - at `md`, a right slideover;
    - below `md`, a full-screen sheet with a back button.
  - The detail uses a fresh `GetAnnotation`. The catalog entry is only shown while it loads, with a "From the catalog" badge.
  - A 404 shows "Annotation not found".
- **`AnnotationDetail` header:**
  - Type badge, name, status, and the key with copy.
  - A menu with copy link, copy key, and delete (type the key to confirm).
  - Close.
- **Breadcrumb and lineage:**
  - `AnnotationBreadcrumb` follows the tree perspective from the URL. Missing ancestors are struck through, and every segment links to its annotation.
  - `AnnotationLineage` is a popover listing every ancestor level in merge order.
- **Tabs:** child routes. Phase C has the Annotation tab; phase D adds Configuration, Versions and Schema.
- **Annotation tab (`AnnotationForm`):** one VeeValidate form, with one Zod schema (`forms/annotation.schema.ts`, which extends the generated `SetAnnotationBody`) for all three modes.
  - **Form mode:**
    - read-only name, type and key;
    - title;
    - description with Write/Preview tabs (`MarkdownText`: a small, escaped Markdown subset);
    - documentation link with an open button;
    - disabled switch;
    - Valid from and Expires, using `ZonedDateTimeInput`: `UInputDate` + `UInputTime` with `@internationalized/date`, interpreted in the annotation's time zone, with the local time in a tooltip and a clear button;
    - `TimeZoneSelect`: searchable, virtualized, with the current offset;
    - `LabelsInput`: `UInputMenu` with suggestions and counts from the catalog, plus create;
    - `ValuesEditor`: typed rows, popover editors for objects and arrays, "Edit all as JSON/YAML", and duplicate and empty keys reported;
    - read-only relation links and name chips.
  - **JSON/YAML mode:** edits the whole annotation through `DocumentEditor`. Changing key fields (`AnnotationKey`, `Name`, `AnnotationType`, `ProjectName`, `ViewName`) is reported and blocks saving. Zod errors are listed under the editor.
  - **Remembered mode:** in `?format=` when it differs from the preference. `⌘⇧F` cycles the modes; a syntax error blocks the switch.
  - **Save:**
    - Save (`⌘S`) and Revert sit in a sticky footer, with an "Unsaved changes" marker.
    - Saving calls `SetAnnotation`, updates the catalog entry and invalidates the annotation's path.
    - Empty strings and collections are sent as absent.
  - **Unsaved changes:** `useUnsavedChanges` asks "Discard changes?" through a promise-based `ConfirmModal` (`useConfirm` with `useOverlay`) when leaving or switching rows, sets `beforeunload`, and adds a dot to the document title.
  - **Readers:** see a read-only form with a lock badge.

### Mock API

- **Data:**
  - `mock/annotations.ts` generates deterministic catalogs per scope (seeded PRNG): about 400 annotations for `acme/billing/default`, and smaller sets for the other scopes.
  - Each annotation has titles, descriptions, labels, values, time zones, and disabled, scheduled or expired states.
  - One orphaned unit of execution makes the "(missing)" rows visible.
  - Scenario `?mock-scenario=large` generates about 16,000 annotations.
  - Only edits are stored: overrides and deletions in `sessionStorage`.
- **Handlers:** `mock/annotation-handlers.ts` serves:
  - the seven per-type lists and `GetAnnotations`, with pages of 1000 and offset tokens;
  - get, set and delete of one annotation;
  - `SetAnnotations`;
  - the configuration listing (a deterministic third of the keys).

  `SetAnnotation` creates missing ancestors like the API.

### SDK

The Orval input transformer has a third correction:
- **Problem:** `Values` of `Annotation` and `Configuration` is `IReadOnlyDictionary<string, object>` in C#, but the OpenAPI document declares a map of objects. The generated Zod schemas would reject `{ "retries": 3 }`.
- **Fix:** the transformer widens it to any JSON value.
- **Result:** the SDK was regenerated (5 generated files changed), and its README documents all three corrections.

### Deviations from the spec

1. **Editor foundation** moved from phase D to C, as described in the overview.
2. **Tree rows** are flattened by the page instead of using TanStack sub-rows, because of the extra expanded rows.
3. **Column reorder** (drag) is not implemented. Visibility, size, pinning and multi-sort are.
4. **Mobile filters:** the filter box sits on top of the list, not in a bottom sheet, and the tree mode is desktop-only.
5. **Unit fields:** `UnitType` and `UnitDefinition` are shown read-only, because the `Annotation` body of `SetAnnotation` has no fields for them.
6. **Spec text corrected:** the "Open configuration" and "Show in graph" actions come with phases D and G.

## Tests

`npm test` runs 96 tests in 12 files, and all pass. New in phase C:

| File | Covers |
| --- | --- |
| `test/unit/utils/filter-parser.test.ts` | tokenizer with quotes; parsing of qualifiers, negation, OR and text; error messages; every qualifier (`type` aliases, `label`, `is` with time, `has` incl. `config`, key parts, `key` prefix and glob, `ancestor`, text); ignored error terms; `removeTerm`, `setQualifier` |
| `test/unit/utils/annotations.test.ts` | status precedence, edges, invalid dates and offsets; tree by subject with a missing chain; tree by responsibility; filtered ancestor chains with depths; `expandableKeys`; type metadata; labels; the safe Markdown subset (escaped HTML, no `javascript:` links) |
| `test/unit/utils/json-yaml.test.ts` | round trip and key order, serialization options, syntax errors with lines (JSON and YAML duplicate keys), the 2^53 warning |
| `test/unit/annotations/catalog.test.ts` | parallel streams with continuation tokens, label counts, children, configured set; fallback to `GetAnnotations`; local upsert and remove; snapshot shown first and then replaced (`fake-indexeddb`); error state |
| `test/unit/annotations/annotation-form.test.ts` | schema UI rules (title, URL, time zone, labels, value keys, date order), primitive values, `toAnnotationBody`, read-only key fields, `chunk`, `runLimited` concurrency |
| `test/unit/annotations/editors.test.ts` | `DocumentEditor` serialization, parsed values, blocked format switch with the line, switching from the last valid value, semantic issues; `ValuesEditor` editing, adding rows and duplicate keys (in `<UApp>`, with a textarea in place of CodeMirror) |

These also pass:
- `vue-tsc --build`;
- oxlint and ESLint, with no findings;
- `npm run build`.

**Performance** was measured in the mock dev server (unminified) with Chrome on the development laptop, on the `large` scenario (16,317 annotations):

| Metric | Measured | Budget |
| --- | --- | --- |
| First rows | 1.55 s | 1.5 s |
| Full catalog | 3.3 s | 6 s |
| Typing a 3-term filter until the count updates (including the 200 ms debounce and rendering) | about 330 ms | – |
| Scrolling the grid | 55 fps | – |

**Screenshots checked, with no console errors:**
- grid (light);
- tree with a filter (dark), including the "(missing)" chain;
- detail with the form;
- detail in YAML mode (dark);
- mobile list;
- create dialog;
- command palette search.

## Follow-ups

- Column reorder by drag in the grid.
- The bottom-sheet filter on phones (phase H, mobile pass).
- Phase D: Configuration, Versions and Schema tabs; schema-aware linting and completion in `CodeEditor`; `DiffEditor`.
