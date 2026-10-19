# Administrator Role for the Export and Import of a View

Linear: [424-66 Decide whether Readers may export a view and read modeling data through the API and sform](https://linear.app/424fun/issue/424-66/decide-whether-readers-may-export-a-view-and-read-modeling-data)

## Problem

Since 424-38 ([Modeling Page in the View Section](../../ui.admin/specs/2026-10-18%20Modeling%20Page%20in%20the%20View%20Section.md), section 3), the admin UI locks the whole **Modeling** page (Schemas, Templates, Export & Import, Deleted, Compare views) below `Contributor`. That spec left the API and `sform` unchanged on purpose (its Out of scope: "Permissions of the API and `sform`. The lock is in the admin UI only."). So a Reader still exports a view with `sform story export` or the API, and a Contributor imports into it.

The issue asks whether the lock is a choice of the UI or a permission rule. The decision on the issue:

> The permission rule must be enforced and `Admin` is the minimum role required. Apply to both Export and Import.

The issue lists what follows from it:

- the Storyteller API requires Administrator for the export of a view, and for the import;
- `sform story export` and `sform story import` and their error messages follow;
- the access docs (`access.md`, `export-import.md`) and the API tests are updated;
- the admin UI shows the Export & Import tab only to Administrators and Owners.

It also says "and possibly for modeling reads". Section 1 decides that.

CodeRabbit made a coding plan on the issue (approach, phases for the API, `sform`, the admin UI and the docs). This spec follows that plan in most places. The places where it differs are listed in section 10.

## Current State

All paths are relative to `src/Platform/` unless they start with `docs/`.

### API

- **The five endpoints.** `Storyteller/Api.Functions/src/V1/ViewTransferHttp.cs` has `StartViewExport` (POST `…/exports`), `GetViewExport` (GET `…/exports/{id}`), `GetViewExportArchive` (GET `…/exports/{id}/archive`), `StartViewImport` (POST `…/imports`) and `GetViewImport` (GET `…/imports/{id}`). Each calls `request.CheckAccessToProjectAsync(_access, organization, project, …)`:
  - the three export endpoints pass no role, so `Reader` (lines 71, 106, 134);
  - the two import endpoints pass `AccountRole.Contributor` (lines 178, 238).
- **Order of the checks.** The starts parse the body or the query first (`400`), then check the scopes of the included kinds (`CheckScopes`, `ViewTransferScopes.Ensure`, `403`), then the project access. The three reads check the project access first, then read the job (`404`), then check the scopes of its `Include` (`403`).
- **Project access** (`Storyteller/Api.Functions/src/Security/HttpRequestDataExtensions.cs`, `CheckAccessAsync`, lines 352–386):
  - **A machine** must be a machine of the project and pass its credential policy (`SecurityTokenException`, `401`), and must not be restricted to an annotation (`AccessDeniedException` with `AnnotationRestricted`, `403`). The role argument is never used for a machine.
  - **A user** gets `EnsureRole` (lines 194–200): below the minimal role it throws `AccessDeniedException("No {role} access to '{organization}.{project}'.")`. `ErrorResponseMapping` answers it with `403` and `ErrorCode: AccessDenied`. The role is read from the user's access map by the exact key `{organization}.{project}`.
  - The user role check is inside `#if !DEV_AUTH`. `DEV_AUTH` is defined for `Debug|AnyCPU` only (`Storyteller/Api.Functions/src/Api.Functions.csproj`), so Debug builds, also under Aspire, never check a user's role. `ViewTransferHttp.ChecksScopes` and `CheckScope` skip the scopes in Debug too; the transfer tests turn `ChecksScopes` on.
- **Roles.** `AccountRole` (`Storyteller/Abstractions.Access/src/AccountRole.cs`): `None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner`.
- **OpenAPI.** The `403` of the export endpoints is described by `Definitions.Descriptions.ResponseTransferForbiddenExport` ("No role on the project, or a scope of an included kind is missing (AccessDenied). …"), the `403` of the import endpoints by `ResponseTransferForbiddenImport` ("The role on the project is below Contributor, …") (`Storyteller/Api.Functions/src/Definitions.cs`, lines 492–493). Both texts are in the newest snapshot `open.api.v0.9.31.json`, and NSwag copies them into `ApiSdk.g.cs` as the message of the `ApiException<ErrorResponse>` of each `403`. The TypeScript SDK has no response descriptions.
- **Background work.** The queue functions (`Transfers/ViewTransferFunctions.cs`) check no access; the job runs with the actor that started it.
- **Other Administrator operations reject machines** (`KeyValueAuthorization.CheckAdministratorAsync`, the permanent delete of an annotation, view management). The transfers have accepted machines with the scopes of the included kinds since the first spec ([Export and Import of Configurations](2026-10-14%20Export%20and%20Import%20of%20Configurations.md), section 2.2).
- **Modeling reads.** Schemas (`ConfigurationSchemaHttp*.cs`), templates (`TemplateHttp.cs`), deleted annotations and history (`AnnotationsHttp.Deleted.cs`) and the reads behind Compare views (`ViewsHttp.cs`, `ConfigurationHttp.cs`, the view diff) all need `Reader`. They also serve the configuration pages of the admin UI and `sform`.

### `sform`

- `Cli/src/Commands/Storyteller/StorytellerExportCommand.cs` and `StorytellerImportCommand.cs` catch `ApiException` with `400`, `401`, `403`, `409` (and `413` for the import) only around the start. They print `exception.GetErrorMessage()` (`Cli/src/Services/AnnotationsApiExtensions.cs`) with `ThemedError` and return `WARNING_UNAUTHORIZED_ACCESS` (44) for `401`/`403`, else `ERROR_WRONG_INPUT` (2).
- Polling (`Cli/src/Services/TransferPolling.cs`) and the archive download have no catch. A `401` or `403` there reaches `Program.Main` (lines 84–101), which prints "You are not authorized to execute the command or you don't have access to requested resources.", adds a hint for a machine sign-in, and returns 44. The server's message is lost.
- A `401` has no body; NSwag throws an untyped `ApiException("Missing or invalid credentials.", …)`, whose `Message` ends with "Status: 401" and the response. At the start, the commands print that text.
- The descriptions of the commands name no role. Other commands put the role in brackets, for example "(Administrator)" (`KeyValueAuditCommand.cs`, `MachineIssuerSetCommand.cs`).

### Admin UI

All paths below are relative to `ui.admin/src/`.

- **Roles.** `app/composables/useRole.ts`: `capabilityRoles` (`read`, `edit`, `machines`, `secrets`, `manageAccess`, `owner`), `useCan(capability)`. `hasRole` from the SDK ranks the roles like the API.
- **Tabs.** `app/utils/modeling.ts` has a static `modelingTabs` (`as const`) with five entries. `app/components/modeling/ModelingHeader.vue` renders all five, and `app/utils/navigation.ts` gives all five to the Modeling entry as `tabs`, which only the command palette lists (`app/components/shell/CommandPalette.vue`).
- **Locks.** `app/pages/[org]/[project]/[view]/modeling.vue` locks the whole page below `Contributor` ("Contributors only", "The schemas, the templates, the export and import, the deleted annotations and the comparison of views are available to the project's Contributors."). `app/pages/[org]/[project]/monitor.vue` is an "Administrators only" `EmptyState` precedent.
- **The page.** `app/pages/[org]/[project]/[view]/modeling/export-import.vue` has no lock of its own; its comment relies on the Modeling lock. `canImportNow` uses `useCan('edit')`. `app/components/transfer/ImportPanel.vue` shows "Importing needs the Contributor role" / "You can export the view; ask a Contributor of the project to import an archive." below `edit`. `ExportPanel.vue` has no role code.
- **Mock API.** `app/mock/transfer-handlers.ts` calls ``requireRole(db, `${params.org}.${params.project}`, 'Reader')`` on the three export routes and `'Contributor'` on the two import routes. The mock user is Owner of `acme.billing`, Contributor of `acme.shipping` and Reader of `northwind.portal` (`app/mock/db.ts`).

### Documentation

- `docs/Platform/Storyteller/access.md`, row "Export a view, import into a view, and read those jobs": "Any role on the project to export, `Contributor` to import and to read an import, and the scopes of every included kind".
- `docs/Platform/Storyteller/export-import.md`: the Access section ("The roles stay as in the first spec: any role on the project to export …, `Contributor` to import …"), the error table, the exit codes of `sform` (`1` for "any other error … while waiting", `44` for "the start"), the Navigation, Who and mock sections of the admin UI ("a Reader still exports with `sform story export`").
- `docs/Platform/Storyteller/authentication.md` (the role summary), `annotation-lifecycle.md` ("Any Contributor may do it" for an import that overwrites).
- The public site: `docs/42for.net/platform/admin-ui.md` ("The API and `sform` do not change", "needs the Contributor role or a higher one", "`sform story export` works for a Reader too"), `using-the-platform.md` ("Exporting needs any role on the project, and importing `Contributor`"), `configuration.md` (the `sform story export|import` section, no role).
- `ui.admin/README.md`, the Modeling and Export & Import notes.

## Proposed Changes

### 1. The rule

1. **Users.** Every transfer endpoint needs the `Administrator` role on the project, so `Administrator` or `Owner`: start an export, read an export, download its archive, start an import, read an import. `ContributorWithSecrets` and below get `403` `AccessDenied` with the shared message "No Administrator access to '{organization}.{project}'.".
2. **Machines.** A machine has no role. Machines keep the current rule: the scopes of every included kind, and a machine restricted to an annotation gets `403` `AnnotationRestricted`. This differs from the other Administrator operations, which reject machines, on purpose: the export and import of a view by a machine (a backup, a copy between projects from CI) is documented and tested, and the decision names a role, which only a user has. The scopes already split a reading machine (`Default.Read` exports) from a writing one (`Default.ReadWrite` imports).
3. **Modeling reads stay at `Reader` in the API.** The decision names the export and the import only. Schemas, templates, deleted annotations, history and the reads behind Compare views serve Reader flows outside Modeling (the configuration pages, `sform`). The Modeling lock of the admin UI for those tabs remains a choice of the UI, and the docs say so.
4. **Jobs already started.** The status and the archive are checked with the caller's current role on every request, so a user who lost the role gets `403`. A job already queued finishes; the queue functions check no access, as today.

### 2. Access checks of the API

`Storyteller/Api.Functions/src/V1/ViewTransferHttp.cs`:

- Pass `AccountRole.Administrator` to the five `CheckAccessToProjectAsync` calls (lines 71, 106, 134, 178, 238).
- Keep the order of the checks (section Current State), the scope checks, the machine path and the error mapping. No new error code, no machine rejection, no use of `KeyValueAuthorization.CheckAdministratorAsync` (it rejects machines and needs `User.Impersonation`).
- The class comment says that users need the Administrator role (424-66) and machines the scopes of the included kinds.

### 3. OpenAPI descriptions and SDKs

1. `Storyteller/Api.Functions/src/Definitions.cs`:
   - `ResponseTransferForbiddenExport`: "The user's role on the project is below Administrator, or a scope of an included kind is missing (AccessDenied), or the machine is restricted to an annotation (AnnotationRestricted). Annotations need Annotation.Read, Annotation.ReadWrite, Default.Read or Default.ReadWrite; schemas, templates and configurations need Configuration.Read, Configuration.ReadWrite, Default.Read or Default.ReadWrite. Reading an export needs the scopes of the kinds it includes. The message names the missing role or scope."
   - `ResponseTransferForbiddenImport`: the same start, then the import scopes as today ("Annotations need Annotation.ReadWrite or Default.ReadWrite; …"), and "Reading an import needs the scopes of the kinds it includes. The message names the missing role or scope."
   - The operation descriptions name no role and do not change.
2. Export a new snapshot with `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`), as `Storyteller/Sdk.NSwag/README.md` describes, copy it to `Storyteller/sdk.typescript/`, regenerate `Sdk.NSwag/src/ApiSdk.g.cs` with `nswag openapi2csclient` and the TypeScript SDK with `npm run generate`, and update the snapshot name in `Sdk.NSwag/README.md`. Follow the last regeneration (`7a4498dd`, 0.9.31) for which files change. The NSwag client then throws the new descriptions for the five `403`s; the TypeScript SDK changes its version only.
3. This is a separate `chore(platform)` commit.

