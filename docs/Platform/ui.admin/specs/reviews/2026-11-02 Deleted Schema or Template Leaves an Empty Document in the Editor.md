# Deleted Schema or Template Leaves an Empty Document in the Editor

## Overview

This review covers the fix of [424-158](https://linear.app/424fun/issue/424-158/admin-ui-deleted-schema-or-template-leaves-in-the-editor-and-asks-to). After a schema is deleted, its editor now shows the starter document with **Create**. After a template is deleted, the detail shows the empty state "No <type> template". In both cases you can close the detail without "Discard changes?". The fix was delivered in one phase, as planned in [the spec](../2026-11-02%20Deleted%20Schema%20or%20Template%20Leaves%20an%20Empty%20Document%20in%20the%20Editor.md).

## What Was Done

1. **No echo of an external value (`app/components/editor/CodeEditor.vue`).** The CodeMirror update listener now emits `update:modelValue` only when the new text differs from `props.modelValue`. When the parent replaces the document (a revert, a reload, the `{}` shown after a delete), the dispatch no longer comes back as an edit. Typing still emits, because the prop holds the previous text until the parent updates it.
2. **No emit of an unchanged value (`app/components/editor/DocumentEditor.vue`).** `onText` emits only when the parsed value (as JSON) differs from `lastEmitted`. This guards against an echo from any inner editor. Text that parses to the same value, such as a whitespace edit, now leaves the model alone.
3. **Consistent reset of the schema editor (`app/components/schemas/SchemaDetail.vue`).**
   - The `missing` watch is now `immediate`. When the schema is missing and the draft is clean, it puts a copy of the starter document into **both** `original` and `draft`. An untouched starter document is therefore not a change: the alert "This schema does not exist yet" shows (before, it never did), **Revert** is disabled, and leaving asks nothing. This also covers a remount when the 404 is already cached.
   - `remove()` still resets both refs to `undefined` before the refetch. The 404 then puts the starter document in both.
   - **Deviation from the plan:** the starter document is no longer dirty, but **Ctrl+S** (`defineShortcuts` and the editor's `@save`) was gated on `dirty`. A Ctrl+S on an untouched starter document stopped creating the schema, which the UI automation test MOD-SCH-02 (`Schemas_NewSchemaDialog_OpensDescendantTypeEditor`) caught. A new computed `canSave = (dirty || missing) && textValid` now gates the **Create**/**Save** button, the shortcut and the editor's save, so all three behave the same.
4. **Template editor (`app/components/templates/TemplateDetail.vue`).** No logic change; only a comment was added to `remove()`. With sections 1 and 2, `draft` stays `undefined` until the 404 arrives, so the empty state replaces the editor and nothing is dirty.
5. **Tests.**
   - Unit (`test/unit/configuration/templates-schemas.test.ts`, describe "deleting a schema or a template"): `EchoingCodeEditorStub` (new in `test/helpers.ts`) reports every external value back, as CodeMirror did before the fix. `mountScreen`/`mountInApp` accept a `stubs` override for it. The four tests fail without the fix and pass with it:
     - `DocumentEditor` does not emit an external value back;
     - the starter document of a missing schema (`rst`) is unchanged, with **Create** enabled;
     - a deleted type schema (`uxe`) goes back to the starter document with **Create**, no **Delete**, a disabled **Revert** and the "does not exist yet" alert;
     - a deleted template (`exe`) shows "No execution template" with **Create template**.
   - End-to-end (`test/e2e/modeling.spec.ts`, new, desktop and mobile, with the real CodeMirror):
     - a deleted schema goes back to the starter document and closes without a prompt;
     - a deleted template shows the empty state and closes without a prompt;
     - Ctrl+S creates the untouched starter document of a missing schema.

     The first two fail on the old code.
   - UI automation (`src/Platform/UI.Admin.AutomationTests`): MOD-SCH-03 (Testiny 96), MOD-SCH-07 (105) and MOD-TPL-01 (147) are tightened. Removed: the "Admin UI bug (424-152 review)" comments, the `DeletedSchemaDocument` fallback and the `DiscardIfShownAsync` workaround. The tests now expect only the starter document or the empty state, a clean title (no "● "), and a close without "Discard changes?".
   - **Beyond the plan:** CFG-CRE-04 (`Configurations_DeleteOwn_NeedsTypedKey`, Testiny 81) is tightened the same way. The editor change also removes the prompt after **Delete own** (the configuration bug of 424-157, which has the same root cause). The test's "no prompt" branch, `DiscardIfShownAsync(ConfigurationPanel.Gone)`, then failed: Playwright considers the `body:not(:has(section#…))` locator hidden, so `DetailPanel.Gone` cannot be the outcome of `DiscardIfShownAsync`. That branch had never run before. `Gone` is no longer used by any test; it was left as it is. CFG-INH-01 still uses `DiscardIfShownAsync` with a visible outcome and passes, so it was not changed.
   - Runs: the whole unit suite (1468 tests; under full parallel load a few timing-sensitive tests in other areas fail at random, and they pass alone), lint, `vue-tsc`, and the e2e suites `modeling`, `navigation` and `configuration` (the visual suite needs the Linux Docker image and was not run). The UI automation runs against the mock e2e build: the Modeling schema and template classes on desktop and phone, the Configurations area on desktop and phone, and the full mock suite on desktop.
6. **Docs.** `docs/42for.net/platform/admin-ui.md` now explains that the starter document of a missing schema is not an unsaved change, and what the editor shows after a schema or a template is deleted. `docs/Platform/UI.Admin.AutomationTests/test-cases.md` did not mention the bug, so it needed no change.

PR #157 (424-157) fixes the configuration editor on its own. It changes the default `CodeEditorStub` so that it echoes, and resets `ConfigurationEditor.remove()` to `{}`. Both changes are compatible with this fix. If #157 merges first, `EchoingCodeEditorStub` duplicates the default stub and can be dropped.
