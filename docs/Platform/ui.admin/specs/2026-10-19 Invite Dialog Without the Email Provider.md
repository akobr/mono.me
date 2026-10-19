# Invite Dialog Without the Email Provider

Linear: [424-65](https://linear.app/424fun/issue/424-65/remove-text-from-invitation-dialog)

## Problem

The **Invite people** dialog of the organization's and the project's **Members** pages in the Administration UI (`src/Platform/ui.admin`) ends its description with "WorkOS sends the emails." Which service sends the invitation emails, and how, is an implementation detail. The people who invite others do not need it, so the issue asks for the sentence to be removed.

## Current State

All paths are relative to `src/Platform/ui.admin/src`.

- **Invite dialog:** `app/components/access/InviteDialog.vue`.
  - Line 146: the `UModal` gets `:description="`To ${accessPointKey}. Everyone gets the same role and expiry; WorkOS sends the emails.`"`. The provider is a clause after a semicolon, not a sentence of its own. Nuxt UI renders the description under the title and links it to the dialog as its accessible description (`aria-describedby`).
  - Lines 11–14: the component's doc comment ends with "Storyteller stores the invitation; WorkOS sends the email." It is read by developers only.
- **Both pages use the same dialog:** `app/pages/[org]/members.vue` (line 44) passes `context.org`, and `app/pages/[org]/[project]/members.vue` (line 58) passes `context.projectKey`. One change covers both, as the issue asks.
- **The rest of the app:** no other screen names the email provider to the user. `InvitationsTable.vue` says "The email could not be sent; use Resend or copy the link." without naming it. `app/pages/unsupported.vue` names WorkOS AuthKit as the only supported sign-in provider. That is a deployment requirement, not how emails are sent, and it stays.
- **Tests:**
  - `test/unit/access/invite-dialog.test.ts`, "commits the typed address on a space and on a comma" (lines 81–90): `expect(text()).toContain('Everyone gets the same role and expiry')`. This passes with or without the provider clause, so nothing checks that it is gone.
  - `test/e2e/access.spec.ts`: "invites a member to the project from its Members page" (project) and "invites a pasted list to the organization from its Members page" (organization), with `expect(dialog).toContainText('To acme.')`.
  - `test/e2e/accessibility.spec.ts`: axe on the open invite dialog.
  - The visual baselines (`test/e2e/visual.spec.ts`) have no invite dialog, so no screenshot changes.
- **Docs:**
  - `docs/42for.net/platform/admin-ui.md`, Members row (line 204), already says only "Everyone gets the same role and expiry." It does not mention the provider.
  - `docs/Platform/Storyteller/access.md` (line 93) describes how Storyteller delivers the email through WorkOS. This is product documentation of the backend, where the provider matters, and it stays.
  - The spec and review of 424-41 (`2026-10-17 Invite Button in the Top Bar and Lists of Emails.md`) quote the old description. They are historical and are not changed.

## Proposed Changes

### 1. Description of the dialog (`InviteDialog.vue`)

Line 146 drops the clause. The semicolon becomes the full stop of the sentence before it:

```vue
:description="`To ${accessPointKey}. Everyone gets the same role and expiry.`"
```

- The destination and the shared role and expiry stay. They are what the user acts on.
- Nothing else changes: the submission, the roles, the expiry, the permissions and the email delivery stay as they are.
- The doc comment at lines 11–14 keeps "WorkOS sends the email". Developers do need to know where the email comes from, and the issue is about what the user sees.

### 2. Unit test (`test/unit/access/invite-dialog.test.ts`)

A new test after "commits the typed address on a space and on a comma":

- It opens the dialog for `acme.billing`.
- The description is exactly "To acme.billing. Everyone gets the same role and expiry." It is read from the element that the dialog names in `aria-describedby`, so the test also checks that it is the dialog's accessible description.
- The dialog's text does not contain "WorkOS".

The assertion "Everyone gets the same role and expiry" in the existing test moves into the new test, so the existing test checks the tags and the help text only.

### 3. End-to-end tests (`test/e2e/access.spec.ts`)

Both Members pages are checked, on desktop and mobile:

- The project page ("invites a member to the project from its Members page"): `await expect(dialog).toHaveAccessibleDescription('To acme.billing. Everyone gets the same role and expiry.')`.
- The organization page ("invites a pasted list to the organization from its Members page"): `toContainText('To acme.')` becomes `toHaveAccessibleDescription('To acme. Everyone gets the same role and expiry.')`. An exact description also proves that the provider clause is gone.

The axe check of the dialog stays as it is.

### 4. Documentation

Nothing changes in the user documentation. `admin-ui.md` already describes the dialog without the provider (see Current State). The README's list of the e2e flows does not quote the description.

### Out of scope

- The OpenAPI description of `POST /access/points/{key}/invitations` ("With AuthKit and a management key, WorkOS sends the email.") and the SDK comments generated from it. They are written for developers who call the API, and they are not shown in the admin UI.
- The text of the invitation email, which WorkOS renders.

## The CodeRabbit Plan

The issue has a plan from CodeRabbit, taken as a second opinion:

- **Taken:** the single edit of `InviteDialog.vue:146`, with the semicolon turned into a full stop. The edit covers both dialogs, and there are no `.esproj` or backend changes. Its validation (the unit test of the dialog, the "invites" e2e flows and the axe check of the dialog) is run.
- **Added:** the plan relies on the existing tests, which pass with and without the clause. This spec adds assertions of the exact description, in the unit test and on both Members pages in the e2e tests, so that the clause cannot come back unnoticed.