### 4. `sform story export` and `sform story import`

1. **One helper.** `Cli/src/Output/TransferConsoleExtensions.cs` gets `WriteRefusal(this IExtendedConsole, ApiException)`: it prints `exception.GetErrorMessage()` with `ThemedError`, as the commands do today, and returns `WARNING_UNAUTHORIZED_ACCESS` for `403` and `ERROR_WRONG_INPUT` otherwise. Both commands use it at every step.
2. **The start** catches `400`, `403`, `409` (and `413` for the import) as today, through the helper.
3. **A read of the job and the archive download** catch `403`, through the helper. A Contributor who lost the role while the job runs then reads "No Administrator access to 'org.project'." and gets 44. The download keeps the file untouched: nothing is written when the archive is refused.
4. **A `401` at any step** is left to `Program.Main`, also at the start, where the commands caught it until now. A `401` has no body, so there is no server message to print; `Program.Main` explains it, adds the hint of a machine sign-in, and returns 44 as before. The exit code of a `401` does not change, only its text at the start.
5. **No role check in `sform`.** A `403` can also be a missing scope or `AnnotationRestricted`, so the server's message stays the authority (not a fixed sentence like the one of `sform story delete --permanent`).
6. **Help.** The command descriptions name the role in brackets, like the other commands, and the machine rule:
   - export: "Export the view as a zip (Administrator): its annotations, schemas, templates and configurations, as a folder of JSON files with the versions, or as a git repository with every version as a commit. A machine needs the scopes of the included kinds instead of a role."
   - import: "Import a zip or a folder into the view (Administrator): annotations, schemas, templates and configurations, each recognized by its file name. Only current documents are imported, never versions, and nothing is deleted. A machine needs the scopes of the included kinds instead of a role."
