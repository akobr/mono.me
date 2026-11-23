# Phase 3 of Inbox of a View

Linear: [424-31 Prepare notification center (inbox) in Admin UI](https://linear.app/424fun/issue/424-31/prepare-notification-center-inbox-in-admin-ui).

## Overview

Phase 3 delivers the administration UI of [Inbox of a View](../../../Storyteller/specs/2026-11-22%20Inbox%20of%20a%20View.md): the page **Inbox** of the view section, the mark of open items in the menu, the tab that dismisses the item of a job it already showed, the mock API, their tests and the living documentation (spec section 7 with the tests of 7.7, and the phase 3 rows of section 8). The back end is reviewed in [phase 1](../../../Storyteller/specs/reviews/2026-11-22%20Phase%201%20of%20Inbox%20of%20a%20View.md), the API and the SDKs in [phase 2](../../../Storyteller/specs/reviews/2026-11-22%20Phase%202%20of%20Inbox%20of%20a%20View.md). In a deployment the page shows the ends of exports and imports to Administrators. No producer suggests an action yet, so nothing there can be accepted until 424-8.

| Commit | Subject |
| --- | --- |
| `d625c44b5` | feat(platform): mock the inbox in the admin UI (424-31) |
| `815b0f86c` | feat(platform): Inbox page of the view section in the admin UI (424-31) |
| `a97e9c927` | test(platform): the Inbox entry in the visual baselines and the UI automation tests (424-31) |
| `5c8108ab4` | docs(platform): describe the Inbox page (424-31) |

The code was written in three steps: the mock API, then the page with the mark and the toaster, then the visual baselines and the UI automation suite. A review step and a fix step followed the second step, before the commits (see [Reviews](#reviews)). No code of phases 1 and 2 was changed: the phase touches `src/Platform/ui.admin/src` and `src/Platform/UI.Admin.AutomationTests`, and the documentation.

`UI/` stands for `src/Platform/ui.admin/src/`, as in the spec.

## What Was Done

### 7.1 Menu and routes

As planned, with these details:

- `UI/app/utils/navigation.ts`: **Inbox** (`i-lucide-inbox`, `…/{view}/inbox`) is the sixth and last entry of the view section, with `editRole: 'Contributor'` and `lockedHint: 'Contributors only'`. Every role sees it; a Reader sees it with the lock.
- `UI/app/utils/context-paths.ts`: `'inbox'` is the last of `viewSections`. A switch of the view or the project keeps the page and drops its `?tab=`. `inbox` is not a reserved name of a view: the page is the fourth segment of the path.
- `g-i` in `UI/app/components/shell/CommandPalette.vue`, and **Inbox** in the group **Go to** of `UI/app/composables/useShortcutsHelp.ts`. The shortcut opens the page of the view the menu shows, for every role. The palette needed nothing else: it lists every entry of `buildNavigation`, a locked one included, and passes no marks.
- `UI/app/pages/[org]/[project]/[view]/inbox.vue` is the page. `typed-router.d.ts` (the route) and `components.d.ts` (the two components) are regenerated and committed. `auto-imports.d.ts` was rewritten with other line endings only, so the commit has no change of it.

### 7.2 The mark of open items

As planned, with these details:

- `buildNavigation(context, marks?: NavigationMarks)` sets `marked: true` on the Inbox entry when `marks.inbox` is true, and on no other entry; `NavigationEntry.marked` is only there when it is true.
- `UI/app/components/shell/AppSidebar.vue` reads the open items of the view the menu shows with `useViewInbox(navigation, 'Open', { enabled: canAsk })`. `canAsk` is the Contributor role on the project. The entry is marked when `canAsk` holds, the query is not in error and `OpenCount` is above zero.
- `marked` becomes `chip: true` of the menu item: a dot on the icon, in the expanded sidebar, the icon rail and the menu of a phone. The text of the link stays `Inbox` and its trailing badge stays the lock.
- **In words.** Nuxt UI passes every `aria-*` key of a menu item on to its link, so the marked entry has `aria-describedby` on a hidden element with the text "Open items" (`#navigation-inbox-mark`). The tooltip "Open items" is set on the entry too (see deviation 9 for where it shows).
- The sidebar never polls. It reads once for the view it shows, and it observes the query the page reads, so the reads of the page and of the toaster update the mark.
- The query is silent (`meta: { silent: true }`). A read that failed (`404`, `403`, `5xx`) or has not answered leaves no mark, and a later read that fails takes the mark away.
- Outside a project the menu has no Inbox entry and nothing is requested. Outside a view the mark is the one of the remembered view, or of `default`.

### 7.3 The page

As planned, with these details:

- **Header.** `AppNavbar` with the title **Inbox** and **Refresh** (deviation 8). The navbar draws a title from 1536 px up inside an organization, as on every page.
- **Tabs.** **Open**, with `OpenCount` as a badge when it is above zero, and **Done**. The tab is in the address (deviation 6).
- **The lock.** Below Contributor: "Contributors only" with "The inbox of the view, with the results of background jobs and the actions the platform suggests, is available to the project's Contributors.", no tabs, no **Refresh**, no request. A `403` of the open items or of the shown list shows the same lock without the tabs, and keeps **Refresh**.
- **States.** "Loading the inbox…"; the shared `ErrorState` with **Retry**, which reads the shown list again ("Not found" for a `404`, "The API failed" for a `5xx`); the empty states "Nothing to do" (`i-lucide-check-check`, with the two texts of the spec by the role) and "Nothing done yet" with "Accepted and dismissed items stay here until they expire."
- **The list.** A bordered list labelled "Open items" or "Done items", at most `max-w-5xl` wide, with `ph-no-capture`. **Load more** appends the next page with the same `state`; a generation counter drops a page that arrives for an older first page. A re-read with the same answer keeps what was loaded, because the query keeps the object of an equal answer; a changed first page, the other tab and another view start the list again.
- **An item** (`InboxItemRow.vue`): the icon of its kind, or of its level for a notification (`i-lucide-info`, `i-lucide-circle-check`, `i-lucide-triangle-alert`, `i-lucide-circle-x`), with the same in words as the `title` of the icon and as a text for screen readers; the title and the text, which break inside a word; the badge **Administrators only**; "25 minutes ago by System" with `RelativeTime` and `AuditActorLink`, and "via …" for an actor; of a closed item "Dismissed by Grace Hopper, 4 hours ago" or "Accepted by …".
- **Buttons** (`inboxOffers`): an open action has **Accept** and **Dismiss**; every other open item **Dismiss**; a notification of an export that succeeded **Download** with "Expires …", open or done; a notification of an import that ran **Show the result**, open or done (deviation 7).
- **Accept** asks first: "Accept this action?" with "`<title>`: the work starts now and runs in the background. Its result arrives in this inbox." **Dismiss** asks nothing. Both are plain SDK calls in a `try`. A set of item ids disables both buttons of an item while its call is on its way, and both lists are read again after every answer.
- **Answers.** `409` `InboxItemClosed`: "The item was already handled". `404`: "The item is gone". `501`: "This action is not available in this deployment". Anything else: the message of the API under "The action could not start" for an accept (deviation 4 for a dismiss).
- **Download** calls `downloadArchive` with the id and the file name of the job. A `404` says "The archive has expired"; another failure "The archive was not downloaded". A set of job ids keeps the spinner per item (review finding 1).
- **Show the result** (`InboxImportResult.vue`): a slideover of `max-w-5xl` with the title of the item, the report read with `getViewImport` and shown by `ImportResult` under "Report", without **Import now**. A `404` closes it with "The report has expired"; another failure shows in the slideover with **Retry**. A report that arrives for another item is dropped.
- **Freshness.** The open items are read again every 20 seconds (`inboxPollMs`), whatever the tab, and not while the query is in error. The Done list is a second query, enabled only while its tab is shown. An answer counts as fresh for 15 seconds (`inboxStaleMs`), so the page opens with the answer of the menu when that is newer.
- **A switch of the view** while the page is open clears the busy items and closes the slideover. The archives being fetched stay tracked, so their buttons are still busy when the person comes back.

### 7.4 Results the tab already showed

As planned, with the differences of deviations 1 to 3:

- `UI/app/components/transfer/TransferToaster.vue` calls `settleInbox(job)` first for every end it hears, also for an import that a page shows and that gets no toast.
- `dismissJobItem(client, scope, jobId)` (`UI/app/queries/inbox.ts`) reads the open items of the job's view, looks for the notification whose `Job.Id` is the job's, looks once more after two seconds (`jobItemRetryMs`) when it is not there, dismisses it and then invalidates the inbox of the view. Every failure is ignored.
- The item is then under **Done**, dismissed by that person, with its **Download** or **Show the result**. The toast, the automatic download and the result on the Export & Import page are unchanged.

### 7.5 Code

As planned:

- `UI/app/queries/inbox.ts`: `useViewInbox(scope, filter, { enabled, poll })` over the generated `useGetViewInbox`, always with an explicit `state` and `meta: { silent: true }`; `invalidateInbox(client, scope)`; `dismissJobItem`; the constants `inboxPollMs`, `inboxStaleMs` and `jobItemRetryMs`.
- `UI/app/utils/inbox.ts`: `inboxAppearance`, `inboxOffers`, `isAdministratorsOnly`, `inboxClosedLabel`, `inboxAcceptConfirmation` and `inboxEmptyState`, pure. Every member of an item is optional in the SDK, so each answers for an item without it.
- `UI/app/components/inbox/InboxItemRow.vue` and `InboxImportResult.vue`. The row emits `accept`, `dismiss`, `download` and `result`; the page runs them.
- `UI/app/stores/transfers.ts`: `downloadArchive(scope, id, fileName)`, which saves under the file name or as `export-<view>.zip`. `download(job)` of a followed job now calls it.
- `UI/app/components/transfer/ImportResult.vue`: the property `title`, "Latest import" by default.

### 7.6 Mock API

As planned, with the differences of deviations 10 to 15:

- `UI/app/mock/inbox.ts`: a port of the ids, the publisher, the service and `ViewTransferNotifications`: `inboxOf`, `pushInboxItem`, `listInbox`, `acceptInboxItem`, `dismissInboxItem`, the tokens, `exportNotification` and `importNotification`, and the seed.
- `UI/app/mock/inbox-handlers.ts`: the three routes, registered in `all-handlers.ts` after the transfer handlers. They check the Contributor role first (`403`), then the `state` and the token (`400` `InvalidQuery`). An item above the role is `404` before its kind and state are looked at.
- `MockDb.inbox` (`UI/app/mock/db.ts`) is optional, keyed `org/project/view` and filled on first use. The storage key of the database is unchanged.
- **The seed**: five items for the scopes of `scopeSeed` in the legacy datasets and for every view of a showcase project; any other view starts empty. Open: the action "Generate a schema for 4 similar configurations" (`SchemaSuggestion` / `GenerateSchema`, Contributors, by System), "Export failed" (Administrators, by another user) and the message "Schema suggestions are on for this view" (Contributors). Done: "Import finished" (dismissed, without a job) and "Generate a schema for 7 similar configurations" (accepted).
- **Accept** runs the action `GenerateSchema` at once: the item is accepted and the notification "Schema generation finished" arrives (topic `SchemaSuggestion`, `Success`, for the role of the accepted item, by System, without a `Job`). Any other action code answers the `501` of the API.
- `UI/app/mock/transfer-handlers.ts` pushes `exportNotification` or `importNotification` at the two places where a mock job ends. The job routes are otherwise untouched.

### 7.7 Tests

As planned, with these details:

- **Unit**, seven new files with 256 tests:

  | File | Tests | Covers |
  | --- | --- | --- |
  | `test/unit/mock/inbox.test.ts` | 91 | The ids, the tokens, the visibility, the `state`, the push, the notifications with the cases of `ViewTransferNotificationsTests`, the seed |
  | `test/unit/mock/inbox-handlers.test.ts` | 42 | The three routes through SDK calls: the seed per role, the refusals, the pages, dismiss, accept, `404` instead of a conflict, the ends of jobs |
  | `test/unit/inbox/inbox-utils.test.ts` | 34 | What an item offers, how it looks, the texts |
  | `test/unit/inbox/inbox-row.test.ts` | 15 | The row |
  | `test/unit/inbox/inbox-page.test.ts` | 40 | The page through `mountScreen` over the mock API: the lists, the locks, the states, accept and dismiss with each answer, the download, the report, **Load more**, a switch of the view, the re-read |
  | `test/unit/inbox/inbox-menu.test.ts` | 20 | The mark, and the menu next to the page |
  | `test/unit/inbox/inbox-toaster.test.ts` | 14 | The toaster and the inbox, and `dismissJobItem` |

- **Unit, changed** (15 more tests): `utils/navigation.test.ts` ("ends the view section of %s with Modeling" became "lists the six entries of the view section of %s, with Modeling before Inbox"; a new group for the Inbox entry, the mark and `g i`), `utils/shell.test.ts` (`inbox` as a section), `transfers/transfer-page.test.ts` (the `title` of `ImportResult`; the toaster test now expects the inbox among the invalidated keys, exactly once) and `transfers/transfers.test.ts` (`downloadArchive`).
- **The answers of a real API that the mock does not give by itself** are tested on top of it. MSW overrides answer a handler's own `409` (`TransferLimit`), the `404` of an accept, and the `404` and the `500` of the list. The `501` is the mock's own answer for an action code it does not run, and the `403` its answer to a session that still says Contributor.
- **End to end**: `test/e2e/inbox.spec.ts` with eight tests on desktop and phone, none of them skipped on a phone (deviation 16). `navigation.spec.ts` lists the six entries and the lock of Inbox per role. `accessibility.spec.ts` has the screen `inbox`, in light and dark. No step waits for the 20 seconds.
- **Visual**: the screen `inbox` in `visual.spec.ts`, and the baselines regenerated in Docker: 4 new (`inbox-desktop-light`, `-dark`, `inbox-mobile-light`, `-dark`) and 30 changed (15 desktop screens in light and dark), of 92. The changed files differ only inside the sidebar, where the new entry moves the Project and Organization sections 32 px down; `members-desktop-light` and `-dark` also differ in three pixels by one colour level. No mobile baseline changed.
- **UI automation suite**: `AppRoutes.Inbox`; the two assertions of `NavigationMenuTests` (cases 24 and 27) expect the six entries; the comments of two more files name `g i` (deviation 17).

### 8. Documentation (the phase 3 rows)

As planned, in `5c8108ab4`, with these details:

- `docs/Platform/Storyteller/inbox.md`: a new section **Administration UI** with the menu, the mark, the page, an item and its buttons, accept and dismiss with a table of the answers and their toasts, **Download** and **Show the result**, the freshness, the results the tab already showed, the code, the mock API with its seed and its simplifications, and the tests of the page. The introduction and the list of code locations name the UI, **Tests** points to the tests of the page, and **Not there yet** keeps only the first suggested action.
- `docs/Platform/Storyteller/export-import.md`: the sentence in **Jobs** that no page reads the inbox is replaced; **Administration UI** got the inbox next to the page, the `title` of `ImportResult`, `downloadArchive`, and the rule of the toaster; **Mock API** the notification of an ended job; **Tests of the page** the changed and the new tests.
- `docs/Platform/Deployment/runbook-azure-production.md` (**Later**): if the running build of the API predates the inbox, the API is published right after the merge that brings the page to `main`, or before it from the branch of the pull request. Until then the menu shows no mark and the page shows its error state.
- `src/Platform/ui.admin/README.md`: the inbox in the opening list, a bullet of Mock mode with the handlers, the seed and the simplifications, a bullet of the end-to-end tests, the screen among those of axe and of the visual baselines, and a bullet **Inbox** under Architecture.
- `docs/42for.net/platform/admin-ui.md`: a section **Inbox** with what a deployment shows today and one sentence that suggested actions come with a later release; `I` in the keyboard table; one sentence in **Export and import**. The suggestion of the mock is not described there.
- `docs/Platform/Storyteller/access.md`: one passage, although its row is phase 2 (deviation 18).

### Test results

Run on the committed tree on 2026-11-22, with Node 26.13 locally:

| Suite | Result |
| --- | --- |
| `npm run lint` | Clean, with the one warning that was there before (`app/mock/key-value-handlers.ts:203`, `no-shadow`) |
| `npm run typecheck` | Clean |
| Unit tests, `NODE_OPTIONS=--no-experimental-webstorage npm test` | 133 files, 2720 tests passed (126 files and 2449 tests before the phase) |
| End to end, the whole suite, `npm run test:e2e` (desktop and phone) | 480 passed, 14 skipped, 4.2 minutes; nothing listened on port 4174 before the run. The skips are the `test.skip` calls for a phone that other specs already had; `inbox.spec.ts` has none. |
| Visual, `npm run test:visual:docker`, run by the step that regenerated the baselines | 92 of 92 without `--update` |
| `dotnet build src/Platform/UI.Admin.AutomationTests/src` | 0 errors |

**Not run:** the UI automation suite, against the mock or a deployment (it needs a target); the test run of the whole solution; the page against a real API, because no deployment has the routes yet. So everything about the answers of a real API (a handler's own `409`, the `501`, the `403`, the `404`) rests on the unit tests over the mock and its MSW overrides.

During the page step, one regression run of the twelve specs outside its own set had one failure: `access-audit.spec.ts:18` on desktop timed out waiting for the Audit link on `/acme`. It did not repeat in three runs of that spec or in a second run of the twelve, and the final run of the whole suite passed. Its trace was overwritten, so the cause is not known.

### Reviews

Two reviewers read the change, and a second reader checked every finding. One reviewer reported four findings and the other one. Three were confirmed and two partly confirmed, and all five were fixed before the commits:

1. **One flag for every download** (a bug; confirmed; fixed). While the archive of one export was being fetched, **Download** on any other item did nothing: no spinner, no toast, and a button that looked enabled. The same flag was cleared by the watcher of the view and by the `finally` of an older download. `inbox.vue` now tracks a set of job ids (`downloads`): `download()` returns only for a job that is already being fetched and removes only its own id, and the watcher no longer clears it. Tests: `fetches the archive of another export while a slow one is still on its way` and `keeps the Download of an archive that is still being fetched off when the person comes back to the view`.
2. **No test switched the view while the page was open** (a gap of the tests; confirmed; fixed, tests only). Without the watcher of `context.scope` every test passed, and the report of view A would have stayed open over view B, with its document links built for B. The new group `a switch to another view while the page is open` has `shows the inbox of the other view: the report of the view before closes, and its late next page is dropped` and `shows the lock in a project where the caller is a Reader, and asks for nothing there`, which also checks that no request follows after `inboxPollMs`.
3. **No test of the guard against a second accept or dismiss** (a gap of the tests; confirmed; fixed, a test only). Without the set of busy ids or its binding every test passed, and a double click on **Dismiss** would have sent two requests and told the person "The item was already handled". `takes no second dismiss and no accept of an item while its dismiss is on its way` holds the dismiss request, expects both buttons of that item disabled and those of another item not, and also emits `dismiss` and `accept` straight from the row, because happy-dom ignores a click on a disabled button.
4. **The mark outlived the role** (a bug; partly confirmed, as a minor consistency fix; fixed). The mark was computed from the cached answer alone. When the session fell below Contributor without a reload, the query was only disabled and kept its answer, so a locked entry showed the dot and "Open items". `AppSidebar.vue` now has `canAsk`, used for `enabled` and required by the mark. Test: `takes the mark away when the session falls below Contributor, although the answer with open items is cached`.
5. **A query parameter given twice** (the mock against the API; partly confirmed, as optional; fixed). The mock read the first value, the API reads all of them joined by a comma: `?state=Open&state=Done` answered `200` in the mock and `400` `InvalidQuery` in the API, and a repeated `continuationToken` likewise. No caller sends a parameter twice. The handler now reads `query.getAll('state').join(',')` and the token the same way. Tests, with a raw `fetch`: `refuses a state that is given twice: the API reads the two as one value with a comma` and `refuses a continuation token that is given twice, although each of the two is one of the inbox`.

The reviewer of the mock compared every other rule with the back end and found them equal: the role before the query, the visibility before the kind and the state, the conflicts and their messages, the expiry, the token, the paging, `OpenCount`, the ids, the keyed push, the cuts, the lifetimes, the notification texts and the seed.

The implementers also broke the code on purpose and restored every file by its checksum. These are their own checks, not a review:

- 25 single-rule mutations of the three mock files: each failed at least one test.
- 76 mutations of the sources of the page step. Two survived the first pass (no count next to a later error, and a list that never goes stale); tests were added for both, and all 76 now fail a test.
- 15 mutations of the fixed or guarded behaviours of the fix step: all were caught.

### Deviations from the spec

1. **The toaster dismisses a job's item only when the end it showed is the server's end**: a job shown as done whose status is `Succeeded`, or one shown as failed whose status is `Failed`. The spec says only that it dismisses the item. When the tab gave up after 15 minutes, could not read the job, or could not download the archive of an export that succeeded, it reported a failure the server does not have, and the open item is what helps.
2. **The inbox is invalidated once, after the dismissal**, not before it. Invalidating first would light the mark for a moment for the job the person just saw. When nothing is dismissed, the tab only invalidates.
3. **`dismissJobItem` looks at the first page of the open items only**, the 50 newest, and only at notifications. A job that just ended is at the top, and an open action that names the same job must stay.
4. **A refused dismiss has its own title**, "The item was not dismissed", with the message of the API. The spec gives "The action could not start" to every other refusal, which is wrong for a dismiss.
5. **An accepted action gets the toast "Action accepted"** with "Its work runs in the background; the result arrives in this inbox."; a dismiss gets none. The spec names no toast for a success. The accepted item leaves the list, and its result arrives later.
6. **The tab is kept in `?tab=done`**, as Monitor keeps its tab, so a reload and a link open the same tab. The spec names no place for it. A switch of the view or the project drops it.
7. **Download and Show the result need more than the spec names**: the kind `Notification`, a level other than `Error`, a job with an id, and a topic and a job kind that agree (`ViewExport` with a `ViewExport` job, likewise for imports). The archive and the report are read by the routes of that kind of job.
8. **Refresh is an icon button** with the tooltip and the accessible name "Refresh", where the spec says a button.
9. **The description of the mark is a hidden element outside the menus**, so `AppSidebar.vue` has more than one root node, and **the tooltip "Open items" shows only in the collapsed rail**. The docked sidebar renders its menu a second time in the slideover of a phone, and an id must stay unique. Nuxt UI renders the tooltips of a vertical menu only when it is collapsed; elsewhere the mark is the dot and the description.
10. **The mock's id of a keyed push is not a SHA-256.** It has the format of the API, and the same key and time give the same id, but its 32 digits come from a small synchronous hash, as in `access-audit.ts`. A real SHA-256 needs the asynchronous `crypto.subtle` inside the synchronous job handlers.
11. **The mock's `state` also takes the numbers `0` and `1`**, as the API does (deviation 2 of phase 2).
12. **The mock filters expired items by `ExpiresAt`** in the list, the count and the lookups, where the API relies on the `ttl` of the store. The mock database has no `ttl`. A keyed push that meets an expired item creates it again.
13. **The mock has no reopening and no lost-replace retries.** Its one handler never throws and nothing writes at the same time, so `409` `Conflict` is never answered.
14. **A result of the mock expires with the mock job as it is**: a day after the job was created, not after it ended. The job routes were not changed.
15. **Choices of the seed that the spec leaves open.** "Another user" is the first other account that administers the project, then its organization, and Grace Hopper otherwise (Grace for `acme`, Alan for `northwind.portal`, Margaret for `fabrikam.fitness-cloud`). The open message has the topic `SchemaSuggestion` and says where the suggested action comes from. The text of the failed export is a throttled read of Cosmos DB, cut at 500 characters. The dismissed import was started by the mock user and dismissed by the other user. The ids of two views seeded in the same millisecond are equal.
16. **`inbox.spec.ts` has eight tests, two more than the spec lists**: the page in the command palette, and the result of an import in its slideover.
17. **The UI automation suite.** The Reader's test (case 30) is left as it is: it checks the lock per named path, and a line for Inbox would move it away from the steps of the case in Testiny. Beyond the spec's list, the comments of `NavigationShortcutTests.cs` and `ShortcutsHelpDialog.cs` name `g i` and **Inbox**; no assertion there changed.
18. **Documentation.**
    - `access.md` got a passage in this phase: its paragraph on what the admin UI shows per role had no word of the page.
    - The page has no document of its own. As the table of section 8 says, it is described in `inbox.md`, in the section **Administration UI**; `docs/Platform/ui.admin/` holds only specs and reviews.
    - Section 8 has no row for `docs/Platform/UI.Admin.AutomationTests/`. Its `automation-tests.md` and `test-cases.md` list neither the view entries nor the go-to shortcuts, and the titles of cases 24 and 27 name no number of entries, so neither file changed.
    - The note of the runbook is in **Later**, and it names the order as it can happen: right after the merge, or before it from the branch.
19. **Additions on the page that the spec does not name**: the text under "Nothing done yet"; the kind or the level of an item in words; the freshness of 15 seconds; the toast "The next page did not load"; a failure of the report shown in the slideover with **Retry**.

### Known limits

- **Against a real API every Accept answers `501`** until 424-8, and no deployment has an action to accept. What the mock seeds and runs is not a feature of a deployment.
- **The mark follows the reads of the sidebar.** The sidebar never polls, so on other pages an item that arrives does not mark the entry by itself: the mark comes with the next read of the page or the toaster, or with a reload.
- **"Expires …" shows only next to Download.** **Show the result** has no such line, although the report expires with its job too.
- **After a `403` the lock stays until Refresh**, and the menu keeps the entry without a lock, because the access map of the session is not read again.
- **An error of the open items while Done is shown** is not shown on the **Done** tab: the tab only loses its number, and the re-read stays off until **Refresh**, or until **Retry** on the **Open** tab.
- **A failed download still shows its toast** after the person has switched to another view.
- **The visual baselines cover only the Open list.** The **Done** tab, the empty states, the lock and the slideover are not captured.
- **A flake of the annotation grid**, not from this change: a row separator under the pinned columns is sometimes drawn faded. It showed in 9 of 36 captures of the committed tree before the phase. It is 380 pixels, below the tolerance of 0.2 %, so no run fails on it, but a later `--update` can write it into a baseline.

### Left for later

- **424-8**: the first real action and its handler. Until then the acceptance on the page is only reached in the mock.
- **Testiny**: the steps of cases 24 and 27 list five view entries, and the page has no case of its own. `AppRoutes.Inbox` has no caller yet, and no automated test checks the lock of Inbox for a Reader against a deployment.
- **A rebase onto `main`** changes the version of the OpenAPI document, so the document must be exported again and both SDKs regenerated (see the review of phase 2).
- **Publishing the API.** The merge deploys the admin UI by itself; the API is published by hand, as the runbook now says.
