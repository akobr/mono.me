# No Access for a Reader on the Machines Page

## Overview

A Reader who opened `/{org}/{project}/machines` against a local API saw the machines of the project, because a Debug build of the Functions API (`DEV_AUTH`) skips the role checks of users and the admin UI showed "No access" only when the API refused. A Release API already refuses the list below Contributor (`403`), and the rule stays `Contributor`. The Machines page and the machine detail now check the role before they ask and show the refusal themselves, the API tests assert the Reader's `403` in every build, and the docs say what a local Debug API does not check. Spec: [2026-11-03 No Access for a Reader on the Machines Page](../2026-11-03%20No%20Access%20for%20a%20Reader%20on%20the%20Machines%20Page.md). Linear: [424-164](https://linear.app/424fun/issue/424-164/reader-sees-the-machine-list-on-the-machines-page-instead-of-no-access).

## What Was Done

### 1. The Machines page checks the role before it asks

`app/pages/[org]/[project]/machines.vue`:

- `useGetMachineAccesses` is enabled only with `canManage` (Contributor), so below it no request for the machines is sent.
- The **Machine access** section first shows `ErrorState` with the refusal of `roleRequiredError(capabilityRoles.machines, 'Machines')`: **No access**, the shield icon, "Machines need the Contributor role or a higher one in this project." and **Retry**.
- **Retry** calls `session.loadAccount(true)`; with a role of Contributor or higher after the reload, the query runs and the list shows.
- `useHashScroll` does not wait for the list when it is never asked for.
- Deviation found in review: the condition is `context.projectKey && !canManage`, not `!canManage`. The context guard clears the project before the page of a navigation out of the project has loaded, so the role read as `None` and an Owner saw **No access** for the moment the next page took to load. Without the project the section falls back to the loading state, as before the change.

### 2. The machine detail does the same

`app/pages/[org]/[project]/machines/[id].vue`: the query is enabled with `!!context.project && canManage`, and the panel shows the same refusal with **Retry** (also only while the context has the project). The Orval default of `enabled` only checks for `null`, so it never held the query back before.

### 3. A shared refusal

`app/utils/errors.ts`: `roleRequiredError(role, subject)` returns a `StorytellerError` with `status: 403` and `errorCode: 'AccessDenied'`, so `describeError` and `ErrorState` render it exactly like a refusal of the API.

### 4. The role checks of users in a Debug build, for tests

- `Api.Functions/src/Security/UserRoleChecks.cs` (internal): in a Debug build `AreSkipped` is true unless `Enforce()` set an `AsyncLocal<bool>` for the current flow; the returned scope restores the previous value. In a Release build `AreSkipped` is always false and `Enforce()` does nothing, so nothing can turn a check off.
- `HttpRequestDataExtensions.CheckUserAccessAsync` runs `EnsureUserRoleAsync` unless `UserRoleChecks.AreSkipped`, instead of the `#if DEV_AUTH` return. This covers the role check on the access point of every route that goes through it, and the role that the scopes of a machine need (`EnsureRoleForMachineScopesAsync`).
- As planned, `User.Impersonation`, the secret rights of `GetSecretRightsAsync` and `System` for anonymous requests keep their `DEV_AUTH` branches. The review found that the first wording of the class comment and of `access.md` claimed a Debug build then checks "as a Release build does"; both now name the one check the switch covers.

### 5. API tests

- `MachineHttpTests.ListAndGet_RoleChecksEnforced_NeedAContributorInEveryBuild` (a theory over the six roles, inside `UserRoleChecks.Enforce()`): `None` and `Reader` get `AccessDeniedException` "No Contributor access to 'org1.proj1'." from `GetMachines` and `GetMachine`, mapped by `ErrorResponseMapping.ToActionResult` to `403` with `ErrorCode` `AccessDenied`, and the access service is not called; the other roles get `200`. Each call looks up the role once.
- `UserRoleChecksTests`: the default per build, nested scopes and their restore, and a flow started before `Enforce()` keeping the default while a flow started inside inherits it.
- The existing Release-only Reader case of `GetAndList_OfAMachineWithEveryScope_NeedOnlyAContributor` is unchanged.
- Run: `dotnet test --project src/Platform/Storyteller/Api.Functions/test` in Debug and with `-c Release`, 2752 passed each.

### 6. Admin UI tests

- `test/unit/access/machines-page.test.ts` (new, six tests), with the mock handlers and, where it matters, an override that answers the list and the machine with `200` whatever the role, as a Debug API does:
  - a Reader of `northwind/portal` sees **No access** and **Retry** in **Machine access**, no table, no **New machine**, no **Shared certificates** or **Trusted issuers**, and no request is sent;
  - **Retry** reloads the account, and once the account gives Contributor the list shows;
  - **Retry** keeps **No access** while the role stays Reader, still without a request;
  - a Contributor of `acme/billing` sees the list without the sections of Administrators;
  - an Owner does not see **No access** when the context loses the project (the navigation out);
  - the detail of a machine shows **No access** to a Reader without a request.
  Without the fix, the four Reader tests fail; without the project check, the Owner test fails.
- `test/e2e/access.spec.ts`: "a Reader gets No access on Machines and on the detail of a machine", desktop and mobile. The mock API refuses a Reader itself, so this test describes the expected screen (ACC-MCH-08) rather than reproducing the defect; the unit tests do that.
- Run: the whole Vitest suite (102 files, 1484 tests), `npm run lint`, `vue-tsc --build`, and the new e2e test on desktop and mobile.

### 7. Automation tests and documentation

- `UI.Admin.AutomationTests/src/Pages/MachinesPage.cs`: the comments say the page shows **No access** with **Retry** below Contributor without asking. ACC-MCH-08 is unchanged; it was not run against a local stack here, and with this change it no longer depends on the build of the API.
- `docs/42for.net/platform/admin-ui.md`, Machines row: a Reader who opens the page or the detail by its address sees **No access**; **Retry** reads the role again.
- `src/Platform/ui.admin/README.md`: the **Roles** bullet describes the check of the Machines page and `roleRequiredError`, and the e2e list has the Reader case.
- `docs/Platform/Storyteller/access.md`, **Roles**: a row for listing and reading machines (`Contributor`, users only), the sentence on `UserRoleChecks.Enforce()` and what it covers, and the Machines page next to the Modeling lock of the admin UI.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`, the `dev` profile: a local Debug API skips the role checks of users, so the role tests pass on what the admin UI enforces and on the checks of the services (ACC-MEM-04); a refusal that only a route's role check gives shows only against a Release API.
- `Api.Web` has no machine routes, so nothing changed there.