7. `Program.cs` and `TransferPolling` do not change.

### 5. Admin UI

All paths are relative to `ui.admin/src/`.

1. **Capability.** `app/composables/useRole.ts`: `transfer: 'Administrator'` in `capabilityRoles`.
2. **Tabs.** `app/utils/modeling.ts`: the Export & Import entry gets `minRole: 'Administrator'`. The list gets a type with an optional `minRole: AccountRoleName` (`satisfies`), so the filters need no `'minRole' in tab`. `ModelingTab`, `modelingTab(path)` and the redirects keep all five values. A helper `modelingTabsFor(role)` returns the tabs a role sees, in their order.
3. **Header.** `app/components/modeling/ModelingHeader.vue` renders `modelingTabsFor(projectRole)`. Compare views stays disabled as today.
4. **Command palette.** `app/utils/navigation.ts` gives the Modeling entry `modelingTabsFor(context.projectRole)` as its `tabs`, so the palette lists Export & Import only for Administrators and Owners. The comments in `navigation.ts` and `CommandPalette.vue` ("the five of Modeling") follow. The `g e` shortcut stays: for a Contributor it opens the lock below, as `g s` opens the Modeling lock for a Reader.
5. **The page.** `app/pages/[org]/[project]/[view]/modeling/export-import.vue`:
   - `const canTransfer = useCan('transfer')`.
   - Below Administrator the body is an `EmptyState` with the icon `i-lucide-lock`, the title "Administrators only" and the description "The export and the import of the view are available to the project's Administrators.", as on Monitor. No `ExportPanel`, `ImportPanel` or `ImportResult` is mounted, so nothing is requested.
   - `ModelingHeader` stays, so a Contributor who opens the URL (a link, `g e`, a switch of project) reaches the four other tabs; no tab is selected.
   - `canImportNow` uses `canTransfer`.
   - The page marks its latest import as shown in the page (`transfers.showInPage`) only when it shows it, so on the lock the toaster still reports it.
   - The comment names the rule.
