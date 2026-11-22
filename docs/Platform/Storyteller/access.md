# Access Points, Views, Members and Invitations

Organizations and projects are **access points**. An organization has the key `{organization}` and a project the key `{organization}.{project}`. Each access point keeps a map of account IDs to roles, and each account keeps the same map from its side. Both documents live in the main partition (`access`) of the `core` container, so every membership change replaces them together in one transactional batch, guarded by their ETags, with its [access audit](access-audit.md) record. A concurrent change of either document makes the batch fail; the change is then evaluated once more on fresh data, and a second failure answers `409 Conflict`.

Every change of members, access points, machines, the policies of a project, trusted issuers and shared certificates is recorded in the [access audit](access-audit.md), in the same transaction as the change.

An organization role does not grant access to the organization's projects. Each project has its own members, and can have a [description](#projects) and [settings](#project-settings).

## Names

New organizations, projects, and views follow one rule: 2 to 63 characters from `a-z`, `0-9`, and `-`, starting with a letter or a digit (`^[a-z0-9][a-z0-9-]{1,62}\z`). Names become route segments, the first level of the partition key of the shared container `organizations`, and parts of keys, where `.` separates the segments, so upper case and dots are not allowed. Some words are reserved because the API or the admin UI uses them in addresses:

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

`?discover=true` also lists every view that holds documents of the project (annotations, configurations, templates, schemas, and their history) with `IsRegistered = false`. It is a cross-partition `SELECT DISTINCT VALUE c.ViewName` over the organization's partitions of the shared container `organizations` (filtered by `c.Organization`, with the organization as the key prefix), so it is meant for an administrator's occasional check, not for every page load.

## Projects

A project can have a description, like a view. An organization has none.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points` | An account | `AccessPoint[]`: the organizations and projects where the caller is `Administrator` or `Owner` |
| GET | `v1/access/points/{key}` | Administrator | `AccessPoint` of the organization or project |
| POST | `v1/access/points` | Owner of the organization when it exists | `{ "Organization", "Project", "ProjectDescription" }` → `AccessPoint` of the new project |
| PUT | `v1/access/points/{key}` | Administrator, users only | `{ "Description" }` → `AccessPoint` of the project |
| GET | `v1/access/projects` | An account, users only | `ProjectSummary[]`: the caller's projects, ordered by key |
| GET | `v1/access/exhibition` | A user, also without an account | `Exhibition`: the names and the description of the deployment's [exhibition](#exhibition) project; `404` when it is not offered |
| GET | `v1/access/points/{key}/audit` | Administrator, users only | `AccessAuditResponse`: the [access audit](access-audit.md#reading-the-trail) of the organization or project, newest first |

Every route needs the scope `User.Impersonation`. An `AccessPoint` has `Key`, `AccessMap` (the members and their roles), `MachineAuthentication` and `Description`, and a project also its [`Settings`](#project-settings). POST creates the project, and the organization when it does not exist yet; the caller becomes the `Owner` of what it creates. In an existing organization it needs `Owner` of the organization (`403`), and an existing project answers `409 AccessPointExists`. The access points, the owner's account and their access audit records are written in one transactional batch, so a failure leaves nothing half created; a project or organization created concurrently answers `409` (`AccessPointExists`, or `Conflict` after one retry).

An access point carries the members and the machine authentication policy, so reading one needs `Administrator`, and `GET v1/access/points` lists only the access points the caller administers. Every member reads the descriptions of their projects with `GET v1/access/projects` instead, as every member reads the descriptions of views. A `ProjectSummary` has `Key` (`{organization}.{project}`), `Role` (the caller's role), `Description` and `Settings` (the [project settings](#project-settings), the defaults at `Version` 0 when none were set), and nothing of the members or the policies. The list holds every project where the account has a role above `None`; organizations are not listed, and a key whose project does not exist is left out. Without an account, `GET v1/access/points` and `GET v1/access/projects` answer `404`.

**The description.** It is trimmed, and a blank one is none: nothing is stored, and the project is returned without `Description`. The server has no length rule; the body limit of 8 KiB bounds it, and the admin UI takes 500 characters.

* `ProjectDescription` of `POST v1/access/points` and [`POST v1/access/account`](#accounts) describes the new project. An organization created with it gets none.
* PUT replaces the description of a project. A missing body (empty or `null`), `{}` or a blank `Description` clears it, as for views. The `Administrator` check comes first, so a key the caller does not administer answers `403`, also when the project does not exist. After it, an organization key answers `400` (`Access point key must be in organization.project format.`) and a body that is not a project update `400`. Debug builds (`DEV_AUTH`) skip the role check of users and answer `404` for a project that does not exist.
* PUT and `GET v1/access/projects` are for users: machine credentials get `403`.
* PUT changes only the description and keeps the members, the policies and the settings. It replaces the document guarded by its ETag, in a batch with its access audit record when the description changes; a concurrent change of the project (a member, a policy) makes it read the project again and retry once, and a second failure answers `409 Conflict`.

The description is stored on the project's access point document (`apt.{organization}.{project}` in the `access` partition of `core`). `GET v1/access/projects` reads only the keys, the descriptions and the settings of the caller's projects, in one query.

## Project settings

The settings of a project say how every client shows it: the names, colors and icons of its seven annotation types, the time zone that new annotations start in, and the date format. They belong to the project, so every member sees the same in every browser, and every client can read them from the API.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/{organization}/{project}/settings` | Reader; machines `Annotation.Read` or `Configuration.Read`, also one restricted to an annotation | `ProjectSettings` with its `ETag`; `304` for a matching `If-None-Match` |
| PUT | `v1/{organization}/{project}/settings` | Administrator, users only | `ProjectSettings` → the stored `ProjectSettings` with its `ETag` |
| PATCH | `v1/{organization}/{project}/settings` | Administrator, users only | A JSON Merge Patch → the stored `ProjectSettings` with its `ETag` |

