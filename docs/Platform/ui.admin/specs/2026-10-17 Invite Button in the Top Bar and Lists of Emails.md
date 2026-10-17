# Invite Button in the Top Bar and Lists of Emails

Linear: [424-41](https://linear.app/424fun/issue/424-41/move-invite-button-to-top-panel)

## Problem

The organization's and the project's **Members** pages of the admin UI (`src/Platform/ui.admin`) are the only pages whose main action is not in the top bar. **Invite** sits in a row above the **Invitations** table, below the members, so you have to scroll to find it. Every other page puts its main action at the right of the top bar: **New project**, **New view**, **New machine**, **New schema**, **New key** and **New** annotation.

The invite dialog takes several emails, but each address must be committed with Enter. A space or a comma does not separate addresses. A pasted list such as `ada@example.com, grace@example.com` ends up as one invalid entry.

The issue asks for two things:

- **Invite** in the top bar of both Members pages, like the main actions of the other pages;
- a dialog that invites several people from addresses typed with spaces or commas between them. Everyone gets the same role and the same validity.

## Current State

- **Members pages:**
  - `app/pages/[org]/members.vue` (organization) and `app/pages/[org]/[project]/members.vue` (project) render `<AppNavbar title="Members">` with only `<RoleLegend />` in its `#right` slot (lines 11–13 and 25–27).
  - The body has a members section (`<MembersTable>`) and an **Invitations** section (`<InvitationsTable>`). On the project page, that section is `<section id="invitations" tabindex="-1">` (line 40). The old Invitations page redirects to `members#invitations`, and `useHashScroll` scrolls to it.
- **Invitations table:** `app/components/access/InvitationsTable.vue`.
  - Lines 42–46 are the row with the button: `<RoleGate capability="manageAccess" :scope="accessPointKey.includes('.') ? 'project' : 'org'"><UButton label="Invite" icon="i-lucide-user-plus" … /></RoleGate>`.
  - `inviting` (line 18) opens `<InviteDialog v-model:open="inviting" :access-point-key="accessPointKey" />` (line 112).
  - Resend, Revoke and Copy link stay in the rows.
- **Top bar:** `app/components/shell/AppNavbar.vue` renders the page's `#right` slot before the search button and the user menu (lines 30–33). The right group does not shrink; on a phone, the breadcrumb on the left gets cut instead.
  - The pages gate their main action with `v-if` and `useCan(…)` (`machines.vue` line 88, `views.vue` line 90, `schemas.vue` line 76).
  - The single primary action comes last, right before the search button. Secondary actions are neutral and come first.
  - Since the values store (2026-10-13 Phase 4 review), a top-bar button is icon-only below `sm`: one button with `aria-label` and `:ui="{ label: 'hidden sm:inline' }"` (`key-values.vue` lines 217–219).
- **Roles legend:** `app/components/access/RoleLegend.vue` is a neutral ghost `sm` button **Roles** (`i-lucide-circle-help`) with a popover. It keeps its label on a phone. In the 390 px baseline, about 20 px are left between the breadcrumb (`acme / billing`) and **Roles**.
- **Invite dialog:** `app/components/access/InviteDialog.vue`.
  - `emails` (line 22) is bound to `<UInputTags v-model="emails" placeholder="ada@example.com" autofocus />` (line 83). The help says "Type an address and press Enter."
  - Nuxt UI 4.11.3 forwards `delimiter`, `addOnPaste`, `addOnBlur`, `addOnTab` and `duplicate` to reka-ui 2.10.5 `TagsInputRoot`. The defaults are a `,` delimiter and `false` for the rest. The component exposes `inputRef`, the inner `<input>`, and passes `$attrs` such as `@paste` to that input.
  - Reka commits the input text in these cases:
    - on Enter (Tab and blur only when enabled);
    - when an `input` event inserts exactly the delimiter (`delimiter.test(event.data)`). A pasted or filled string comes as one insert, so a delimiter pattern that matches a single character inside it glues the addresses together;
    - on a paste, only with `addOnPaste`, split with the delimiter and without trimming. That gives empty tags and splits `Ada Lovelace <ada@example.com>` into words.
  - Reka checks duplicates with `includes`, so `Ada@example.com` and `ada@example.com` are both added.
  - Send is disabled while the list is empty (line 105). Text typed but not committed is not sent.
  - `invalid` (line 36) checks each address with `z.email()`. The role (Owner only for Owners) and **Expires after (days)** (1–30, 7 by default) apply to every address.
  - `send()` (lines 41–66) calls `createInvitation(accessPointKey, { Email, Role, ExpiresInDays })` once per address. It reports failures per address, `InvitationExists` as "A pending invitation already exists.", and keeps the failed addresses in the box. It refreshes the access queries with `invalidateAccess`.
- **API:** there is no bulk endpoint. `POST v1/access/points/{key}/invitations` takes one `{ Email, Role, ExpiresInDays }`. `CosmosInvitationService.NormalizeEmail` trims, lower-cases and rejects a display-name form such as `Ada <ada@example.com>` with `400` (`docs/Platform/Storyteller/access.md`, line 79).
- **Tests:**
  - `test/e2e/access.spec.ts` (lines 5–18) clicks **Invite** inside `#invitations`, fills `marie@example.com`, presses Enter and sends.
  - `test/e2e/accessibility.spec.ts` (line 14) scans `/acme/billing/members`.
  - `test/e2e/visual.spec.ts` (line 35) captures the project Members page: `members-{desktop,mobile}-{light,dark}.png`.
  - No unit test covers `InviteDialog` or `InvitationsTable`. Nothing covers the organization's Members page.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 173 (Members row of the People, machines and settings table): "Below the members, **Invitations**: invite by email with a role and an expiry; resend, revoke or copy the link."

## Proposed Changes

### 1. Invite in the top bar of both Members pages (`pages/[org]/members.vue`, `pages/[org]/[project]/members.vue`)

- Add `const canInvite = useCan('manageAccess', 'org')` on the organization page and `useCan('manageAccess')` on the project page. That is the same rule as today's `RoleGate`: Administrator of the access point itself, because an organization role grants nothing in its projects.
- Add a `const inviting = ref(false)`, and put the button after `<RoleLegend />` in `#right`, so it is the last page action, right before the search button:

  ```vue
  <UButton
    v-if="canInvite"
    label="Invite"
    aria-label="Invite"
    icon="i-lucide-user-plus"
    :ui="{ label: 'hidden sm:inline' }"
    @click="inviting = true"
  />
  ```

  It is the primary button with its label from `sm`, icon-only with the same accessible name below, as **New key** on the values store.
- Render `<InviteDialog v-if="…key" v-model:open="inviting" :access-point-key="…" />` at the end of the body, with `context.org` on the organization page and `context.projectKey` on the project page.
- Readers, Contributors and organization members without Administrator see no **Invite**, as today.

### 2. No Invite in the Invitations table (`components/access/InvitationsTable.vue`)

Remove the button row (lines 42–46), `inviting` and `<InviteDialog>`. The table keeps its rows, Copy link, Resend and Revoke, and the revoke confirmation. The dialog refreshes the access queries itself (`invalidateAccess`), so the table still shows new invitations.

### 3. Roles legend icon-only on a phone (`components/access/RoleLegend.vue`)

Give the **Roles** button `aria-label="Roles"` and `:ui="{ label: 'hidden sm:inline' }"`. Below `sm`, the icon-only **Roles** and **Invite** take about as much room as **Roles** does today, so the breadcrumb is not cut. The accessible name stays **Roles**.

### 4. Lists of emails (`utils/emails.ts`, new)

Pure functions, so they can be unit tested:

- `emailDelimiter = /^[\s,;]$/`: the `delimiter` of the tags input. A typed space, comma or semicolon commits the text. The pattern is anchored, so a multi-character insert (a paste without our handler, autofill, Playwright `fill`) never commits half of a list.
- `splitEmails(text: string): string[]` reads a typed or pasted list:
  - The separators are spaces, tabs, line breaks, commas and semicolons, in any number. Leading, trailing and doubled separators add nothing.
  - A mail client's form `Ada Lovelace <ada@example.com>`, `"Lovelace, Ada" <ada@example.com>` or `<ada@example.com>` gives the bare address, which is what the API accepts. When the text has such a form, the words of the display names are dropped. Otherwise every word is kept, so a typo shows as invalid instead of disappearing.
  - A `mailto:` prefix is removed. Addresses are lower-cased, as the API stores them, and duplicates are removed, keeping the first one.
- `normalizeEmails(list: readonly string[]): string[]`: applies `splitEmails` to each entry and removes duplicates across the list.
- `isEmail(value: string): boolean`: `z.email()`, as the dialog checks today.

### 5. Invite dialog (`components/access/InviteDialog.vue`)

- **Tags input:** `<UInputTags ref="tagsInput" v-model="emailTags" :delimiter="emailDelimiter" duplicate add-on-blur autofocus @paste="onPaste" />`.
  - `emailTags` is a computed with `get: () => emails.value` and `set: (value) => (emails.value = normalizeEmails(value))`. Every committed text, a single address or a whole list, goes through `normalizeEmails`.
  - `duplicate` makes reka accept every commit and clear the input. The setter removes the duplicates, also those that differ only in case.
  - `add-on-blur` commits the text when the field loses focus, for example on a click on Send.
  - No `add-on-paste`, because reka would split pasted text without our rules.
- **Paste:** `onPaste(event)` leaves a paste without a separator or `<` to the browser, so a fragment pasted into a half-typed address joins it. A paste with one of them is a list. `onPaste` prevents the default and commits the text before the selection, the pasted text and the text after it as one entry, so `normalizeEmails` splits it. Then it clears the input.
- **Send:**
  - `send()` first commits text left in the input (`tagsInput.value?.inputRef`). If the list is then empty, the field shows "Enter at least one email address." and nothing is sent. If an address is invalid, the field shows it and nothing is sent.
  - Send is disabled only while sending or while an address is invalid, not while the list is empty, so a typed address that is not committed yet can be sent.
  - The rest stays: one `createInvitation` per address, in order, with the same role and **Expires after (days)**. Failures are listed per address, and the failed addresses stay in the box. The toast counts the sent invitations.
- **Texts:**
  - The help becomes "Separate addresses with spaces or commas, or paste a list."
  - The description becomes "To {key}. Everyone gets the same role and expiry; WorkOS sends the emails."
  - The label **Emails**, the placeholder `ada@example.com`, the title **Invite people** and **Send invitations** stay.
- **Invalid addresses** are listed in the field error as today. Each tag gets the same mark through the `item-text` slot: `text-error` and a line-through for an invalid address.

### 6. Unchanged on purpose

- One request per address. The API has no bulk endpoint, and each invitation sends its own WorkOS email.
- The rules for the role and the validity, the failure messages and the retry of failed addresses.
- The Invitations sections, their `#invitations` anchor on the project page, and the redirect of the old Invitations page.
- **Issue certificate** above the shared certificates on Machines. It is not the page's main action (that is **New machine**), and the issue covers only the Members pages.

### 7. Tests

- **Unit** (`test/unit/utils/emails.test.ts`, new):
  - `splitEmails` with mixed separators (spaces, tabs, `\r\n`, commas, semicolons), leading, trailing and doubled separators, empty and whitespace-only text;
  - case-insensitive duplicates, keeping the first one, in lower case;
  - `Name <address>`, quoted names with a comma, a bare `<address>` and `mailto:`;
  - typos without `@` kept when the text has no `<…>` form;
  - `normalizeEmails` across entries, and `isEmail`;
  - `emailDelimiter` matching one separator only.
- **Unit** (`test/unit/access/invite-dialog.test.ts`, new, with the mock API):
  - typing `ada@example.com` followed by a space commits a tag, and a comma does the same;
  - a pasted list becomes one tag per address;
  - Send posts one invitation per address, with the same `Role` and `ExpiresInDays`;
  - an address left in the input is sent too;
  - `katherine@example.com`, already pending on `acme.billing`, is reported as a failure and stays in the box, while the others are sent;
  - an invalid address blocks Send, and an empty list shows "Enter at least one email address.".
- **Unit** (`InvitationsTable`): the table has no **Invite** button.
- **End to end** (`test/e2e/access.spec.ts`, desktop and mobile):
  - The project test clicks **Invite** in the top bar. On a phone that is the icon button with the same name. **Invite** is no longer inside `#invitations`.
  - It types three addresses separated by a space and a comma, and sends them. All three appear in the **Invitations** table.
  - A new test on the organization's Members page (`/acme/members`) pastes a list and sends it. The addresses appear in that page's Invitations table.
- **Accessibility** (`test/e2e/accessibility.spec.ts`): axe on the open invite dialog with tags. The Members page scan covers the new top-bar buttons.
- **Visual:** regenerate `members-{desktop,mobile}-{light,dark}.png` with `npm run test:visual:docker -- --update`.

`npm test`, `npm run test:e2e`, `npm run test:visual:docker`, `npm run typecheck` and `npm run lint` must pass.

### 8. Documentation

- `docs/42for.net/platform/admin-ui.md`, Members row (line 173):
  - **Invite** is in the top bar of both Members pages;
  - it takes several addresses separated by spaces or commas, or a pasted list;
  - everyone gets the same role and expiry.
- `src/Platform/ui.admin/README.md`, e2e list (line 62): "an invitation" becomes "invitations to several addresses from the top bar of the project and organization Members pages".