6. **Import panel.** `app/components/transfer/ImportPanel.vue` uses `useCan('transfer')`; its fallback is "Importing needs the Administrator role" / "Ask an Administrator or an Owner of the project to import an archive.". The panel is mounted only for Administrators now, so this is a fallback only.
7. **Modeling lock.** `app/pages/[org]/[project]/[view]/modeling.vue` keeps the `Contributor` gate. Its description becomes "The schemas, the templates, the deleted annotations and the comparison of views are available to the project's Contributors, the export and the import to its Administrators."

### 6. Mock API

`ui.admin/src/app/mock/transfer-handlers.ts`: the five transfer routes call ``requireRole(db, `${params.org}.${params.project}`, 'Administrator')``. The seed does not change (no Administrator project, so the visual baselines stay); Owner stands in for Administrator in e2e, as for the permanent delete.

### 7. Tests

**API** (`Storyteller/Api.Functions/test/ViewTransferHttpTests.cs`):

- The access double returns a configurable role for users (Owner by default), and keeps its machine and annotation-restriction behavior.
- Users below Administrator (`None`, `Reader`, `Contributor`, `ContributorWithSecrets`): each of the five endpoints throws `AccessDeniedException` with `AccessDenied` and "No Administrator access to '{organization}.{project}'.", and no transfer service call is made. These tests return early under `DEV_AUTH` (the existing `DevAuth` property), like `KeyValuesHttpTests`.
- `Administrator` and `Owner`: the five endpoints succeed.
- A machine with the full scopes, for which the double has no role: the five endpoints succeed, in both builds.
- The class comment, which says the role checks "pass for an Owner", follows.
- An OpenAPI test checks that the `403` of the five operations names Administrator.
- Run in Debug and in Release (`-c Release`), because the role check exists in Release only.

**`sform`** (`Cli/test/Commands/StorytellerTransferCommandTests.cs`):

