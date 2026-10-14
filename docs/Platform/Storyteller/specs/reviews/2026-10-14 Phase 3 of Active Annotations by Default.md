# Phase 3 of Active Annotations by Default

## Overview

Phase 3 covers section 5 of [Active Annotations by Default](../2026-10-14%20Active%20Annotations%20by%20Default.md), Linear 424-16: the admin UI (`src/Platform/ui.admin`) and its mock API. The API now lists only active annotations by default ([Phase 1](2026-10-14%20Phase%201%20of%20Active%20Annotations%20by%20Default.md)). So the admin UI asks for the inactive ones wherever it shows every status. Without that, the grid, tree, graph and `is:` filters would have lost the disabled, scheduled and expired annotations.

## What Was Done

### Catalog and detail

- **Catalog.** `app/stores/catalog.ts` builds the params of every page with `pageParams(token)`, which is `{ continuationToken, includeInactive: true }`. That covers the seven per-type streams and the `GetAnnotations` fallback.
  - It doesn't send `includeDeleted`, because deleted annotations keep their own page (**Deleted**).
  - The IndexedDB snapshots need no migration: they were written before the change, when the API returned every status.
- **Detail.** `useAnnotation` in `app/queries/scope.ts` passes `{ includeInactive: true }` as the new `params` argument of `useGetAnnotation`. Without it, the detail of a disabled annotation would answer `404 AnnotationInactive`.
  - The Vue Query key now ends with that params object.
  - `invalidateByPath` and `removeByPath` match by prefix, so `invalidateAnnotation` and `forgetAnnotation` still reach the query.
- **No other reads changed.** Nothing else in the admin UI reads annotation lists or single annotations. The lookup of deleted keys, restore, purge and history have their own routes.

### Mock API

`app/mock/annotation-handlers.ts`:

- **`includesOf(request)`** reads `includeInactive` and `includeDeleted` from the query string.
- **`readableOf(db, params, request, code?)`** builds a list the way the API does:
  - it adds the trash of the scope with `includeDeleted`;
  - without `includeInactive`, it keeps only annotations whose `annotationStatus` is `active`;
  - so a deleted annotation that is not active needs both flags.

  The per-type lists and `GET …/annotations` use it.
- **`GET …/annotations/:key`:**
  - with `includeDeleted`, it falls back to the trash;
  - it answers `404 AnnotationInactive` ("The annotation '…' is disabled; read it with includeInactive=true.") for an annotation that is not active, unless `includeInactive` is set.

The seeded mock data already contains disabled, scheduled and expired annotations. Mock mode therefore shows straight away whether a screen forgets the flag.

### Documentation

`docs/42for.net/platform/admin-ui.md`, section **The catalog**, now says three things:

- the catalog shows every status;
- the API returns only active annotations unless asked, so `is:active` shows what services see;
- deleted annotations are on their own page.

### Tests

| Suite | Result |
| --- | --- |
| `npm test` | 311 pass (308 before). `catalog.test.ts`: the seven lists are asked with `includeInactive=true` and without `includeDeleted`. `active-annotations.test.ts` (new) checks that the mock API reads like the API: the default list has only the active subject; `includeInactive` adds the disabled, scheduled and expired ones; `includeDeleted` adds the deleted one; `GET` of a disabled key answers `404 AnnotationInactive` without the flag and returns it with the flag; a deleted key needs `includeDeleted`. |
| `npm run typecheck`, `npm run lint` | pass |
| `npm run test:e2e` | 79 pass and 3 skipped (the existing desktop- or mobile-only skips), against the mocked API on desktop and mobile. |

The visual suite (`test:visual:docker`) was not run. The screens render the same statuses as before, so no screenshot should change.
