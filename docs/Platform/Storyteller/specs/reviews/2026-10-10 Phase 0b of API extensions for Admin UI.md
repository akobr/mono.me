# Phase 0b of API Extensions for the Admin UI

## Overview

Phase 0b covers E2 (members), E4 (accounts without a first project) and E3 (invitations) of [TypeScript SDK and API extensions for the Administration UI](../2026-10-09%20TypeScript%20SDK%20and%20API%20extensions%20for%20the%20Administration%20UI.md). Administrators can list members with names, set an exact role, and remove members; members can leave. Invitations are Storyteller documents: administrators create, resend and revoke them, and the invitee lists, accepts or declines them with a token whose verified email matches. With AuthKit and a management key, WorkOS sends the invitation email. Every membership change now writes the access point and the account in one ETag-guarded transactional batch, which also closes the follow-up left by phase 0a for grant and revoke. The product-level description is the new [access.md](../../access.md).

## What Was Done

### E2. Members

- **Models** (`Abstractions.Access/src/Model`): `AccessPointMember` (`AccountId`, `UserName`, `Name`, `Role`) and `MemberRoleUpdate` (`Role`).
- **`IAccessService`** gains:
  - `GetMembersAsync(key, actorId)`
  - `SetMemberRoleAsync(key, accountId, role, actorId)`
  - `RemoveMemberAsync(key, accountId, actorId)`
  - `JoinAccessPointAsync(key, accountId, role)`, a trusted "join or raise" used by invitation acceptance (see E3).
- **`CosmosAccessService`**:
  - All membership writes, including grant and revoke, go through a private `UpdateMembershipAsync`. It reads the access point and the account with their ETags (`TryReadItemWithETagAsync`), runs the rule, and replaces both documents in one `TransactionalBatch`, with `IfMatchEtag` on each item. Both documents are in the `access` partition, so the change is atomic. On `412` it runs the rule once more on fresh data; a second `412` becomes `ConflictException` (`Conflict`).
  - The rules read the caller's role from the **access point's** map, read in the same transaction. Before, grant and revoke read it from the caller's account document. Both maps hold the same data; the access point is the one guarded by the batch.
  - `GetMembersAsync` loads the account names with `SELECT c.id, c.UserName, c.Name FROM c WHERE ARRAY_CONTAINS(@ids, c.id)` in the main partition and orders owners first, then by name. A dangling account ID is listed without names.
- **Rules:**
  - `None` is rejected; the HTTP layer returns `400`.
  - Owner changes need `Owner`.
  - The last owner can't be demoted, removed, or leave (`LastOwner`).
  - Nobody can change their own role (`SelfRoleChange`).
  - PUT requires an existing membership (`404`, `MemberNotFound`).
  - DELETE by the member themselves means "leave".
- **HTTP** (`Api.Functions/src/V1/MembersHttp.cs`):
  - Endpoints: `GET v1/access/points/{key}/members`, `PUT …/members/{accountId}` and `DELETE …/members/{accountId}` (`204`).
  - Keys are trimmed and lower-cased, as `GetAccessPoint` already does.
  - New route, route-id and parameter constants are in `Definitions`. 403, 404 and 409 come from the phase 0a middleware mapping.

### E4. Account without an initial project

- `AccountCreate.Organization` and `AccountCreate.Project` are optional, in both the API model (`V1/Models/AccountCreate.cs`) and the service model.
- `CosmosAccessService.CreateAccountAsync` creates an account with an empty `AccessMap` when both are absent. Exactly one of them throws `ArgumentException`.
- `AccessHttp.PostAccount` returns `400` for exactly one of them, before resolving the profile.

### E3. Invitations

- **Model** (`Abstractions.Access`): `Invitation`, `InvitationCreate` and `InvitationStatus` (`Pending`, `Accepted`, `Declined`, `Revoked`, and the computed `Expired`).
- **Contracts** (`Backend.Core/src/Accessing`):
  - `IInvitationService`.
  - `InvitationIdentity`, which holds the account ID, email, verified flag and display names from the token.
  - `IInvitationSender` and `NoopInvitationSender`.
  - `InvitationOptions` (section `Invitations`: `DefaultExpiresInDays` 7, `MaxExpiresInDays` 30).
- **Storage:** `InvitationEntity`, with the id `inv.{InvitationId}` in the `access` partition of `core`. Status and role are stored as strings. Queries filter on `STARTSWITH(c.id, 'inv.')`.
- **`CosmosInvitationService`** (`Backend.CosmosDb/src/Accessing`, registered as transient with an injectable `TimeProvider`):
  - **Create:**
    - The email is normalized to lower case. It must be a single plain address, so a display-name form is rejected.
    - The role must not be `None`, and the validity must be 1 to 30 days.
    - Callers need Administrator, and Owner to offer `Owner`.
    - Only one unexpired pending invitation per email and access point is allowed (`InvitationExists`).
    - The email is sent first, then the document is created; if the create fails, the external invitation is revoked.
  - **Resend:** revokes the external invitation and sends a new one, and restarts the validity with the original length. Expired pending invitations can be resent.
  - **Revoke** and **decline:** set the status and revoke the external invitation, best effort.
  - **Pending for an email:** unexpired pending invitations, oldest first.
  - **Accept:**
    - Checks the email against the invitation (`EmailMismatch`) and requires a verified email (`EmailNotVerified`).
    - Requires the invitation to be pending (`InvitationNotPending`) and unexpired (`InvitationExpired`).
    - Creates the account without memberships when it is missing, using the E4 path; a parallel creation is tolerated.
    - Calls `JoinAccessPointAsync`, which never lowers a higher existing role.
    - Marks the invitation accepted. Accepting twice returns the account.
  - Invitation writes use `ReplaceItemAsync` with `IfMatchEtag`; a `412` becomes `Conflict`.
