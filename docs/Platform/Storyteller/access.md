# Access Points, Views, Members and Invitations

Organizations and projects are **access points**. An organization has the key `{organization}` and a project the key `{organization}.{project}`. Each access point keeps a map of account IDs to roles, and each account keeps the same map from its side. Both documents live in the main partition (`access`) of the `core` container, so every membership change replaces them together in one transactional batch, guarded by their ETags. A concurrent change of either document makes the batch fail; the change is then evaluated once more on fresh data, and a second failure answers `409 Conflict`.

An organization role does not grant access to the organization's projects. Each project has its own members.

## Names

New organizations, projects, and views follow one rule: 2 to 63 characters from `a-z`, `0-9`, and `-`, starting with a letter or a digit (`^[a-z0-9][a-z0-9-]{1,62}$`). Names become route segments, container names (`org.{organization}`), and parts of keys, where `.` separates the segments, so upper case and dots are not allowed. Some words are reserved because the API or the admin UI uses them in addresses:

| Kind | Reserved |
| --- | --- |
| Organization | `access`, `auth`, `login`, `callback`, `onboarding`, `invitations`, `account`, `orgs`, `unsupported` |
| Project and view | `access`, `views`, `members`, `invitations`, `machines`, `certificates`, `settings` |

A violation answers `400` with `ErrorCode` `InvalidName`. Only new names are checked: an organization created before the rule keeps working, and new projects can still be added to it, as long as the project name follows the rule. Existing documents are not migrated.

## Views

Views are implicit: the first write to a view creates it, and a write is never blocked because a view is not registered. The registry adds a description and a cheap list for clients.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/{organization}/{project}/views` | Reader; Administrator with `?discover=true` | `View[]`, `default` first |
| POST | `v1/{organization}/{project}/views` | Administrator | `{ "Name", "Description" }` → `View`; `409 ViewExists` when registered, or for `default` |
| PUT | `v1/{organization}/{project}/views/{view}` | Administrator | `{ "Description" }` → `View`; registers the view when needed, including `default` |

A `View` has `Name`, `Description`, `CreatedAt`, `CreatedBy`, `IsDefault`, and `IsRegistered`. `default` is always listed, also when it is not registered. Registrations live in the organization container, partition `{project}.meta`, id `view.{name}`.

`?discover=true` also lists every view that holds documents of the project (annotations, configurations, templates, schemas, and their history) with `IsRegistered = false`. It is a cross-partition `SELECT DISTINCT VALUE c.ViewName` over the organization container, so it is meant for an administrator's occasional check, not for every page load.

## Roles

`None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner`

| Operation | Needs |
| --- | --- |
| List members, set roles, remove members, manage invitations | `Administrator` on the access point |
| Give, change, or remove the role `Owner` | `Owner` |
| Leave an access point (remove yourself) | any membership |

Every access point keeps at least one `Owner`. The last owner can't be demoted, removed, or leave (`409`, `LastOwner`).

## Members

| Method | Route | Body | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points/{key}/members` | | `AccessPointMember[]`: `AccountId`, `UserName`, `Name`, `Role`; owners first |
| PUT | `v1/access/points/{key}/members/{accountId}` | `{ "Role": "Contributor" }` | `AccessPointMember` with the new role |
| DELETE | `v1/access/points/{key}/members/{accountId}` | | `204` |

* PUT sets the exact role, up or down. The account must already be a member (`404`, `MemberNotFound`); new members join through an invitation or through grant. Nobody can change their own role (`409`, `SelfRoleChange`); leave and get invited again instead. `None` is rejected with `400`.
* DELETE removes a member. With the caller's own account ID it means "leave".
* The older `POST v1/access/grant` and `POST v1/access/revoke` stay for `sform`. Grant only raises a role, and revoke must name the role the member holds.

## Invitations

An invitation offers a role on an access point to an email address. It is a Storyteller document (`inv.{id}` in the main partition of `core`). The identity provider only delivers the email.

