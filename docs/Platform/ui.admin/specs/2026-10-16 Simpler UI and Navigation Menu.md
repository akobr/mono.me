# Simpler UI and Navigation Menu

Linear: [424-35](https://linear.app/424fun/issue/424-35/simplify-ui-and-navigation-menu)

## Problem

The admin UI (`src/Platform/ui.admin`) has more navigation than it needs. Every page has a theme button in the top bar, even though the theme can also be changed in the command palette and on the account page. The **Project** section of the sidebar has nine entries. Several of them are small pages that belong with another page: invitations next to the members they create, shared certificates next to the machines that use them, secrets next to the other project settings, and audit next to usage.

The issue asks for six changes:

- Remove the theme button from the top right of the main screen. The theme stays in the command palette (search) and on the account page.
- Rename the **Key values** menu item to **Values store** and change its icon from `key` to `vault`.
- Put the content of the project's **Invitations** page on its **Members** page, as the organization's Members page already does.
- Put the content of the project's **Certificates** page on the **Machines** page.
- Move the **Secrets** page into the project's **Settings** page.
- Combine the project's **Usage** and **Audit** pages into one **Monitor** page with the `activity` icon.

## Current State

- **Top bar:** `app/components/shell/AppNavbar.vue`, line 33, renders `<ThemeToggle />` (`app/components/shell/ThemeToggle.vue`: a Light / Dark / System dropdown, `aria-label="Color mode"`) between the search button and the user menu on every page. Nothing else uses `ThemeToggle`. The theme can also be set in two other places:
  - the command palette, through the `color-mode` prop of `UDashboardSearch` (`CommandPalette.vue`, line 161), which adds a **Theme** group with Light, Dark and System;
  - the **Theme** field of the account page (`app/pages/account.vue`, line 125).
- **Navigation:** `app/utils/navigation.ts`, `buildNavigation`. The sidebar (`AppSidebar.vue`) and the **Pages** group of the command palette (`CommandPalette.vue`, line 70) both read from it.
  - View section, line 96:

    ```ts
    { label: 'Key values', icon: 'i-lucide-key-round', to: `${base}/key-values`, editRole: 'Contributor' },
    ```

  - Project section, lines 113–120:

    | Entry | Icon | Path | Role |
    | --- | --- | --- | --- |
    | Views | `eye` | `views` | everyone |
    | Members | `users` | `members` | Administrator |
    | Invitations | `mail` | `invitations` | Administrator |
    | Machines | `computer` | `machines` | Contributor |
    | Certificates | `shield-check` | `certificates` | Administrator |
    | Settings | `settings` | `settings` | everyone |
    | Secrets | `shield` | `secrets` | everyone, locked below Administrator |
    | Audit | `scroll-text` | `audit` | Administrator |
    | Usage | `gauge` | `usage` | Administrator |

- **Values page:** `app/pages/[org]/[project]/[view]/key-values.vue`, line 214, has the navbar title "Key values". The URL `/{org}/{project}/{view}/key-values` follows the API (`/v1/{org}/{project}/{view}/key-values`).
- **Members:**
  - `app/pages/[org]/members.vue` puts two `<section>`s under uppercase `h2`s, **Organization members** (`MembersTable`) and **Invitations** (`InvitationsTable`).
  - `app/pages/[org]/[project]/members.vue` shows only `MembersTable`, with `?highlight=<account id>` (the audit actor link of `AuditActorLink.vue`).
  - `app/pages/[org]/[project]/invitations.vue` shows only `InvitationsTable`, which brings its own **Invite** button and dialogs.
- **Machines:** `app/pages/[org]/[project]/machines.vue` (Contributor and up, `useCan('machines')`) has the machine table, **New machine** in the navbar, and a `<RouterView />` for the `machines/[id]` detail panel. While a full-screen detail covers it, the list panel is hidden (`detail.covers`).
- **Certificates:** `app/pages/[org]/[project]/certificates.vue` is for Administrators (the API answers `403` below that). It has:
  - **Issue certificate** in the navbar;
  - the alert "Shared certificates are disabled" when the machine authentication policy does not enable `CertificateAndApiKey`;
  - the certificate table (Label, Thumbprint, Valid from, Expires, Status, Revoke);
  - the issue modal, the revoke confirmation (type the first 8 characters of the thumbprint) and the one-time secret dialog.

  `OneTimeSecretDialog.vue`, line 134, refers to "the project's shared certificates (Certificates page)".
- **Settings:** `app/pages/[org]/[project]/settings.vue` is a `max-w-3xl` stack of cards: Project, Annotations, Dates, Annotation types, Machine authentication (its submit button "Save policy", line 275) and Certificate authority.
- **Secrets:** `app/pages/[org]/[project]/secrets.vue` loads `useKeyValuePolicy()` and shows `SecretsPolicyCard` ("Allowed levels", with its own **Save policy** button), `SecretsKeysCard`, `ByokCard` and `RecipientsCard` in the same `max-w-3xl` column. Every reader sees them, and Administrators (`useCan('manageAccess')`) change them. Other places point at this page:
  - `ByokBanner.vue`, line 24, has the action **Secrets settings**, linked to `/{org}/{project}/secrets`;
  - `KeyValueDetail.vue` (lines 127, 156 and 158) and `KeyValueCreateDialog.vue` (lines 73 and 96) say "… in the Secrets settings".
- **Audit and Usage:**
  - `app/pages/[org]/[project]/audit.vue` has a filter toolbar (view, key, operation, from, to) in the header, a full-height table with a sticky header (`body: 'p-0 sm:p-0 gap-0 overflow-hidden'`) and **Load more**.
  - `app/pages/[org]/[project]/usage.vue` has a centered `max-w-4xl` body with a date range, Totals, Estimated charges and a daily chart.
  - Both show an "Administrators only" empty state below Administrator, and both have date inputs labelled "From" and "To".
- **Context switcher:** `app/utils/context-paths.ts`, line 7, `projectSections = ['views', 'members', 'invitations', 'machines', 'certificates', 'settings']`. Switching the project keeps one of these sections; any other project page falls back to `default/annotations`.
- **Anchors:** no page reads `route.hash`, and `createRouter` (`app/router/index.ts`) has no `scrollBehavior`. Vue Router would scroll the window anyway, while the scroll container here is the `UDashboardPanel` body. `MembersTable.vue` (lines 43–52) scrolls a highlighted row into view by hand, with `nextTick` and `scrollIntoView`.
- **Redirect stubs:** `app/pages/[org]/[project]/index.vue` and `annotations/[key]/configuration.vue` are pages with only `definePage({ redirect })` and `<span hidden />`, so saved links keep working.
- **Icons:** `@iconify-json/lucide` 1.2.142 has `vault` and `activity`. Neither is the icon of a built-in annotation type (`typeMeta`), which `test/unit/utils/navigation.test.ts` checks for every entry.
- **Tests:**
  - **Unit:** `test/unit/utils/navigation.test.ts` and `test/unit/utils/shell.test.ts` check `buildNavigation` (sections, labels, locks, icons) and the context paths. They do not check the entries of the project section.
  - **E2E:**
    - `access.spec.ts` opens `/invitations` and `/certificates`, and clicks **Save policy** on Settings;
    - `settings.spec.ts` reads a date from the `/certificates` table;
    - `key-values.spec.ts` clicks the **Key values** menu link and opens `/secrets`, `/audit` and `/usage`;
    - `accessibility.spec.ts` checks `/secrets`, `/audit` and `/usage`.
  - **Visual:** `visual.spec.ts` has 13 screens, among them `secrets` (`/acme/billing/secrets`), each in both themes and both viewports (52 baselines, 0.2 % tolerance, made in the Playwright Linux container). Every screen with the top bar shows the theme button.
- **Docs:**
  - `docs/42for.net/platform/admin-ui.md`, lines 169–177, has a table with the Members, Invitations, Machines, Certificates and Settings screens. Line 218 says you pick the theme on the account page.
  - `docs/Platform/Storyteller/key-values.md`, lines 400–406, describe the **Key values** section and the **Secrets**, **Audit** and **Usage** pages of the sidebar.
  - `src/Platform/ui.admin/README.md`, line 163, mentions "the project's Secrets settings … the Audit and Usage pages".

## Proposed Changes

### 1. No theme button in the top bar (`AppNavbar.vue`)

Remove `<ThemeToggle />` from the `#right` slot of `AppNavbar` and delete `ThemeToggle.vue`, which then has no user. The command palette (`color-mode`) and the account page keep the theme. `public/theme-init.js` still applies the stored theme before the first paint, because the account page writes the same `vueuse-color-scheme` key.

### 2. Values store (`app/utils/navigation.ts`, `key-values.vue`)

- The view entry becomes `{ label: 'Values store', icon: 'i-lucide-vault', … }`; its path and roles do not change.
- The label is in sentence case, like every other entry ("Compare views", and before this change "Key values", which the issue also writes as "Key Values").
- The navbar title of `key-values.vue` becomes "Values store" too, so the page has the name of its menu entry.
- **Not changed:**
  - the URL `…/key-values`, which follows the API;
  - the texts about the pairs themselves ("No key values in this view", "Import key values"), since a key value is still what the store holds;
  - the **Value** tab of the detail, which keeps the `key-round` icon of a single value.

### 3. Invitations on the Members page (`pages/[org]/[project]/members.vue`)

- Mirror the organization's Members page: two sections under uppercase `h2`s.
  - **Project members** keeps the existing description and `MembersTable`, with `?highlight=`.
  - **Invitations** (`<section id="invitations">`) holds `InvitationsTable :access-point-key="context.projectKey"`.
- Both entries were Administrator-only, so the role gating stays the same.
- Remove the **Invitations** entry from the menu.
- `invitations.vue` becomes a redirect stub to `/{org}/{project}/members#invitations`.

### 4. Shared certificates on the Machines page

- **New `app/components/access/SharedCertificatesTable.vue`:** the content of `certificates.vue`, as a self-contained component like `InvitationsTable`. It has:
  - the shared-certificates and machine-policy queries;
  - the "Shared certificates are disabled" alert;
  - an **Issue certificate** button in a `flex justify-end` row above the table, as **Invite** in `InvitationsTable`;
  - the table, with the same columns and order;
  - the issue modal, the revoke confirmation and its own one-time secret dialog.
- **`machines.vue`:**
  - The body becomes two sections. The first is **Machine access**, with the loading, error and empty states and the table, all unchanged.
  - The second is **Shared certificates** (`<section v-if="canAdmin" id="shared-certificates">`, with `canAdmin = useCan('manageAccess')`). The `v-if` keeps Contributors from mounting the component and so from calling an API that answers `403`.
  - The navbar keeps one primary action, **New machine**.
  - The detail panel and the dialogs of the machines stay where they are.
- **`OneTimeSecretDialog.vue`:** "(Certificates page)" becomes "(Machines page)".
- **Menu:** remove the **Certificates** entry.
- **Old URL:** `certificates.vue` becomes a redirect stub to `/{org}/{project}/machines#shared-certificates`.

### 5. Secrets in Settings (`pages/[org]/[project]/settings.vue`)

- **New section:** after the Certificate authority card, add `<section id="secrets">`.
  - It starts with an uppercase **Secrets** `h2` and a one-line description: the protection of the project's key values, which every reader sees and Administrators change.
  - Then come `SecretsPolicyCard`, `SecretsKeysCard`, `ByokCard` and `RecipientsCard`, fed by `useKeyValuePolicy()` (named `secretsPolicy`, next to the existing `machinePolicy`) and `editable: canAdmin`, as on the old page.
  - The section has its own loading and error states, so the other cards render while the policy loads.
- **Two "Save policy" buttons would be on one page.** That is ambiguous for people who move between buttons, and it is a strict-mode failure for the e2e locators. Name the buttons after what they save, as their toasts already do:
  - **Save machine policy** on the Machine authentication card;
  - **Save secrets policy** on `SecretsPolicyCard`.
- **Links and messages:**
  - The **Secrets settings** action of `ByokBanner.vue` links to `{ path: '/{org}/{project}/settings', hash: '#secrets' }`.
  - The five messages of `KeyValueDetail.vue` and `KeyValueCreateDialog.vue` say "the Secrets section of Settings" instead of "the Secrets settings".
  - The comments of `ByokBanner.vue` and `app/utils/key-values.ts` change to match.
- **Menu:** remove the **Secrets** entry.
- **Old URL:** `secrets.vue` becomes a redirect stub to `/{org}/{project}/settings#secrets`.

### 6. Monitor: Usage and Audit on one page

- **New components** in `app/components/monitor/`, one per old page:
  - `UsageReport.vue` gets the script and the `max-w-4xl` body of `usage.vue`.
  - `AuditTrail.vue` gets the script, the filter toolbar and the table of `audit.vue`. Its root is `flex min-h-0 flex-1 flex-col`: the filters sit in a `UDashboardToolbar` at the top, and the table below scrolls with its sticky header.
- **New page `app/pages/[org]/[project]/monitor.vue`** (`UDashboardPanel id="monitor"`, title "Monitor"):
  - **Tabs:** a `UDashboardToolbar` under the navbar holds `UTabs` (`variant="link"`, `:content="false"`) with **Usage** (`gauge`, the default) and **Audit** (`scroll-text`).
  - **URL:** the tab is kept in `?tab=audit`, the pattern of `TemplateDetail.vue` and `SchemaDetail.vue`. The default tab has no query, and switching uses `router.replace`.
  - **Body:** only the active tab is mounted, so only its query runs. On the Audit tab the body uses `p-0 sm:p-0 gap-0 overflow-hidden`, as `audit.vue` did, so the table fills the panel.
  - **Roles:** below Administrator the page shows one "Administrators only" empty state for both tabs, and the components no longer have their own.
- **Menu:** the Audit and Usage entries become `{ label: 'Monitor', icon: 'i-lucide-activity', to: `${base}/monitor`, minRole: 'Administrator' }`.
- **Old URLs:** `audit.vue` becomes a redirect stub to `/{org}/{project}/monitor?tab=audit`, and `usage.vue` to `/{org}/{project}/monitor`.
- **Reserved names:** `monitor` is not added to the reserved project and view names (`app/forms/names.schema.ts`, `NameRules.cs`, `access.md`).
  - That list mirrors the API routes, and `monitor` is a page of the UI only.
  - It cannot clash: there is no `/{org}/monitor` page and no `/{org}/{project}/{view}` index page.
  - `secrets` was not reserved either.

### 7. The project section of the menu and the context switcher

- After the change, the **Project** section is:

  | Entry | Icon | Path | Role |
  | --- | --- | --- | --- |
  | Views | `eye` | `views` | everyone |
  | Members | `users` | `members` | Administrator |
  | Machines | `computer` | `machines` | Contributor |
  | Settings | `settings` | `settings` | everyone |
  | Monitor | `activity` | `monitor` | Administrator |

  So an Owner or Administrator sees five entries, a Contributor sees Views, Machines and Settings, and a Reader sees Views and Settings.
- In `context-paths.ts`, `projectSections` becomes `['views', 'members', 'machines', 'settings', 'monitor']`. Switching the project on the Monitor page keeps Monitor. The redirect stubs are never the current path, so they are not listed.

### 8. Section anchors (`app/composables/useHashScroll.ts`)

A redirect or a link to `#invitations`, `#shared-certificates` or `#secrets` has to show that section, and nothing scrolls the panel body to an anchor today.

- **New composable:** `useHashScroll(ready?: MaybeRefOrGetter<boolean>)`. When the route has a hash and `ready` is true, it waits for `nextTick` and scrolls the element with that id into view (`block: 'start'`). It runs again when the hash changes, but not when the data is only refetched.
- **The pages pass `ready`** once the content above the section has loaded, so the section does not move after the scroll:
  - Members: the members of the project. The page reads the same `useGetMembers` query as `MembersTable`, which TanStack Query shares.
  - Machines: the machines.
  - Settings: the secrets policy and, for Administrators, the machine policy.

### 9. Tests

- **Unit**
  - `test/unit/utils/navigation.test.ts`:
    - the project entries per role: Owner and Administrator `Views, Members, Machines, Settings, Monitor`; Contributor `Views, Machines, Settings`; Reader `Views, Settings`;
    - **Values store** has `i-lucide-vault` and the `key-values` path, and no entry is labelled "Key values";
    - **Monitor** has `i-lucide-activity`;
    - no entry links to `invitations`, `certificates`, `secrets`, `audit` or `usage`.

    The role and label tests fail before the change.
  - `test/unit/utils/shell.test.ts`: `currentSection('/acme/billing/monitor')` is the project section `monitor`, and `projectTarget` keeps it.
  - A test of `useHashScroll`:
    - it scrolls to the element of the hash once `ready` turns true;
    - it does not scroll without a hash, or before `ready`.
- **E2E** (desktop and mobile)
  - `access.spec.ts`:
    - invite from the **Invitations** section of `/acme/billing/members`;
    - **Issue certificate** and the disabled alert on `/acme/billing/machines`;
    - **Save machine policy** on Settings.
  - `settings.spec.ts`: the expiry date of `ci-pipeline` is read from the Machines page.
  - `key-values.spec.ts`:
    - the menu link **Values store**;
    - a Reader on `/northwind/portal/settings` sees the secrets cards locked and no **Save secrets policy**;
    - BYOK runs on Settings;
    - audit and usage run on `/acme/billing/monitor`, switching the tab, with `?tab=audit` in the URL.
  - `navigation.spec.ts`:
    - the project section has the entries of section 7 for the Owner of `acme/billing`, the Contributor of `acme/shipping` and the Reader of `northwind/portal`;
    - there is no "Color mode" button, and the command palette still offers Light, Dark and System;
    - the five old URLs land on their new page, and the anchored section is in the viewport.
  - `accessibility.spec.ts`: `/secrets`, `/audit` and `/usage` are replaced by `/acme/billing/settings#secrets`, `/acme/billing/monitor` and `/acme/billing/monitor?tab=audit`. Members, Machines and Settings now cover the merged content.
- **Visual**
  - The `secrets` screen becomes `settings` (`/acme/billing/settings#secrets`), so the moved cards stay covered.
  - Two screens are added: `monitor` and `monitor-audit`.
  - The four `secrets-*.png` baselines are removed.
  - Every screen with the top bar changes (no theme button), and the desktop screens with the project sidebar change too (fewer entries). Regenerate them with `npm run test:visual:docker -- --update`, then check that `npm run test:visual:docker` passes.
- `npm test`, `npm run test:e2e`, `npm run typecheck` and `npm run lint` must pass.

### 10. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - in the table of "People, machines and settings":
    - Members includes the invitations;
    - Machines includes the shared certificates (Administrator);
    - Settings includes the secrets;
    - a new **Monitor** row (Administrator) has usage and the audit trail of secrets;
    - the Invitations and Certificates rows are removed;
  - the theme sentence (line 218) names the account page and the command palette.
- `docs/Platform/Storyteller/key-values.md`, "Admin UI": **Values store** in every view, the **Secrets** section of the project's **Settings**, and the **Monitor** page with the **Usage** and **Audit** tabs.
- `src/Platform/ui.admin/README.md`, line 163: the same names and the new files.

### Out of scope

- Renaming the `key-values` URL, the components or the API concept.
- A router-wide `scrollBehavior`. Anchors are only used by the three merged sections.
