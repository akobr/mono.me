# Phase 4 of Soft-delete and Audit of Annotations

## Overview

Phase 4 covers the Administration UI (sections 4.1 to 4.7) of [Soft-delete and audit of annotations](../../../Storyteller/specs/2026-10-11%20Soft-delete%20and%20audit%20of%20annotations.md), Linear 424-6. It builds on the API of Phase 2 and the regenerated TypeScript SDK (`open.api.v0.9.1.json`).

- **Annotation detail:** two tabs, **Annotation** and **History**. The configuration tabs moved to the configuration detail, and their old paths redirect there.
- **Detail header:** a configuration link drawn by where the configuration comes from, and the created and updated stamps.
- **Deleting:** a soft delete. The confirmation no longer asks to type the key, and the toast offers **Undo**.
- **Deleted screen:** restore, restore without descendants, and permanent delete for Administrators.
- **Creating:** a new annotation over a deleted key, or below a deleted ancestor, asks first: restore and update, or create new.
- **Mock API:** it mirrors the lifecycle and the audit of the API, so `npm run dev:mock` and the end-to-end suites work offline.

## What Was Done

### 4.1 Detail tabs

- **`AnnotationDetail.vue`:**
  - The tabs are **Annotation** and **History**.
  - The `vN` badge and the **Open configuration** menu item are gone.
  - The header has `ConfigurationLink` and the line "Created … · Updated … by …" from `Created` and `Modified`. The line links to History, and the update part is hidden while `Modified` equals `Created`.
  - With `deleted`, the header hides the tabs, the configuration link and Delete.
- **Routes:**
  - New: `annotations/[key]/history.vue` and `graph/[key]/history.vue`.
  - The six removed tab files are redirect pages (`definePage` `redirect`) to the configuration detail. `configuration` maps `?cfg=effective|resolved` to the same tab there, and `versions` keeps `?v=` and `?from=`.

### 4.2 Configuration link

- **`utils/configuration-origin.ts`:** `configurationOrigin` returns one of four origins:
  - `own`, with the version;
  - `inherited`, from ancestors in merge order and from type templates;
  - `none`;
  - `unknown`, while the summaries load.

  `describeInheritance` writes the tooltip text, for example "rst.invoicing and the Usage template".
- **Template types:** they come from `useGetTemplates`, through the new `useTemplateTypes` in `queries/scope.ts`.
- **`ConfigurationLink.vue`:** the looks of the spec.
  - Own content is solid ("Configuration · v7"), inherited content is outline, nothing is ghost and muted, and an unknown origin is a plain ghost button.
  - The tooltip for `none` reads "Create a configuration" for Contributors.
  - `data-origin` carries the state, for the tests.
- **Catalog:** `catalog.refreshConfigurations()` reloads only the configuration summaries after a delete or a restore.

### 4.3 History tab

- **`AnnotationHistory.vue`:**
  - It uses `useAnnotationHistory` (`useGetAnnotationHistory`) and shows the entries in a `UTimeline` inside a `section` named "History".
  - Each entry shows its time (relative, absolute in a tooltip), the action badge, the actor, the cause ("with …" or "for …"), the descendant count and the changes.
  - A `Recreated` entry has a "New lifetime" separator above it.
  - A configuration save links "Configuration saved as v7" to `configurations/{key}/versions?v=7`.
  - The footer says when the history was trimmed.
- **`utils/audit.ts`:** the presentation, as pure functions.
  - `auditChanges` turns an entry into change rows: fields with both values (cut values end with `…`), `Description changed`, label chips, values, and objects or arrays only as "object changed".
  - Also `actorName`, `causeText`, and the action labels, colors and icons.
- **`AuditActorLink.vue`:** how the actor appears.
  - An account links to `/{org}/{project}/members?highlight={id}` for Administrators. For everyone else it shows the name, with the id in a tooltip.
  - A machine links to its page for Contributors; System is plain text.
  - `MembersTable` takes `highlight`: the row is marked and scrolled into view.
- **`DeletedAnnotationBanner.vue`:**
  - It reads "Deleted on … by … with N descendants. Purged automatically on …", or "together with …" for a cascaded key.
  - It offers **Restore**, with **Restore without descendants** in its menu, for Contributors.
- **Invalidation:** the history query sits under the annotation's path, so saves, deletes and restores refresh it. `invalidateConfiguration` now also refreshes the history.

### 4.4 Delete

- **`deletedWith`:** every key that has the key among its ancestors, for all seven types. It is used only for the confirmation text.
- **`useAnnotationActions`:**
  - `removeMany` returns `{ failed, deletions }` and drops exactly the returned `DeletedKeys` from the catalog.
  - `restoreMany` sends 100 keys per request, puts the restored annotations into the catalog (`catalog.upsert`) and returns the restored keys, the created ancestors and the failures.
  - `purgeMany` runs four at a time and treats a `404` as already purged.
  - `undoKeys` picks the roots of the deletions made now.
- **Confirmation:** the detail and the bulk bar say "… move to Deleted, with their configuration. You can restore them from Deleted for 90 days." Nothing has to be typed.
- **`useDeletionToast`:** the toast after a delete.
  - It shows the descendant count and "Restorable from Deleted until …", the date taken from the returned `PurgeAt`.
  - **Undo** restores every root of the batch in one `restoreAnnotations` request, and the detail navigates back to the key.

### 4.5 Deleted screen and creating over deleted keys

