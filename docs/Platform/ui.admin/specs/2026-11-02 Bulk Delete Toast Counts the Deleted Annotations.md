# Bulk Delete Toast Counts the Deleted Annotations

Linear: [424-156](https://linear.app/424fun/issue/424-156/admin-ui-bulk-delete-toast-reports-too-few-deleted-annotations) — Admin UI: bulk delete toast reports too few deleted annotations.

## Problem

On **Annotations**, selecting two rows, clicking **Delete 2** in the bulk bar and confirming deletes both annotations, but the toast says "1 annotations deleted" (Undo still restores both). Depending on timing it can say "0" or show no toast at all. The texts of the bar also ignore the singular ("1 annotations deleted", "Delete 1 annotations?").

## Current State

- `src/Platform/ui.admin/src/app/components/annotations/AnnotationBulkBar.vue`, `deleteSelected()`:
  - line 39 calls `actions.removeMany(props.keys)`;
  - line 49 computes `const deleted = props.keys.length - failed.length` after that call.
- `removeMany` (`composables/useAnnotationActions.ts`, line 146) removes every deleted key from the catalog (`catalog.remove`) as each request finishes.
- The page (`pages/[org]/[project]/[view]/annotations.vue`, line 114) passes `selectedKeys`, a computed that keeps only the selected keys still in `catalog.byKey`. So `props.keys` shrinks while the delete runs, and by line 49 it no longer holds the keys that were deleted. When every key is gone, `v-if="selectedKeys.length && canEdit"` unmounts the bar.
- The confirmation dialog shows `count` (from `props.keys`), so its title and button ("Delete 2 annotations?", "Delete 2") also shrink during the run, while the dialog shows its loading state.
- The texts use "annotations" for any count: the success toast, the warning toast ("1 annotations could not be deleted") and the dialog.
- The UI test ANN-CAT-02 (Testiny 114), `AnnotationCatalogTests.AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo` on branch `feat/424-152` (PR #155, not merged yet), accepts `^[12] annotations deleted$` because of this bug.

## Proposed Changes

### 1. Count from a snapshot of the keys

In `deleteSelected()` take `const keys = [...props.keys]` before `removeMany`, pass `keys` to it and compute `deleted = keys.length - failed.length`. Nothing read after the `await` depends on `props.keys`.

### 2. Keep the dialog count steady while deleting

Add `deleting = ref<number>()`, set to `keys.length` when the delete starts and cleared in `finally`. The dialog shows `deleting ?? props.keys.length`, so it keeps "Delete 2 annotations?" until it closes. The "N selected" label of the bar keeps following the live selection.

### 3. Singular and plural

A local helper `annotations(count)` returns the formatted count with "annotation" or "annotations". It is used in:

- the success toast: "1 annotation deleted", "2 annotations deleted";
- the warning toast: "1 annotation could not be deleted", "3 annotations could not be deleted";
- the dialog title and description: "Delete 1 annotation?", "The 1 annotation and its descendants …" / "The 2 annotations and their descendants …".

The confirm button stays "Delete N".

### 4. Tests

A Vitest component test `test/unit/components/annotation-bulk-bar.test.ts` mounts `AnnotationBulkBar` with a mocked `useAnnotationActions` whose `removeMany` shrinks the `keys` prop (as the page does) and a mocked `useDeletionToast`, and checks:

- two deleted keys → "2 annotations deleted" with both deletions passed to the toast;
- one deleted key → "1 annotation deleted";
- one of two failing → a warning "1 annotation could not be deleted" and "1 annotation deleted".

The UI test ANN-CAT-02 on `feat/424-152` should be tightened to "2 annotations deleted" once both branches are on `main`; that change belongs to PR #155, which is not on this branch.

### 5. Documentation

No user documentation describes the toast texts; only the review is added.
