# Phase 5 of Restore a Previous Version

## Overview

Phase 5 of [Restore a Previous Version of a Configuration, Template or Schema](../2026-11-10%20Restore%20a%20Previous%20Version%20of%20a%20Configuration,%20Template%20or%20Schema.md) (Linear 424-221): **Restore** in the version history of the admin UI for configurations, templates and the three schema kinds, and the mock API that answers the restore routes as the server does. The spec's section 9, a nice-to-have of the issue; the audit labels came with phase 2.

## What Was Done

### 9. Admin UI

- **Version history** (`components/versions/`):
  - `VersionSource` has `document` (`configuration`, `template` or `schema`, for the wording), an optional `restore(version, force?)` and an optional `afterRestore(result)`; `versionExpired` and `versionsQueryKey` are shared helpers.
  - `VersionTimeline` shows **Restore** (`Restore vN`) on every version that is not current and not expired, and "restored from vN" on a version with `RestoredFrom`.
  - `VersionsView` offers it only when the source can restore and the user may edit (`useCan('edit')`), also as **Restore this version** for the open version. It asks first (`useConfirm`, "Restore vN?"), toasts "Restored vN as vM" or "Nothing changed" (the version is current, or the current version has its content), then refreshes through `afterRestore` or the source's query key. A `409` with schema errors shows the non-complying configurations with **Restore anyway**, which confirms again and retries with `force`, as **Save anyway** does. Any other error toasts "vN was not restored".
- **Sources:** `ConfigurationVersions.vue` (`restoreConfigurationVersion`; `afterRestore` updates the catalog's summary and awaits `invalidateConfiguration`, which refreshes the annotation history too), `TemplateDetail.vue` (`restoreTemplateVersion`, then the template save's refresh), `SchemaDetail.vue` and `utils/schema-targets.ts` (the three schema restores with `force`, then `invalidateSchemas`).
- **A deleted document** keeps its Versions tab when it has versions, with a note above them, so the deleted configuration, template or schema can be brought back; without versions the empty state stays.
- **Not in the spec:** a soft-deleted annotation's configuration versions answer `404 AnnotationDeleted`. The first cut showed them as an error with a retry that could never succeed; the branch's review found it, and the tab now says that the annotation is deleted and has to be restored first, with a link to it (`fix(platform): explain a deleted annotation in the configuration versions of the admin UI`).
- **Mock API** (`app/mock/`):
  - `documents.ts`: a deleted document keeps its versions (`deleted`), `getDocument` hides it from the lists and current reads while `getHistory` returns it, `putVersion` continues the numbering after a delete and records `RestoredFrom`, and `planRestore` applies the no-op rules.
  - `configuration-handlers.ts`: `POST …/configuration/:key/versions/:version/restore` with the server's rules (Contributor; `409 AnnotationDeleted`, `404 AnnotationNotFound`, `404 VersionNotFound`; the schema check unless `force`, skipped for an empty version as the API skips it; `RestoredFrom`; `ConfigurationRestored` in the audit). The versions list of a soft-deleted annotation answers `404 AnnotationDeleted`, as the API does.
  - `template-schema-handlers.ts`: the restore route in `versionedHandlers` for the template and the three schema kinds (the pair rule first, the annotation check of the keyed kinds, the compliance check unless `force`). The mock does not check that a restored schema version is a usable schema; the server answers `400` for that.
- **Tests:** `test/unit/configuration/version-restore.test.ts` (22: the mock's rules, the deleted documents brought back, the schema kinds, the button's visibility, the confirmation, the toasts, the force path, the errors, a Reader, the deleted annotation's state), `soft-delete.test.ts` (`auditChanges` with `FromVersion`, the history line), and the e2e flow "restores a previous version as the newest one". `npm run typecheck`, `npm run lint` and `npm test` (1554) pass, and the e2e suite passed on desktop and mobile.
- **Docs:** `docs/42for.net/platform/admin-ui.md` (Versions and History) and the mock and e2e notes of `ui.admin/README.md`.
