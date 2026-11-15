# Review Findings of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

After the four phases of the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md) were committed (`c7057e9d4` to `4871da3f9`), four reviewers read the branch's diff, one per area (the API, the TypeScript SDK and the admin UI, the .NET clients, the documents and the deployment), and a skeptic per area tried to refute each of their findings in the code. Of 16 findings, 12 held, none of them high; they are fixed in `552ae2b6a` (the .NET clients, the API tests, the documents) and `1520d10ad` (the admin UI). Four were refuted; one of them was hardened anyway.

## What Was Done

### The API

- **Confirmed, low: an invitation's role and body checks come after the step-up.** `CreateInvitation` checks the Administrator role, the email, the role and the validity in the service, after `EnsureRecentSignIn`, so a person without the role, or with a bad invitation, gets `401` before the `403` or `400`. The plan placed the guard there; the documents said otherwise. **Fix:** `authentication.md` and `access.md` list the invitation among the checks that run in the service, after the step-up. No client is hurt: `sform` and the MCP server create no invitations, and the admin UI asks for the sign-in before Invite opens.
- **Confirmed, low: no route tests of delete, import, restore and purge with a stale sign-in.** **Fix:** `KeyValuesHttpTests` covers them; the service double refuses the delete and the import of a secret without the write right, and answers restore and purge with the items a test gives.
- Refuted: that the step-up runs before other cheap refusals (a missing machine, an immutable issuer, a project without `CertificateAndApiKey`, a malformed recipient key); the skeptic found them either answered after a check that a new sign-in cannot change, or only after the service read the stored state, which the guard must not wait for. Refuted: that disabling or renaming a recipient should not need a sign-in; the guard is on purpose for every update, and removing one is the kill switch.

### The admin UI

- **Confirmed, medium: input typed into a dialog was lost without the promised warning.** Only page editors counted as unsaved changes. **Fix:** `useDirtySource` in `useUnsavedChanges.ts`, used by the import, New key, Invite, New machine, federation rules, trusted issuer, shared certificate and recipient dialogs while they hold typed input; the import dialog asks for the sign-in before Check and Import of a document with secret rows.
- **Confirmed, medium: Back from the AuthKit page could restore the page from the back/forward cache with the dialog stuck in its redirecting state.** **Fix:** `StepUpDialog` resets it on `pageshow` (`abandonRedirect`), with the leave prompt and the stored reveal of the abandoned attempt.
- **Confirmed, low: the toast "You signed in again" also showed when the sign-in had not moved.** **Fix:** `takeReturnNotice` shows it only when `signedInSince` holds.
- **Confirmed, low: the Resolved tab said that secrets were hidden during the hint's last minute, and kept its text across refetches.** **Fix:** `isSignInTooOld` (no margin), judged again whenever the read answers.
- **Confirmed, low: a pending reveal for another screen could stay for the whole page load.** **Fix:** `takePendingReveal(candidates)` reads and removes it on the first screen that asks, matching or not.
- **Confirmed, low: tests missing for the versions replay, the partial undo, New key, New machine and Invite.** **Fix:** added to `step-up.test.ts` (18 tests became 32); the tests of the five fixes above fail on the code before them.

### The .NET clients

- **Confirmed, low: `config get -r` blamed a missing `Secrets.Read` for the bindings the API had withheld.** **Fix:** with a withheld reason, its warning stands alone.
- **Confirmed, low: `config set -r` printed the resolved configuration without saying that secrets were withheld.** **Fix:** it reads through the conditional twin and names the reason and its remedy as `config get -r` does.
- Refuted: that `IsStepUpRequired` throws inside the 401 handlers on a `max_age` no `TimeSpan` holds or on an `ErrorCode` that is no string; only a server that breaks the API's contract sends that. It was hardened anyway (no window, or no step-up, instead of an exception), with tests.

### The documents and the deployment

- **Confirmed, low: the reason given for leaving the key/value policy and the machine authentication policy unguarded was false.** Both widen what machine credentials that already exist can do. **Fix:** `authentication.md` names it an accepted risk, and a later change may guard both.
- **Confirmed, low:** the order of the checks for invitations (the API's first finding above).
- Refuted: that the MFA policy states WorkOS behaviour without a source; WorkOS documents that the MFA responses may come for any method in an environment that requires MFA, and the runbook's check verifies it.

### Still open

- Turning MFA on in the production WorkOS environment (the owner, in the dashboard).
- The staging check of whether a device-code approval in a signed-in browser counts as an active authentication.
- Guarding the two policies, and re-signing in the automation tests' cached sessions on a deployed target.
- 424-262 (refusing WorkOS impersonation tokens on the API routes) is not merged; this branch only makes such a token never count as recent.
