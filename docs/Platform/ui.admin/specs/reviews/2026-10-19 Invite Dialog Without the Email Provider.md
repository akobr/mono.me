# Invite Dialog Without the Email Provider

Linear: [424-65](https://linear.app/424fun/issue/424-65/remove-text-from-invitation-dialog)

## Overview

This review covers the whole spec [2026-10-19 Invite Dialog Without the Email Provider](../2026-10-19%20Invite%20Dialog%20Without%20the%20Email%20Provider.md), delivered in one phase. The **Invite people** dialog of the organization's and the project's **Members** pages no longer says "WorkOS sends the emails". Its description is now "To {key}. Everyone gets the same role and expiry." The unit and e2e tests check this exact description, so the clause cannot come back unnoticed. The work followed the plan, with no deviations.

## What Was Done

### 1. Description of the dialog (`InviteDialog.vue`)

As planned:

- `app/components/access/InviteDialog.vue`, line 146, is now `` :description="`To ${accessPointKey}. Everyone gets the same role and expiry.`" ``.
- The doc comment of the component still says that WorkOS sends the email, for developers.
- Nothing else changed in the dialog or in the two Members pages.

### 2. Unit test (`test/unit/access/invite-dialog.test.ts`)

As planned:

- New test: "describes the destination and the shared role and expiry, not who sends the emails (424-65)". It reads the element named by the dialog's `aria-describedby` and expects exactly "To acme.billing. Everyone gets the same role and expiry.". The dialog's text must not contain "WorkOS".
- The assertion "Everyone gets the same role and expiry" moved out of "commits the typed address on a space and on a comma".
- Check against broken code: with the old `InviteDialog.vue`, the new test fails and the other 8 pass.

### 3. End-to-end tests (`test/e2e/access.spec.ts`)

As planned:

- The project page test expects the accessible description "To acme.billing. Everyone gets the same role and expiry.".
- The organization page test expects "To acme. Everyone gets the same role and expiry." instead of `toContainText('To acme.')`.

Results:

- `access.spec.ts` and `accessibility.spec.ts`, desktop and mobile: 138 passed.
- The unit suite: 831 passed and 1 failed. The failure, `templates-schemas.test.ts` › "selects the first type that can descend when the key changes", already fails on `main`, and is not related to this change.
- `npm run lint` and `npm run typecheck` pass.

### 4. Documentation

As planned, there are no changes. `docs/42for.net/platform/admin-ui.md` already describes the dialog without the provider. `docs/Platform/Storyteller/access.md` keeps how Storyteller sends the email through WorkOS, because that page documents the backend.
