# Phase 3 of the Access Audit Trail: Admin UI

Linear: [424-263](https://linear.app/424fun/issue/424-263/record-an-append-only-audit-trail-for-role-membership-and-machine).

## Overview

This review covers phase 3 of the spec [Append-Only Access Audit Trail](../../../Storyteller/specs/2026-11-15%20Append-Only%20Access%20Audit%20Trail.md), section 8: the organization's **Audit** page of the admin UI, its query, the mock API, the tests and the docs. Commit: `d6f485ca9`. It was implemented as planned, with the deviations below. The recording and the read API are reviewed in [phase 1](../../../Storyteller/specs/reviews/2026-11-15%20Phase%201%20of%20the%20Access%20Audit%20Trail%20-%20Recording.md) and [phase 2](../../../Storyteller/specs/reviews/2026-11-15%20Phase%202%20of%20the%20Access%20Audit%20Trail%20-%20Reading.md).

## What Was Done

### 8. The admin UI

- **Page**: `app/pages/[org]/audit.vue` (the project name `audit` is reserved, so the path hides no project). `useCan('manageAccess', 'org')` gates it: below Administrator of the organization itself an `EmptyState` "Administrators only" and no request; a role on a project alone is not enough, as for the API.
- **Trail**: `app/components/access/AccessAuditTrail.vue`, modelled on `monitor/AuditTrail.vue`: a toolbar with Project ("Every project" and the organization's projects, also those seen in the loaded records), Action (readable labels), Target id (sent 300 ms after typing stops), From and To (inclusive) and the number of loaded records; a table with Time, Action (a created access point reads "Organization created" or "Project created"; the detail is cut to two lines), Project ("Organization" for its own records), Target (kind icon, name and id), Change (`AccessAuditChange.vue`: roles as badges, other values cut to a line, JSON, long values and long details in full, indented, in a popover), Actor (`AuditActorLink`, with "via mcp" when `Via` is set) and Correlation (8 characters and a copy button); Load more with the continuation token and the generation guard of `AuditTrail.vue`; loading, error with Retry, and an empty state that names the two-year retention. The table and the popover carry `ph-no-capture`.
- **Helpers**: `app/utils/access-audit.ts` (labels, the project of a key, the value helpers); `useAccessAudit` in `app/queries/scope.ts` (`meta.silent`, enabled only at the role).
- **Navigation**: "Audit" after Members in the organization section, for Administrators. The navigation test's check against an `audit` entry is narrowed to the other sections, with new cases for the organization section by role.
- **Invalidation**: `invalidateAccess` and `invalidateKeyValuePolicy` refresh the organization's trail. **Deviation:** `invalidateProject` does too, because a description change is recorded.
- **Mock**: `app/mock/access-audit.ts` (`accessAuditOf`, `recordAccessAudit`, `recordAccessChange`, `queryAccessAudit`, seeds for `acme` of the legacy datasets and `fabrikam` of the showcase), the GET handler in `access-handlers.ts` (filters, the 403 and 400 rules, 100 per page), and records from every mocked access write: member role and removal, invitation accept, machine authentication, machines, rules, issuers and certificates (`access-handlers.ts`), access points and descriptions (`handlers.ts`, two records for a new organization), and the key/value policy when its access view changes (`key-value-handlers.ts`; verify and rotate record nothing). A change that changes nothing records nothing, and no value holds a secret.

### 9. Tests

- Unit: `test/unit/access/access-audit-page.test.ts` (11: rows, filters in the request, paging that appends and stops, no request below Administrator), `access-audit-handlers.test.ts` (15, the mock), `test/unit/utils/access-audit.test.ts` (7) and the navigation cases. The whole run: 110 files, 2,001 tests.
- E2E: `test/e2e/access-audit.spec.ts` (3 tests on desktop and mobile, among them the action filter on `/acme/audit`), the page in the axe screens (light and dark); 408 passed, 14 skipped by the tests themselves.
- Visual: the page joins the screens list (4 new baselines, as monitor and export-import did), and the desktop baselines of the 15 `acme` screens in both themes were rewritten for the sidebar entry (30 files) in the Playwright Linux image. The entry alone stays inside the 0.2 % tolerance, so `--update` rewrote only the failing `configurations-desktop-*` (already out of date: it lacked the "New" button); the baselines were written with `--update-snapshots=all`, and the 6 that had drifted for other reasons were reverted. The compare in Docker passes, 72 of 72.

### 10. Documentation

`src/Platform/ui.admin/README.md` (the summary, the mock, the e2e list, `ph-no-capture`, Roles and an "Access audit" bullet of the architecture) and `docs/42for.net/platform/admin-ui.md` (an "Audit (organization)" row).

## PR review

From the reviews of PR #202: the sentinel of "every project" and "every action" is `*`, which no project name can be, so a project called `all` can be filtered; a new first page (a refetch after a change) starts a new generation, so a page of Load more still loading for the old one is dropped; the mock pages by the last record of the page (time and id) instead of an offset, so records written between pages shift nothing; and the mock's issuer value has a thumbprint of the key set, so a replaced key is a change, as in the API. The page notes the default retention, its setting and the DbCreator run of older deployments.

## Notes

- The API's `project` narrows to one project but cannot ask for the organization's own records alone, so the Project filter has no "organization only".
- `target` matches the exact id, so the field is "Target id"; a machine target has no name, only its id. `Count` is the size of the page, so the toolbar counts the loaded records, not the total.
- On this machine's Node 26, `npm test` needs `NODE_OPTIONS=--no-experimental-webstorage`: its built-in `localStorage` hides happy-dom's, and 16 tests of 4 files this change does not touch fail without it. The CI's Node is not affected.
