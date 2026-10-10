# Phase F of Administration UI

## Overview

Phase F adds the access management screens of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 13:

- **Views:** create, edit, register discovered views, compare.
- **Members:** organization and project members with the member rules.
- **Invitations:** invite, resend, revoke and copy link; the signed-in user's pending invitations; the accept landing of the invitation email. A user without an account is sent to their invitations before onboarding.
- **Machines:** the machine list and detail, create, reset and delete, with one-time credentials.
- **Shared certificates:** issue (shown once), revoke, expiry warnings.
- **Project settings:** machine policy, CA download, project info.

They use the API extensions of phases 0b and 0c (companion spec E2, E3 and E5).

## What Was Done

### Organizations, projects and views

- **`[org]/[project]/views.vue`** (`GetViews` with `discover=true` for Administrators):
  - The list shows each view with its default, registered or discovered state, description and creation time.
  - **New view** (Administrator): VeeValidate with the view name rules (`viewNameSchema`); `ViewExists` and `InvalidName` are mapped to the field.
  - **Edit** (Administrator) changes the description, and **Register** registers a discovered view; both use `UpdateView`.
  - **Compare** opens `compare.vue?to=`.
- **Context switcher:** the view dropdown has "Manage views" again; it was left out until this page existed.

### Members (companion spec E2)

- **`utils/roles.ts`:**
  - `roleDescriptions` feeds the role legend.
  - `memberRules(member, members, callerId, callerRole)` encodes the three rules: only Owners grant or revoke Owner, the last Owner cannot be demoted or removed, and Administrators cannot change their own role. Below Administrator nothing is editable. It returns the roles offered, the permissions and the tooltip reason.
- **`MembersTable`** (organization or project access point):
  - columns: avatar and name with a "you" badge, user name and account id, and a role select (exact role, `PUT …/members/{id}`);
  - remove, after a confirmation;
  - the API error codes `LastOwner`, `ElevatedRole`, `SelfRoleChange` and `MemberNotFound` are shown as sentences;
  - a change of the caller's own role reloads the account (AccessMap).
- **`RoleLegend`:** a popover explaining each role.
- **Pages:**
  - `[org]/[project]/members.vue`.
  - `[org]/members.vue`, with organization members and organization invitations. The page states that organization and project roles are independent.

### Invitations (companion spec E3)

- **`InvitationsTable`:**
  - Columns: email, role, status (with a warning when the email was not sent), invited by and expiry.
  - Actions: **Resend**, **Revoke** (confirm) and **Copy link** (`/invitations/accept?id=`). The role gates follow the organization or project scope of the access point.
- **`InviteDialog`:**
  - Several emails (`UInputTags`, validated with Zod), a role (Owner only for Owners) and an expiry (7 days by default).
  - One `CreateInvitation` per email; failures such as `InvitationExists` are listed and the failed emails stay in the box.
- **`/invitations`:**
  - Pending invitations of the signed-in user (`GetMyInvitations`), with Accept and Decline.
  - Accept stores the returned account and lands in the project (or organization).
  - The error codes `EmailNotVerified`, `EmailMismatch`, `InvitationExpired` and `InvitationNotPending` are explained.
  - The page is reachable without an account (`allowWithoutAccount`) and links to onboarding as the alternative.
- **`/invitations/accept`** (public):
  - The landing page of the WorkOS email.
  - A signed-out user continues with `authkit.signIn({ invitationToken, state: { returnTo: '/invitations' } })`. `invitationToken` exists in authkit-js 0.20, as established in phase B.
  - A signed-in user goes straight to `/invitations`.
- **Auth guard:** a signed-in user without an account goes to `/invitations` when `GetMyInvitations` has a pending one, and to `/onboarding` otherwise. This was deferred from phase B. The check runs once per session (`session.hasPendingInvitations`).

### Machines

- **`machines.vue`** (Contributor):
  - **Columns:** id (linking to `machines/[id]`, a nested detail panel), scope, annotation restriction, credential kind, and certificate thumbprint with an expiry badge.
  - **Expiry badge:** computed from `LastRenewalAt` and the policy lifetime (`certificateDaysLeft`); warning at 30 days or less, error when expired.
  - **Last renewal** is the final column.
- **`MachineCreateDialog`:**
  - Scope with an explanation per scope (`scopeDescriptions`).
  - An optional annotation key, autocompleted from the catalog of the default view.
  - The certificate lifetime, shown when the project policy issues certificates.