- **`pages/[org]/[project]/[view]/deleted.vue`:**
  - **Entry points:** a sidebar entry "Deleted" (`i-lucide-archive-restore`, `editRole: 'Contributor'`). The command palette lists it with the other pages.
  - **List:** loaded with `collectAll` over `getDeletedAnnotations`, under the SDK's query key, so `invalidateDeleted` refreshes it.
  - **Columns:** Key, Title, Deleted, By, Descendants and Purged. Purged is relative, and red under 3 days.
  - **Show cascaded** (`?cascaded=1`) lists every deleted annotation, with a Root column.
  - **Row menu:** Show details, Restore, Restore without descendants, and Delete permanently (`useCan('manageAccess')`). Delete permanently confirms by typing the key.
  - **Bulk bar:** restores one request per 100 keys, or deletes permanently key by key, with progress.
  - **Row detail:** a `USlideover` with the read-only JSON and the History.
  - **Deviations:** a plain table, like Certificates and Machines, instead of `UTable`. On phones, Title and Descendants are hidden.
- **`AnnotationDetailPanel.vue`:** when `GetAnnotation` answers `404`, it asks `useDeletedAnnotation`. A deleted key shows the History with the banner; an unknown key still shows "Annotation not found".
- **`useDeletedKeyCheck` and `DeletedKeysDialog.vue`:**
  - `check(keys)` looks up the keys and their ancestors (`getAllAncestors`), 100 per request. With no deleted key it answers `none`; otherwise it waits for the dialog's choice. Closing the dialog cancels.
  - The dialog lists type, key, title, when and by whom each key was deleted, and the descendants. It offers **Restore and update**, **Create new** and **Cancel**.
- **`AnnotationCreateDialog.vue`:** **Restore and update** restores the deleted keys with `Descendants: false`, then writes the annotation. **Deviation:** for a restored key, the dialog's title is written over the restored content instead of replacing it. The create dialog has only a title, and replacing would drop the description, labels and values that the restore brought back.

### 4.6 Validation

- **`forms/annotation.schema.ts`:**
  - It takes the limits from the generated constants (`setAnnotationBodyTitleMax`, `…DescriptionMax`, `…DocumentationLinkMax`), exported as `annotationLimits`.
  - Its messages: "At most 200 characters.", "At most 10,000 characters.", "At most 2048 characters."
- **`AnnotationForm.vue`:** shows a character counter above 90 % of the description limit. A `400 InvalidAnnotation` goes to the named fields through `setFieldError`; `invalidAnnotationFields` splits the server's message into sentences per field.
- **Deviation:** the form had no server error mapping before; it showed a toast only. The toast stays.

### 4.7 Mocks and tests

**Mock API:**

- **`mock/db.ts`:** the overrides have `trash` (soft-deleted annotations with their `Deletion`) and `audit` (entries per key).
- **`mock/audit.ts`:** entries, the mock actor, and `diffAnnotations`. The diff works like `AnnotationAuditDiff`: field values, the description flag, labels, and values by kind, cut at 256.
- **`mock/annotation-handlers.ts`:**
  - soft delete with the cascade and a repeated delete;
  - the deleted list (`roots`, `deletionId`);
  - one deleted annotation, the lookup, and the restore of many keys (ancestors first, children of a key restored alone become roots);
  - purge (Administrator, `409 AnnotationNotDeleted`, drops the audit and the configuration);
  - history (a synthetic `Created` by System for seeded keys);
  - writes that re-create deleted keys and ancestors (`Recreated`, configuration cleared);
  - `400 InvalidAnnotation` for the three limits.
- **`mock/configuration-handlers.ts`:** configurations of deleted annotations are hidden. Reads answer `404 AnnotationDeleted`, writes `409 AnnotationDeleted`, and saves and clears add audit entries.
- **`mock/annotations.ts`:** the seeded annotations carry System stamps, 30 days old.

**Tests:**

| Suite | Result |
| --- | --- |
| Vitest | 171 pass (157 before). <ul><li>New `test/unit/annotations/soft-delete.test.ts` covers: `configurationOrigin` (own, inherited from ancestors and templates, none, unknown); `auditChanges` and the actor and cause texts; the limits and `invalidAnnotationFields`; the mock lifecycle (cascade, roots, lookup, restore alone, purge role and `409`, history `404` after purge); `useDeletedKeyCheck` with `DeletedKeysDialog` (none, the key deleted, only ancestors deleted, Create new, Cancel); `AnnotationHistory` (every action, cause, labels, configuration link, actors, the deleted banner); `ConfigurationLink` in its three states; the redirects of the removed tabs.</li><li>`deletedWith` follows the new rule.</li></ul> |
| Playwright | 59 pass, desktop and mobile, run in the installed Chrome (`PLAYWRIGHT_CHANNEL=chrome`). New `test/e2e/deleted.spec.ts` covers: delete and Undo; Deleted with restore, and restore without descendants; create over a deleted key with Restore and update (`Restored` in History) and with Create new (`Re-created`); permanent delete only for Administrators; History with Created and Updated and the actor; the configuration link in its three states; axe on Deleted. |
| Visual | New baselines from the Playwright Linux container: the annotation detail and the new History and Deleted screens. The desktop annotations, template and schemas baselines changed only by the new sidebar entry. 40 pass. |

`npm run lint` and `npm run build` pass.

**Not covered:** the create flow over a deleted ancestor is covered by the unit tests, not end to end, because the part fields of the create dialog are comboboxes with free entry.

### Documentation

- [admin-ui.md](../../../../42for.net/platform/admin-ui.md) (public site): the two detail tabs, the configuration link, the stamps, "Deleting and restoring", and creating over deleted keys.
- `src/Platform/ui.admin/README.md`: the lifecycle of the mock API and the new end-to-end flows.
