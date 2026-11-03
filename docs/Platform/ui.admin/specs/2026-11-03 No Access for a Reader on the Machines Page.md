# No Access for a Reader on the Machines Page

Linear: [424-164](https://linear.app/424fun/issue/424-164/reader-sees-the-machine-list-on-the-machines-page-instead-of-no-access) — Reader sees the machine list on the Machines page instead of "No access".

## Problem

A project **Reader** who opens `/{org}/{project}/machines` by its URL (the sidebar hides **Machines** below Contributor) sees the **Machine access** table with the machines of the project: their ids, scopes, annotations and credential kinds. Test case ACC-MCH-08 (Testiny 163, `MachineTests.Machines_Reader_ShowsNoAccess`) expects **No access** with **Retry** in that section, no **New machine** and no **Shared certificates** or **Trusted issuers** section. The test failed in run `20261102-230831-hviz` against the local admin UI (`http://localhost:5173`, the `dev` profile) with the API answering `GET v1/{org}/{project}/access/machines` with `200` for the Reader.

The cause is the build of that API, not its rule:

- The list needs `Contributor` (`AccessHttp.GetMachines`, `CheckUserAccessToProjectAsync(…, AccountRole.Contributor)`), as `docs/Platform/Storyteller/authentication.md` documents (get and list of machines need `Contributor`, because Contributors create machines). A Release API answers `403 AccessDenied` to a Reader.
- A Debug build of the Functions API (`DEV_AUTH`, the default of Aspire, `func start` and the IDEs) skips the role checks of users (`docs/Platform/Storyteller/access.md`, **Roles**), so locally it answers `200` to everyone.
- The admin UI asks for the list whatever the role, and shows "No access" only when the API refuses. Every other Reader case of the suite passes against the same local API because the UI hides what the role does not allow; the Machines list is the one place where it relies on the API alone. The MSW mock enforces the role (`requireRole(db, key, 'Contributor')`), so the `mock` profile showed "No access" and passed.

So production is not exposed, but the UI does not keep its own role rule on this page, the local stack shows a Reader the machines, and the only API test of a Reader on the machine list (`MachineHttpTests.GetAndList_OfAMachineWithEveryScope_NeedOnlyAContributor`) asserts the refusal in Release builds only, which no routine test run uses.

The issue suggests the list may need `Administrator`. It stays at `Contributor`: Contributors create, reset and delete machines (with the role their scopes need) and list them for that; the documented rule is `Contributor`.

## Current State

- `src/Platform/ui.admin/src/app/pages/[org]/[project]/machines.vue`:
  - lines 41–42: `canManage = useCan('machines')` (Contributor), `canAdmin = useCan('manageAccess')` (Administrator).
  - lines 46–50: `useGetMachineAccesses` is enabled for any project (`enabled: () => !!context.project`), whatever the role.
  - lines 138–148: `LoadingState` while pending, `ErrorState` on an error (a `403` shows "No access" with **Retry**, `utils/errors.ts:19–20`), `EmptyState` "No machine access", else the table.
  - **New machine** (lines 130, 146) needs `canManage`; **Shared certificates** and **Trusted issuers** (lines 241, 245) need `canAdmin`; so a Reader already gets neither.
  - line 120: `useHashScroll` waits for `!machines.isPending`.
- `src/Platform/ui.admin/src/app/pages/[org]/[project]/machines/[id].vue` lines 30–35, 73–74: the detail of a machine asks the API whatever the role, the same gap for `/{org}/{project}/machines/{id}`.
- `src/Platform/ui.admin/src/app/components/common/ErrorState.vue`: the title and the icon come from `describeError` (`403` → "No access", `i-lucide-shield-x`), with **Retry**.
- `src/Platform/ui.admin/src/app/composables/useRole.ts`: `capabilityRoles.machines = 'Contributor'`.
- `src/Platform/ui.admin/src/app/stores/session.ts`: `loadAccount(true)` reloads the account and its `AccessMap` (used by `DefaultLayout`, `MembersTable` and the create dialogs).
- `src/Platform/ui.admin/src/app/mock/access-handlers.ts:467–479`: the mock answers `403` below Contributor.
- `src/Platform/Storyteller/Api.Functions/src/Security/HttpRequestDataExtensions.cs:485–493`: `CheckUserAccessAsync` returns before the role check under `#if DEV_AUTH`. `Security/BearerValidationMode.cs` is the precedent of a switch that lets tests run the Release path of a check in a Debug build.
- `src/Platform/Storyteller/Api.Functions/test/MachineHttpTests.cs:674–705`: the Reader case of `GetAndList_OfAMachineWithEveryScope_NeedOnlyAContributor` expects `200` when `DevAuth`.
- `src/Platform/Storyteller/Api.Web` has no machine routes; nothing to change there.
- `src/Platform/UI.Admin.AutomationTests/src/Pages/MachinesPage.cs:16–17,37–40`: the page object says the API refuses the list below Contributor; the locators (`NoAccess`, `RetryButton` inside the section of the "Machine access" heading) already match the expected state.
- Docs: `docs/42for.net/platform/admin-ui.md` (Machines row of **People, machines and settings**), `src/Platform/ui.admin/README.md` (**Roles**), `docs/Platform/Storyteller/access.md` (**Roles**), `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` (the `dev` profile) say nothing about a Reader on the Machines page or about the local API skipping role checks.

## Proposed Changes

### 1. The Machines page checks the role before it asks (`machines.vue`)

- The machine list query is enabled only with `canManage` (`enabled: () => !!context.project && canManage.value`): below Contributor the page does not request the machines, so no build of the API can show them.
- The **Machine access** section shows, before the loading state, `ErrorState` with a `403` for a role below Contributor: the heading **No access**, the shield icon and **Retry**, as when the API refuses. Its message: "Machines need the Contributor role or a higher one in this project."
- **Retry** reloads the account (`session.loadAccount(true)`), so a role granted since the sign-in takes effect: from Contributor up the query runs and the list shows. A Reader still sees No access.
- `useHashScroll` no longer waits for a list that is never asked for: `(!canManage || !machines.isPending) && …`.
- **New machine**, **Shared certificates** and **Trusted issuers** keep their role conditions (unchanged).

### 2. The machine detail does the same (`machines/[id].vue`)

The detail query is enabled only with `canManage`, and below Contributor the panel shows the same **No access** state with **Retry** instead of the machine.

### 3. A shared refusal (`utils/errors.ts`)

`roleRequiredError(role: AccountRoleName, subject: string): StorytellerError` builds the refusal a screen shows when it knows the role is too low before it asks: `status: 403`, `errorCode: 'AccessDenied'`, message `` `${subject} need the ${role} role or a higher one in this project.` ``. `ErrorState` then renders it like an API refusal. Both pages pass `capabilityRoles.machines`.

### 4. The role checks of users in a Debug build of the API, for tests (`Api.Functions`)

- New `Security/UserRoleChecks.cs` (internal, beside `BearerValidationMode`): `AreSkipped` tells whether the role checks of users are skipped, and `Enforce()` turns them on for the current async flow until the returned scope is disposed.
  - Debug builds (`DEV_AUTH`): skipped unless a test enforced them in its flow (an `AsyncLocal<bool>`), so a test runs the role checks of a Release build while the other tests, in parallel, keep the Debug behaviour; no static state to serialize.
  - Release builds: never skipped, and `Enforce()` changes nothing. Nothing can turn the checks off.
- `HttpRequestDataExtensions.CheckUserAccessAsync` skips `EnsureUserRoleAsync` only when `UserRoleChecks.AreSkipped`. `User.Impersonation` and the other `DEV_AUTH` branches (secret rights, `System` for anonymous requests, token decoding) are unchanged.

### 5. API tests

- `MachineHttpTests`: a new theory, `ListAndGet_RoleChecksEnforced_NeedAContributorInEveryBuild`, with `UserRoleChecks.Enforce()`: `None` and `Reader` get `AccessDeniedException` "No Contributor access to 'org1.proj1'." from `GetMachines` and `GetMachine`, which `ErrorResponseMapping.ToActionResult` maps to `403` with `ErrorCode` `AccessDenied`, and the access service is not called; `Contributor`, `ContributorWithSecrets`, `Administrator` and `Owner` get `200` after one role lookup each.
- `UserRoleChecksTests`: outside a scope `AreSkipped` equals `HttpRequestDataExtensions.IsDevelopmentAuthentication`; inside `Enforce()` it is false; after the scope it is restored; a flow that did not enforce (a task started before) keeps the default.

### 6. Admin UI tests

- New `test/unit/access/machines-page.test.ts` (Vitest, mounted with `mountScreen`, MSW server with the mock handlers and an override answering the machine list with `200`, as a Debug API does):
  - a Reader (`northwind/portal` of the default seed) sees **No access** and **Retry** in the **Machine access** section, no table, no **New machine**, no **Shared certificates** or **Trusted issuers**, and no request for the machine list is sent;
  - **Retry** reloads the account: when the account now gives Contributor, the list shows;
  - a Contributor sees the machine list;
  - the detail page of a machine shows **No access** to a Reader without a request.
- `test/e2e/access.spec.ts`: a Reader on `/northwind/portal/machines` sees **No access** with **Retry**, no **New machine** and no Administrator sections (desktop and mobile), as ACC-MCH-08 describes.

### 7. Automation tests and documentation

- `UI.Admin.AutomationTests/src/Pages/MachinesPage.cs`: the comment says the page shows No access with Retry below Contributor (without asking the API), as when the API refuses. ACC-MCH-08 itself is unchanged.
- `docs/42for.net/platform/admin-ui.md`, Machines row: a Reader who opens the page by its address sees **No access**; **Retry** reloads the role.
- `src/Platform/ui.admin/README.md`, **Roles**: the Machines page and the machine detail check the role before they ask, as a Debug API skips its role checks of users.
- `docs/Platform/Storyteller/access.md`, **Roles**: the operations table gets the machines (list and read: `Contributor`), and the Debug sentence mentions `UserRoleChecks.Enforce()` for tests.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`, the `dev` profile: a local Debug API skips the role checks of users, so there the role cases verify what the admin UI enforces itself; the API's refusals are verified against a Release API (a deployed target).
