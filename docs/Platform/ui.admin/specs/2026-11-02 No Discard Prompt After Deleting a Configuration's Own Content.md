# No Discard Prompt After Deleting a Configuration's Own Content

Linear: [424-157](https://linear.app/424fun/issue/424-157/admin-ui-discard-changes-after-deleting-a-configurations-own-content) — Admin UI: "Discard changes?" after deleting a configuration's own content.

## Problem

In the admin UI (`src/Platform/ui.admin`), open a configuration with its own content, choose **Delete own**, type the key and confirm. Then close the detail or open the **Effective** tab. The app asks **Discard changes?**, and the tab title has the "● " dirty mark. Nothing was edited: the own content was deleted on purpose. The app should leave straight away and the title should be clean.

The UI automation tests of 424-152 (CFG-CRE-04 `ConfigurationCreateTests.Configurations_DeleteOwn_NeedsTypedKey` and CFG-INH-01) found it. They work around it with `DiscardIfShownAsync`.

## Current State

- `app/components/configuration/ConfigurationEditor.vue`:
  - Lines 49–50: `original` (the stored own content, the base of the patch) and `draft` (the edited content).
  - Line 60: `dirty` compares `JSON.stringify(draft ?? null)` with `JSON.stringify(original ?? null)`. `useUnsavedChanges(dirty)` (line 75) guards the route and drives the title dot.
  - Lines 64–73: a watch copies the own content into both refs when it (re)loads and nothing is dirty. After a delete the own query answers 404, so it never runs.
  - Lines 217–237, `remove()`: after `deleteConfiguration` it sets `original` and `draft` to `undefined`, then invalidates the queries.
  - Line 322: `DocumentEditor` gets `:model-value="draft ?? {}"`.
- `app/components/editor/DocumentEditor.vue`: an external value change re-serializes the text buffer, and CodeMirror reports the new text back through `onText`, which emits `update:modelValue`. So `draft` becomes `{}` while `original` stays `undefined`: `"{}" !== "null"`, the editor is dirty.
- After the invalidation the own query answers 404 (`noOwn`), and the template shows the **No configuration yet** empty state instead of the editor. The dirty state stays, and nothing can be saved from there.

## Proposed Changes

1. **Reset to one value after a delete.** In `remove()`, set `original` and `draft` to the same empty document (`{}`) instead of `undefined`. The editor shows `{}` anyway, so its echo is not a change.
2. **Nothing stored means nothing to discard.** `dirty` is `false` while the configuration has no own content (`noOwn`) or the annotation is missing (`missing`). The editor is not shown then and a patch would fail with 404, so there is no unsaved work. This also covers an echo that differs from `{}`, such as defaults the schema form might fill in.
3. **Unit test** (`test/unit/configuration/configuration-editor.test.ts`): delete the own content of a configured execution through **Delete own** and the typed-key dialog. Then the own content is gone, the empty state shows, and `hasUnsavedChanges()` is `false`.
4. **The UI automation tests** live on the 424-152 branch (PR #155), not on `main`. Their `DiscardIfShownAsync` workaround is removed there once this fix is merged; this change does not touch them.
5. **Documentation:** the user docs of the configuration editor need no change, as they describe the expected behaviour.
