# Phase 2 of Inbox of a View

Linear: [424-31 Prepare notification center (inbox) in Admin UI](https://linear.app/424fun/issue/424-31/prepare-notification-center-inbox-in-admin-ui).

## Overview

Phase 2 delivers the API and the SDKs of [Inbox of a View](../2026-11-22%20Inbox%20of%20a%20View.md): three routes for users below a view under the new tag `Inbox` (the list, accept and dismiss), the helper that reads the caller's role on the project and hands it to the service, the OpenAPI snapshot 0.10.58, the eighth typed client of the C# SDK and the `inbox` tag of the TypeScript SDK, their tests and the living documentation (spec sections 5 and 6, the phase 2 rows of section 8, and the API tests of section 9). Nothing of the platform calls the routes yet: the page of the administration UI is phase 3.

| Commit | Subject |
| --- | --- |
| `9fbad1c29` | feat(platform): inbox routes of the API for users (424-31) |
| `c70608247` | chore(platform): regenerate the SDKs from OpenAPI 0.10.58 (424-31) |
| `376175ad1` | docs(platform): describe the inbox routes and the SDK clients (424-31) |

The code was written in two steps: the routes with their tests, then the snapshot and the SDKs. A review step and a fix step followed the first step, before its commit (see [Reviews](#reviews)). No code of phase 1 was changed.

## What Was Done

### 5. The HTTP API

As planned, with these details:

- `Api.Functions/src/V1/ViewInboxHttp.cs` (new): `ViewInboxHttp(IViewInboxService, IAccessService)` with the functions `GetViewInbox`, `AcceptViewInboxItem` and `DismissViewInboxItem`. No route catches anything: what the service refuses reaches `ExceptionHandlingMiddleware` as it was thrown and is answered by its type (`ErrorResponseMapping`).
- `Definitions.cs`: `Routes.Inbox.V1` with `Inbox`, `ItemAccept` and `ItemDismiss`; `RouteIds.Inbox` with the three ids, equal to the function names; `Tags.Inbox`; the descriptions `IdInboxItem`, `ResponseForbiddenViewInbox` (worded as the spec gives it) and `ResponseNotFoundInboxItem`. No new parameter name: `state`, `id` and `continuationToken` existed.
- `Security/HttpRequestDataExtensions.cs`: `GetUserRoleInProjectAsync(request, accessService, organization, project, minimalRole)`, right after `CheckUserAccessToProjectAsync`. It calls `EnsureUser`, reads `UserRoleChecks.AreSkipped` before the identity and then answers `Owner` without a lookup; otherwise it reads the role with `IAccessService.GetAccountRoleAsync`, runs `EnsureRole` and answers the role.
- `OpenApi/OperationAccess.cs`: the three ids in `UserOnly`.
- **The list** takes the role with `AccountRole.Contributor`, reads `state` with `KeyValueQuery.GetEnum<ViewInboxFilter>` (`Open` without it), passes the continuation token as it came and the token of the invocation, and answers `200` with the `ViewInboxResponse` of the service.
- **Accept and dismiss** take the role the same way and give the service the role, `request.GetAuditActor()` and the token of the invocation. They read no body and answer `200` with the `ViewInboxItem`.
- **The order of the checks** in a route: a machine (`403`), `User.Impersonation` (`401`, Release builds), the role (`403`), the query of the list (`400`), the service.
- **Documented statuses**: `200`, `400`, `401`, `403` and `500` for the list; `200`, `401`, `403`, `404`, `409`, `501` and `500` for accept; `200`, `401`, `403`, `404`, `409` and `500` for dismiss. The `401` is `ResponseUnauthorizedUserOnly`, without a body.
- No step-up, no access audit record, no check of the view and no machine scope, as planned.

### 6. OpenAPI snapshot and SDKs

As planned, with these details:

- **Tests of the document and the routes** (in `9fbad1c29`): the three ids in `UserOnlyOperations` of `OpenApiExportTests`, which `OperationAccess_MatchesTheTablesOfTheTests` compares with `OperationAccess.UserOnly`; `Document_HasTheViewInboxOperations_UnderTheInboxTag`; `ViewInboxSchemas_ArePublished` in `OpenApiDocumentTests`; `InboxRoutes_AreRegistered` in `RouteTableTests`; eight rows in `Request_MatchesExactlyItsFunction` of `RouteDispatchTests`.
- **Snapshot.** The version of the document is `0.10.58.40890`; its fourth part is the commit id of `9fbad1c29`. `Sdk.NSwag/open.api.v0.10.58.json` is new, and every older snapshot is kept. `sdk.typescript/` has a byte-identical copy, and its `open.api.v0.10.56.json` is removed.
- **What changed in the document.** A comparison of the parsed documents 0.10.56 and 0.10.58 finds seven differences: the version, three added paths (`GetViewInbox`, `AcceptViewInboxItem`, `DismissViewInboxItem`, all under the tag `Inbox`) and three added schemas (`ViewInboxItem`, `ViewInboxJob`, `ViewInboxResponse`). The operations go from 150 to 153 and the schemas from 145 to 148; nothing is removed or changed.
- **C#.** `ApiSdk.g.cs` was regenerated with NSwag 14.6.3 and the command of the README; it gained 860 lines and lost none:
  - `IInboxApiClient` and `InboxApiClient` with `GetViewInboxAsync(organization, project, view, state = null, continuationToken = null, cancellationToken)`, `AcceptViewInboxItemAsync` and `DismissViewInboxItemAsync`;
  - the models `ViewInboxItem`, `ViewInboxJob` and `ViewInboxResponse`, and the enums `ViewInboxItemKind`, `ViewInboxItemLevel`, `ViewInboxItemMinimalRole`, `ViewInboxItemState` and `ViewInboxJobKind`.
- **C#, by hand.**
  - `ApiSdk.partial.cs`: the partial `InboxApiClient` with the constructor that takes `ISdkConfiguration`, `UpdateJsonSerializerSettings` and `PrepareRequest`.
  - `ServicesCollectionExtensions.cs`: `ConfigureHttpClient<IInboxApiClient, InboxApiClient>` as the last registration, and "eight typed clients" in the comment.
  - `src/Platform/Cli/test/Sdk/SdkHttpClientConfigurationTests.cs`: `nameof(IInboxApiClient)` as the last name; the test is now `ConfigureHttpClient_RunsForEachOfTheEightTypedClients`.
  - `Sdk.NSwag/README.md`: the snapshot name in the command, and "eight typed clients".
  - The hand-written twins of generated methods (`ApiSdk.Conditional.cs`, `ApiSdk.Annotations.cs`, `ApiSdk.ProjectSettings.cs`) were compared with their generated methods and are unchanged.
- **TypeScript.** `npm run generate` (Orval 8.41.0) set the package version to 0.10.58. Thirteen files are new: `generated/{client,vue-query,zod}/inbox/inbox.ts`, `generated/vue-query/inbox/inbox.msw.ts` and nine models. Of the 315 changed generated files, 311 change only the version in their header; the other four are the indexes.
  - The functions are `getViewInbox`, `acceptViewInboxItem` and `dismissViewInboxItem`; the composables `useGetViewInbox` (with `getGetViewInboxQueryKey`), `useAcceptViewInboxItem` and `useDismissViewInboxItem`.
  - By hand: `getInboxMock` in `src/msw.ts`, imported, exported and part of `getStorytellerMock()`; three rows in `test/msw.test.ts`, one per operation against its Zod schema; the README with the snapshot name and a section "Inbox of a view".
- **A probe of the C# client.** No committed test covers `InboxApiClient` beyond the registration test. A throwaway probe of five tests, deleted afterwards, confirmed the three routes, the authorization through the partial, the date parsing, and that `AddStorytellerSdk` resolves `IInboxApiClient`.
- **`sform` and the MCP server** got nothing, as planned: no command and no tool. Their coverage checks pass unchanged.

### 8. Documentation (the phase 2 rows)

As planned, with these details (in `376175ad1` unless named otherwise):

- `docs/Platform/Storyteller/inbox.md`: the introduction and the list of code locations name the routes; a new section **Routes** with the table of the three routes, the access (users only, the role and how it reaches the service, actors, no step-up, no audit record, the view, Debug builds, the order of the checks), the list with its query and a page as JSON, accept and dismiss with every status and error code, examples with `curl`, and the OpenAPI document with the names of both SDKs. **Errors** got the statuses, **Tests** the seven test classes of `Api.Functions/test` with the Debug and Release commands, and **Not there yet** keeps only the page of the administration UI and the first suggested action.
- `access.md`: a row of the inbox in the table of **Roles** (Contributor, users only, the minimal role of an item, the results of exports and imports for Administrators), and the rows `409 InboxItemClosed, InboxItemNotAction` and `501 InboxActionNotAvailable` in **Error codes**.
- `authentication.md`: the three routes in the sentence of the routes for users only.
- `mcp.md` (Limits): the inbox of a view among what the server does not offer.
- `CLAUDE.md`: one clause about the inbox on the `Api.Functions/` bullet of Storyteller.
- `Sdk.NSwag/README.md` and `sdk.typescript/README.md` (in `c70608247`): see section 6. `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md` needed no change (deviation 8).
- `export-import.md`: two sentences, although the spec gives the file to phases 1 and 3 (deviation 9).

### 9. Tests of the back end (the API part)

As planned, with these details:

- `Api.Functions/test/ViewInboxHttpTests.cs` (new, 40 cases): each route with a recording service (`RecordingInbox`) and an access double that records every lookup (`RecordingRoleAccess`).
  - The role of the caller is handed over for the four roles from `Contributor` to `Owner`, with one lookup per request.
  - `None` and `Reader` get `403 AccessDenied` with "No Contributor access to 'acme.web'." before the service is asked.
  - A machine is refused on every route in every build, also one whose credentials carry every scope and `User.Impersonation`, before a role is looked up.
  - A token that does not act for a user is `401` in a Release build.
  - Without `Enforce()` in a Debug build the service gets `Owner` and no role is looked up, also for a request without an identity, which is stamped `System`.
  - `state`: `Open` without it, the names in any casing, and `400 InvalidQuery` with "The query parameter 'state' must be Open, Done." for seven other values.
  - The actor of accept and dismiss, with `Via` for a user who acts through an actor.
- The tests of the roles use `UserRoleChecks.Enforce()` and run the same in both builds; the tests of the Debug behaviour and of the `401` branch on `IsDevelopmentAuthentication`. So the class is run in Debug and in Release.
- `Api.Functions/test` has 70 new cases: 40 in `ViewInboxHttpTests`, 9 in `HttpRequestDataExtensionsTests`, 14 in `RouteDispatchTests`, 4 in `RouteTableTests`, and one each in `OpenApiExportTests`, `OpenApiDocumentTests` and `FunctionTransactionNamesTests`. The commit only adds lines: no line of an existing test was changed.
- The implementer broke the new code on purpose twice and restored both files by their checksums. With the default of `state`, the continuation token, the token of the invocation, the role, the actor, a body read and `EnsureUser` all wrong, 15 cases failed. With only the read of the identity moved before `AreSkipped`, exactly `RequestWithoutAnIdentity_IsAnOwnersInADebugBuild_AndUnauthorizedInAReleaseBuild` failed.

### Test results

Run on the committed tree on 2026-11-22:

| Project | Result |
| --- | --- |
| `Api.Functions/test` (Debug) | 3464 passed, 0 failed (3394 before the phase) |
| `Api.Functions/test` (Release) | 3464 passed, 0 failed |
| `Sdk.NSwag/test` | 595 passed |
| `Cli/test` | 2578 passed |
| `Mcp/test` | 776 passed |
| `Cli.AutomationTests/src`, `--treenode-filter "/**[Category=Coverage]"` | 9 passed |
| `dotnet build src/Platform` | 0 errors |
| TypeScript SDK | typecheck and lint clean; `npm test` 508 passed, 1 skipped (the smoke test that needs a running API); build clean |
| Administration UI with the new SDK, otherwise unchanged | typecheck clean; 2449 unit tests passed |

**Not run:** `Api.Functions/testint` (it has no inbox request), the Cosmos DB suites (nothing of them changed in this phase), the test run of the whole solution, and the end-to-end tests of the administration UI (phase 3).

### Reviews

Two reviewers read the API change, and a second reader checked every finding. Each reviewer reported one finding. Both were confirmed, both are gaps of the tests and not of the code, and both were fixed before the commit `9fbad1c29`:

1. **No test pinned that the routes need no step-up** (confirmed; fixed, a test only). Every request of `ViewInboxHttpTests` carried a recent sign-in, so a `request.EnsureRecentSignIn()` added to a route, for example copied from `MembersHttp`, would have passed the whole class in both builds. The tests of the document compare only the documented `401` with `OperationAccess.StepUp`. A Contributor whose sign-in is older than the window of `Auth:StepUp` would then get `401 StepUpRequired` on the list, accept and dismiss. `EveryRoute_WithAStaleSignIn_NeedsNoStepUp` now calls the three routes with `TestRequests.StaleSignIn`, once as the build runs and once under `UserRoleChecks.Enforce()`, and expects six calls of the service.
2. **No test pinned that the three schemas publish no `required` list** (confirmed; fixed, a test only). A `[JsonProperty(Required = Required.Always)]` on a member of the model, as `Exhibition` has, would add the list to the document and change the types of both SDKs at the next regeneration, while every test stayed green. `Document_HasTheViewInboxOperations_UnderTheInboxTag` now asserts that `ViewInboxResponse`, `ViewInboxItem` and `ViewInboxJob` have no `required` key.

The fix step checked both with temporary changes and reverted each:

- With `EnsureRecentSignIn()` after the role call of the three routes, only the new test failed, in Debug and in Release.
- With `EnsureRecentSignIn()` after `EnsureRole` in the helper, only the new test failed, and only in its pass under `Enforce()`. In a Debug build without `Enforce()` the helper returns before that line. This is why the test has the second pass, which the checked text of the fix did not have.
- With `Exhibition` added to the loop of the schemas, the assertion failed for it and passed for the three inbox schemas. The model itself was not changed for the check: it is code of phase 1.

Besides the findings, the reviewers walked the routes, the helper and every case of section 9 for each caller in both builds, and compared the attributes, the route templates, the dispatch rows, `OperationAccess.UserOnly` and the wording of `ResponseForbiddenViewInbox` with the spec. They found no defect in the code.

### Deviations from the spec

1. **Names the spec leaves open, and two descriptions beyond it.** The route constants are `Inbox`, `ItemAccept` and `ItemDismiss`, after `InvitationAccept`; the spec names only their class. `IdInboxItem` and `ResponseNotFoundInboxItem` are not in the spec: accept and dismiss share the description of the id and the text of the `404`.
2. **`state` also takes the numbers of the enum.** `KeyValueQuery.GetEnum` accepts a defined number as well as a name, as on the key/value routes, so `0` is `Open` and `1` is `Done`. The spec and the description of the parameter name only `Open` and `Done`. The tests pin the names in any casing and `2` as `400`, not the two numbers; `inbox.md` describes them.
3. **The role is checked before the query.** A Reader or a machine with a wrong `state` gets `403`, not `400`, as on `AccessAuditHttp`. The spec lists the `400` without an order.
4. **A machine gets the default message**, "Only users may call this operation; machines cannot." The helper calls `EnsureUser` without a text of the route, unlike the access audit or the project settings. The spec asks only for the `403`.
5. **The `409` of accept names `TransferLimit`** as an example of a refusal of the handler, although no handler is registered. The spec's table says "or the handler's own"; the description gives a client one code it can expect, and `OpenApiExportTests` pins it.
6. **Tests beyond sections 6 and 9.**
   - In `ViewInboxHttpTests`: a user through an actor on the list, the continuation token, the token of the invocation, no read of the request body, no step-up (review finding 1), the refusals of the service with their statuses (also a `TransferLimit` of a handler), the routes with their tag, and a request without an identity under enforced role checks.
   - In `HttpRequestDataExtensionsTests`: four tests of `GetUserRoleInProjectAsync`, which pin that the minimal role is honoured and the caller's own role is answered.
   - In `RouteTableTests`: `InboxRoutes_AreTheListAndTheTwoActionsOfAnItem`, which allows no fourth route with `/inbox`.
   - In `RouteDispatchTests`: the theory `InboxRequest_OutsideTheListAndTheTwoActions_MatchesNoFunction` (six rows), and among the eight rows of `Request_MatchesExactlyItsFunction` a view named `inbox`, the reserved names `key-values` and `usage` in the place of the view, and an id that reads `accept`.
   - In `FunctionTransactionNamesTests`: one row for `AcceptViewInboxItem`.
7. **The snapshot is 0.10.58, and there is none of 0.10.57.** The spec's Current State names 0.10.56 as the snapshot of the day. The commit of phase 1 (`a4a15ccb5`) changed `Api.Functions/src/Program.cs` too, so the version of the API moved twice before the export.
8. **`package-readme.md` is unchanged, and the README of the C# SDK names no tag.** The row of section 8 expected the snapshot name, the eighth client and the tag `Inbox` in three files. `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md` names no snapshot and neither names nor counts a client. `Sdk.NSwag/README.md` has no list of clients or tags, so it got the snapshot name and the count only. The README of the TypeScript SDK got all three, as a section of its own.
9. **`export-import.md` was changed in phase 2.** Its sentence in **Jobs** that no route reads the inbox was no longer true, and **Routes** got one sentence that sends a reader from the jobs, which have no list, to the inbox. The sections of the administration UI and of the mock API are left for phase 3, as planned.
10. **The new partial also sets `DateParseHandling.None`** (`UpdateJsonSerializerSettings`), as the partials of the annotations, configurations, schemas, templates and transfers do. The spec names only "the partial of the new client". A member of an item that this SDK does not know yet then keeps its date-like string as stored; the typed dates (`CreatedAt`, `ExpiresAt`, the stamps) still parse.

### Left for later

- **Phase 3**: the page **Inbox** of the administration UI, its mock API and tests, and the documentation of the page: `src/Platform/ui.admin/README.md`, `docs/42for.net/platform/admin-ui.md`, the sections of the administration UI and the mock API in `export-import.md`, and the row of `docs/Platform/Deployment/runbook-azure-production.md` that the API with the inbox routes is published before the administration UI that calls them.
- **A rebase onto `main`** changes the fourth part of the version, the commit id. The document must then be exported again and both SDKs regenerated, as earlier features did.
- **The two SDK READMEs name different filters** for the export of the document: `--filter-class "_42.Platform.Storyteller.Api.Functions.UnitTests.OpenApiExportTests"` in `Sdk.NSwag/README.md` and `--filter "FullyQualifiedName~OpenApiExportTests"` in `sdk.typescript/README.md`. Both are older than this phase and were left as they are.
- **No committed test of `InboxApiClient` itself.** The registration test names the client; what the probe confirmed is not kept.
- **`sform` and the MCP server** have no command and no tool for the inbox. That is out of the scope of the plan.
- **The first handler** comes with 424-8. Until then no deployment reaches the `200` of the accept route: an open action answers `501`, and the notifications of exports and imports `409 InboxItemNotAction`.
- **The numbers of `state`** (`0` and `1`) have no test.
- **The `404` of a request outside the three routes** is the answer of the modelled dispatch of `RouteDispatchTests`. `Api.Functions/testint/HostRoutingTests.cs` has no inbox request, so no test asks a running host.
