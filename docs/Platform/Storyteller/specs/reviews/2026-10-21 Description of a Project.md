# Description of a Project

Linear: [424-78 Add description to project in 2S platform](https://linear.app/424fun/issue/424-78/add-description-to-project-in-2s-platform)

## Overview

A project now has a description, like a view. Administrators set it with `PUT v1/access/points/{key}`, or at creation with `ProjectDescription` on `POST v1/access/points` and `POST v1/access/account`. Every member reads it with the new `GET v1/access/projects`, which returns `ProjectSummary` (key, role, description) and nothing of the members or the policies. The description is stored on the project's access point document and replaced under its ETag. The OpenAPI snapshot is `open.api.v0.9.45.json`, and both SDKs are regenerated from it. In the admin UI, onboarding, "New organization" and "New project" take a description, the organization overview shows it on the project cards, and Settings shows it to every member and lets Administrators edit and clear it. Every project of every mock dataset has a description: the 14 showcase projects carry the texts offered on the issue, and the three legacy projects have their own. This review covers [Description of a Project](../2026-10-21%20Description%20of%20a%20Project.md) in full, delivered in the phases of its section 10, one review for all of them.

The branch starts from `feat/424-77` (`e89894aa`, PR #123, not merged yet).

| Commit | Subject |
| --- | --- |
| `056b6fbb` | docs(platform): plan the description of a project |
| `282507b1` | Phase 1, feat(platform): store the description of a project in Storyteller |
| `d5b09c34` | Phase 2, feat(platform): set and read the description of a project in the Storyteller API |
| `6dc6d14e` | Phase 3, chore(platform): regenerate the SDKs from OpenAPI 0.9.45 |
| `19906dc9` | Phase 4, feat(platform): describe every project of the admin UI mock data |
| `33e5d58d` | Phase 5, feat(platform): show and edit the description of a project in the admin UI |
| `41ef961e` | docs(platform): document the description of a project |
| `32abd2f2` | test(platform): put a grant between the read and the replace of a project description |
| `eb1f014a` | fix(platform): wait for a running fetch of the projects list in the admin UI |
| `cd76bd48` | fix(platform): break long project descriptions on the overview cards of the admin UI |
| `8dd5217f` | fix(platform): show the stored description after a save in the admin UI settings |
| `6816cf56` | test(platform): check the project description the admin UI sends on creation |
| `e9a88016` | test(platform): name the SDK test of the project update path after its assertions |
| `07c4bcfc` | fix(platform): invalidate the saved project after a description save in the admin UI |

## What Was Done

### 1. The model

As planned:

- `AccessPoint` gets `string? Description { get; init; }` (null for an organization); `IAccessPoint` gets `string? Description { get; }`, getter-only like its other members.
- New `Abstractions.Access/src/Model/ProjectSummary.cs` with `Key`, `Role` and `Description`.
- `AccessPointCreate` and the internal `AccountCreate` get `ProjectDescription`; new `Backend.Core/src/Accessing/Model/AccessPointUpdate.cs` with `Description`.
- `IAccessService` gets `GetProjectsAsync` and `UpdateAccessPointAsync` with default implementations that throw `NotSupportedException`, so no hand-written test double changed.

### 2. Storage

As planned, in `Backend.CosmosDb/src`:

- `AccessPointEntity.Description`; `AccessMappings.ToAccessPoint` and `ToEntity` copy it. Old documents read as null.
- `CosmosAccessService.NormalizeDescription` is the rule of views: trimmed, blank is null.
- `CreateAccessPointAsync` writes the description to the new project only; `CreateAccountAsync` forwards it.
- `GetProjectsAsync` reads the account, keeps the keys with a dot and a role above `None`, and runs `SELECT ap.Key, ap.Description FROM ap WHERE ARRAY_CONTAINS(@ids, ap.id)` in the `access` partition into a private `ProjectDescriptionRow`. A key whose point is missing is left out; the result is ordered by key.
- `UpdateAccessPointAsync` throws `ArgumentException` for an organization key, reads the point with `TryReadItemWithETagAsync`, replaces `entity with { Description }` with `IfMatchEtag`, reads again and retries once on `412`, and throws `ConflictException` (`Conflict`) on the second. A missing project throws `NotFoundException`.

### 3. The HTTP API

As planned, in `Api.Functions/src`, with these details:

- `PUT v1/access/points/{key}` is the handler `PutAccessPoint` (operation id `UpdateAccessPoint`), named like `PutMachine` and `PutMachineAuthentication` in the same file. Its order is the spec's: machine refusal (`403`, "Describing a project needs a user with the Administrator role."), `CheckScope(User.Impersonation)`, the normalized key, `Administrator`, `TrySplitProjectKey` (`400`), the body under `RequestBodyLimits.Control` ("a project update"), then the service with `model ?? new AccessPointUpdate()`.
- `GET v1/access/projects` (`GetProjects`) refuses machines first ("Listing projects needs a user."), then checks the scope and returns `GetProjectsAsync` of the caller.
- `PostAccount` forwards `ProjectDescription` and answers `400` ("A project description needs an organization and a project.") for a description that is not blank without them.
- The `404` and `409` of the PUT are documented with an `ErrorResponse` body, which the middleware writes for those exceptions. The OpenAPI descriptions of `CreateAccount` and `CreateAccessPoint` mention the project description.
- A private `EnsureNotMachine(request, message)` throws `AccessDeniedException` for both new operations.
- `RequestBodyLimits.cs` names the project update and the project description under `Control`.

### 4. API tests

- `CosmosAccessServiceTests` (emulator): creation trims onto the project and not the organization, blank descriptions are not stored, account creation describes only the project, the update sets, replaces and clears, keeps `AccessMap`, `MachineAuthentication` and `KeyValues`, rejects unknown projects and organization keys, and later grants, key/value policy writes and machine policy writes keep the description. `GetProjects` covers every role, organizations left out, a missing point and an unknown account.
- Beyond the spec, `ReplaceInterceptor.cs` (a Cosmos `RequestHandler` that runs an action right before the replace of one document) makes the update's ETag guard testable: `UpdateAccessPoint_GrantBeforeReplace_RetriesAndKeepsTheMember` puts a grant between the read and the replace and expects two replaces with both changes kept; `UpdateAccessPoint_ChangedBeforeEveryReplace_ThrowsConflict` changes the point before both replaces and expects `ConflictException`. Both fail without `IfMatchEtag`. `ConcurrentGrantAndDescription_BothLand` stays, with a comment that claims only what it proves.
- `AccessPointHttpTests`: forwarding, clearing on `""`, `null` and `{}`, `400` for `[1, 2]`, `{"Description": 42}` and an organization key without an update call, `403` for machines on both operations, refused lower roles outside `DEV_AUTH`, and the projects list. `MembersHttpTests`, `RouteTableTests` (`ProjectRoutes_AreRegistered`), `OpenApiDocumentTests` and `OpenApiExportTests` (the new body row and `Document_ProjectOperations_ArePublished`) as planned.

### 5. OpenAPI snapshot and SDKs

As planned. The export gave `info.version` `0.9.45.54704`, so the snapshot is `open.api.v0.9.45.json` in `Sdk.NSwag/` and `sdk.typescript/`; `sdk.typescript/open.api.v0.9.44.json` is removed. `ApiSdk.g.cs` has `UpdateAccessPointAsync` and `GetProjectsAsync` and is byte-identical to a fresh run of the README command. `npm run generate` is reproducible and set the SDK package to `0.9.45`. `helpers.test.ts` and `msw.test.ts` cover `updateAccessPoint` and `getProjects`. `Sdk.NSwag/README.md` and `docs/Platform/Storyteller/export-import.md` name the new file. As for `AccessPoint.Key`, the generated `ProjectSummary.Key` is optional in TypeScript, because the OpenAPI document marks no property as required.

### 6. Mock data of the admin UI

As planned:

- `ShowcaseProject.summary`, which nothing read, is now the required `description`; the 14 project files carry the texts of the issue comment word for word, and `seed.ts` puts them on the project points. Organizations have none.
- The legacy `accessPoint` helper in `db.ts` takes an optional description; `acme.billing`, `acme.shipping` and `northwind.portal` have the spec's texts in the `default`, `large`, `new-user` and `invited` scenarios.
- `handlers.ts` stores the trimmed `ProjectDescription` on creation and answers `PUT /access/points/:key` and `GET /access/projects`.

Details that follow the real API rather than the spec's mock section: the PUT checks the role, then the organization key, then the body, then the project (`404`), as `PutAccessPoint` and the service do; besides text that is not JSON, it rejects JSON that is not an update (`"x"`, `[…]`, `{"Description": 42}`); `GET /access/projects` leaves out a key whose point is missing.

Tests: `showcase.test.ts` (every project has a trimmed description of at most 500 characters on its point, organizations none), the new `access/project-description.test.ts` (13 cases against the mock API) and `mock/legacy.test.ts` (every legacy scenario, taken from `scenarios` without `showcase`).

### 7. Admin UI

As planned, with these details:

- `forms/names.schema.ts` has `descriptionSchema`; "New project" has a "Description" textarea, `OrganizationForm` a "Project description" textarea, and `OrganizationCreateDialog` and onboarding send the trimmed `ProjectDescription`.
- `queries/projects.ts` has `useProjectSummaries()`. It compares the project keys of the session's `AccessMap` with the listed ones, also when a page mounts, because the account can change while no page shows the list (a new organization from `/orgs`, an accepted invitation). It waits for a fetch under way instead of cancelling it, asks at most once per set of keys, and runs only with an account.
- Settings: lower roles see the description in a second `<dl>` under the grid, with `LoadingState`, `ErrorState` and "No description" only once the list has loaded. Administrators get the form at full width; it validates with `descriptionSchema.safeParse`, as the machine policy form validates its own fields, instead of VeeValidate. The text follows the loaded description without overwriting edits and is filled again when the project changes, because Settings keeps its page instance across projects. After a save it shows the stored, trimmed text unless the user typed during the save, and the save invalidates the key it wrote to. `useHashScroll` waits for the list for every role.
- `invalidateProject` invalidates `/v1/access/projects` and the prefix `/v1/access/points/{key}`, which also refetches the machine policy; structural sharing keeps its data object, so the policy form is not reset.
- The overview cards show the description muted, at most two lines, `break-words` (a description without spaces fills both lines with an ellipsis), with the full text in `title`.
- Tests beyond the spec: `access/project-summaries.test.ts` (refetch rules, including a stale list on mount and the once-per-key-set guard with changing answers), `access/create-dialogs.test.ts` (both dialogs send the trimmed description), a project switch and typing during a save in `access/settings-description.test.ts`, the request body of onboarding in `onboarding.spec.ts`, and the two-line card in `settings.spec.ts`.
- Only the four onboarding baselines changed; the settings baseline, scrolled to `#secrets`, did not.

### 8. Documentation

- `docs/Platform/Storyteller/access.md`: a `## Projects` section with the five routes, the description rules and `ProjectSummary`; a row in the roles table; `## Accounts`; the `403`, `409 AccessPointExists`, `409 Conflict` and `413` rows. It documents one thing the spec did not foresee: outside `DEV_AUTH` the role check comes before the project lookup, so a PUT on a project that does not exist answers `403`; Debug builds answer `404`.
- `docs/Platform/Storyteller/authentication.md`: the operations for users only and the body limits.
- `docs/42for.net/platform/using-the-platform.md` and `admin-ui.md`: the description and where the admin UI shows and edits it.
- `src/Platform/ui.admin/README.md`: descriptions in every dataset, the `description` of a showcase project, the e2e coverage.

### 9. Concurrency and known risk

Unchanged, as planned: the machine authentication policy store still upserts without an ETag, and the DbCreator seed still replaces `42dotnet` and `42dotnet.main`.

### Test results

| Project | Result |
| --- | --- |
| `Backend.CosmosDb/test` (local emulator) | 454 passed |
| `Api.Functions/test`, Debug (`DEV_AUTH`) | 1353 passed |
| `Api.Functions/test`, Release | 1344 passed, 9 failed. The 9 (`PutMachineAuthentication_*` ×5, `MachineAuthentication_OfAnOrganization_IsBadRequest`, `GetMachine_StoredRecord_ReturnsIt` and `KeycloakId_GetResetAndDelete_ReachTheAccessService` in `MachineHttpTests`, and `AnnotationsHttpTests.Purge_ByUser_CallsTheService`) fail the same way on `e89894aa`: their doubles answer a role below `Administrator`, or expect call lists without the role check of a Release build. |
| `Cli/test` | 757 passed |
| `sdk.typescript` | typecheck, lint, build; 130 passed, 1 skipped (the smoke test that needs a running API) |
| ui.admin `npm run build`, `npm run lint` | pass |
| ui.admin `npm test` | 96 files, 1421 tests. `page-memory.test.ts` timed out in a hook in some full runs while other suites ran in parallel and passed alone. |
| ui.admin `npm run test:e2e` | 356 passed, 8 skipped (mobile-only skips). Single runs had unrelated failures that passed on rerun: `configuration.spec.ts:283` on desktop, `theme.spec.ts` (the accent colour) on mobile, and two `ERR_CONNECTION_REFUSED` on mobile. |
| ui.admin `npm run test:visual:docker` | 64 passed, with the four onboarding baselines regenerated |

The Backend.CosmosDb tests ran against the local Cosmos DB emulator, whose fixture deletes every database there first.

### Reviews

- **The spec** was reviewed before the first commit from four lenses (backend and security, admin UI and mock data, SDK pipeline and process, completeness against the issue and the CodeRabbit plan). That moved the machine refusal before the scope check, pointed the update at `TryReadItemWithETagAsync`, narrowed the projects query to two fields, added the hash-scroll readiness, the loading and error states and the SDK reinstall, replaced `summary` with `description`, rewrote two legacy descriptions to fit the generated catalog, and corrected the merge notes for #124.
- **Each phase** was checked by an independent reviewer who reran the builds and tests before its commit.
- **The whole branch** was reviewed from five lenses (backend, API, admin UI, mock data and tests, docs). Nine findings came out; three skeptics tried to refute each, and the five that survived were fixed in `32abd2f2` (the update's retry was not really tested), `eb1f014a` (a stale list on mount was fetched twice, and the once-per-key-set test could not fail), `cd76bd48` (a description without spaces was cut to one line) and `8dd5217f` (no test switched project in Settings). A last pass fixed the key the save invalidates (`07c4bcfc`). The refuted ones: the two create dialogs untested (a coverage gap, not a defect; the tests were added anyway as a follow-up), no test of the mock leaving out a key without its point (the mock never builds such an account map), the wording of the mock's `400` (no client sees it), and the comment of `EnsureNotMachine` (true for both callers).
- The follow-ups the phase reviewers noted are done: the SDK test title, the onboarding request body, the create-dialog tests, the trimmed text after a save, and the scenario list of `legacy.test.ts`.
- The admin UI was also checked by hand on the showcase data: the descriptions on the `fabrikam` overview for Owner, Administrator and `ContributorWithSecrets` projects, the read-only description of the Reader project `meridian-health/clinical-integrations`, and an Owner's save on `fabrikam/finance-cloud`, trimmed and shown on the overview.

### Deviations from the spec

1. The PUT handler is `PutAccessPoint`; the operation id is `UpdateAccessPoint` as planned.
2. `IAccessPoint.Description` is getter-only.
3. The `404` and `409` of the PUT document an `ErrorResponse` body.
4. The mock PUT checks in the order of the real API and rejects JSON that is not an update; `GET /access/projects` of the mock leaves out a key without its point.
5. `useProjectSummaries` also compares on mount and waits for a running fetch, instead of reacting only to changes of the keys.
6. Settings validates with `safeParse`, refills the text when the project changes, and the reader's description is a second `<dl>`.
7. Tests beyond the spec: `ReplaceInterceptor` and the two retry tests, `Document_ProjectOperations_ArePublished`, `project-summaries.test.ts`, `create-dialogs.test.ts`, `legacy.test.ts` and a few cases named above.
8. `access.md` documents the `403` of a PUT on a project that does not exist outside `DEV_AUTH`.

### Left for later

- `sform account points get` could print the description and `points create` take one; the regenerated C# SDK already has the fields.
- The machine authentication policy store upserts the project document without an ETag, and the DbCreator seed replaces its two documents whenever it runs (spec section 9).
- `GET v1/access/points/{key}` answers `200` with an empty body for a missing access point.
- The edit modal of a view description has no 500-character rule, and the mock `PUT …/views/:view` does not trim.
- The 9 Release-only failures of `MachineHttpTests` and `AnnotationsHttpTests` predate this branch.

### Merge notes

As in the spec. Merge after #123 and rebase with `git rebase --onto origin/main e89894aa`. If #124 (`feat/424-79`) lands first: drop `6dc6d14e` during the rebase, move the two new operations to `EnsureUser` and `CheckUserAccessToAsync`, add `UpdateAccessPoint` and `GetProjects` to `UserOnlyOperations` with the user-only `401` and `403` texts, use `UserOnlyRoutes` for the machine cases, and re-export after the last commit that touches `Api.Functions/src` (`0.9.47` expected), deleting #124's `sdk.typescript/open.api.v0.9.45.json`.
