# Description of a Project

Linear: [424-78 Add description to project in 2S platform](https://linear.app/424fun/issue/424-78/add-description-to-project-in-2s-platform)

## Problem

A view has a description, a project has none. A project only has a key (`{organization}.{project}`), so a person who opens an organization in the admin UI sees a list of names and has to know from elsewhere what each project holds. The issue asks:

- for a description of a project, like the description of a view;
- for a description of every project in all the mock data of the admin UI, using the texts offered in a comment on the issue where they exist;
- for the change in the admin UI (`src/Platform/ui.admin/src/ui.admin.esproj`) and in the Storyteller API (`src/Platform/Storyteller/Api.Functions/src/Api.Functions.csproj`).

The inputs to this spec are:

- the issue and its comment with descriptions for the 14 showcase projects of [424-77](https://linear.app/424fun/issue/424-77/new-mock-data-for-admin-ui);
- the CodeRabbit coding plan attached to the issue (`2026-10-21 Add description to project in 2S platform.md`), used as a second opinion and a source of ideas, not as the decision. Section 12 lists where this spec decides differently.

The branch starts from `feat/424-77` (the showcase mock data, PR #123, not merged yet), because the issue describes the showcase projects. Line numbers refer to that tree (`e89894aa`).

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The project document

- A project is an access point. `AccessPointEntity` (`Backend.CosmosDb/src/Entities/Access/AccessPointEntity.cs`) lives in the `core` container, partition `access`, id `apt.{Key}`, and holds `Key`, `AccessMap`, `MachineAuthentication` and `KeyValues`. The Cosmos client ignores null values (`Backend.CosmosDb/src/CosmosClientProvider.cs:34-37`), so a new nullable property is absent from old documents and from new documents without a value. Point reads deserialize with System.Text.Json, queries with the SDK serializer; both read a missing property as null.
- The public model `AccessPoint` (`Abstractions.Access/src/Model/AccessPoint.cs`) has `Key`, `AccessMap` and `MachineAuthentication`; `IAccessPoint` has the same members. `AccessMappings.ToAccessPoint` (`Backend.CosmosDb/src/AccessMappings.cs:23-28`) maps the entity; `ToEntity` (`:30-35`) is not used.
- Four runtime writers replace the document:
  - membership changes (`UpdateMembershipAsync` in `Backend.CosmosDb/src/Accessing/CosmosAccessService.cs:673-748`, called `CosmosAccessService` below) read both documents with their ETags (`TryReadItemWithETagAsync`, `:684-691`) and replace them in one transactional batch with `pointEntity with { AccessMap = … }`, retrying once on `412`;
  - the key/value policy (`CosmosKeyValuePolicyStore.SetAsync`, `Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyStore.cs:51-92`) replaces `entity with { KeyValues = policy }` with `IfMatchEtag`;
  - the machine authentication policy (`CosmosMachineAuthenticationPolicyStore.SetAsync`, `Backend.CosmosDb/src/Accessing/CosmosMachineAuthenticationPolicyStore.cs:53-74`) reads without an ETag and upserts `entity with { MachineAuthentication = policy }`;
  - `CreateAccessPointAsync` creates it.

  These four copy the entity with `with`, so a new property survives their writes. A fifth writer is the DbCreator seed (`Backend.CosmosDb/src/Data.cs:58-62`, `Create.BaseAccessPoints`), which upserts fresh documents for `42dotnet` and `42dotnet.main` each time it runs. It already drops members added later, `KeyValues` and the machine policy of those two documents; it would drop a description too.

### Creating a project

- `POST v1/access/points` (`Api.Functions/src/V1/AccessHttp.cs:168-199`, called `AccessHttp.cs` below) reads `AccessPointCreate` (`Backend.Core/src/Accessing/Model/AccessPointCreate.cs`: `Organization`, `Project`, `OwnerId`) directly as its body under `RequestBodyLimits.Control` (8 KiB), and sets `OwnerId` to the caller. `CosmosAccessService.CreateAccessPointAsync` (`:162-227`) creates the organization when it is new (`:188-193`) and then the project (`:204-210`).
- `POST v1/access/account` (`AccessHttp.cs:74-127`) reads the public `Api.Functions/src/V1/Models/AccountCreate.cs` (`Organization`, `Project`), rejects one without the other, and maps it to the internal `Backend.Core/src/Accessing/Model/AccountCreate.cs`. `CreateAccountAsync` (`:51-96`) passes organization and project to `CreateAccessPointAsync` (`:75-80`). The admin UI's onboarding uses this route.

### Reading a project

- `GET v1/access/points/{key}` (`AccessHttp.cs:147-166`) needs `Administrator`, because the access point carries the `AccessMap` (the members, which only Administrators list) and the machine policy.
- `GET v1/access/points` (`:129-145`, `GetAccessPointsAsync`, `CosmosAccessService.cs:110-147`) returns only the access points where the caller is `Administrator` or `Owner`.
- Nothing else returns the project document. A `Reader` or `Contributor` of a project knows it only from the `AccessMap` of their own account (`GET v1/access/account`).

### The description of a view, the model to follow

- `View`, `ViewCreate` and `ViewUpdate` (`Abstractions.Annotations/src/Model/View.cs`) have `string? Description`. `CosmosViewService.NormalizeDescription` (`Backend.CosmosDb/src/Annotating/CosmosViewService.cs:114-117`) trims it and stores a blank value as null. The server has no length rule; the body limit `RequestBodyLimits.View` (64 KiB) bounds it.
- `PUT v1/{organization}/{project}/views/{view}` (`Api.Functions/src/V1/ViewsHttp.cs:100-132`) needs a user with `Administrator`; machines get `403` (`EnsureNotMachine`, `:135-141`). A missing body clears the description (`model ?? new ViewUpdate()`).
- Every member reads the description: `GET v1/{organization}/{project}/views` needs `Reader`.
- The admin UI enters, edits and shows it on the Views page (`src/Platform/ui.admin/src/app/pages/[org]/[project]/views.vue`): `z.string().max(500).optional()` on create (`:46`), a plain `UTextarea` in the edit modal, `updateView` then `invalidateByPath` (`:52`, `:76-89`), and a muted, truncated line in the list (`:119`).

### OpenAPI and SDKs

The committed snapshot is `open.api.v0.9.44.json`, in `Sdk.NSwag/` and `sdk.typescript/`. Its version is `0.9` plus the number of commits that touched `Api.Functions/src` (44 on this branch). The C# SDK (`Sdk.NSwag/src/ApiSdk.g.cs`, used by `sform`) and the TypeScript SDK (`sdk.typescript/src/generated/`, used by the admin UI) are generated from it; CI checks that `npm run generate` leaves `generated/` unchanged.

### Admin UI

Paths in this section are relative to `src/Platform/ui.admin/src/`.

- **Creating a project.** `app/components/orgs/ProjectCreateDialog.vue` ("New project" on the organization overview) sends `createAccessPoint({ Organization, Project })` (`:29`). `app/components/orgs/OrganizationForm.vue` (fields "Organization" and "First project") is shared by `app/pages/onboarding.vue` (`createAccount`, `:14`) and `app/components/orgs/OrganizationCreateDialog.vue` (`createAccessPoint`, `:13`). The schemas are `organizationSchema` and `projectSchema` in `app/forms/names.schema.ts:44-51`.
- **Showing a project.** The organization overview (`app/pages/[org]/index.vue:46-59`) shows one card per project with the name, the role and the key. The cards come from the account's `AccessMap` (`app/utils/memberships.ts`), not from access point documents.
- **Settings.** `app/pages/[org]/[project]/settings.vue` has a "Project" card (`:158-170`) with the key, the caller's role and, for Administrators, the member count from `useGetAccessPoint` (`:66-68`, enabled only for Administrators). The "Machine authentication" card (`:238-296`) is the pattern for an Administrator form on this page: a lock badge for lower roles, loading and error states, a local form state, and a save with a `saving` flag, `invalidateAccess` and toasts (`:116-134`).

### Mock data

Paths in this section are relative to `src/Platform/ui.admin/src/app/mock/`.

- **Legacy datasets** (`--legacy` = `default`, `--large` = `large`, and the `new-user` and `invited` scenarios) share the access points of `db.ts:179-194`: the projects `acme.billing`, `acme.shipping` and `northwind.portal`. `new-user` and `invited` copy the points with a spread (`db.ts:136-141`). `large` only generates more annotations; it has no extra projects.
- **Showcase dataset** (the default): 14 projects in `showcase/projects/*.ts`, each a `ShowcaseProject` (`showcase/types.ts:26-40`) with a one-sentence `summary` that nothing reads at run time. `showcase/seed.ts` builds the access points (`accessPoint`, `:33-42`, and `:133-139`) and maps `ShowcaseView.description` to `View.Description` (`viewsOf`, `:46-55`).
- **Handlers.** `handlers.ts` creates bare access points on `POST /access/account` (`:91-92`) and `POST /access/points` (`:125-126`), answers `GET /access/points` with the caller's Administrator points (`:97-106`) and `GET /access/points/:key` for Administrators (`:136-144`). There is no write of an access point itself. `access-handlers.ts` mutates `AccessMap` in place (`:224`, `:246`) and spreads the point on a machine policy write (`:392`), so a new field survives.

### Documentation

`docs/Platform/Storyteller/access.md` documents views, roles, members, invitations and accounts, but not the access point routes. `docs/42for.net/platform/using-the-platform.md:21` and `docs/42for.net/platform/admin-ui.md` describe views and the admin UI screens.

## Proposed Changes

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### 1. The model (`Abstractions.Access`, `Backend.Core`)

- `AccessPoint` and `IAccessPoint` get `string? Description { get; init; }`, with the comment that only a project has one; it is always null for an organization.
- New `Abstractions.Access/src/Model/ProjectSummary.cs` (namespace `_42.Platform.Storyteller`), the description of a project for any of its members:

  ```csharp
  // A project as one of its members sees it: no members and no policies.
  public record class ProjectSummary
  {
      // organization.project
      public required string Key { get; init; }

      // The caller's role on the project.
      public required AccountRole Role { get; init; }

      public string? Description { get; init; }
  }
  ```

- `AccessPointCreate` and the internal `AccountCreate` get `string? ProjectDescription { get; init; }`. The name says which of the two access points the request may create gets it.
- New `Backend.Core/src/Accessing/Model/AccessPointUpdate.cs`, like `ViewUpdate`:

  ```csharp
  // The new description of a project; null or blank clears it.
  public record class AccessPointUpdate
  {
      public string? Description { get; init; }
  }
  ```

- `IAccessService` gets two members, with default implementations that throw `NotSupportedException`, as the federation members do (`IAccessService.cs:50-55`), so the hand-written test doubles keep compiling:
  - `Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(string accountId)`: every project where the account has a role above `None`; `NotFoundException` for an unknown account, as `GetAccessPointsAsync`;
  - `Task<AccessPoint> UpdateAccessPointAsync(string key, AccessPointUpdate model)`: replaces the description of a project; `NotFoundException` when the project doesn't exist, `ArgumentException` for an organization key (the API answers `400` before).

### 2. Storage (`Backend.CosmosDb`)

- `AccessPointEntity` gets `string? Description { get; init; }`. Old documents read as null; no migration. `ToAccessPoint` copies it; the unused `ToEntity` copies it too.
- One helper in `CosmosAccessService`, the rule of `CosmosViewService.NormalizeDescription`: trim, and a blank value becomes null. No length rule on the server, as for views.
- `CreateAccessPointAsync` writes the normalized `ProjectDescription` to the new project entity only, never to an organization it creates.
- `CreateAccountAsync` forwards `ProjectDescription` into the `AccessPointCreate` it builds. The account document doesn't get it.
- `GetProjectsAsync` reads the account, takes the keys with a dot and a role above `None`, and reads only what it returns with one query in the `access` partition: `SELECT ap.Key, ap.Description FROM ap WHERE ARRAY_CONTAINS(@ids, ap.id)`, into a private record, as `GetAccountProfilesAsync` (`:760-764`, `:788-796`) does for accounts. The members and policies of the projects are not read. It returns a `ProjectSummary` per point found, with the role from the account, ordered by key. A key whose point is missing is left out.
- `UpdateAccessPointAsync` reads the entity and its ETag with `TryReadItemWithETagAsync` (as `UpdateMembershipAsync` and `CosmosKeyValuePolicyStore.ReadAsync` do), replaces `entity with { Description = normalized }` with `IfMatchEtag = etag`, and on `412` reads again and retries once, as `SetMachineFederationRulesAsync` (`:567-609`) loops; a second `412` throws `ConflictException` (`Conflict`). `AccessPointEntity` gets no ETag property. The update never upserts and never rebuilds the entity from the `AccessPoint` model, which lacks `KeyValues`.

### 3. The HTTP API (`Api.Functions`)

- **Create.** The public `V1/Models/AccountCreate.cs` gets `string? ProjectDescription`. `PostAccount` forwards it. A non-blank `ProjectDescription` without organization and project answers `400` ("A project description needs an organization and a project."), next to the existing both-or-neither check. `POST v1/access/points` binds the extended `AccessPointCreate` without a handler change. Both bodies stay under `RequestBodyLimits.Control`.
- **Update.** `PUT v1/access/points/{key}`, route `Definitions.Routes.Access.V1.AccessPoint` (the GET route), operation id `UpdateAccessPoint`, tag `Access`, body `AccessPointUpdate` (not required), `200` `AccessPoint`, `400` `ErrorResponse` (an organization key or a malformed body), `401`, `404` (no such project), `409` (concurrent changes), `500`, `403` from the document filter. The handler:
  1. refuses a machine identity (`TryGetApplicationIdentity`) with `403` `AccessDenied` ("Describing a project needs a user with the Administrator role."), as views do. It comes first: no machine carries `User.Impersonation`, so after `CheckScope` a machine would get `401` in every build except `DEV_AUTH`;
  2. `CheckScope(Scopes.User.Impersonation)`;
  3. trims and lower-cases the key;
  4. `CheckAccessToAsync(…, AccountRole.Administrator)`;
  5. `TrySplitProjectKey`: an organization key answers `400`;
  6. reads the body with `ReadJsonBodyAsync<AccessPointUpdate>(…, "a project update", RequestBodyLimits.Control)`; a malformed body answers `400` without calling the service; a missing, empty or `null` body is `new AccessPointUpdate()` and clears the description, as for views;
  7. returns `UpdateAccessPointAsync`'s `AccessPoint`.
- **Read.** `GET v1/access/projects`, new route `Definitions.Routes.Access.V1.Projects`, operation id `GetProjects`, tag `Access`, `200` `ProjectSummary[]`, `401`, `404` (no account), `500`, `403` from the document filter. It refuses a machine identity with `403` first ("Listing projects needs a user."), checks `User.Impersonation`, takes the caller's id like `GetAccessPoints` and returns `GetProjectsAsync`. Any member reads the description of their projects this way, as any member reads the descriptions of views; the members and the policies stay behind `GET v1/access/points/{key}` and `Administrator`. `projects` is a literal segment under `v1/access/`; `access` is a reserved organization name and no route has the shape `v1/{a}/{b}`, so the route meets no organization route.
- **Why a new read.** Three other ways were weighed. Opening `GET v1/access/points/{key}` to `Reader` would expose the members (`AccessMap`, which only Administrators list) and the machine policy, or need a per-role contract. Extending `GET v1/access/points` to every role has the same problem for each point. Putting the descriptions on the `Account` would copy each description into every member's account document, or add a query to `GetAccountAsync`, which also runs behind every access check. A list scoped to the caller's own projects costs one request on the overview and in Settings and returns only what a member may already know, plus the description.
- `RequestBodyLimits.cs`: the header comment lists the project update under `Control`.

### 4. API tests (`Api.Functions/test`, `Backend.CosmosDb/test`)

- `CosmosAccessServiceTests` (Cosmos emulator):
  - creation trims the description onto the project; an organization created with it has none; a blank description is not stored;
  - account creation with `ProjectDescription` stores it on the project, not on the account or the organization;
  - update sets, replaces and clears (null and whitespace) the description and keeps `AccessMap`, `MachineAuthentication` and `KeyValues`;
  - update of an unknown project throws `NotFoundException`, of an organization key `ArgumentException`;
  - a later grant, a later key/value policy write and a later machine policy write keep the description;
  - `GetProjectsAsync` lists the projects of every role with their descriptions, leaves out organizations, and throws `NotFoundException` for an unknown account.
- `AccessPointHttpTests`: creation forwards `ProjectDescription`; the PUT forwards the description and the normalized key, clears on `""`, `null` and `{}`, answers `400` for a malformed body and an organization key without calling `UpdateAccessPointAsync`, and `403` for a machine (in every build, because the refusal comes before the scope check); `GetProjects` returns the service's list for the caller and refuses a machine. The recording double answers `GetAccountRoleAsync` with `Administrator`, because outside `DEV_AUTH` the role check calls it. Denied roles for users only run outside `DEV_AUTH`, as in `MachineHttpTests`.
- `MembersHttpTests`: account creation forwards `ProjectDescription`; a description without organization and project answers `400`.
- `RouteTableTests`: rows for `PUT v1/access/points/{}` and `GET v1/access/projects`.
- `OpenApiDocumentTests`: `AccountCreate` has `Organization`, `Project` and `ProjectDescription`.
- `OpenApiExportTests.Document_JsonRequestBodies_KeepTheirSchemaAndRequiredFlag`: `(UpdateAccessPoint, "AccessPointUpdate", false)`.

### 5. OpenAPI snapshot and SDKs

After the API commit, export the snapshot with `OpenApiExportTests` and `STORYTELLER_OPENAPI_EXPORT`, name it `open.api.v0.9.N.json` after the exported `info.version` (major, minor and patch), put it in `Sdk.NSwag/` and `sdk.typescript/`, remove `sdk.typescript/open.api.v0.9.44.json`, run the NSwag command of `Sdk.NSwag/README.md` and `npm run generate`. `0.9.45` is expected: the version counts the commits that touch `Api.Functions/src` (44 on this branch), and Phase 2 is the only one this plan adds. If a later commit touches `Api.Functions/src`, the export and the regeneration are redone after it, so the regeneration always follows the last such commit. Nothing generated is edited by hand. The commit also:

- bumps the file name in `Sdk.NSwag/README.md` and `docs/Platform/Storyteller/export-import.md`;
- extends `sdk.typescript/src/test/helpers.test.ts` and `test/msw.test.ts` for `updateAccessPoint` and `getProjects`, as earlier commits that added operations did;
- checks that a second `npm run generate` leaves `generated/` unchanged, and that `sform` (`src/Platform/Cli`) builds and its tests pass.

The admin UI installs the SDK as a packed copy of its `dist/` (`install-links=true`). Before Phases 4 and 5, run `npm run build` in `sdk.typescript/src` and `rm -rf node_modules/@42for.net && npm install` in `ui.admin/src` (`src/Platform/ui.admin/README.md`, Getting started). The reinstall changes no lockfile, and none is committed from it.

### 6. Mock data of the admin UI

Paths are relative to `src/Platform/ui.admin/src/app/mock/`.

- **Showcase.** The unused `summary` of `ShowcaseProject` becomes a required `description: string` ("The description of the project, shown in the admin UI"), so the dataset doesn't keep two one-sentence descriptions of the same project that drift apart. Each of the 14 project files sets it to the text from the issue comment, word for word. `seed.ts` puts it on the project access point as `Description`. Organizations get none.

  | Project | Description |
  | --- | --- |
  | `fabrikam/finance-cloud` | Single-tenant finance back-office SaaS. Each B2B customer runs its own instances (demo, UAT, production), with the applications it bought and the jobs that run in each instance. |
  | `fabrikam/fitness-cloud` | Multi-tenant platform for gym chains. Tenants enable applications and assign them to their members, the end customers of our customers. |
  | `fabrikam/invoicing-suite` | The B2B SaaS of the documentation: Northwind and Contoso, with their environments, the invoicing, payments and portal modules, their contracts and their nightly exports. |
  | `bluefin/money-suite` | Consumer finance suite. People act as themselves or as the companies they represent, and use accounts, payments, cards and investments in each role. |
  | `streamwave/streaming-platform` | Streaming platform across markets. Each market has production and canary cells, and services such as playback, recommendations and search are deployed per cell. |
  | `ledgerline/erp` | Monolithic ERP with several versions in production. It tracks the channels pinned to each version, the modules every build contains and the customer-specific rollouts. |
  | `aurora-motors/vehicle-platform` | Electric vehicle models, their trims and individual VINs, with the subsystems, parts and firmware fitted to each build. |
  | `harbor-industrial/plant-fleet` | Industrial device fleet. Sites run compressors, pumps and gateways, with telemetry, control loops and maintenance tuned per device. |
  | `meridian-health/clinical-integrations` | Clinical integrations of a hospital network: lab, imaging, e-prescription and portal systems connected per department, under change control. |
  | `appforge/white-label-app` | White-label mobile app. Retail brands publish it under their own name, with the features they licensed set up per platform, and campaigns. |
  | `gatherly/conferences` | Conference series and their yearly editions, with ticketing, streaming, badges and the mail jobs booked for each edition. |
  | `pixel-forge/live-ops` | Live operations of game titles. Backend services run per platform and region, and seasonal live events are scheduled per region. |
  | `velvet-retail/storefronts` | Multi-brand e-commerce storefronts across European markets. Checkout, payments, shipping, tax and search are set up per market. |
  | `seabreeze-hotels/guest-platform` | Hotel group distribution. Booking engine, rate manager and guest modules are set up for each hotel on its direct and OTA channels. |

- **Legacy datasets.** `db.ts` gives the three projects a description, which the `default`, `large`, `new-user` and `invited` scenarios share:

  | Project | Description |
  | --- | --- |
  | `acme/billing` | Billing of Acme's B2B customers. Invoicing, payments, reporting, notifications, archiving and dunning are set up per customer and context. |
  | `acme/shipping` | Shipping of Acme's orders. A small catalog of customers and their responsibilities per context. |
  | `northwind/portal` | Northwind's customer portal. A small catalog of customers and their responsibilities per context. |

  The legacy generator gives every project the same billing responsibilities (`annotations.ts:29-36`), so the two smaller projects don't name responsibilities they don't have. The legacy `accessPoint` helper takes the description as an optional parameter.
- **Handlers.**
  - `POST /access/account` and `POST /access/points` store the trimmed `ProjectDescription` on the project point only; a blank one is not stored.
  - New `PUT /access/points/:key`: `requireRole(db, key, 'Administrator')`, `400` for an organization key, `404` for a missing project, then the trimmed description or none, `200` with the point. It reads the body with `request.text()`: an empty text or `null` is `{}` and clears the description, as in the API; text that is not JSON answers `400`. (`request.json()`, which the other handlers use, throws on an empty body.)
  - New `GET /access/projects`: `404` `NotFound` without an account (the `new-user` and `invited` scenarios before onboarding), otherwise the keys of the account's `AccessMap` with a dot and a role above `None`, with `Key`, `Role` and `Description` from `db.accessPoints`, ordered by key.
  - A tab whose mock session started before this change keeps its stored points without descriptions until the mock data is reset; the change needs no migration of the session data.
- **Unit tests** (`src/Platform/ui.admin/src/test/unit/`):
  - `mock/showcase.test.ts`: every showcase project has a non-empty description of at most 500 characters, and the seeded point carries it;
  - a new `access/project-description.test.ts` against the mock API: create with a description (trimmed, blank not stored, nothing on the organization), PUT sets, replaces and clears (also with an empty body), `403` for a Contributor, `400` for an organization key and for a body that is not JSON, the description survives a member change and a machine policy write, and `GET /access/projects` lists the projects of every role with their descriptions;
  - the legacy seed: the three projects have descriptions in every scenario.

### 7. Admin UI

Paths are relative to `src/Platform/ui.admin/src/app/`.

- **Schema.** `forms/names.schema.ts` gets `descriptionSchema = z.string().max(500, 'At most 500 characters.')`, the limit of view descriptions. `projectSchema` and `organizationSchema` get `description: descriptionSchema.optional()`.
- **Creating a project.** `components/orgs/ProjectCreateDialog.vue` gets an optional "Description" textarea (`UTextarea autoresize`) under the name and sends `ProjectDescription: values.description?.trim() || undefined`. `components/orgs/OrganizationForm.vue` gets a "Project description" textarea under "First project"; `OrganizationCreateDialog.vue` sends it with `createAccessPoint` and `pages/onboarding.vue` with `createAccount`. The label "Project description" does not contain "First project" or "Organization", so the e2e selectors of those fields stay unique.
- **The projects list.** A composable in `queries/` (for example `useProjectSummaries()`) wraps `useGetProjects` with `meta: { silent: true }`, exposes the summary of a key, and invalidates `/v1/access/projects` when the set of project keys in the session's `AccessMap` changes. That one place covers every way a project appears or goes: onboarding, both create dialogs, an accepted invitation (`pages/invitations/index.vue:39`, `session.setAccount`), the Members table reloading the account, and leaving a project. Without it, a list cached for 30 s (`boot/query.ts:28`) would miss a new project's description.
- **Settings.** The "Project" card of `pages/[org]/[project]/settings.vue` shows the description to every role:
  - Lower roles see a full-width "Description" row under the `<dl>` (the `dl` has a fixed 10rem label column that leaves little room on a 390 px phone), with `whitespace-pre-line break-words`. While the list is pending it shows `LoadingState`; on an error, `ErrorState compact` with a retry; "No description" only once the list has loaded without one.
  - An Administrator sees, in place of that row and in the same states, a form under the `<dl>` at full width: `UFormField label="Description"` with a `UTextarea autoresize`, the 500-character rule and its error, and a right-aligned "Save description" button, as on the machine policy card. A watch fills the text when the description loads, as the machine policy form does (`:88-98`), and does not replace text the user has changed. The button is disabled until the trimmed text differs from the loaded description, and has its own `savingDescription` flag (the page's `saving` belongs to the machine policy button).
  - Save calls `updateAccessPoint(projectKey, { Description: text.trim() || undefined })`, invalidates `/v1/access/projects` and `/v1/access/points/{key}`, and shows the toast "Description saved"; a failure shows "The description was not saved" with `errorMessage(error)` and keeps the text.
  - The Project card is above `#secrets`, so the projects query joins the readiness of the hash scroll for every role (`useHashScroll`, `:79-83`): `!projects.isPending.value && …`. Otherwise the card grows after the scroll and moves `#secrets`.
- **Organization overview.** The project cards of `pages/[org]/index.vue` show the description under the key, muted, at most two lines (`line-clamp-2`), with the full text in `title`. A card without a description, or before the list arrives, looks as today. The cards stay built from the account's `AccessMap`, and `utils/memberships.ts` doesn't change.
- **Invalidation.** `queries/invalidation.ts` gets `invalidateProject(client, key)` for the two paths of the settings save.
- **Tests.**
  - `test/unit/components/organization-form.test.ts`: the submitted values include the description; a new case submits one.
  - A unit test of the settings description: an Administrator saves and clears it, a Reader sees it without the form, a text over 500 characters shows the error and does not save, and the pending and error states of the list show their states instead of "No description".
  - e2e (`test/e2e/`): `onboarding.spec.ts` creates the organization with a project description and sees it on the organization overview; `settings.spec.ts` edits, reloads, sees the saved description and clears it as an Administrator (`getByRole('textbox', { name: 'Description', exact: true })`), and sees it read-only as a Reader on `northwind/portal`, where Ada is `Reader` in the legacy data the e2e tests run on; `showcase.spec.ts` sees a description on an organization overview card.
  - Visual baselines (`test/e2e/__screenshots__/`): only the onboarding screens change, with the new field. They are regenerated in the Playwright Linux image (`npm run test:visual:docker -- --update`) and checked by eye. The settings baseline is scrolled to `#secrets`, where the Project card is off screen, and must not change; a change there means the hash scroll is not waiting for the list.

### 8. Documentation

- `docs/Platform/Storyteller/access.md`: a new `## Projects` section after `## Views` with the routes (`GET v1/access/points`, `GET v1/access/points/{key}`, `POST v1/access/points`, `PUT v1/access/points/{key}`, `GET v1/access/projects`), who needs what, the description rules (trimmed, blank is none, no length rule, a missing PUT body clears it, users only), `ProjectSummary`, and the storage on the project document. `## Accounts` mentions `ProjectDescription`; the `409 Conflict` row names a project description that changed concurrently twice; the `413` row names the project bodies.
- `docs/Platform/Storyteller/authentication.md`: the `413` row names the project update if it lists bodies.
- `docs/42for.net/platform/using-the-platform.md`: one sentence on the description of a project and its routes, next to the view description.
- `docs/42for.net/platform/admin-ui.md`: onboarding and "New project" take a description, the organization overview shows it, and Settings shows it to every member and lets Administrators edit it (the Settings row of the table).
- `src/Platform/ui.admin/README.md`, Mock mode: every project of every dataset has a description; the `description` field (formerly `summary`) when adding a showcase project.
- `docs/Platform/Storyteller/export-import.md` and `Sdk.NSwag/README.md`: the snapshot name (section 5).
- Specs and earlier reviews are not changed.

### 9. Concurrency and known risk

The description update and the membership and key/value writes are guarded by ETags. The machine authentication policy store upserts without an ETag (`CosmosMachineAuthenticationPolicyStore.cs:70`): a machine policy saved at the same moment as a description can overwrite the description with the value it read. It already can overwrite a concurrent membership or key/value change. The DbCreator seed replaces `42dotnet.main` whenever it runs (Current State). This spec changes neither; section "Out of scope" records them.

### 10. Phases and commits

1. `docs(platform): plan the description of a project` (this spec)
2. **Phase 1**, `feat(platform): store the description of a project in Storyteller`: sections 1, 2 and the Cosmos tests of 4.
3. **Phase 2**, `feat(platform): set and read the description of a project in the Storyteller API`: sections 3 and the API tests of 4.
4. **Phase 3**, `chore(platform): regenerate the SDKs from OpenAPI 0.9.N` (`0.9.45` expected): section 5.
5. **Phase 4**, `feat(platform): describe every project of the admin UI mock data`: section 6.
6. **Phase 5**, `feat(platform): show and edit the description of a project in the admin UI`: section 7.
7. `docs(platform): document the description of a project`: section 8.
8. `docs(platform): review the description of a project`: one review of all phases in `docs/Platform/Storyteller/specs/reviews/`.

Every commit body names `Refs: 424-78`.

### 11. Verification

- `dotnet build 42.mono.slnx`
- `dotnet test src/Platform/Storyteller/Backend.CosmosDb/test` (Cosmos emulator), `dotnet test src/Platform/Storyteller/Api.Functions/test`, `dotnet test src/Platform/Cli/test`
- `sdk.typescript/src`: `npm run typecheck`, `npm run lint`, `npm test`, `npm run build`, and a second `npm run generate` without a diff
- `ui.admin/src`, after reinstalling the rebuilt SDK (`rm -rf node_modules/@42for.net && npm install`): `npm run build`, `npm run lint`, `npm test`, `npm run test:e2e`, `npm run test:visual:docker`

### 12. Differences from the CodeRabbit coding plan

Kept from the plan: the description on the project access point document; `PUT v1/access/points/{key}` with `AccessPointUpdate`; `ProjectDescription` on both creation bodies; the trim-and-blank-is-null rule of views without a server length rule; Administrator for the update, users only; the ETag replace with one retry; no change of the machine policy store; a regenerated snapshot and SDKs, rebuilt and reinstalled in the admin UI; the description in the creation forms, in Settings with loading, error and retry states, and on the project cards; one spec and one review in `docs/Platform/Storyteller/specs/`.

Differences:

1. **Every member reads the description.** The plan reads it only through the Administrator-only `GET v1/access/points/{key}` and `GET v1/access/points`, so a `Reader` or `Contributor` sees no description anywhere. A view's description is read with `Reader`, and the issue asks for the same thing for a project. In the showcase, Ada is below Administrator on 8 of the 14 projects (`Reader` on 1, `Contributor` on 4, `ContributorWithSecrets` on 3), so 8 of 14 cards would stay empty. This spec adds `GET v1/access/projects` with `ProjectSummary` (key, role, description), which exposes neither members nor policies (section 3, "Why a new read").
2. **The update body is bounded by `Control` (8 KiB)**, the bound of the creation bodies that carry the same description, instead of a new 64 KiB limit like views. 8 KiB is far above the 500 characters of the admin UI.
3. **Default interface implementations** for the two new `IAccessService` members, as for the federation members, instead of adding the methods to every hand-written test double.
4. **Every project of every dataset gets a description**, the showcase ones from the issue comment in the `description` field that replaces the unused `summary`, instead of describing some seeded projects and leaving others without one. Projects without a description are still covered by tests that create one.
5. **Settings shows the description to every role** from the projects list; only the form needs `Administrator`. The plan showed and edited it only for Administrators, through the access point detail.
6. **The organization overview reads `GET v1/access/projects`**, which covers every role, instead of the Administrator-only `GET v1/access/points`.
7. **The projects list is refreshed in one place** when the account's projects change, instead of invalidating after each creation path.

### Out of scope

- `sform`: the issue names the API and the admin UI. `sform account points get` could print the description and `points create` could take one later; the regenerated C# SDK already carries the fields.
- A description of an organization. `AccessPoint.Description` stays null for organizations.
- The Organizations page (`/orgs`), the Account page, the context switcher and the command palette keep listing projects without descriptions, as view descriptions appear only on the Views page.
- The DbCreator seed project (`42dotnet.main`) gets no description; the issue names the mock data of the admin UI.
- Found while mapping, not changed here:
  - the machine authentication policy store upserts the project document without an ETag (section 9);
  - the DbCreator seed replaces `42dotnet` and `42dotnet.main` whenever it runs, dropping later members and policies (Current State);
  - `GET v1/access/points/{key}` answers `200` with an empty body for a missing access point instead of `404` (`AccessHttp.cs:162-165`);
  - the edit modal of a view description in the admin UI has no 500-character rule, unlike its create form;
  - the mock `PUT …/views/:view` stores the description without trimming it.

### Merge notes

1. **After #123.** The branch starts from `feat/424-77` (`e89894aa`). Once #123 is on `main`, rebase with `git rebase --onto origin/main e89894aa`.
2. **With #124** (`feat/424-79`, also on top of `main`). It overlaps far beyond the snapshot: `AccessHttp.cs`, `Definitions.cs`, `CosmosAccessService.cs`, `AccessMappings.cs`, `AccessPointHttpTests.cs`, `MembersHttpTests.cs`, `OpenApiExportTests.cs`, the mock `db.ts`, `access.md`, `authentication.md`, `export-import.md`, `admin-ui.md`, `using-the-platform.md`, both READMEs, and every generated SDK file. It also replaces the guards this spec uses: `CheckScope`/`EnsureNotMachine`/`CheckAccessToAsync` become `EnsureUser`/`CheckUserAccessToAsync`, user-only operations document `ResponseUnauthorizedUserOnly` and `ResponseForbiddenUserOnly`, and a new OpenAPI test lists every operation (`UserOnlyOperations`). If #124 lands first:
   - drop this branch's regeneration commit during the rebase instead of resolving conflicts in the snapshot and the generated files;
   - in `PUT v1/access/points/{key}` call `request.EnsureUser("Describing a project needs a user with the Administrator role.")` and `CheckUserAccessToAsync(…, AccountRole.Administrator)`; in `GET v1/access/projects` call `request.EnsureUser()`; document both as user-only operations and add `UpdateAccessPoint` and `GetProjects` to `UserOnlyOperations`; use `UserOnlyRoutes` for the machine cases of the tests;
   - re-export after the last commit that touches `Api.Functions/src`, name the file after the exported `info.version` (`0.9.47` expected: 44, two commits of #124 and one of this branch), delete #124's `sdk.typescript/open.api.v0.9.45.json`, regenerate both SDKs and rerun the `sform` and SDK tests.
3. If this branch lands first, #124 does the same in reverse.
