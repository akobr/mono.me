# Bulk Delete Toast Counts the Deleted Annotations

Linear: [424-156](https://linear.app/424fun/issue/424-156/admin-ui-bulk-delete-toast-reports-too-few-deleted-annotations) — Admin UI: bulk delete toast reports too few deleted annotations.

## Overview

This review covers the whole spec [2026-11-02 Bulk Delete Toast Counts the Deleted Annotations](../2026-11-02%20Bulk%20Delete%20Toast%20Counts%20the%20Deleted%20Annotations.md), delivered in one phase. Deleting two selected annotations from the bulk bar on **Annotations** now shows "2 annotations deleted" (it showed "1 annotations deleted"). The bar counts from a snapshot of the keys taken before the delete starts, the confirmation dialog keeps its count while the delete runs, and the texts of the bar use the singular for one annotation.

## What Was Done

### 1. Count from a snapshot of the keys

`AnnotationBulkBar.vue`, `deleteSelected()`: `const keys = [...props.keys]` is taken before `busy` is set and `actions.removeMany(keys)` runs; `deleted = keys.length - failed.length`. The comment next to the snapshot names the reason: `removeMany` drops each deleted key from the catalog, and the page's `selectedKeys` follows the catalog, so the prop shrinks during the run.

### 2. Keep the dialog count steady while deleting

`deleting = ref<number>()` is set to `keys.length` with the snapshot and cleared in `finally`. A computed `deleteCount` (`deleting ?? props.keys.length`) feeds the dialog title, description and confirm label (`Delete ${formatter.format(deleteCount)}`). The "N selected" label of the bar still follows the live selection, as planned.

### 3. Singular and plural

A local `annotations(value)` returns "1 annotation" or "N annotations" with the formatted count. It is used by the success toast ("1 annotation deleted", "2 annotations deleted"), the warning toast ("1 annotation could not be deleted") and the dialog title ("Delete 1 annotation?").

Deviation: for one annotation the dialog description reads "The annotation and its descendants move to Deleted, …" instead of "The 1 annotation and its descendants …", which reads better; more than one keeps "The 2 annotations and their descendants …".

`useDeletionToast` ("N annotations were not restored", "N annotations restored") and the Deleted page have the same plural pattern; they are out of the scope of this issue and were left unchanged.

### 4. Tests

`test/unit/components/annotation-bulk-bar.test.ts` (Vitest, happy-dom) mounts the bar inside `UApp` as the page does (rendered only while the selection is not empty). `useAnnotationActions` is mocked with a `removeMany` that removes each deleted key from the selection between awaits, as the catalog does; `useDeletionToast` is mocked to record its title and deletions. Three cases:

- two keys → `removeMany` gets both, the toast says "2 annotations deleted" with two deletions;
- one key → dialog "Delete 1 annotation?", toast "1 annotation deleted";
- one of two failing → warning toast "1 annotation could not be deleted" (read from the rendered toaster) and "1 annotation deleted".

All three fail on the previous component ("1 annotations deleted", "Delete 1 annotations?") and pass with the fix.

Lint (`npm run lint`) is clean and `vue-tsc` on `tsconfig.vitest.json` reports no errors. The full Vitest run in the fresh worktree had 67 failures in 16 unrelated files (access, federation, configuration, modeling, build prebundling, history loading) whose number varied between runs (77, then 67); none of them uses the bulk bar.

The UI test ANN-CAT-02 (`AnnotationCatalogTests.AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo`) lives on `feat/424-152` (PR #155) and is not on this branch. Its accepted text `^[12] annotations deleted$` should become `2 annotations deleted` once both branches are on `main`.

### 5. Documentation

No user documentation describes these texts; the spec and this review are the only documents.