- **Sending** (`Access.AuthKit`):
  - `WorkOsManagementClient` gains `SendInvitationAsync` (`POST user_management/invitations`) and `RevokeInvitationAsync` (`POST user_management/invitations/{id}/revoke`, where 404 counts as already gone). They use the new `WorkOsInvitationCreate` and `WorkOsInvitation` records, and like other POSTs they are not retried except on 429.
  - `AuthKitInvitationSender` sends an application invitation (no `organization_id`). The validity is clamped to 1 to 30 days. `inviter_user_id` is set only for WorkOS user IDs (`user_…`). Without `Auth:AuthKit:ApiKey` it sends nothing.
  - `AddAuthKitUserAuthentication` replaces the default `NoopInvitationSender` with `services.Replace`, so the order of registration does not matter.
- **Email verification:**
  - New `AuthKitOptions.RequireVerifiedEmail` (default `true`).
  - `InvitationsHttp.IsEmailVerified`: the `email_verified` claim decides when present. Otherwise AuthKit tokens are verified only when the option is off, and Entra ID tokens count as verified.
  - `AuthKitClaimNormalizer` needed no change, because it already passes unknown claims such as `email_verified` through.
  - The JWT template in `authentication.md` now includes `email_verified`.
- **HTTP** (`Api.Functions/src/V1/InvitationsHttp.cs`):
  - The seven endpoints listed in the spec.
  - The invitee's email is read from `preferred_username`, then `email`, `unique_name`, `upn`.
  - Display names come from `GetIdentityProfileAsync`, and missing names are tolerated.
  - `GetMyInvitations` returns an empty list when the token has no email.
  - `ArgumentException` from creation becomes `400`.

### Deviations from the spec

1. **Ids:** invitation ids are `Guid.CreateVersion7()` in `N` format (time-ordered) instead of ULIDs, so no new package is needed.
2. **`IsEmailSent`:** new flag on `Invitation`. A failed or impossible send keeps the invitation, and the UI then offers "Copy link".
3. **No TTL** on invitation documents. Containers are created without a default TTL (`ContainerFactory`), so an item `ttl` would be ignored. Answered invitations stay as history.
4. **Self role change:** every member is blocked from changing their own role, not only administrators.
5. **Deprecation:** grant and revoke are not marked deprecated in OpenAPI. That would make NSwag emit `[Obsolete]` for the CLI. The TypeScript SDK can mark them in doc comments (phase A).
6. **Existing members:** invitation creation does not check whether the email already belongs to a member, because accounts are not indexed by email. Accepting such an invitation keeps the higher role.

## Tests

| Project | New tests | Result |
| --- | --- | --- |
| `Api.Functions.UnitTests` | `InvitationsHttpTests`: the `email_verified` and provider matrix (7 cases), acceptance passes the token identity, missing names, `mine` without email, `ArgumentException` → 400, missing body. `MembersHttpTests`: normalized key and caller on set, invalid role → 400, remove → 204, list, `PostAccount` with only one of organization and project → 400, and without both → a bare account. | 125 passed |
| `Access.AuthKit.UnitTests` | `AuthKitInvitationSenderTests`: request shape (email, `expires_in_days`, `inviter_user_id`, no `organization_id`), a non-WorkOS inviter is omitted and the validity clamped to 30, no management key → nothing sent, a WorkOS error surfaces its message, the revoke endpoint, unknown invitation → `false`, the registration replaces the default sender | 132 passed |
| `Backend.CosmosDb.UnitTests` (emulator) | `CosmosAccessServiceTests` (+17): account without project, only an organization → `ArgumentException`, members with names and owners first, members by contributor → 403, role set down and up in both documents, Owner by Administrator → 403, own role → `SelfRoleChange`, not a member → `MemberNotFound`, demote the other owner, remove by administrator, leave, removal by contributor → 403, last owner leaving → `LastOwner`, join raises but never lowers, **two concurrent grants both land** (ETag retry). `CosmosInvitationServiceTests` (19 facts and 5 theory cases): create and send, failed send kept, duplicate until expiry, Owner by Administrator, by contributor, invalid input, pending by email (case and expiry), accept creates the account and grants, accept keeps a higher role, `EmailMismatch`, `EmailNotVerified`, expired, revoked, unknown, decline, resend, resend from another point, list by contributor | 184 passed |
| `Access.Certificates.IntegrationTests` | none | 23 passed |

All 41 Platform projects in `42.mono.slnx` build. As in phase 0a, the build used a separate artifacts path, because a running Functions host locks `Api.Functions/src/bin/Debug`.

## Documentation

- New `docs/Platform/Storyteller/access.md`: access points, roles, members, invitations (lifecycle, acceptance, email delivery), accounts, and error codes.
- `docs/Platform/Storyteller/authentication.md`:
  - `email_verified` in the JWT template.
  - New `RequireVerifiedEmail` row, and invitation emails added to the `ApiKey` row.
  - New dashboard step 6 (management key and the invitation link to the admin UI).
  - Link to `access.md` from "401 and 403".

## Follow-ups

- Phase 0c: views, listings, name validation and CORS docs (E5-E8, E11, E12). Then export `open.api.v0.9.x.json` and regenerate both SDKs.
- `sform member …` and `sform invite …` commands (companion spec, open question 1).
- An admin-UI-side "Copy link" for invitations without an email (`IsEmailSent = false`).
