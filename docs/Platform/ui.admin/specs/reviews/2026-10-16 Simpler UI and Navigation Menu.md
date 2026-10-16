# Simpler UI and Navigation Menu

Linear: [424-35](https://linear.app/424fun/issue/424-35/simplify-ui-and-navigation-menu)

## Overview

This review covers the whole spec [2026-10-16 Simpler UI and Navigation Menu](../2026-10-16%20Simpler%20UI%20and%20Navigation%20Menu.md). It was delivered in one phase, with one code commit per change of the issue and the fixes of the code review after them.

The top bar no longer has a theme button. The theme is set in the command palette or on the account page.

The view entry **Key values** is now **Values store**, with the `vault` icon.

The **Project** section of the sidebar went from nine entries to five: **Views**, **Members**, **Machines**, **Settings** and **Monitor**. The removed pages live on as parts of other pages:

| Old page | Where its content is now |
| --- | --- |
| Invitations | a section of **Members** |
| Certificates | a section of **Machines**, for Administrators |
| Secrets | the last section of **Settings** |
| Usage and Audit | the two tabs of the new **Monitor** page (`activity` icon) |

The old URLs redirect to these sections, and the anchored section is scrolled into view and focused. The API, the SDK and the mock API did not change.

## What Was Done

### 1. No theme button in the top bar (`AppNavbar.vue`)

As planned:

- `<ThemeToggle />` is gone from `AppNavbar`.
- `ThemeToggle.vue` is deleted, and its line is removed from `app/components.d.ts`.
- The command palette (`color-mode` of `UDashboardSearch`) and the **Theme** field of the account page are unchanged.

### 2. Values store (`app/utils/navigation.ts`, `key-values.vue`)

As planned:

- The view entry is `{ label: 'Values store', icon: 'i-lucide-vault', … }`, with the same path and roles.
- The navbar title of `key-values.vue` is "Values store".
- The URL, the texts about the pairs and the `key-round` icon of the **Value** tab stay.

**Addition:** the empty state of the page ("No key values in this view") uses the `vault` icon too, so the page shows the icon of its menu entry, as 424-27 did for the machines.

### 3. Invitations on the Members page (`pages/[org]/[project]/members.vue`)

As planned:

- The page has the sections **Project members** (the description, `MembersTable` and `?highlight=`) and **Invitations** (`<section id="invitations">` with `InvitationsTable`), styled like the organization's Members page.
- `invitations.vue` is a redirect stub to `members#invitations`.
- The **Invitations** menu entry is removed.
- `invitations` is removed from `projectSections`, because the stub is never the current path.

The page reads the same `useGetMembers` query as `MembersTable` (TanStack Query shares it), only to know when to scroll to `#invitations` (section 8).

### 4. Shared certificates on the Machines page

As planned:

- **`app/components/access/SharedCertificatesTable.vue`** (new) has the whole content of `certificates.vue`: the two queries, the "Shared certificates are disabled" alert, the table with the same columns, the issue modal, the revoke confirmation and its own `OneTimeSecretDialog`. **Issue certificate** sits in a `flex justify-end` row above the table, as **Invite** in `InvitationsTable`.
- **`machines.vue`:**
  - The body has the sections **Machine access** (the loading, error and empty states and the table, unchanged) and **Shared certificates**.
  - The second section is `<section v-if="canAdmin" id="shared-certificates">`, with `canAdmin = useCan('manageAccess')`, so Contributors don't mount the component and don't call an API that answers `403`.
  - The navbar keeps only **New machine**.
  - The detail panel (`machines/[id]`) and the dialogs of the machines are where they were.
- **`OneTimeSecretDialog.vue`:** "(Machines page)" instead of "(Certificates page)".
- **Menu and redirect:** the **Certificates** entry is removed from the menu and from `projectSections`, and `certificates.vue` redirects to `machines#shared-certificates`.

### 5. Secrets in Settings (`pages/[org]/[project]/settings.vue`)

As planned:

- **The section:** after the Certificate authority card, `<section id="secrets">` has an uppercase **Secrets** heading and the description "How the key values of the project are protected. Everyone in the project sees these settings; Administrators change them."
- **The cards:** `SecretsPolicyCard`, `SecretsKeysCard`, `ByokCard` and `RecipientsCard` come from `useKeyValuePolicy()` (`secretsPolicy`), with `editable: canAdmin`. The section has its own loading and error states.
- **The buttons** are **Save machine policy** (Machine authentication) and **Save secrets policy** (`SecretsPolicyCard`), matching their toasts.
- **Links and messages:**
  - The **Secrets settings** action of `ByokBanner.vue` links to `{ path: …/settings, hash: '#secrets' }`.
  - The five messages of `KeyValueDetail.vue` and `KeyValueCreateDialog.vue` say "the Secrets section of Settings".
  - The comments of `ByokBanner.vue` and `app/utils/key-values.ts` follow.
- **Menu and redirect:** the **Secrets** entry is removed, and `secrets.vue` redirects to `settings#secrets`.

**Addition (code review):** the titles of the four cards are `h3` under the `h2` "Secrets", and the BYOK steps are `h4`. Before, they were `h2`s next to "Secrets", so a screen reader saw an empty Secrets section. The cards are only used in this section now, and their look did not change.

### 6. Monitor: Usage and Audit on one page

As planned:

- **New components:** `app/components/monitor/UsageReport.vue` and `AuditTrail.vue` take the content of `usage.vue` and `audit.vue`.
  - The audit filters are a `UDashboardToolbar` at the top of `AuditTrail`. It sits above a table that scrolls with its sticky header (`flex min-h-0 flex-1 flex-col`).
  - Both components still pass `useCan('manageAccess')` as the `enabled` flag of their query.
- **New page `app/pages/[org]/[project]/monitor.vue`:**
  - **Tabs:** **Usage** (`gauge`, the default) and **Audit** (`scroll-text`) in a toolbar under the navbar, kept in `?tab=audit` and switched with `router.replace`.
  - **Body:** only the open tab is mounted. On the Audit tab the body has no padding and does not scroll itself, as `audit.vue` had.
  - **Roles:** below Administrator the page shows one "Administrators only" empty state.
- **Menu:** the Audit and Usage entries became **Monitor** (`i-lucide-activity`, Administrator), and `monitor` is in `projectSections`.
- **Redirects:** `audit.vue` redirects to `monitor?tab=audit`, and `usage.vue` to `monitor`.

**Deviations:**

- **Load more:** the watch that reads the continuation token of the first audit page now runs immediately. When the first page came from the cache, which happens every time the Audit tab opens again within 15 seconds, **Load more** used to stay hidden until the data changed. The old page had the same gap when it was opened again.
- **Query of the old URL:** all five redirect stubs keep the query of the old URL. Vue Router does this by default for a redirect without its own query, and `audit.vue` adds `tab=audit` to the query instead of replacing it. This came up in the first e2e run, where the redirect tests did not expect the `mock-scenario` parameter to be kept.
- **Reserved names (code review):** `monitor` stays a legal view name, as planned. But `currentSection` took `/{org}/{project}/monitor/graph`, the graph of a view named `monitor`, for the Monitor page. The context switcher then lost the view's section, and the page memory ignored that view.
  - A project section now matches a longer path only if it has pages below it. That is only `machines` (`machines/{id}`), so `/acme/billing/monitor/graph` is the view's graph again.

### 7. The project section of the menu and the context switcher

As planned:

- The **Project** section is Views, Members (Administrator), Machines (Contributor), Settings and Monitor (Administrator).
- `projectSections` is `['views', 'members', 'machines', 'settings', 'monitor']`.
- The sidebar and the **Pages** group of the command palette both read `buildNavigation`, so they changed together.

### 8. Section anchors (`app/composables/useHashScroll.ts`, new)

As planned:

- `useHashScroll(ready)` scrolls to the element of `route.hash` (`block: 'start'`) once `ready` is true.
- It scrolls again for another hash, but not when the data is refetched.
- The pages pass these `ready` conditions:
  - Members: the members query is loaded.
  - Machines: the machines query is loaded.
  - Settings: the secrets policy is loaded and, for Administrators, also the machine policy and the access point (the Members row of the Project card).
- The sections have `scroll-mt-4`, so their heading does not touch the top edge.

**Additions (code review):**

- **Focus:** the composable also focuses the section (`focus({ preventScroll: true })`). The three sections have `tabindex="-1"` and `outline-none`. Keyboard and screen-reader users continue from the section, as after a followed anchor, and do not have to tab through the whole Settings page to reach the secrets.
- **Decoding:** the hash is used as Vue Router decoded it. Decoding it a second time threw `URIError` for a hash with a `%`.

**Deviation:** `openApp` of the e2e tests (`test/e2e/support.ts`) puts the mock scenario before the hash of a path with an anchor. Before, `/settings#secrets` became `/settings#secrets?mock-scenario=default`.

### 9. Tests

- **Unit** (12 new tests, 2 files changed, 1 new):
  - `test/unit/utils/navigation.test.ts`, 7 new:
    - **Values store** has `i-lucide-vault`, the `key-values` path and `editRole` Contributor, and no entry is labelled "Key values";
    - the project entries for Owner, Administrator, Contributor and Reader (`it.each`), which fail with the old menu;
    - **Monitor** has `i-lucide-activity` and Administrator;
    - no entry links to `invitations`, `certificates`, `secrets`, `audit` or `usage`.
  - `test/unit/utils/shell.test.ts`:
    - the Monitor page is the project section `monitor`, and switching the project keeps it;
    - "tells the Monitor page from a view named monitor": `currentSection`, `viewLocation`, `projectTarget` and `viewTarget` of `/acme/billing/monitor/graph…` treat it as a view, and `machines/graph` is still the Machines section. This test fails with the first version of `currentSection`.
  - `test/unit/components/hash-scroll.test.ts` (new, 4 tests):
    - it scrolls to the section of the hash and focuses it;
    - it waits until `ready`;
    - it does not scroll without a hash, or again after a reload of the content, but does for another hash;
    - it looks up `#100%25` as `100%`.
- **E2E** (desktop and mobile):
  - `navigation.spec.ts`:
    - "changes the theme in the command palette, not in the top bar": there is no "Color mode" button, the palette offers Light, Dark and System, Dark sets the `dark` class, and the account page shows Dark.
    - "lists the project pages each role can open, with Monitor and the values store": the Owner of `acme/billing`, the Contributor of `acme/shipping` and the Reader of `northwind/portal`, by `href`, because **Members** appears in both the project and the organization section.
    - Five redirect tests: the URL without the kept mock parameter, and the anchored section in the viewport and focused.
  - `access.spec.ts`: invite from the **Invitations** section of Members; the disabled alert and **Issue certificate** in `#shared-certificates` of Machines; **Save machine policy**.
  - `settings.spec.ts`: the expiry date of `ci-pipeline` is read from `#shared-certificates` on Machines.
  - `key-values.spec.ts`:
    - the menu link **Values store**;
    - the Reader on `/northwind/portal/settings#secrets`: the section is in view, the switches are disabled, and there is no **Save secrets policy**;
    - BYOK on `/acme/billing/settings#secrets`;
    - Usage, then the Audit tab (`?tab=audit`) with the filters, and back to Usage on `/acme/billing/monitor`.
  - `accessibility.spec.ts`: `settings#secrets`, `monitor` and `monitor?tab=audit` instead of `/secrets`, `/audit` and `/usage`.
- **Visual:**
  - `visual.spec.ts` has `settings` (`/acme/billing/settings#secrets`, so the moved cards are covered), `monitor` and `monitor-audit` instead of `secrets`.
  - The four `secrets-*.png` were removed, and the 12 new baselines were written by `npm run test:visual:docker -- --update`.
  - It also rewrote the 32 baselines that changed beyond the tolerance: every desktop screen with the project sidebar, the phone screens whose top bar lost the theme button, and Members and Machines with their new sections.
  - The theme button alone is below the 0.2 % tolerance on `organizations-desktop-*` and on a few phone screens, so those baselines were left as they were.

### 10. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - **Members** includes the invitations, and **Machines** the shared certificates (Administrator).
  - **Settings** includes the Secrets section.
  - A new **Monitor** row was added, and the Invitations and Certificates rows were removed.
  - The command palette also switches the theme, and the theme sentence names the command palette and the account page.
- `docs/Platform/Storyteller/key-values.md`, "Admin UI": **Values store**, the **Secrets** section of **Settings**, and **Monitor** with the **Usage** (default) and **Audit** (`?tab=audit`) tabs. The links now point to "the Secrets section of Settings".
- `src/Platform/ui.admin/README.md`:
  - the summary line;
  - the **Values store** bullet with `components/monitor/` and `monitor.vue`;
  - a **Merged pages** bullet about the redirect stubs and `useHashScroll`;
  - the new e2e flows.
- `docs/Platform/Storyteller/access.md` is not changed, because `monitor` is not reserved.

### Results

- `npm test`: 442 passed (48 files), 12 of them new.
- `npm run test:e2e` (desktop and mobile, installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`, `CI=1`): 155 passed and 5 skipped (the existing desktop-only and mobile-only skips). The first run failed four of the five redirect tests on both viewports (`audit` replaced the query and passed), which led to the query deviation in section 6.
- `npm run test:visual:docker`: 60 passed against the new baselines, run after the fixes of the code review (the focused sections have no outline, and the heading levels did not change the look).
- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the new files and in the files whose layout changed. The secrets and key-values components and `test/e2e/key-values.spec.ts` were not Prettier-clean on `main`; only their changed lines were edited, so their diff stays small.

### Notes

- The branch starts from `main` (`908ff981`).
- **The code review** ran four reviewers (behaviour parity, routing and anchors, completeness, UX and accessibility), each finding checked by two skeptics. The parity reviewer found no difference from the old pages. Upheld and fixed:
  - the view named `monitor`;
  - the second decoding of the hash;
  - the focus of the anchored section;
  - the headings of the secrets cards.

  Two findings were about the state before the baselines and the redirect tests were updated. One was refuted: a Contributor who switches project on Members sees two "No access" states, which the organization's Members page does too.
- **Merges:**
  - `fix/424-32` and `fix/424-34` regenerate some of the same desktop baselines. Whichever branch merges second has to run `npm run test:visual:docker -- --update` again.
  - `git merge-tree` finds no other conflict with the open branches that merge cleanly into `main`.
- **Out of scope:** the `key-values` URL and the components keep their names.