- The creation theories stay; a `401` at the start leaves the command as an `ApiException` (to `Program.Main`), for both commands.
- A `403` at the first read and at a later read of the job, for both commands: exit 44 and the server's message.
- A `403` at the archive download: exit 44, the message, and no file.
- A `401` while polling leaves the command.
- The descriptions contain "(Administrator)".

**Admin UI**:

- `test/unit/utils/navigation.test.ts`: the Modeling tabs of a Contributor are the four without Export & Import; Administrator and Owner get five.
- `test/unit/modeling/modeling.test.ts`: the header shows four tabs for Contributor and ContributorWithSecrets, five for Administrator and Owner (the `mountHeader` helper gets an access map); the Reader lock text keeps "available to the project's Contributors".
- `test/unit/transfers/transfer-page.test.ts`: the Import panel fallback names Administrator; the page shows the "Administrators only" lock and no transfer panels for a Contributor, and mounts them once the role becomes Administrator.
- `test/unit/transfers/mock-transfers.test.ts`: the five routes answer `403` `AccessDenied` for the Contributor (`acme.shipping`) and Reader (`northwind.portal`) projects, and succeed for Owner and for an Administrator set in the mock db.
- `test/e2e/transfers.spec.ts` and `navigation.spec.ts`: on `acme/shipping` (Contributor) the Modeling tabs have no Export & Import and the Export & Import URL shows the lock with the four other tabs; the stale comment "a Reader exports with sform or the API" goes. `accessibility.spec.ts`: axe on that lock.
- No visual baseline changes: the visual screens run as Owner.

### 8. Documentation

- `docs/Platform/Storyteller/access.md`: the transfer row (Administrator for users; machines with the scopes; `AnnotationRestricted`), and a sentence that schemas, templates, deleted annotations and the comparison of views are read with `Reader` in the API, the Modeling lock being a choice of the admin UI.
- `docs/Platform/Storyteller/export-import.md`: the Access section (the role, the message, machines, Debug builds), the error table, the OpenAPI snapshot name, the exit codes and the steps of `sform`, the Navigation, Who, mock and test notes of the admin UI, and a link to this spec.
- `docs/Platform/Storyteller/authentication.md` (the role summary) and `annotation-lifecycle.md` (the import that overwrites needs Administrator).
- `docs/42for.net/platform/admin-ui.md`, `using-the-platform.md` and `configuration.md`: the public rule.
- `ui.admin/README.md`: the Export & Import tab and lock.
- Historical specs and reviews do not change.

### 9. Phases and commits

1. The spec.
2. API: the role, the descriptions, the tests (`fix`, `test`).
3. The SDKs regenerated (`chore`).
4. `sform`: the helper, the steps, the help, the tests (`fix`, `test`).
5. Admin UI and mock API, with the tests (`fix`, `test`).
6. Documentation (`docs`).
7. The review (`docs`).

### 10. Differences from the CodeRabbit coding plan

- **`sform` and `401`.** The plan catches `401` and `403` at every step. A `401` has no server message, so here it goes to `Program.Main` at every step, which keeps the machine sign-in hint (section 4).
- **The helper** lives in the existing `Output/TransferConsoleExtensions.cs`, next to the other transfer output, not in a new class next to the commands.
- **Help text.** The role is in brackets, like the other commands, plus one sentence for machines.
- **OpenAPI.** The plan updates a description only "if" one names a role. Both do (`Definitions.cs`), and the SDKs are regenerated from a new snapshot.
- **Tests.** The role matrix also covers `None` and `ContributorWithSecrets`, the role just below Administrator, and a machine through all five endpoints; it is run in Release, where the role check exists.
- **Admin UI.** The plan filters the "sidebar tabs"; the sidebar does not render them, the command palette does, so the filter is in `buildNavigation`. The lock keeps `ModelingHeader` (the Monitor precedent hides its tabs, `modeling.vue` replaces the panel). The plan misses `navigation.test.ts`, which asserts five tabs for a Contributor, and the "You can export the view" text of the Import panel.
- **Docs.** The plan misses `authentication.md`, `annotation-lifecycle.md`, the public pages and the README of the admin UI. Its "`403`, or `401` for a machine" correction is not needed: a machine does get `401` at that step (missing machine, credential policy); the text gains `AnnotationRestricted`.

### Out of scope

- Raising the modeling reads (section 1.3).
- Rejecting machines from transfers (section 1.2).
- Binding a job to the user who started it, or re-checking access in the queue functions.
- How the admin UI store treats a `403` while it follows or restores a job (it retries three times, then reports the server's message; a restored import stays unreadable until it expires). It only happens to a user who lost the role during a job.
- The general `401`/`403` text of `Program.Main` for other commands.
