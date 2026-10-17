# Invite Button in the Top Bar and Lists of Emails

Linear: [424-41](https://linear.app/424fun/issue/424-41/move-invite-button-to-top-panel)

## Overview

This review covers the whole spec [2026-10-17 Invite Button in the Top Bar and Lists of Emails](../2026-10-17%20Invite%20Button%20in%20the%20Top%20Bar%20and%20Lists%20of%20Emails.md), delivered in one phase.

**Invite** is now the main action of the organization's and the project's **Members** pages. It sits at the right of the top bar, like **New machine** or **New key**, and it is no longer above the **Invitations** table. The invite dialog turns addresses typed with spaces, commas or semicolons into tags. It turns a pasted list into one tag per address, including a list copied from Gmail or Outlook. Everyone in one send gets the same role and expiry.

The code review led to four changes from the spec:

- no commit of the typed text on blur;
- a dialog locked while it sends;
- the focus placed on the field after a refused Send;
- a readable color for a selected invalid tag.

## What Was Done

### 1. Invite in the top bar of both Members pages (`pages/[org]/members.vue`, `pages/[org]/[project]/members.vue`)

As planned:

- `canInvite` is `useCan('manageAccess', 'org')` on the organization page and `useCan('manageAccess')` on the project page. This is the same rule as the old `RoleGate`: Administrator of the access point itself.
- The button follows `<RoleLegend />` in `#right`. It has `label="Invite"`, `aria-label="Invite"`, `icon="i-lucide-user-plus"` and `:ui="{ label: 'hidden sm:inline' }"`, so it is icon-only below `sm` and keeps its name.
- `<InviteDialog v-if="context.org">` (organization) and `<InviteDialog v-if="context.projectKey">` (project) are at the end of the body, opened by `inviting`.
- Each page has a short comment that names 424-41 and the rule.

### 2. No Invite in the Invitations table (`components/access/InvitationsTable.vue`)

As planned: the button row, `inviting` and `<InviteDialog>` are gone. The doc comment says that **Invite** is in the top bar of the Members pages. Copy link, Resend, Revoke and the revoke confirmation do not change.

### 3. Roles legend icon-only on a phone (`components/access/RoleLegend.vue`)

As planned: `aria-label="Roles"` and `:ui="{ label: 'hidden sm:inline' }"`, with a comment. On the 390 px baseline, `acme / billing`, the **Roles** icon, the **Invite** icon, search and the avatar fit in the bar, and the breadcrumb is not cut.

### 4. Lists of emails (`utils/emails.ts`, new)

As planned: `emailDelimiter` (`/^[\s,;]$/`), `splitEmails`, `normalizeEmails` and `isEmail`.

**Additions (code review):** the first version removed every quoted run (`/"[^"]*"/g`), so `"ada@example.com","grace@example.com"` from a CSV row, or a JSON array, gave nothing. It also split Outlook's unquoted `Lovelace, Ada <ada@example.com>; …` at the comma, which left the surnames as invalid tags. Now:

- `quotedName` removes only a `"…"` or `'…'` name directly before `<`;
- quotes and square brackets end a word, so CSV cells and JSON arrays give their addresses, and a surrounding pair of single quotes is removed from a word (`'ada@example.com'`). A single quote inside an address stays (`o'brien@example.com`);
- `entriesOf` splits on semicolons and line breaks, then on commas. A single name word (no `@`, no dot) before a comma joins the next entry when that entry has an address in brackets, so `Lovelace, Ada <…>` is one entry. A typo such as `grace.example.com, Ada <…>` still shows as an invalid tag.

### 5. Invite dialog (`components/access/InviteDialog.vue`)

As planned:

- `<UInputTags>` takes `:delimiter="emailDelimiter"` and `duplicate`. Its `emailTags` computed sends every commit through `normalizeEmails`.
- `onPaste` turns a paste with a separator or `<` into tags. It merges the text before and after the selection. A paste without a separator stays a normal paste. It reads `text/plain`.
- `send()` commits the text left in the input. An empty list shows "Enter at least one email address."; an invalid address blocks Send.
- The help says "Separate addresses with spaces or commas, or paste a list.", and the description says "To {key}. Everyone gets the same role and expiry; WorkOS sends the emails."
- One `createInvitation` per address. The failure messages, the failed addresses kept in the box and the toast are unchanged.

**Deviations (code review):**

- **No `add-on-blur`.** A press on Send blurred the input, the tag added on blur could wrap the field, and the centred modal moved Send about 13 px down before the release. A click or tap on the upper part of Send was then lost, and so was one on Cancel or the close button. `send()` already commits the typed text, so the input keeps it until Send, a separator, Enter or a paste. A comment explains why.
- **Locked while sending.** The list, the role and the expiry are read once before the first request. While sending:
  - the tags input, **Role**, **Expires after (days)** and **Cancel** are disabled;
  - the modal cannot be dismissed (`:dismissible` and `:close` follow `sending`, as in `AnnotationBulkCreateDialog`);
  - a second `send()` returns at once.

  `sending` is reset in a `finally` after `invalidateAccess`. Before, a role changed during a send applied to the rest of the list, and an address added during a send was dropped. Both behaviours were older than this change.
- **Focus after a refused Send.** On an empty list, on an invalid address, and after a send with failures, the focus goes to the input. Its `aria-describedby` reads the field error out. A Send that got disabled no longer drops the focus to the body.
- **Invalid tags.** The `item-text` slot gives an invalid address `text-error line-through decoration-error`, as the spec says. `in-data-[state=active]:text-default` keeps the tag's own color while the tag is selected (ArrowLeft or Backspace): there the red of the dark theme measured 3.6:1 on `bg-accented`.

### 6. Unchanged on purpose

As planned: one request per address, the role and validity rules, the failure messages, the Invitations sections with the `#invitations` anchor and the redirect of the old Invitations page, and **Issue certificate** on Machines.

### 7. Tests

- **Unit** (`test/unit/utils/emails.test.ts`, new, 12 tests):
  - separators;
  - empty text;
  - case-insensitive duplicates;
  - mail client forms;
  - Outlook's unquoted and single-quoted names;
  - CSV and JSON quotes;
  - `mailto:`;
  - typos kept, also before a named entry;
  - `normalizeEmails`, `isEmail` and `emailDelimiter`.
- **Unit** (`test/unit/access/invite-dialog.test.ts`, new, 8 tests). The dialog is mounted with `mountScreen` and the mock API, and a pass-through MSW handler records the request bodies:
  - a space and a comma commit tags;
  - a pasted list gives one tag per address, and a fragment paste stays native;
  - one invitation per address with the same `Role` and `ExpiresInDays`, including the address left in the input;
  - `katherine@example.com` (already pending) stays in the box, and the others are sent;
  - an empty list and an invalid address block the send and move the focus to the input. The invalid tag is struck through;
  - a blur leaves the typed text in the input;
  - the dialog is locked while a held request is pending, and every body has the same role;
  - `InvitationsTable` has no **Invite** button.

  All 6 tests of the first version fail against the components on `main`. Adding `add-on-blur` back fails the blur test.
- **End to end** (`test/e2e/access.spec.ts`, desktop and mobile):
  - The project test clicks **Invite** in the top bar and checks that `#invitations` has no **Invite**.
  - "invites several people typed with spaces and commas, with the same role" (new) types three addresses with `pressSequentially`, picks **Contributor**, and clicks Send 3 px below its top edge with the last address still in the input. It expects "3 invitations sent" and three Contributor rows.
  - "invites a pasted list to the organization from its Members page" (new) dispatches a paste of `Marie Curie <…>, "Hopper, Grace" <…>;\n…` on `/acme/members`, and expects two tags, "2 invitations sent" and both rows.
- **Accessibility** (`test/e2e/accessibility.spec.ts`): "the invite dialog has no serious accessibility violations" (new, light and dark). It runs axe with a valid and an invalid tag, then again with the invalid tag selected. It also checks that the selected tag keeps the line-through and takes the tag's own color.
- **Visual:** `members-{desktop,mobile}-{light,dark}.png` were regenerated in the Playwright container. No other baseline changed.

### 8. Documentation

- `docs/42for.net/platform/admin-ui.md`, Members row: **Invite** in the top bar invites several people at once from addresses typed with spaces or commas, or a pasted list, also one copied from a mail client. Everyone gets the same role and expiry. The Invitations section keeps resend, revoke and copy link.
- `src/Platform/ui.admin/README.md`, e2e list: "an invitation" became the invitations from the top bar of both Members pages, typed or pasted, with axe on the invite dialog. The axe line at the end of the list was left alone, because `feat/424-40` edits it.

### Results

- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed files.
- `npm test`: 465 passed (51 files).
- `npm run test:e2e` (desktop and mobile, `PLAYWRIGHT_CHANNEL=chrome`), last run on the final code: 210 passed, 5 skipped and 1 failed. The failure was "shows the usage and filters the audit trail on the Monitor page" on mobile: the **Usage** tab was not found within 10 s. This change does not touch that page, and the test passed in the earlier full run and in 6 out of 6 reruns (`--repeat-each=3`, desktop and mobile).
- `npm run test:visual:docker -- --update`: 60 passed. Only the four Members baselines were rewritten.

### Notes

- **Code review.** Four agents reviewed the change (dialog correctness, parsing edge cases, UI and accessibility, tests and docs). Two skeptics then checked each of the 14 findings.
  - **Upheld (fixed above):**
    - the click lost through `add-on-blur`;
    - quoted addresses that were dropped;
    - Outlook's `Last, First` names;
    - the dialog editable while sending;
    - the silent refused Send;
    - the contrast of the selected invalid tag and the missing line-through.
  - **Refuted:**
    - a double send through the gap between `sending = false` and `invalidateAccess`. It was older than this change, and the `finally` and the early return now close it anyway;
    - zero-width characters in addresses;
    - the 390 px top bar touching the breadcrumb (the new baseline shows it fits);
    - a claimed undocumented change to the display-name rule.
- **Other branches.** The branch starts from `main` (`6bc7e0ae`). `git merge-tree` merges it with `feat/424-40` (PR 99) and `docs/platform-study-usage` without a conflict.
- **Out of scope:**
  - A retry of a failed send still posts an address whose invitation is already pending. The issue did not ask about failures.
  - The mock API accepts an invalid email or expiry, which the real API rejects with `400`. The dialog never sends one.