| Method | Route | Who |
| --- | --- | --- |
| GET | `v1/access/points/{key}/invitations` | Administrator: all invitations of the access point, newest first |
| POST | `v1/access/points/{key}/invitations` | Administrator (Owner to offer `Owner`): `{ "Email", "Role", "ExpiresInDays" }` |
| POST | `v1/access/points/{key}/invitations/{id}/resend` | Administrator: sends the email again and restarts the validity |
| DELETE | `v1/access/points/{key}/invitations/{id}` | Administrator: revokes a pending invitation |
| GET | `v1/access/invitations/mine` | Any signed-in user: pending invitations for the email in the token |
| POST | `v1/access/invitations/{id}/accept` | The invitee: joins with the offered role |
| POST | `v1/access/invitations/{id}/decline` | The invitee |

**Lifecycle.** A new invitation is `Pending` and valid for 7 days by default (1 to 30, `Invitations:DefaultExpiresInDays` and `Invitations:MaxExpiresInDays`). It ends as `Accepted`, `Declined`, or `Revoked`. A pending invitation past its `ExpiresAt` is reported as `Expired`; it can still be resent or revoked, but not accepted (`409`, `InvitationExpired`). Answering an invitation that is no longer pending returns `409`, `InvitationNotPending`. Only one pending invitation per email and access point exists at a time (`409`, `InvitationExists`). Emails are trimmed and stored in lower case; a display-name form such as `Ada <ada@example.com>` is rejected with `400`.

**Accepting.** The invitee signs in first, so an account may not exist yet. Acceptance:

1. compares the email of the token (`preferred_username`, then `email`, `unique_name`, `upn`) with the invitation, ignoring case (`403`, `EmailMismatch`);
2. requires a verified email (`403`, `EmailNotVerified`): the token's `email_verified` claim decides when present. Without it, AuthKit tokens count as verified only when `Auth:AuthKit:RequireVerifiedEmail` is `false`; Entra ID tokens count as verified;
3. creates the account without memberships when it does not exist, with the name and email from the token;
4. joins the access point with the offered role, or keeps a higher role the account already has. The inviter's current role is not checked again: the invitation was authorized when it was created.

Accepting the same invitation twice returns the account without changes.

**Email delivery.** With `Auth:Provider = AuthKit` and `Auth:AuthKit:ApiKey`, Storyteller creates a WorkOS application invitation (`POST /user_management/invitations`, no organization), and WorkOS sends the email. Its link signs the invitee up even when AuthKit sign-up is disabled. A WorkOS application invitation can be accepted with any email address, which is why Storyteller always compares the email of its own invitation with the signed-in user. Resending revokes the WorkOS invitation and sends a new one, because WorkOS resends keep the old expiry. Revoking or declining also revokes the WorkOS invitation. A failed send is logged and the invitation is kept with `IsEmailSent = false`; share the admin UI link instead. Entra ID deployments and AuthKit without a management key never send emails.

## Accounts

`POST v1/access/account` creates the caller's account. With `Organization` and `Project` it also creates them and makes the caller their owner. Without both, it creates an account with no memberships, for a user who joins through invitations. Giving only one of them is rejected with `400`.

## Error codes

All errors use `ErrorResponse` with a stable `ErrorCode` and without exception details. See also [401 and 403](authentication.md#401-and-403).

| Status | `ErrorCode` | When |
| --- | --- | --- |
| `400` | `InvalidName` | A new organization, project, or view name breaks the [name rules](#names). |
| `403` | `AccessDenied` | The caller's role on the access point is too low. |
| `403` | `EmailMismatch`, `EmailNotVerified` | Accepting or declining with another or an unverified email. |
| `404` | `NotFound` | The access point, account, or invitation does not exist. |
| `404` | `MemberNotFound` | The account is not a member of the access point. |
| `409` | `LastOwner`, `SelfRoleChange` | Member rules above. |
| `409` | `InvitationExists`, `InvitationNotPending`, `InvitationExpired` | Invitation rules above. |
| `409` | `ViewExists` | The view is already registered, or is `default`. |
| `409` | `Conflict` | The membership or invitation changed concurrently twice; repeat the request. |
