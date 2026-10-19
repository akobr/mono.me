# Administrator Role for the Export and Import of a View

## Overview

The export and import of a view are now a permission rule of the API, not only a lock of the admin UI. A user needs the `Administrator` role on the project, so `Administrator` or `Owner`, on all five transfer routes: start, read and download an export, start and read an import. Below it the API answers `403` `AccessDenied` with "No Administrator access to '{organization}.{project}'.". Machines have no role and keep the scopes of the included kinds. The modeling reads (schemas, templates, deleted annotations, Compare views) stay at `Reader`. The OpenAPI descriptions and both SDKs (0.9.32), `sform story export` and `sform story import`, the Export & Import tab and page of the admin UI, its mock API and the docs follow. This review implements [Administrator Role for the Export and Import of a View](../2026-10-19%20Administrator%20Role%20for%20the%20Export%20and%20Import%20of%20a%20View.md) in full, in one phase. Linear: [424-66](https://linear.app/424fun/issue/424-66/decide-whether-readers-may-export-a-view-and-read-modeling-data).

## What Was Done

### 1. The rule

As specified:

- users need `Administrator` on every transfer route;
- machines keep the scopes of the included kinds, and a machine restricted to an annotation still gets `403` `AnnotationRestricted`;
- the modeling reads stay at `Reader`;
- status and download check the current role on every request, and a queued job still finishes.

**One consequence to confirm.** Contributors create the machines of a project, with any scope (`AccessHttp.PostMachines` and `PutMachine` need `Contributor`). So a Contributor can still export or import through a machine with the scopes. Code review raised this. It follows from decision 1.2 of the spec, which keeps machines on their scopes, so it is not changed here. `export-import.md` now says it in plain words. The rule limits what a user does with their own sign-in, and is not a boundary for the data. A Contributor can already read and write each document one by one. If the owner wants the rule to bind machines too, the options are to reject machines from transfers, or to need `Administrator` for a machine with transfer scopes. Both break the documented pipeline use and are out of scope here.

### 2. Access checks of the API

- `ViewTransferHttp.cs` passes `AccountRole.Administrator` to the five `CheckAccessToProjectAsync` calls. The order of the checks, the scope checks, the machine path and the error mapping are unchanged.
- The class comment names the rule for users (424-66) and for machines.

### 3. OpenAPI descriptions and SDKs

- `Definitions.Descriptions.ResponseTransferForbiddenExport` and `ResponseTransferForbiddenImport` match the spec word for word. Each starts with "The user's role on the project is below Administrator, or a scope of an included kind is missing (AccessDenied), or the machine is restricted to an annotation (AnnotationRestricted).", then lists its own scopes, and ends with "The message names the missing role or scope."
- `OpenApiExportTests` exported `info.version` `0.9.32.54789`. The document differs from `open.api.v0.9.31.json` only in the version and in the five `403` descriptions.
- `Sdk.NSwag/open.api.v0.9.32.json` was added next to the older snapshots, and the README command points at it. In the regenerated `src/ApiSdk.g.cs`, five lines changed: the messages that the five transfer methods throw for a `403`.
- `sdk.typescript/`: `open.api.v0.9.31.json` was replaced by `open.api.v0.9.32.json`. `npm run generate` changed only the version header of the generated files and set `package.json` to `0.9.32`. `typecheck`, `lint`, `test` (126 passed, 1 skipped) and `build` pass.
- Code review noted that "The message names the missing role or scope." does not fit `AnnotationRestricted`, whose message names the restriction. The wording is the one the spec gives. Changing it would need a third snapshot, so it stays as it is.

### 4. `sform story export` and `sform story import`

- `Output/TransferConsoleExtensions.WriteRefusal(IExtendedConsole, ApiException)` prints `GetErrorMessage()` with `ThemedError`. It returns `WARNING_UNAUTHORIZED_ACCESS` (44) for a `403` and `ERROR_WRONG_INPUT` (2) for anything else.
- **Export.** The start catches `400`, `403` and `409`. Polling and the archive download each catch `403`. All three go through the helper.
- **Import.** The start catches `400`, `403`, `409` and `413`. Polling catches `403`. Both go through the helper.
- The download is in its own `try`. The save and the extraction now run in a `using (archive)` block. A refused archive writes nothing, neither the file nor the `--extract` folder.
- **A `401`** is no longer caught at the start. At every step it leaves the command for `Program.Main`, which prints its sentence and the hint of a machine sign-in, and returns 44 as before.
- **Help.** Both descriptions match the spec: "(Administrator)", plus "A machine needs the scopes of the included kinds instead of a role."
- `Program.cs` and `TransferPolling` are unchanged. `sform` does no role check of its own.

### 5. Admin UI

- **The rule in the code.**
  - `useRole.ts` has `transfer: 'Administrator'`.
  - In `utils/modeling.ts`, the tab list is checked with `satisfies readonly ModelingTabEntry[]`. Export & Import has `minRole: 'Administrator'`, and the new `modelingTabsFor(role)` filters the list. `ModelingTab`, `modelingTab(path)` and the redirects still know all five tabs.
- **Where the tabs show.**
  - `ModelingHeader.vue` renders `modelingTabsFor(projectRole)`.
  - `utils/navigation.ts` gives the Modeling entry the same tabs, so the command palette lists Export & Import only for Administrators and Owners. The comments in `navigation.ts` and `CommandPalette.vue` follow.
  - `g e` is unchanged.
- **The Export & Import page** shows the "Administrators only" `EmptyState` below Administrator, as specified, under `ModelingHeader`:
  - no section is mounted;
  - `canImportNow` uses `canTransfer`;
  - the `showInPage` watch reads no import while the page is locked, so the toaster still reports one.
- **The fallback texts.** `ImportPanel.vue` and the Modeling lock of `modeling.vue` have the new texts.
- **Addition (code review).** `ModelingHeader` keys its `UTabs` on the values of its tabs. A user on Export & Import who switches to a project where they are a Contributor keeps the same page and header, since `context-paths` keeps the tab. Then no tab is selected, and Reka's `TabsIndicator` kept its last position: the underline stayed where Export & Import had been, under "Deleted". With the key, a new set of tabs gets new tabs and no stale underline.

### 6. Mock API

- The five transfer routes call `requireRole(…, 'Administrator')`, as specified.
- The seed is unchanged, so Owner stands in for Administrator in e2e, and the visual baselines stay.
- The mock keeps the message of its shared `requireRole` ("The Administrator role on {key} is required."), not the server's. The mock tests check the mock's own text.

### 7. Tests

- **Api.Functions** (`ViewTransferHttpTests`):
  - The access double `OwnerAccess` became `ProjectAccess.Create(role = Owner, machineAnnotationKey)`. Its machine and restriction behaviour is unchanged.
  - `RecordingTransfers` also records job reads, so "nothing is read" can be asserted.
  - New tests:
    - `EveryRoute_ByAUserBelowAdministrator_IsRefused` for `None`, `Reader`, `Contributor` and `ContributorWithSecrets`: all five routes throw `AccessDenied` with "No Administrator access to 'acme.web'.", and nothing is queued or read. It returns early under `DEV_AUTH`.
    - `EveryRoute_ByAnAdministratorOrAnOwner_Succeeds`.
    - `EveryRoute_ByAMachineWithTheFullScopes_SucceedsWithoutARole`.
    - `Document_TheForbiddenOfEveryRoute_NamesTheAdministratorRole`, on the rendered document.
  - A mutation check (`GetViewExport` back to `Contributor`) failed the refusal cases in Release.
  - The suite passes: 109 of 109 in Debug and in Release.
  - The whole project:
    - 979 of 979 pass in Debug.
    - In Release, 973 pass. The 6 failures predate this change (five in `MachineHttpTests`, plus `AnnotationsHttpTests.Purge_ByUser_CallsTheService`, as the Phase 2 review of the Export and Import of a View records), and none of them touches transfers.
  - `Backend.Core`: 810 pass.
- **Cli**:
  - New tests:
    - the help names the role;
    - a `401` at the start, while waiting and at the download leaves the command;
    - a `403` at the first read and at a later read of the job, for both commands, gives exit 44 and the server's message;
    - a `403` at the download, as a zip and with `--extract`, gives exit 44, the message, and no file or folder.
  - `TransferConsoleExtensionsTests` has a theory for `WriteRefusal`.
  - 589 pass. The spec did not list the `401`-at-download test.
- **Admin UI** (Vitest):
  - `navigation.test.ts`: the tabs per role.
  - `modeling.test.ts`:
    - `mountHeader` takes a role, and four or five tabs show per role;
    - no tab is selected on a Contributor's page;
    - the tabs are rebuilt when the role drops on that page, which fails without the key of section 5.
  - `transfer-page.test.ts`:
    - the Import fallback;
    - a Contributor's lock, which turns into the panels when the role becomes Administrator.
  - `mock-transfers.test.ts`: `403` on all five routes for `acme.shipping` and `northwind.portal`, also for jobs started while the user was Administrator; success for Owner, and for an Administrator set in the mock db.
  - Results: `npm run lint`, `npm run typecheck`, `npm run build` and `npm test` (847 tests) pass.
- **Admin UI** (Playwright):
  - `transfers.spec.ts`: the lock and the four tabs of a Contributor on `acme/shipping`, with Schemas still reachable. The stale comment about Readers exporting is gone.
  - `navigation.spec.ts`: the four tabs, and the command palette with and without Export & Import.
  - `accessibility.spec.ts`: axe on the lock, light and dark.
  - Results: 304 passed and 6 skipped (mobile-only skips that were already there), desktop and mobile. The Docker visual tests were not run; they capture Owner screens only, which do not change.
- **A test outside this change.** The New schema dialog test of 424-59 expected the old `schemas/` path, and failed on main since 424-38 moved the schemas below `modeling/`. It now expects `modeling/schemas/` (`1f75bb60`).

### 8. Documentation

- `access.md`:
  - the transfer row gives the rule for users and machines;
  - a paragraph says the modeling reads need only `Reader`, and that the Modeling lock is a choice of the admin UI.
- `export-import.md`:
  - the check order, the Access section (users, machines, Debug builds), the error table and the OpenAPI section with `open.api.v0.9.32.json`;
  - the steps and exit codes of `sform`: `44` for a `403` at any step and for any `401`, and the stale "while waiting" under `1`;
  - the Navigation, Who, store, mock and test notes of the admin UI, and a link to the spec.
  - Code review added the sentence about machines that Contributors create.
- The rest:
  - `authentication.md`: the role summary, and a stale "of configurations".
  - `annotation-lifecycle.md`: the import that overwrites, and who runs `sform story export|import`.
  - The public pages `admin-ui.md`, `using-the-platform.md`, `configuration.md` and `annotations.md`.
  - The README of the admin UI.
- Specs and earlier reviews are unchanged.

### Code review

A review workflow ran five reviewers: the API, `sform`, the admin UI, the docs, and conformance with the spec. A skeptic then checked each finding against the code.

Two findings were confirmed:

- the stale underline of the tabs after a role change on the Export & Import page, fixed in section 5;
- the missing review, which is this file.

The skeptics refuted these findings:

- A Contributor can export and import through a machine they create. The facts hold, but the spec chose it (decision 1.2). It is recorded in section 1 and in `export-import.md` for the owner to confirm.
- The closing sentence of the `403` descriptions does not fit `AnnotationRestricted`. It is the spec's wording (section 3).
- The docs do not say that a Contributor can use a machine. This is now said (section 8).
- The review was missing while the last phase had not run yet.

The `sform` reviewer found nothing.
