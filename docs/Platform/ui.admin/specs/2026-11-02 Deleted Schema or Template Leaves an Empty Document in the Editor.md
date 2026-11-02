# Deleted Schema or Template Leaves an Empty Document in the Editor

Linear: [424-158](https://linear.app/424fun/issue/424-158/admin-ui-deleted-schema-or-template-leaves-in-the-editor-and-asks-to) — Admin UI: deleted schema or template leaves "{}" in the editor and asks to discard.

## Problem

In the admin UI (`src/Platform/ui.admin`), Modeling › Schemas (or Templates): open an existing schema (or a type template), delete it with the typed confirmation, then close the detail.

- **Expected:** the toast "Schema deleted" / "Template deleted". Then the schema editor shows the starter document with **Create**, and the template detail shows the empty state "No <type> template" with **Create template**. Closing the detail leaves without a prompt.
- **Actual:** the editor keeps `{}` with **Create**. The schema gets no starter document, the template no empty state, and closing asks "Discard changes?".

The UI automation tests of 424-152 found it. MOD-SCH-03 (Testiny 96), MOD-SCH-07 (105) and MOD-TPL-01 (147) accept both states for now.

## Current State

- **`app/components/schemas/SchemaDetail.vue`**
  - Lines 44–45: `original` and `draft` refs. Line 69: `dirty` compares `JSON.stringify(draft ?? null)` with `JSON.stringify(original ?? null)`.
  - Lines 53–62: the content of the loaded document is copied into `original` and `draft` while the draft is clean.
  - Lines 63–67: when `missing` (a 404) turns true and `draft` is `undefined`, `draft` gets the starter document. `original` stays `undefined`, so an untouched starter document is dirty. The alert "This schema does not exist yet" (`missing && !dirty`, line 174) therefore never shows, and leaving an untouched new schema asks "Discard changes?". The watch is not `immediate`: a remount with the 404 already cached never sets the starter document.
  - Lines 125–139: `remove()` deletes the schema, sets `original` and `draft` to `undefined` and then invalidates the schema queries; the 404 of the refetch turns `missing` true.
  - Line 218: the editor renders `:model-value="draft ?? {}"` and writes every `update:model-value` back to `draft`.
- **`app/components/templates/TemplateDetail.vue`**
  - Lines 152–167: `remove()` does the same as the schema's.
  - Lines 205–212: the empty state renders for `missing && draft === undefined`; line 221 renders `draft ?? {}`.
- **`app/components/editor/DocumentEditor.vue`**
  - Lines 64–74: `onText` parses each text the inner `CodeEditor` reports and emits the parsed value.
  - Lines 76–89: an external value change (here `{}`) re-serializes `text`.
- **`app/components/editor/CodeEditor.vue`**
  - Lines 107–111: the CodeMirror update listener emits `update:modelValue` for **every** document change.
  - Lines 120–129: an external `modelValue` replaces the document with a dispatch. That dispatch is a document change too, so the listener reports the external value back as if typed.
- **The chain after a delete:** `draft` becomes `undefined` → the editor gets `{}` → `DocumentEditor` serializes `"{}"` → `CodeEditor` dispatches it → the listener echoes `"{}"` → `DocumentEditor.onText` emits `{}` → the detail sets `draft = {}`. `draft` (`{}`) now differs from `original` (`undefined`): the editor is dirty. When the 404 arrives, `draft` is no longer `undefined`: the schema gets no starter document and the template no empty state.
- The configuration editor's **Delete own** has the same root cause (the issue mentions it); it gets the same echo through the same editor.
- **Tests:** `test/unit/configuration/templates-schemas.test.ts` mounts `SchemaDetail` and `TemplateDetail` against the MSW mock. `CodeEditorStub` (`test/helpers.ts`) is a textarea that emits only on input, so the echo cannot be reproduced there. No unit test covers a delete.

## Proposed Changes

### 1. No echo of an external value (`CodeEditor.vue`)

The update listener emits `update:modelValue` only when the new document differs from `props.modelValue`. A dispatch made for an external value then emits nothing; typing still emits, because the prop holds the previous text until the parent updates it.

### 2. No emit of an unchanged value (`DocumentEditor.vue`)

`onText` emits only when the parsed value (as JSON) differs from `lastEmitted`, the last value emitted or received. Text that parses to the value already held (an echo, or whitespace and formatting changes) leaves the model alone. This guards the editor against an echo from any inner editor.

### 3. Consistent reset of the schema editor (`SchemaDetail.vue`)

- The `missing` watch becomes `immediate` and sets **both** `original` and `draft` to a copy of the starter document when the schema is missing and the draft is clean (`draft` equals `original`). An untouched starter document is not a change: "This schema does not exist yet" shows, **Create** stays enabled (`(!dirty && !missing)`), and leaving asks nothing. Edits of the starter document are still changes.
- `remove()` keeps resetting `original` and `draft` to `undefined` together before the refetch; the 404 then puts the starter document in both.
- **Revert** already falls back to the starter document (`original ?? starterSchema`).

### 4. Template editor (`TemplateDetail.vue`)

`remove()` keeps resetting both to `undefined`. With sections 1 and 2, `draft` stays `undefined` until the 404, so the empty state "No <type> template" replaces the editor and nothing is dirty. No other change.

### 5. Tests

- Unit (`test/unit/configuration/templates-schemas.test.ts`): an echoing `CodeEditor` stub (it emits its `modelValue` back on every external change, like CodeMirror) reproduces the bug.
  - `SchemaDetail`: deleting a type schema leaves the starter document, **Create**, the "does not exist yet" alert and a clean editor.
  - `TemplateDetail`: deleting a template shows "No execution template" with **Create template**.
  - `DocumentEditor`: an external value is not emitted back by an echoing inner editor.
- UI automation (`src/Platform/UI.Admin.AutomationTests`): tighten MOD-SCH-03, MOD-SCH-07 and MOD-TPL-01 to the fixed behaviour: the starter document after a schema delete, the empty state after a template delete, and closing without "Discard changes?". Remove the "Admin UI bug (424-152 review)" comments and the `DeletedSchemaDocument` fallback.

### 6. Docs

Update `docs/42for.net/platform/admin-ui.md` if it describes what the editor shows after a delete, and the test case descriptions in `docs/Platform/UI.Admin.AutomationTests/test-cases.md` if they mention the bug.