Every machine of the project reads them, also one restricted to an annotation, because they hold no annotation data; without either read scope it gets `403 AccessDenied` ("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with."). `GET v1/access/projects` returns the settings of every project of the caller in its `ProjectSummary` items, so a client gets all of them in one request, and `GET v1/access/points/{key}` returns those of a project in its `AccessPoint` (see [Projects](#projects)). The operations are in the tag `Access` of the OpenAPI document (`GetProjectSettings`, `SetProjectSettings`, `PatchProjectSettings`).

**The document.**

| Member | Content |
| --- | --- |
| `Types` | The appearance of each type the project changed, keyed by its lower-case type code (`rst`, `unt`, `sbt`, `usg`, `cnt`, `exe`, `uxe`). A type left out has its built-in appearance. An appearance (`ProjectTypeAppearance`) has `Label` (the singular name, such as `Company`), `Plural` (only with a `Label`; clients derive it from the label when it is absent), `Color` and `Icon`, each optional. |
| `DefaultTimeZone` | The IANA time zone id that new annotations start in, such as `Europe/Prague`. Absent for `UTC`. |
| `DateFormat` | How clients show dates: `yyyy-mm-dd`, `dd.mm.yyyy`, `dd/mm/yyyy`, `mm/dd/yyyy` or `d mmm yyyy` (`mmm` is the English abbreviation of the month). Absent for `yyyy-mm-dd`. |
| `Version` | Raised by every change, `0` before the first. Read-only. |
| `Modified` | The [audit stamp](audit-stamps.md) of the last change, absent before the first. Read-only. |

```json
GET v1/acme/billing/settings

200 OK
ETag: "3"
Cache-Control: private, no-cache

{
  "Types": {
    "sbt": { "Label": "Company", "Plural": "Companies", "Color": "pink", "Icon": "i-lucide-building-2" },
    "rst": { "Label": "Solution", "Color": "green" }
  },
  "DefaultTimeZone": "Europe/Prague",
  "DateFormat": "dd.mm.yyyy",
  "Version": 3,
  "Modified": { "At": "2026-11-15T10:00:00+00:00", "By": { "Kind": "Account", "Id": "user-1", "Name": "Ada" } }
}
```

The allowed values are the **catalog of project settings**, `src/Platform/Storyteller/conformance/project-settings.json`: the type codes, the built-in appearance of each type, 18 colors (the admin UI's palette, such as `pink`), 65 icons (Lucide names, such as `i-lucide-building-2`), the five date formats, the defaults, the length limits and the time zone ids of the tz database. The API embeds it (`ProjectSettingsCatalog` in `Abstractions.Annotations`), the TypeScript SDK exports it as constants and types, and its vectors pin the rules below for every client (see [Conformance](conformance.md#project-settings)).

**Defaults.** A project whose settings were never set answers `{ "Types": {}, "Version": 0 }`, never `404`: every type built-in, the time zone `UTC` and the date format `yyyy-mm-dd`. `UTC` and `yyyy-mm-dd` given explicitly are stored as given. Once written, the settings are never removed: a write that clears everything stores empty settings with a higher `Version`, so a tag never repeats.

**The time zone is a default of the clients.** The API never applies it: an annotation created without a `TimeZone` is stored without one. Clients start new annotations in it.

**Rules.** Every write is checked against the catalog (`ProjectSettingsJson.Read` and `ProjectSettingsRules`), and nothing is dropped silently:

| Rule | Refuses |
| --- | --- |
| `type-code` | A key of `Types` that is not a type code, or two keys that differ only in case |
| `label`, `plural` | A `Label` or a `Plural` longer than 40 UTF-16 code units after trimming |
| `control-character` | A `Label` or a `Plural` that holds a control character after trimming: a C0 or C1 control (U+0000–U+001F, U+007F–U+009F), the line or paragraph separator (U+2028, U+2029) or a bidirectional control (U+061C, U+200E, U+200F, U+202A–U+202E, U+2066–U+2069); the message names the field and the character |
| `plural-without-label` | A `Plural` without a `Label` |
| `color`, `icon`, `time-zone`, `date-format` | A value that is not in the catalog, such as the Windows zone `Central Europe Standard Time` |
| `unknown-member` | A member the document does not have, such as `Theme` |
| `case-collision` | Two members that differ only in case |
| `type` | A value of the wrong JSON type: a body, `Types` or a type that is not an object, another member that is not a string or `null` |
| `format` | A `Format` that is not `2s.project-settings.v1` |

- Member names, type codes, colors, icons, date formats and time zones match in any ASCII case and are stored in the catalog's spelling: `europe/prague` is stored as `Europe/Prague`, `SBT` as `sbt`.
- Labels and plurals are trimmed of Unicode white space first, so a tab or a line break at either end is removed and only one inside is refused. A blank label, plural, time zone or date format means none, and so does `null`. An appearance without any value is removed.
- `Version` and `Modified` are ignored, and `Format` may be the marker `2s.project-settings.v1` (the file shape of data sets), so a read or a settings file can be sent back as it is.
- A problem has `Path` (a JSON path in the canonical spelling, such as `$.Types.sbt.Color`), `Rule` and `Message`.

**ETag and conditions.** The tag is strong: the `Version` in quotes, such as `"7"`, and `"0"` before the first write. A browser on another origin cannot read `ETag` (see [CORS for browser clients](authentication.md#cors-for-browser-clients)), so it builds `If-Match` from the `Version` of the body it read: `If-Match: "7"`.

- GET answers with the `ETag` and `Cache-Control: private, no-cache`. `If-None-Match` compares weakly (`W/"7"` and `*` match too), and a match answers `304` without a body. A header that is not a list of entity tags is ignored, and the read answers `200`.
- PUT and PATCH take an optional `If-Match`: `*` (any version, as the settings always exist) or a list of tags. The comparison is strong: a weak tag, or a tag that is not a version in its plain spelling (`"07"`), never matches. A mismatch answers `412` with `ErrorCode` `SettingsPreconditionFailed`, "The project settings changed meanwhile; they are at version 7." and `Details: { "Version": 7 }`.
- A malformed `If-Match` (not `*` or a list of tags, `*` together with tags, or an empty value) answers `400 InvalidPrecondition`, and so does an `If-None-Match` on a write ("The project settings always exist; a write takes If-Match.").
- Without `If-Match`, the last write wins.

**PUT** replaces the settings: a type left out goes back to its built-in appearance, a missing `DefaultTimeZone` or `DateFormat` to its default. A body that is empty, not JSON, or holds a member twice answers `400 InvalidSettings` with one problem at `$`. A body sent as `application/merge-patch+json` or `application/json-patch+json` answers `415 InvalidContentType`, because a partial body would reset every type it leaves out (send a merge patch with PATCH); so does a `Content-Type` with several media types or one that is no media type.

**PATCH** merges a JSON Merge Patch (RFC 7396) into the stored settings and checks the result as PUT does:

- A member left out keeps its value, and `null` removes it: a type goes back to its built-in appearance, `DefaultTimeZone` to `UTC`, `DateFormat` to `yyyy-mm-dd`, and `"Types": null` resets every type.
- `Types` merge type by type, and the members of a type one by one. Member names and type codes match in any case.
- The names of the patch are checked as PUT checks those of a body, at the root, under `Types` and in a type: two names that differ only in case (`DateFormat` and `dateFormat`, `sbt` and `SBT`) are refused by `case-collision` (by `type-code` under `Types`), so a later one never overrides an earlier one and a `null` never removes what the same patch sets. A name that is not a member or a type code is refused also with `null`, which RFC 7396 alone would apply as nothing.
- The body is `application/merge-patch+json` (as documented), `application/json`, or has no `Content-Type`. Another media type answers `415 InvalidContentType` with `Accept-Patch: application/merge-patch+json`. A body that is empty, not JSON, or not a JSON object answers `400 PatchInvalid`.

```json
PATCH v1/acme/billing/settings
Content-Type: application/merge-patch+json
If-Match: "3"

{ "Types": { "sbt": { "Color": "rose" }, "rst": null }, "DateFormat": null }
```

**Answers of a write.**

- `200` with the stored settings and their `ETag`, also for a write that changes nothing: then the `Version` stays, nothing is written and no audit event is recorded.
- `400 InvalidSettings` lists every problem in `Details`, and its `Message` names the first one and their count, for example "The project settings have 2 problems; the first is $.Types.sbt.Color: …".
- A machine is refused first, before any other check: `403 AccessDenied` with "Changing the project settings needs a user with the Administrator role.". Then the role decides (`Administrator`), so a project the caller does not administer answers `403`, also when it does not exist; Debug builds (`DEV_AUTH`) skip the role check and answer `404` for a project that does not exist.
- A body above 16 KiB answers `413 BodyTooLarge`, before any JSON is parsed.
- `409 Conflict` when the project changed concurrently twice in a row; repeat the request.

**Storage.** The settings are the member `Settings` of the project's access point (`apt.{organization}.{project}` in the `access` partition of `core`), next to its `Description`. A project without them reads the defaults; nothing is migrated. A write reads the document with its ETag, checks the condition against the stored `Version`, and replaces the document under that ETag with only `Settings` changed. A concurrent change of the project (a member, the description, a policy) makes it read again and retry once (a patch then merges into the fresh state), and a second failure answers `409 Conflict`. The other writers of the access point (the members, the description, the key/value policy and the machine authentication policy) replace it under its ETag too and keep the settings.

**Audit.** Every change appends a `ProjectSettingsChange` event to the project's audit trail, the one of the key/values (see [Audit trail](key-values.md#audit-trail)): the caller's actor and channel, no view or key, and a `Detail` with the versions and the names of the changed parts, never their values, such as `v7→v8: Types.sbt, DefaultTimeZone`. Administrators read the events with `GET v1/{organization}/{project}/key-values/audit?operation=ProjectSettingsChange`. The event is written after the change; a failed append is logged as a warning and does not fail the write.

## Roles

Scopes describe machines, roles describe people. A user is authorized by the role on the access point only: the token must act for a user (the scope `User.Impersonation`, else `401` with no body; the reason is only logged), and data scopes in a user token grant nothing. Debug builds (`DEV_AUTH`) skip these checks of users, so they start only in the `Development` environment and outside the hosting platforms ([authentication.md](authentication.md#how-authkit-tokens-are-checked)); the checks of machines and the [restrictions of actors](#actors) run in every build. A unit test of `Api.Functions` turns the role check on the access point back on in a Debug build with `UserRoleChecks.Enforce()`, for its own async flow only (also the role that the scopes of a machine need); `User.Impersonation` and the secret rights keep their Debug behaviour there. CI runs `Api.Functions/test` in Release, where every check of users is on, and in Debug (`Platform.Storyteller.Api.Functions.UnitTests`); its tenant isolation tests call every function of another organization as the users and machines of one ([annotation-lifecycle.md](annotation-lifecycle.md#containers)). A machine has no role: its scopes decide, as described below the table.

`None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner`

| Operation | Needs |
| --- | --- |
| List members, set roles, remove members, manage invitations, read the [access audit](access-audit.md) | `Administrator` on the access point |
| Give, change, or remove the role `Owner` | `Owner` |
| Leave an access point (remove yourself) | any membership |
| Join the [exhibition](#exhibition) project as a `Reader` | any user, also without an account; users only: machine credentials get `403` |
| Read deleted annotations and the history of an annotation | `Reader` on the project; machines `Annotation.Read` |
| Delete, restore, and re-create annotations | `Contributor` on the project; machines `Annotation.Write` and `Annotation.Manage`. A machine re-creates a single key (`PUT` or `POST …/annotations/{key}`) with `Annotation.Write` alone; a `PATCH` never re-creates |
| Delete an annotation permanently (`DELETE …/annotations/deleted/{key}`) | `Administrator` on the project, users only: machine credentials get `403` |
| Export a view, import into a view, and read those jobs | `Administrator` on the project for users, without data scopes. Machines have no role and need `Configuration.Transfer` and the scopes of every included kind: to export, `Annotation.Read` for annotations and `Configuration.Read` for the other kinds; to import, the scopes of the direct writes: `Annotation.Write` and `Annotation.Manage` for annotations, `Configuration.Write` and `Schema.Write` for schemas, `Configuration.Write` and `Template.Write` for templates, `Configuration.Write` for configurations; a machine restricted to an annotation gets `403` `AnnotationRestricted` (see [export-import.md](export-import.md#access)) |
| Read the [inbox of a view](inbox.md), and accept or dismiss its items | `Contributor` on the project, users only: machine credentials get `403`, and no scope gives a machine the inbox. An item has a minimal role, `Contributor` or higher, and a caller below it does not find the item (`404`): the results of exports and imports are items for `Administrator` (see [inbox.md](inbox.md#access)) |
| Read key/value secrets, reveal them, export with secrets, resolve a configuration with secret bindings | `ContributorWithSecrets` on the project, and no scope gives it to a user; machines `Secrets.Read`, under the mTLS rule of the project (see [key-values.md](key-values.md#secret-delivery)). Never through an actor under `NoSecretReveal` (see [Actors](#actors)) |
| Write key/value secrets: set, delete, restore, protect, import with secrets | `ContributorWithSecrets` on the project; machines `Secrets.Write`, under the mTLS rule of the project. A restore answers no `403` for a secret: it reports the key as `Forbidden` |
| Change the key/value policy of a project (levels, mTLS rule, customer key, recipients, rotation) | `Administrator` on the project, users only: machine credentials get `403` |
| Set the [description of a project](#projects) | `Administrator` on the project, users only: machine credentials get `403` |
| Read the [settings of a project](#project-settings) | `Reader` on the project; machines `Annotation.Read` or `Configuration.Read`, also a machine restricted to an annotation |
| Change the settings of a project (`PUT` and `PATCH`) | `Administrator` on the project, users only: machine credentials get `403` before any other check |
| List the machines of a project and read one | `Contributor` on the project, whatever the scopes of the machines, users only: machine credentials get `403` |

See [annotation-lifecycle.md](annotation-lifecycle.md#api) for the annotation routes.

**A recent sign-in.** Some operations also need the user's sign-in to be recent: at most `Auth:StepUp:MaxAge` old, 10 minutes by default, on by default under AuthKit (see [Recent sign-in](authentication.md#recent-sign-in-step-up)). They are a grant or revoke, a member's role change, the removal of another member, a new invitation, creating or resetting a machine, replacing its federation rules or a trusted issuer, a shared certificate, the recipients of client-encrypted values, and every read or write of a secret value. A stale sign-in gets `401 StepUpRequired` there, and a resolved configuration without its secrets (`X-2S-Secrets-Withheld: step-up-required`); restore and purge report the secret keys as `Forbidden` with `StepUpRequired`. Leaving an access point needs no recent sign-in, and machines are never affected. The checks a new sign-in cannot fix come first, the role checks of the route and the actor's restriction included, so a user without the role still gets `403`; the role checks of grant, revoke, a role change, a removal and a new invitation (with its email, role and validity) run in the service, after the step-up.

The [MCP server](mcp.md) acts with the role of the signed-in person: its calls carry a Storyteller user token of that person, whose `act` names the server as the actor. The role decides as on a direct call, so an assistant never does more than the person may do, and the audit names the person, with the server's actor id in `Via` and the person's client app in `ViaClient` (see [Audit Stamps](audit-stamps.md#the-actor)). The restrictions of the actor and of the client app apply on top of the role (see [Actors](#actors)). The server offers a part of the API only (see [its limits](mcp.md#limits)).

The API reads schemas, templates, deleted annotations and what the comparison of views needs (the views, the configurations and their diffs) with `Reader`. The admin UI shows them to a Reader read-only on its **Modeling** page; its create, edit, delete and restore actions there need `Contributor`, as in the API, and **Delete permanently** `Administrator`. Its **Export & Import** tab is locked below `Administrator`, as the API requires (see [export-import.md](export-import.md#administration-ui)). Its **Machines** page and the detail of a machine show **No access** below `Contributor` without asking the API, so a Debug build, which skips the role check, shows a Reader no machine either.

Machines have no role; their [scopes](authentication.md#scopes-of-machines) decide. A machine is exactly one flat list of scopes, `MachineAccess.Scopes`, in two groups:

- **Runtime**: `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write`, `Secrets.Read` and `Secrets.Write`, for the per-document reads and writes and for secret values.
- **Management**: `Configuration.Transfer`, `Schema.Write`, `Template.Write`, `Annotation.Manage` and `KeyValue.Manage`, for the bulk and structural operations: the export and import of a view or of key/values, schema and template writes (also by an import), the bulk create, soft delete and restore of annotations (also by an import), and the protect and bulk restore of key/values.

A read needs `X.Read`, a write `X.Write`, and a management operation its runtime scope and its management scope. Creating a machine with `Annotation.Write`, `Configuration.Write` or `Secrets.Write` adds the matching `X.Read`. A scope counts only when it is stored on the machine and carried by its credentials; otherwise the operation answers `403 AccessDenied` with a message that names the scope, in every build. Routes for users only refuse every machine with `403 AccessDenied`, whatever its credentials carry.

The creator of a machine can use its credentials, so a user gives a machine no more than the user may do. Creating a machine, resetting it, replacing its federation rules or deleting it needs `Contributor`, `ContributorWithSecrets` when the machine has a `Secrets.*` scope, and `Administrator` when it has a management scope: a delete stops whatever runs with the scopes. These are role checks of users, so Debug builds skip them. Through an actor under `NoSecretReveal` they are refused for a machine with a `Secrets.*` scope, except the delete, in every build (see [Actors](#actors)). Creating, resetting and replacing the federation rules also need a [recent sign-in](authentication.md#recent-sign-in-step-up), whatever the scopes; deleting does not.

A machine created with an `AnnotationKey` reaches only that annotation and its descendants, and gets `403` `AnnotationRestricted` for other annotations and for operations of the whole project (see [Annotation restriction of machines](authentication.md#annotation-restriction-of-machines)).

Every access point keeps at least one `Owner`. The last owner can't be demoted, removed, or leave (`409`, `LastOwner`).

## Actors

An actor is a service that calls the API for a person, such as the [MCP server](mcp.md). Only the deployment's configuration makes a service an actor (`Auth:TokenExchange:Actors`, see [Configuration of the exchange](authentication.md#configuration-of-the-exchange)); an actor has no project or role of its own. It exchanges the person's sign-in for a Storyteller user token (`POST v1/access/token`), whose `sub` is the person's account id and whose `act` names the actor, for example `mcp`. The API treats that token as the person:

- The person's role on the access point decides, as on a direct call. The token carries `User.Impersonation`, and Debug builds (`DEV_AUTH`) skip the role checks as for any user.
- The audit names the person and records the actor as `Via` and the client app the person signed in to the actor with as `ViaClient` (see [Audit stamps](audit-stamps.md#the-actor)).
- The actor's restrictions apply on top of the role, in every build, `DEV_AUTH` included. A client app the actor does not trust also gets the actor's `UntrustedClientRestrictions`, and an app the deployment denies gets no token (see [Client apps of the exchange](authentication.md#client-apps-of-the-exchange)). The API enforces the restrictions in the token together with those the actor gives the client app now, so a restriction added to an actor reaches tokens already issued once the API runs with the new settings.
- The token carries the time of the person's sign-in at the actor (`auth_time`), so the [step-up](authentication.md#recent-sign-in-step-up) judges it as for a direct call. For reads the restriction comes first: a new sign-in does not lift it, so the person gets `ActorRestricted`, not `StepUpRequired`.

The list of restrictions is closed, and has two entries, `NoSecretReveal` and `ReadOnly`.

**`ReadOnly`** takes every change away from the person: every request other than a `GET`, `HEAD` or `OPTIONS` answers `403 ActorReadOnly`, except two `POST`s that only read, the lookup of deleted annotations and the export of key/values. It comes before the role checks and the step-up, and also refuses the writes that need no role: registering an account, a new organization, joining the [exhibition](#exhibition), leaving an access point. Production gives it to every client app of the MCP server that the deployment does not trust. It says nothing about secrets, which `NoSecretReveal` governs (see [Restrictions of actors](authentication.md#restrictions-of-actors)).

**`NoSecretReveal`**: every actor has it unless its configuration sets `AllowSecrets: true`, and `UntrustedClientRestrictions` can give it to the untrusted client apps of an actor that allows secrets. A secret read through an assistant would enter the model's context, the client's transcript and the model provider, so `NoSecretReveal` takes the reading of secret values away from the person, also from a `ContributorWithSecrets`, an `Administrator` or an `Owner`:

- A reveal of a key/value pair or of one of its versions, a diff of secret versions and an export with secrets answer `403 ActorRestricted`. A reveal is refused before the pair is read, also for a public pair (see [Secret delivery](key-values.md#secret-delivery)).
- A resolved configuration is evaluated without secrets and carries the header `X-2S-Secrets-Withheld: actor-restricted` (see [inheritance.md](inheritance.md#headers)).
- Creating a machine with a `Secrets.*` scope, resetting such a machine and replacing its federation rules answer `403 ActorRestricted`, because the credentials or tokens of the machine read secrets. Deleting such a machine is allowed.
- Replacing an existing [trusted issuer](authentication.md#trusted-issuers) answers `403 ActorRestricted`, because new keys or pins would hand the tokens of the machines its rules name to another workload. Creating a trusted issuer and deleting one are allowed.
- Granting a role that reads secrets, `ContributorWithSecrets` or higher, answers `403 ActorRestricted`: by a grant (`POST v1/access/grant`), by a member's role change and by an invitation, because the person who gets the role could be the one acting through the assistant. Lower roles, revoking a role and removing a member are allowed.
- Writing secrets is not affected, because a write returns no value: the person's role decides a set, delete, restore, protect or import of secrets, as on a direct call. A set or an import through the actor never compares a secret with the stored value, so it does not tell whether a guess was right: the same secret makes a new version, and an import reports it as `Update`.

The restriction takes away only what the role gives. A user whose role gives no secret reads, such as a `Reader`, gets `403 AccessDenied` through the actor, as on a direct call, and the role checks of machines and trusted issuers come before the restriction. Debug builds give every user the secret rights and skip those role checks, so there the answer is `ActorRestricted`. The refusal of a role that reads secrets comes before the role check of the grant, the role change or the invitation, so through the actor it answers `ActorRestricted` also to a person who may not grant that role. Invitations and role changes to `Reader` or `Contributor` stay open to an `Administrator` through an actor.

## Members

| Method | Route | Body | Result |
| --- | --- | --- | --- |
| GET | `v1/access/points/{key}/members` | | `AccessPointMember[]`: `AccountId`, `UserName`, `Name`, `Role`; owners first |
| PUT | `v1/access/points/{key}/members/{accountId}` | `{ "Role": "Contributor" }` | `AccessPointMember` with the new role |
| DELETE | `v1/access/points/{key}/members/{accountId}` | | `204` |
| POST | `v1/access/exhibition/join` | | The caller's `Account`, a `Reader` of the [exhibition](#exhibition) project |

* PUT sets the exact role, up or down. The account must already be a member (`404`, `MemberNotFound`); new members join through an invitation, through grant, or, in the exhibition project, through its join. Nobody can change their own role (`409`, `SelfRoleChange`); leave and get invited again instead. `None` and a missing body are rejected with `400`.
* DELETE removes a member. With the caller's own account ID it means "leave".
* POST makes the caller a `Reader` of the deployment's [exhibition](#exhibition) project. It is the only way into an existing project without an invitation or a grant.
* PUT, and DELETE of another member, need a [recent sign-in](authentication.md#recent-sign-in-step-up) (`401 StepUpRequired` otherwise); leaving does not.
* The older `POST v1/access/grant` and `POST v1/access/revoke` stay for `sform`. Grant only raises a role, and revoke must name the role the member holds. The caller's own account is checked, as for the other routes; the `CreatedById` of the body is not read.
* Every change is recorded in the [access audit](access-audit.md): `MemberGranted`, `MemberRevoked`, `MemberRoleChanged`, `MemberRemoved`, with the role before and after and the actor (with `Via` and `ViaClient` through an actor). A request that changes nothing records nothing.

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

Creating an invitation needs a [recent sign-in](authentication.md#recent-sign-in-step-up) of the Administrator (`401 StepUpRequired` otherwise), because it grants a role once accepted; resending and revoking do not.

**Lifecycle.** A new invitation is `Pending` and valid for 7 days by default (1 to 30, `Invitations:DefaultExpiresInDays` and `Invitations:MaxExpiresInDays`). It ends as `Accepted`, `Declined`, or `Revoked`. A pending invitation past its `ExpiresAt` is reported as `Expired`; it can still be resent or revoked, but not accepted (`409`, `InvitationExpired`). Answering an invitation that is no longer pending returns `409`, `InvitationNotPending`. Only one pending invitation per email and access point exists at a time (`409`, `InvitationExists`). Emails are trimmed and stored in lower case; a display-name form such as `Ada <ada@example.com>` is rejected with `400`.

**Accepting.** The invitee signs in first, so an account may not exist yet. Acceptance:

1. compares the email of the token (`preferred_username`, then `email`, `unique_name`, `upn`; under Keycloak only `email`, because `preferred_username` is a user name the realm may let its users choose) with the invitation, ignoring case (`403`, `EmailMismatch`);
2. requires a verified email (`403`, `EmailNotVerified`): the token's `email_verified` claim decides when present. Without it, AuthKit tokens count as verified only when `Auth:AuthKit:RequireVerifiedEmail` is `false`; Entra ID tokens count as verified; Keycloak tokens do not;
3. creates the account without memberships when it does not exist, with the name and email from the token;
4. joins the access point with the offered role, or keeps a higher role the account already has. The inviter's current role is not checked again: the invitation was authorized when it was created.

Accepting the same invitation twice returns the account without changes. The join is recorded in the [access audit](access-audit.md) as `MemberJoined`, by the invitee, with the invitation and its sender.

A call through an [actor](#actors) never counts as email-verified, whatever the token says: `GET v1/access/invitations/mine` answers an empty list, and accepting or declining answers `403` (`EmailMismatch` or `EmailNotVerified`). The invitee answers an invitation after signing in with the identity provider, not through an actor.

**Email delivery.** With `Auth:Provider = AuthKit` and `Auth:AuthKit:ApiKey`, Storyteller creates a WorkOS application invitation (`POST /user_management/invitations`, no organization), and WorkOS sends the email. Its link signs the invitee up even when AuthKit sign-up is disabled. A WorkOS application invitation can be accepted with any email address, which is why Storyteller always compares the email of its own invitation with the signed-in user. Resending revokes the WorkOS invitation and sends a new one, because WorkOS resends keep the old expiry. Revoking or declining also revokes the WorkOS invitation. A failed send is logged and the invitation is kept with `IsEmailSent = false`; share the admin UI link instead. Entra ID and Keycloak deployments, and AuthKit without a management key, never send emails.

## Exhibition

The exhibition is the demo project of a deployment: a project with the data of a fictional company, which any signed-in user may join as a `Reader`, without an invitation. The onboarding of the admin UI offers it to a new user, and its Organizations page to a user who has an account and is no member of it. A deployment has one exhibition or none, and the exhibition is the view `default` of its project. `sform` and the MCP server have no command or tool for it.

| Method | Route | Needs | Result |
| --- | --- | --- | --- |
| GET | `v1/access/exhibition` | A user, also without an account | `Exhibition`: `Organization`, `Project` and `Description`; `404` when the exhibition is not offered |
| POST | `v1/access/exhibition/join` | A user, also without an account | The caller's `Account`, with the membership; `404` when the exhibition is not offered, `409 Conflict` |

Both routes are for users only: they need the scope `User.Impersonation`, and machine credentials get `403 AccessDenied`. Neither takes a request body. The code is in `Api.Functions/src/V1/ExhibitionHttp.cs` and `Api.Functions/src/Exhibitions/`.

### Settings

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Exhibition:Organization` | `Exhibition__Organization` | empty | The organization of the exhibition project |
| `Exhibition:Project` | `Exhibition__Project` | empty | The exhibition project |
| `Exhibition:Owner` | `Exhibition__Owner` | empty | The id of the account that owns the exhibition project: the `--owner` of its seed |

- **All three or none.** With all three, the deployment has an exhibition. With none, it has none. A blank value counts as not set.
- **The start validation.** The API does not start with one or two of the three, with an organization or a project that is not a valid [name](#names), or with an owner that has white space around it. The message names the three settings, never their values.
- **Compared as written.** The API neither trims the values nor changes their case. A valid name is lower-case. An account id has no white space around it, because a registration and the seed trim it, so an owner pasted with a space or a line break would never match; it stops the start instead.
- **Where they are set.** In production, `main.bicep` writes the three from its parameters `exhibitionOrganization`, `exhibitionProject` and `exhibitionOwner`, and leaves them out unless all three are set ([Deployment README](../../../src/Platform/Deployment/README.md#parameters)). `local.settings.json` has none of them (see [local development](../development.md#the-exhibition)).

### The owner

The join makes every caller a member of the project that the settings name, and anyone who signs in may register a free organization name and becomes its `Owner`. Without a check, settings that are live before the seed (a wrong order of the steps, a typo, a self-hosted deployment) would hand the exhibition to the first person who registers the names: every newcomer's onboarding would offer their project, and they would list the name and the email of everyone who joins.

So both routes offer and join the exhibition only while both of these hold:

1. The account of `Exhibition:Owner` is the `Owner` of the project. A lower role is not enough.
2. No other account is an `Administrator` or an `Owner` of the project.

The owner's role alone proves nothing: an `Owner` makes any existing account an `Owner` without its consent (`POST v1/access/grant`). Whoever registered the names first could add the configured account to a project of their own, and go on managing it. The second condition closes that:

- A second `Administrator` or `Owner` switches the exhibition off: both routes answer `404` and log a Warning, until that role is removed or lowered.
- A member below `Administrator` does not: the visitors, and a `Contributor` or a `ContributorWithSecrets` who writes the content.
- Every request reads the access point of the project, so a change of its members counts from the next request. This is not a check of the caller's role, so Debug builds (`DEV_AUTH`) run it too.

The rule tells who manages the project now, not who created it: the access map does not say who created a project. What somebody wrote into a project before they left it, such as the description and the content, stays. So the project is seeded before the settings are set (see [Turning it on](#turning-it-on)).

### When the exhibition is not offered

Both routes answer `404` without a body, and so without an `ErrorCode`, in four cases. A client cannot tell them apart; the log can:

| Case | Log |
| --- | --- |
| The deployment has no `Exhibition` settings | Nothing: every newcomer of such a deployment asks |
| The project of the settings does not exist | Warning: *"The exhibition is not offered: the project of Exhibition:Organization and Exhibition:Project doesn't exist. Seed the exhibition, or correct the settings."* |
| The account of `Exhibition:Owner` is not the `Owner` of the project | Warning: *"The exhibition is not offered: the account of Exhibition:Owner is not the Owner of the project of Exhibition:Organization and Exhibition:Project. Seed the exhibition with that owner, or correct the settings."* |
| Another account is an `Administrator` or an `Owner` of the project | Warning: *"The exhibition is not offered: the account of Exhibition:Owner is not the only Administrator or Owner of the project of Exhibition:Organization and Exhibition:Project. Remove the other Administrators and Owners or lower their roles, or correct the settings."* |

The cases are checked in this order, and a request logs one Warning. A Warning names the settings, never their values. The join checks them before it writes anything, so nothing is created for an exhibition that cannot be joined.

### Reading it

`GET v1/access/exhibition` answers `200` with an `Exhibition`: `Organization` and `Project`, as the settings name them, and `Description`, the [description](#projects) of the project when it has one. It works before the caller's account exists, and writes nothing.

### Joining

`POST v1/access/exhibition/join` makes the caller a `Reader` of the exhibition project. After the checks above:

1. **The account id** is the id a registration uses, as for [`POST v1/access/account`](#accounts) and the acceptance of an invitation: the lower-case `{tid}:{oid}` under Entra ID, and `sub` under AuthKit and Keycloak.
2. **The account** is created when the caller has none: without an organization and without memberships, with the user name and the name that a registration takes from the token. A token without the names answers `401`, as for `POST v1/access/account`, and nothing is created: an account with empty names would be a blank row of the member list. A caller who has an account needs no names. When the create fails, the route reads the account again. An account that exists now was created by a concurrent request of the same caller, and the join goes on; otherwise the failure of the create is the answer.
3. **The membership.** The account joins the project as `Reader`. The join is idempotent, and a higher role stays: a `Contributor` of the project who calls the route stays a `Contributor`. The role and the project never come from the request. The membership is the project's only: the account gets no role in the organization.
4. **Retries.** Every newcomer writes the same document, the access point of the project, and a membership change gives up after its second attempt with `409 Conflict`. The route tries such a join again up to three more times, each time after a random delay of 50 to 250 ms. Then it answers `409` with `ErrorCode` `Conflict`: repeat the request. Any other failure is answered at once.
5. **The answer** is `200` with the caller's `Account`, whose `AccessMap` holds the project.

The account and the membership are two writes. A join that fails after it created the account leaves an account without memberships. A second call joins, and creates nothing.

Through an [actor](#actors), such as the MCP server, both routes work as for the person, also under `NoSecretReveal`: an actor may give `Reader`, and its token names an account that exists. Under `ReadOnly` the join answers `403 ActorReadOnly`; the read of the exhibition still works.

### What a visitor reads

A visitor is a `Reader` of the project, with no role in its organization. Through the API, a `Reader` reads:

- the annotations, their history and the deleted annotations;
- the configurations with their versions, also resolved: a public key/value pair binds, and a binding of a secret stays as stored;
- the schemas and the templates with their versions, and the binding catalog;
- the views of the project;
- the key/value pairs without the values of secrets, with their versions, and an export without secrets;
- the key/value policy of the project and its recipients. The policy names the project's key in the vault of the deployment (`KeyId`: the URI of the vault and the name of the key, no key material).

A `Reader` reads no member, invitation or machine, no key/value audit or usage and no secret value, and changes nothing.

- **Names in the versions.** A stamp carries the name and the account id of whoever made the change ([audit stamps](audit-stamps.md)), and the versions carry stamps. So change the exhibition through its seed, which writes as `System`: a change by hand shows the name of its author to every visitor. The same holds for a delete: the deleted annotations carry who deleted them, which the admin UI's **Deleted** tab shows in its **By** column and in the details.
- **Other visitors.** A visitor sees no other member. The owner sees every visitor in the list of members, with the name and the user name, which is usually the email.
- **The admin UI shows the same.** A visitor browses its **Modeling** page read-only: the schemas and the templates with their versions, without an action that changes them. Its **Export & Import** tab stays locked below `Administrator`, as in the API (see [Roles](#roles)).
- **Leaving.** A visitor leaves with **Leave project** on the Settings of the project, which the admin UI offers every member but the only Owner. It calls `DELETE v1/access/points/{key}/members/{own account id}`, because any member may remove themselves (see [Members](#members)). The account stays, and a later join adds the membership again: the Organizations page of the admin UI offers the exhibition again.

### Limits

- Every visitor adds one entry to the access map of the project: an account id and a role, about 50 bytes. A Cosmos DB item holds 2 MB, and every join replaces the whole document. At the limit of the item, a join fails with `500`.
- The list of members is not paged.
- This fits a demo with thousands of visitors. It is not reviewed beyond that.
- The exhibition holds what its seed holds (see [the set `exhibit`](data-sets.md#exhibit)).

### Turning it on

Seed first, then set the settings:

1. Seed the project with the account of `Exhibition:Owner` as the owner. The built-in data set [`exhibit`](data-sets.md#exhibit) is made for it: `2sInitDb seed exhibit --organization <organization> --project <project> --owner <account id>`. The seed creates the organization and the project with that account as their `Owner`, and it refuses an organization or a project that exists and that the account does not own or administer (see [The owner and the creation of the target](data-sets.md#the-owner-and-the-creation-of-the-target)).
2. Set the three settings. The API reads them at its start.

Until the seed has run, the names are free for anyone who signs in. Settings that are set before the seed give nothing: every request answers `404` and logs a Warning. A visitor reads the project from the moment the settings are live, so the content is complete before them.

The steps of production are in the runbook, [Turning the exhibition on](../Deployment/runbook-azure-production.md#turning-the-exhibition-on); those of a local API are in [The exhibition](../development.md#the-exhibition). Removing the three settings turns the exhibition off: the project and its members stay.

## Accounts

`POST v1/access/account` creates the caller's account. With `Organization` and `Project` it also creates them and makes the caller their owner. Without both (`{}`, or both `null`), it creates an account with no memberships, for a user who joins through invitations. Giving only one of them is rejected with `400`. `ProjectDescription` is the [description](#projects) of the new project; the account and the organization get none, and a description that is not blank without `Organization` and `Project` is rejected with `400` (`A project description needs an organization and a project.`). The body is required: an empty body or `null` is rejected with `400` too (`The account is missing.`).

An account also comes from two other calls, which create it without memberships when the caller has none: the acceptance of an [invitation](#invitations), and the join of the [exhibition](#exhibition), which then makes it a `Reader` of the exhibition project. `POST v1/access/account` answers `400` without a body to a caller who has an account. So a user whose account comes from an invitation or from the exhibition creates an organization with [`POST v1/access/points`](#projects).

The account id comes from the caller's token under the deployment's provider: the lower-case `{tid}:{oid}` under Entra ID, and `sub` under AuthKit and Keycloak (see [Account ids](authentication.md#account-ids)). An invitation acceptance or a join of the exhibition that creates the account uses the same id. A Storyteller user token of an [actor](#actors) names an account that already exists: the exchange never creates one.

## Error codes

All errors use `ErrorResponse` without exception details, and the errors below carry a stable `ErrorCode`. `Details`, when an error has them, is any JSON value: an object (such as `Details.Version` of `SettingsPreconditionFailed`) or an array (the problems of `InvalidSettings`). See also [401 and 403](authentication.md#401-and-403).

A route that takes a JSON body does not require a `Content-Type`, and answers `400` for an empty body, `null` or JSON that does not fit the documented type, unless it documents a default for a missing body. On the routes of this page, these `400`s name the problem in `Message` and have no `ErrorCode`, except those of the [project settings](#project-settings), which carry `InvalidSettings` (`PatchInvalid` for a merge patch). Each such route reads a limited number of bytes of its body, and answers `413` with `ErrorCode` `BodyTooLarge` for a larger body or a larger declared `Content-Length`, before it parses any JSON.

The `404` of the two [exhibition](#exhibition) routes has no body, and so no `ErrorCode`: the deployment has no exhibition, or does not offer it.

| Status | `ErrorCode` | When |
| --- | --- | --- |
| `400` | `InvalidName` | A new organization, project, or view name breaks the [name rules](#names). |
| `400` | `InvalidSettings` | [Project settings](#project-settings) that break a rule of the catalog, or a settings body that is empty or not JSON. `Details` list every problem with its `Path`, `Rule` and `Message`. |
| `400` | `InvalidPrecondition` | A settings write with a malformed `If-Match`, or with an `If-None-Match`. |
| `400` | `PatchInvalid` | A merge patch of the project settings that is empty, not JSON, or not a JSON object. |
| `401` | `StepUpRequired` | The operation needs a [recent sign-in](authentication.md#recent-sign-in-step-up) and the user's is older than `Auth:StepUp:MaxAge`: a grant, a revoke, a role change, the removal of another member, a new invitation, machine credentials, secret values. `Details.MaxAge` and the `WWW-Authenticate` header name the window in seconds; sign in again and repeat the request. |
| `403` | `AccessDenied` | The caller's role on the access point is too low, a machine lacks a scope of the operation, or a machine calls an operation for users only (such as registering, describing or discovering views, a permanent delete, setting the description of a project, changing its settings, listing projects, or reading or joining the exhibition). |
| `403` | `ActorRestricted` | The token acts for the person through an [actor](#actors) whose restrictions forbid the operation (under `NoSecretReveal`: reading secret values, or giving out what reads them). |
| `403` | `ActorReadOnly` | The token acts for the person through an [actor](#actors) with a client app that may only read (`ReadOnly`), and the operation changes something; before the role checks. |
| `403` | `EmailMismatch`, `EmailNotVerified` | Accepting or declining with another or an unverified email, or through an actor. |
| `404` | `NotFound` | The access point, account, or invitation does not exist. |
| `404` | `MemberNotFound` | The account is not a member of the access point. |
| `409` | `LastOwner`, `SelfRoleChange` | Member rules above. |
| `409` | `InvitationExists`, `InvitationNotPending`, `InvitationExpired` | Invitation rules above. |
| `409` | `ViewExists` | The view is already registered, or is `default`. |
| `409` | `AccessPointExists` | The project already exists. |
| `409` | `Conflict` | The membership, the invitation, or the project whose description or settings are set changed concurrently twice; repeat the request. The join of the [exhibition](#exhibition) answers it only after it tried again three times. |
| `409` | `InboxItemClosed`, `InboxItemNotAction` | An item of the [inbox of a view](inbox.md#accept-and-dismiss) was already accepted or dismissed; or the accepted item is a notification or a message, and only an action can be accepted. |
| `412` | `SettingsPreconditionFailed` | The `If-Match` of a settings write names another version; `Details.Version` is the current one. |
| `501` | `InboxActionNotAvailable` | The deployment has no handler for the action of the accepted [inbox item](inbox.md#accept-and-dismiss); the item stays open. No handler is registered today, so every acceptance of an action answers it. |
| `503` | `AccessAuditUnavailable` | The identity provider replaced the client secret of a machine, but the [access audit](access-audit.md) record of the reset could not be stored; the new secret is withheld, reset the machine again. |
| `413` | `BodyTooLarge` | The body is larger than its route reads: 64 KiB for a view, 16 KiB for the project settings, 8 KiB for an account, a new project, a project update, a member role or an invitation. |
| `415` | `InvalidContentType` | A `PATCH` of the project settings with a media type other than `application/merge-patch+json` or `application/json` (a `PATCH` without a `Content-Type` is read as a merge patch); `Accept-Patch` names the patch format. A `PUT` of the project settings with `application/merge-patch+json` or `application/json-patch+json`. |