- **Reset and delete:**
  - **Reset** confirms with "Existing credentials of this machine stop working".
  - **Delete** requires typing the machine id.
- **`machines/[id].vue`:** scope with its explanation, restriction, credential kind, object id, thumbprint, last renewal and token endpoint.

### One-time credentials

- **`OneTimeSecretDialog`:**
  - Shows the API key with copy (the "clipboard contains a secret" toast).
  - `Certificate` as **Download .pfx**: base64 → `Blob` (`application/x-pkcs12`).
  - `CertificatePassword` with copy, and `TokenEndpoint`/`TokenScope` for client credentials.
  - Ready-to-paste snippets: `curl` with the API's `Authorization: ApiKey …` scheme, `.env`, and the client credentials token request.
- **Lifetime of the secrets:** the dialog cannot be dismissed (no close button, `dismissible: false`) until "I have stored these credentials" is ticked. Create and Reset responses go straight to the dialog through a local ref, never into the query cache or a store.

### Shared certificates and settings

- **`certificates.vue`** (Administrator):
  - Columns: label, thumbprint, validity and status (valid, expires in N days within 30, expired, revoked).
  - **Issue** takes a label and a lifetime; the result goes to `OneTimeSecretDialog`.
  - **Revoke** requires typing the first 8 characters of the thumbprint.
- **`settings.vue`:**
  - Project information: access point, your role, member count.
  - **Machine authentication policy** (Administrator): credential kind with an explanation, and the certificate lifetime when certificates are issued; saved with `SetMachineAuthentication`.
  - **Download CA certificate:** `GetCertificateAuthority` returns the PEM as text through the SDK fetcher, saved as `storyteller-ca.pem`.

### Navigation

- **Project section:**
  - Views (everyone);
  - Members (Administrator);
  - Invitations (Administrator);
  - Machines (Contributor);
  - Certificates (Administrator);
  - Settings (everyone; the policy form is gated).
- **Organization section:** Overview and Members (Administrator).

### Mock API

- **`mock/access-handlers.ts`:**
  - **Members:** list, set role and remove, with the API's rules and error codes (`ElevatedRole`, `SelfRoleChange`, `LastOwner`, `MemberNotFound`). The account's AccessMap stays in sync.
  - **Invitations:** list, create (`InvitationExists`, Owner-only Owner), resend, revoke, mine, accept (creates the account when missing) and decline.
  - **Machines:** list, create, get, reset and delete. Credentials follow the project policy (API key, certificate, both, or client credentials) and are returned once.
  - **Shared certificates:** issue (returned once) and revoke.
  - **Machine policy** and the **CA PEM**.
- **Seed data:**
  - `mock/db.ts` has a directory of three users, seeded invitations, machines (one with an aging certificate) and a certificate expiring in 25 days.
  - A new scenario, `?mock-scenario=invited`, has a user without an account and a pending invitation to `acme.billing`.

### Deviations from the spec

1. **Machine expiry badge:** computed from the last renewal and the project policy's lifetime, because `MachineAccess` carries no expiry date. Contributors cannot read the policy, so they see no badge.
2. **Snippets:** there is no `sform` configuration snippet, because the CLI has no machine API key setting; `curl` and `.env` snippets are offered instead.
3. **Machine detail:** a nested detail panel next to the list (`machines/[id]`), like the other list/detail pairs.

## Tests

`npm test` runs 131 tests in 17 files, and all pass. New and changed in phase F:

| File | Covers |
| --- | --- |
| `test/unit/access/access.test.ts` | member rules (last Owner, Owner-only Owner, self change, below Administrator); certificate expiry and policy kinds; one-time credential mapping; `OneTimeSecretDialog` stays open until acknowledged and downloads a PKCS#12 blob; `MembersTable` against the mock API (last Owner disabled) |
| `test/unit/router/guards.test.ts` | + a user without an account and a pending invitation goes to `/invitations`; only declined invitations → onboarding |
| `test/unit/components/organization-form.test.ts` | The test now waits for VeeValidate's asynchronous outcome (`vi.waitFor`) instead of counting ticks. Before, it failed now and then on a busy machine. |

These also pass:
- `vue-tsc --build`;
- oxlint and ESLint, with no findings;
- `npm run build`.

Every phase F page loaded in mock mode without console errors. Screenshots of the project members (light) and of the one-time credentials after creating a machine (dark) were checked.

## Follow-ups

- Show the machine certificate expiry date once the API returns it, instead of computing it from the policy.
- Phase G: the graph.
