# Access Points, Views, Members and Invitations

Organizations and projects are **access points**. An organization has the key `{organization}` and a project the key `{organization}.{project}`. Each access point keeps a map of account IDs to roles, and each account keeps the same map from its side. Both documents live in the main partition (`access`) of the `core` container, so every membership change replaces them together in one transactional batch, guarded by their ETags. A concurrent change of either document makes the batch fail; the change is then evaluated once more on fresh data, and a second failure answers `409 Conflict`.

An organization role does not grant access to the organization's projects. Each project has its own members, and can have a [description](#projects).

## Names

New organizations, projects, and views follow one rule: 2 to 63 characters from `a-z`, `0-9`, and `-`, starting with a letter or a digit (`^[a-z0-9][a-z0-9-]{1,62}\z`). Names become route segments, container names (`org.{organization}`), and parts of keys, where `.` separates the segments, so upper case and dots are not allowed. Some words are reserved because the API or the admin UI uses them in addresses:

| Kind | Reserved |
| --- | --- |
| Organization | `access`, `auth`, `login`, `callback`, `onboarding`, `invitations`, `account`, `orgs`, `unsupported` |
| Project and view | `access`, `views`, `members`, `invitations`, `machines`, `certificates`, `settings`, `key-values`, `audit`, `usage` |

A violation answers `400` with `ErrorCode` `InvalidName`. Only new names are checked: an organization created before the rule keeps working, and new projects can still be added to it, as long as the project name follows the rule. Existing documents are not migrated.

## Views

Views are implicit: the first write to a view creates it, and a write is never blocked because a view is not registered. The registry adds a description and a cheap list for clients.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/{organization}/{project}/views` | Reader, machines `Annotation.Read` or `Configuration.Read`; Administrator with `?discover=true` | `View[]`, `default` first |
| POST | `v1/{organization}/{project}/views` | Administrator | `{ "Name", "Description" }` → `View`; `409 ViewExists` when registered, or for `default` |
| PUT | `v1/{organization}/{project}/views/{view}` | Administrator | `{ "Description" }` → `View`; registers the view when needed, including `default` |

Every machine of the project lists the views, also one restricted to an annotation; without either scope it gets `403 AccessDenied`, for example "This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with.". Administrator operations (POST, PUT and `?discover=true`) need a user; machine credentials get `403` ("Managing views needs a user with the Administrator role."). PUT keeps the name as given: a legacy name that breaks the name rules (for example `Prod`) returns `400 InvalidName` instead of registering a different id. POST without a body (empty or `null`) returns `400`; PUT without one clears the description.

A `View` has `Name`, `Description`, `Created` (the [audit stamp](audit-stamps.md) of its registration, null for a view that is not registered), `IsDefault`, and `IsRegistered`. `default` is always listed, also when it is not registered. Registrations live in the organization container, partition `{project}.meta`, id `view.{name}`.

`?discover=true` also lists every view that holds documents of the project (annotations, configurations, templates, schemas, and their history) with `IsRegistered = false`. It is a cross-partition `SELECT DISTINCT VALUE c.ViewName` over the organization container, so it is meant for an administrator's occasional check, not for every page load.

## Projects

A project can have a description, like a view. An organization has none.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points` | An account | `AccessPoint[]`: the organizations and projects where the caller is `Administrator` or `Owner` |
| GET | `v1/access/points/{key}` | Administrator | `AccessPoint` of the organization or project |
| POST | `v1/access/points` | Owner of the organization when it exists | `{ "Organization", "Project", "ProjectDescription" }` → `AccessPoint` of the new project |
| PUT | `v1/access/points/{key}` | Administrator, users only | `{ "Description" }` → `AccessPoint` of the project |
| GET | `v1/access/projects` | An account, users only | `ProjectSummary[]`: the caller's projects, ordered by key |

Every route needs the scope `User.Impersonation`. An `AccessPoint` has `Key`, `AccessMap` (the members and their roles), `MachineAuthentication` and `Description`. POST creates the project, and the organization when it does not exist yet; the caller becomes the `Owner` of what it creates. In an existing organization it needs `Owner` of the organization (`403`), and an existing project answers `409 AccessPointExists`.

