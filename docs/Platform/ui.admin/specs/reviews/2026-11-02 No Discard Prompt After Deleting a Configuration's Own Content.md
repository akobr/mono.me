# No Discard Prompt After Deleting a Configuration's Own Content

## Overview

This review covers the fix of [424-157](https://linear.app/424fun/issue/424-157/admin-ui-discard-changes-after-deleting-a-configurations-own-content): after **Delete own** in the configuration editor, the admin UI no longer asks **Discard changes?** when the user leaves, and the title has no "● " mark. It was delivered in one phase, as planned in [the spec](../2026-11-02%20No%20Discard%20Prompt%20After%20Deleting%20a%20Configuration's%20Own%20Content.md).

## What Was Done

1. **Reset to one value after a delete.** `remove()` in `app/components/configuration/ConfigurationEditor.vue` now sets `original` and `draft` to `{}` instead of `undefined`. The `DocumentEditor` shows `{}` and CodeMirror reports it back; that echo now equals the base, so it is not a change.
2. **Nothing stored means nothing to patch.** First implemented as gating `dirty` on `noOwn`/`missing`. After PR review that was changed: `dirty` compares `draft` and `original` only, so an edit is still guarded by the prompt when the own content answers 404 (for example, deleted by someone else). Instead `save()` returns when `noOwn` or `missing`, so ⌘S cannot send a patch that would fail. The reset in point 1 alone fixes the reported bug.
3. **Unit test.** `test/unit/configuration/configuration-editor.test.ts` has a new case, *has no unsaved changes after deleting the own content (424-157)*. It clicks **Delete own**, types the key into the dialog, confirms, and checks three things: the mock store has no own content, **No configuration yet** shows, and `hasUnsavedChanges()` is `false`.
   - Deviation from the plan: the `CodeEditorStub` in `test/helpers.ts` did not echo external changes the way CodeMirror does, so the new test passed even without the fix. The stub now emits `update:modelValue` when its `modelValue` changes from outside, like the real `CodeEditor` (its `updateListener` reports every `docChanged`). With this stub the test fails on the old code and passes with the fix. The whole unit suite (99 files, 1465 tests) passes with the changed stub.
4. **The UI automation tests** (`DiscardIfShownAsync` in CFG-CRE-04 and CFG-INH-01) are on the `feat/424-152` branch (PR #155), not on `main`. They are not changed here. Their workaround can be removed there once this fix is merged.
5. **Documentation:** no user docs changed; they already describe the expected behaviour.

`SchemaDetail.vue` (lines 132–133) and `TemplateDetail.vue` (lines 160–161) still reset to `undefined` after a delete. The issue names that as the related schema/template bug, which is tracked on its own and is out of scope here.