An access point carries the members and the machine authentication policy, so reading one needs `Administrator`, and `GET v1/access/points` lists only the access points the caller administers. Every member reads the descriptions of their projects with `GET v1/access/projects` instead, as every member reads the descriptions of views. A `ProjectSummary` has `Key` (`{organization}.{project}`), `Role` (the caller's role) and `Description`, and nothing of the members or the policies. The list holds every project where the account has a role above `None`; organizations are not listed, and a key whose project does not exist is left out. Without an account, `GET v1/access/points` and `GET v1/access/projects` answer `404`.

**The description.** It is trimmed, and a blank one is none: nothing is stored, and the project is returned without `Description`. The server has no length rule; the body limit of 8 KiB bounds it, and the admin UI takes 500 characters.

* `ProjectDescription` of `POST v1/access/points` and [`POST v1/access/account`](#accounts) describes the new project. An organization created with it gets none.
* PUT replaces the description of a project. A missing body (empty or `null`), `{}` or a blank `Description` clears it, as for views. The `Administrator` check comes first, so a key the caller does not administer answers `403`, also when the project does not exist. After it, an organization key answers `400` (`Access point key must be in organization.project format.`) and a body that is not a project update `400`. Debug builds (`DEV_AUTH`) skip the role check of users and answer `404` for a project that does not exist.
* PUT and `GET v1/access/projects` are for users: machine credentials get `403`.
* PUT changes only the description and keeps the members and the policies. It replaces the document guarded by its ETag; a concurrent change of the project (a member, a policy) makes it read the project again and retry once, and a second failure answers `409 Conflict`.

The description is stored on the project's access point document (`apt.{organization}.{project}` in the `access` partition of `core`). `GET v1/access/projects` reads only the keys and the descriptions of the caller's projects, in one query.

## Roles

Scopes describe machines, roles describe people. A user is authorized by the role on the access point only: the token must act for a user (the scope `User.Impersonation`, else `401` with no body; the reason is only logged), and data scopes in a user token grant nothing. Debug builds (`DEV_AUTH`) skip these checks of users; the checks of machines and the [restrictions of actors](#actors) run in every build. A machine has no role: its scopes decide, as described below the table.

`None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner`

| Operation | Needs |
| --- | --- |
| List members, set roles, remove members, manage invitations | `Administrator` on the access point |
| Give, change, or remove the role `Owner` | `Owner` |
| Leave an access point (remove yourself) | any membership |
| Read deleted annotations and the history of an annotation | `Reader` on the project; machines `Annotation.Read` |
| Delete, restore, and re-create annotations | `Contributor` on the project; machines `Annotation.Write` and `Annotation.Manage`. A machine re-creates a single key (`PUT …/annotations/{key}`) with `Annotation.Write` alone |
| Delete an annotation permanently (`DELETE …/annotations/deleted/{key}`) | `Administrator` on the project, users only: machine credentials get `403` |
| Export a view, import into a view, and read those jobs | `Administrator` on the project for users, without data scopes. Machines have no role and need `Configuration.Transfer` and the scopes of every included kind: to export, `Annotation.Read` for annotations and `Configuration.Read` for the other kinds; to import, the scopes of the direct writes: `Annotation.Write` and `Annotation.Manage` for annotations, `Configuration.Write` and `Schema.Write` for schemas, `Configuration.Write` and `Template.Write` for templates, `Configuration.Write` for configurations; a machine restricted to an annotation gets `403` `AnnotationRestricted` (see [export-import.md](export-import.md#access)) |
| Read key/value secrets, reveal them, export with secrets, resolve a configuration with secret bindings | `ContributorWithSecrets` on the project, and no scope gives it to a user; machines `Secrets.Read`, under the mTLS rule of the project (see [key-values.md](key-values.md#secret-delivery)). Never through an actor under `NoSecretReveal` (see [Actors](#actors)) |
| Write key/value secrets: set, delete, restore, protect, import with secrets | `ContributorWithSecrets` on the project; machines `Secrets.Write`, under the mTLS rule of the project. A restore answers no `403` for a secret: it reports the key as `Forbidden` |
| Change the key/value policy of a project (levels, mTLS rule, customer key, recipients, rotation) | `Administrator` on the project, users only: machine credentials get `403` |
| Set the [description of a project](#projects) | `Administrator` on the project, users only: machine credentials get `403` |

See [annotation-lifecycle.md](annotation-lifecycle.md#api) for the annotation routes.

The [MCP server](mcp.md) acts with the role of the signed-in person: its calls carry a Storyteller user token of that person, whose `act` names the server as the actor. The role decides as on a direct call, so an assistant never does more than the person may do, and the audit names the person, with the server's actor id in `Via` (see [Audit Stamps](audit-stamps.md#the-actor)). The actor's restrictions apply on top of the role (see [Actors](#actors)). The server offers a part of the API only (see [its limits](mcp.md#limits)).

The API reads schemas, templates, deleted annotations and what the comparison of views needs (the views, the configurations and their diffs) with `Reader`. The admin UI locks its **Modeling** page, where they are, below `Contributor`; that lock is a choice of the admin UI. Its **Export & Import** tab is locked below `Administrator`, as the API requires (see [export-import.md](export-import.md#administration-ui)).

Machines have no role; their [scopes](authentication.md#scopes-of-machines) decide. A machine is exactly one flat list of scopes, `MachineAccess.Scopes`, in two groups:

- **Runtime**: `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write`, `Secrets.Read` and `Secrets.Write`, for the per-document reads and writes and for secret values.
- **Management**: `Configuration.Transfer`, `Schema.Write`, `Template.Write`, `Annotation.Manage` and `KeyValue.Manage`, for the bulk and structural operations: the export and import of a view or of key/values, schema and template writes (also by an import), the bulk create, soft delete and restore of annotations (also by an import), and the protect and bulk restore of key/values.

A read needs `X.Read`, a write `X.Write`, and a management operation its runtime scope and its management scope. Creating a machine with `Annotation.Write`, `Configuration.Write` or `Secrets.Write` adds the matching `X.Read`. A scope counts only when it is stored on the machine and carried by its credentials; otherwise the operation answers `403 AccessDenied` with a message that names the scope, in every build. Routes for users only refuse every machine with `403 AccessDenied`, whatever its credentials carry.

The creator of a machine can use its credentials, so a user gives a machine no more than the user may do. Creating a machine, resetting it, replacing its federation rules or deleting it needs `Contributor`, `ContributorWithSecrets` when the machine has a `Secrets.*` scope, and `Administrator` when it has a management scope: a delete stops whatever runs with the scopes. These are role checks of users, so Debug builds skip them. Through an actor under `NoSecretReveal` they are refused for a machine with a `Secrets.*` scope, except the delete, in every build (see [Actors](#actors)).

A machine created with an `AnnotationKey` reaches only that annotation and its descendants, and gets `403` `AnnotationRestricted` for other annotations and for operations of the whole project (see [Annotation restriction of machines](authentication.md#annotation-restriction-of-machines)).

Every access point keeps at least one `Owner`. The last owner can't be demoted, removed, or leave (`409`, `LastOwner`).

## Actors

An actor is a service that calls the API for a person, such as the [MCP server](mcp.md). Only the deployment's configuration makes a service an actor (`Auth:TokenExchange:Actors`, see [Configuration of the exchange](authentication.md#configuration-of-the-exchange)); an actor has no project or role of its own. It exchanges the person's sign-in for a Storyteller user token (`POST v1/access/token`), whose `sub` is the person's account id and whose `act` names the actor, for example `mcp`. The API treats that token as the person:

- The person's role on the access point decides, as on a direct call. The token carries `User.Impersonation`, and Debug builds (`DEV_AUTH`) skip the role checks as for any user.
- The audit names the person and records the actor as `Via` (see [Audit stamps](audit-stamps.md#the-actor)).
- The actor's restrictions apply on top of the role, in every build, `DEV_AUTH` included. The API enforces the restrictions in the token together with those the actor has now, so a restriction added to an actor reaches tokens already issued once the API runs with the new settings.

The list of restrictions is closed, and has one entry, `NoSecretReveal`. Every actor has it unless its configuration sets `AllowSecrets: true`. A secret read through an assistant would enter the model's context, the client's transcript and the model provider, so `NoSecretReveal` takes the reading of secret values away from the person, also from a `ContributorWithSecrets`, an `Administrator` or an `Owner`:

- A reveal of a key/value pair or of one of its versions, a diff of secret versions and an export with secrets answer `403 ActorRestricted`. A reveal is refused before the pair is read, also for a public pair (see [Secret delivery](key-values.md#secret-delivery)).
- A resolved configuration is evaluated without secrets and carries the header `X-2S-Secrets-Withheld: actor-restricted` (see [inheritance.md](inheritance.md#headers)).
- Creating a machine with a `Secrets.*` scope, resetting such a machine and replacing its federation rules answer `403 ActorRestricted`, because the credentials or tokens of the machine read secrets. Deleting such a machine is allowed.
- Replacing an existing [trusted issuer](authentication.md#trusted-issuers) answers `403 ActorRestricted`, because new keys or pins would hand the tokens of the machines its rules name to another workload. Creating a trusted issuer and deleting one are allowed.
- Granting a role that reads secrets, `ContributorWithSecrets` or higher, answers `403 ActorRestricted`: by a grant (`POST v1/access/grant`), by a member's role change and by an invitation, because the person who gets the role could be the one acting through the assistant. Lower roles, revoking a role and removing a member are allowed.
- Writing secrets is not affected, because a write returns no value: the person's role decides a set, delete, restore, protect or import of secrets, as on a direct call.

The restriction takes away only what the role gives. A user whose role gives no secret reads, such as a `Reader`, gets `403 AccessDenied` through the actor, as on a direct call, and the role checks of machines and trusted issuers come before the restriction. Debug builds give every user the secret rights and skip those role checks, so there the answer is `ActorRestricted`. The refusal of a role that reads secrets comes before the role check of the grant, the role change or the invitation, so through the actor it answers `ActorRestricted` also to a person who may not grant that role. Invitations and role changes to `Reader` or `Contributor` stay open to an `Administrator` through an actor.

## Members

| Method | Route | Body | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points/{key}/members` | | `AccessPointMember[]`: `AccountId`, `UserName`, `Name`, `Role`; owners first |
| PUT | `v1/access/points/{key}/members/{accountId}` | `{ "Role": "Contributor" }` | `AccessPointMember` with the new role |
| DELETE | `v1/access/points/{key}/members/{accountId}` | | `204` |

* PUT sets the exact role, up or down. The account must already be a member (`404`, `MemberNotFound`); new members join through an invitation or through grant. Nobody can change their own role (`409`, `SelfRoleChange`); leave and get invited again instead. `None` and a missing body are rejected with `400`.
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
| GET | `v1/access/invitations/mine` | Any signed-in user: pending invitations for the verified email in the token |
| POST | `v1/access/invitations/{id}/accept` | The invitee: joins with the offered role |
| POST | `v1/access/invitations/{id}/decline` | The invitee |

**Lifecycle.** A new invitation is `Pending` and valid for 7 days by default (1 to 30, `Invitations:DefaultExpiresInDays` and `Invitations:MaxExpiresInDays`). It ends as `Accepted`, `Declined`, or `Revoked`. A pending invitation past its `ExpiresAt` is reported as `Expired`; it can still be resent or revoked, but not accepted (`409`, `InvitationExpired`). Answering an invitation that is no longer pending returns `409`, `InvitationNotPending`. Only one pending invitation per email and access point exists at a time (`409`, `InvitationExists`). Emails are trimmed and stored in lower case; a display-name form such as `Ada <ada@example.com>` is rejected with `400`.

**Accepting.** The invitee signs in first, so an account may not exist yet. Acceptance:

1. compares the email of the token (`preferred_username`, then `email`, `unique_name`, `upn`; under Keycloak only `email`, because `preferred_username` is a user name the realm may let its users choose) with the invitation, ignoring case (`403`, `EmailMismatch`);
2. requires a verified email (`403`, `EmailNotVerified`): the token's `email_verified` claim decides when present. Without it, AuthKit tokens count as verified only when `Auth:AuthKit:RequireVerifiedEmail` is `false`; Entra ID tokens count as verified; Keycloak tokens do not;
3. creates the account without memberships when it does not exist, with the name and email from the token;
4. joins the access point with the offered role, or keeps a higher role the account already has. The inviter's current role is not checked again: the invitation was authorized when it was created.

Accepting the same invitation twice returns the account without changes.

A call through an [actor](#actors) never counts as email-verified, whatever the token says: `GET v1/access/invitations/mine` answers an empty list, and accepting or declining answers `403` (`EmailMismatch` or `EmailNotVerified`). The invitee answers an invitation after signing in with the identity provider, not through an actor.

**Email delivery.** With `Auth:Provider = AuthKit` and `Auth:AuthKit:ApiKey`, Storyteller creates a WorkOS application invitation (`POST /user_management/invitations`, no organization), and WorkOS sends the email. Its link signs the invitee up even when AuthKit sign-up is disabled. A WorkOS application invitation can be accepted with any email address, which is why Storyteller always compares the email of its own invitation with the signed-in user. Resending revokes the WorkOS invitation and sends a new one, because WorkOS resends keep the old expiry. Revoking or declining also revokes the WorkOS invitation. A failed send is logged and the invitation is kept with `IsEmailSent = false`; share the admin UI link instead. Entra ID and Keycloak deployments, and AuthKit without a management key, never send emails.

## Accounts

`POST v1/access/account` creates the caller's account. With `Organization` and `Project` it also creates them and makes the caller their owner. Without both (`{}`, or both `null`), it creates an account with no memberships, for a user who joins through invitations. Giving only one of them is rejected with `400`. `ProjectDescription` is the [description](#projects) of the new project; the account and the organization get none, and a description that is not blank without `Organization` and `Project` is rejected with `400` (`A project description needs an organization and a project.`). The body is required: an empty body or `null` is rejected with `400` too (`The account is missing.`).

The account id comes from the caller's token under the deployment's provider: the lower-case `{tid}:{oid}` under Entra ID, and `sub` under AuthKit and Keycloak (see [Account ids](authentication.md#account-ids)). An invitation acceptance that creates the account uses the same id. A Storyteller user token of an [actor](#actors) names an account that already exists: the exchange never creates one.

## Error codes

All errors use `ErrorResponse` without exception details, and the errors below carry a stable `ErrorCode`. See also [401 and 403](authentication.md#401-and-403).

A route that takes a JSON body does not require a `Content-Type`, and answers `400` for an empty body, `null` or JSON that does not fit the documented type, unless it documents a default for a missing body. On the routes of this page, these `400`s name the problem in `Message` and have no `ErrorCode`. Each such route reads a limited number of bytes of its body, and answers `413` with `ErrorCode` `BodyTooLarge` for a larger body or a larger declared `Content-Length`, before it parses any JSON.

| Status | `ErrorCode` | When |
| --- | --- | --- |
| `400` | `InvalidName` | A new organization, project, or view name breaks the [name rules](#names). |
| `403` | `AccessDenied` | The caller's role on the access point is too low, a machine lacks a scope of the operation, or a machine calls an operation for users only (such as registering, describing or discovering views, a permanent delete, setting the description of a project or listing projects). |
| `403` | `ActorRestricted` | The token acts for the person through an [actor](#actors) whose restrictions forbid the operation (under `NoSecretReveal`: reading secret values, or giving out what reads them). |
| `403` | `EmailMismatch`, `EmailNotVerified` | Accepting or declining with another or an unverified email, or through an actor. |
| `404` | `NotFound` | The access point, account, or invitation does not exist. |
| `404` | `MemberNotFound` | The account is not a member of the access point. |
| `409` | `LastOwner`, `SelfRoleChange` | Member rules above. |
| `409` | `InvitationExists`, `InvitationNotPending`, `InvitationExpired` | Invitation rules above. |
| `409` | `ViewExists` | The view is already registered, or is `default`. |
| `409` | `AccessPointExists` | The project already exists. |
| `409` | `Conflict` | The membership, the invitation, or the project whose description is set changed concurrently twice; repeat the request. |
| `413` | `BodyTooLarge` | The body is larger than its route reads: 64 KiB for a view, 8 KiB for an account, a new project, a project update, a member role or an invitation. |
