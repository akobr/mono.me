# Phase 0a of API Extensions for the Admin UI

## Overview

Phase 0a covers E1 (authorization correctness) and E13 (JSON Patch errors) of [TypeScript SDK and API extensions for the Administration UI](../2026-10-09%20TypeScript%20SDK%20and%20API%20extensions%20for%20the%20Administration%20UI.md). Revoke now checks the caller's role. A user without the required role on a project gets `403` instead of `401`. Access-management failures return `403`, `404` or `409` with a stable `ErrorCode` and without exception details. A JSON Patch whose `test` operation fails returns `412`. While writing integration tests, several more defects in `CosmosAccessService` showed up and were fixed. Configuration writes now require `Contributor`, which they never did before. Both are listed in section 3.

## What Was Done

### E1.1 Revoke fix

`CosmosAccessService.RevokePermissionAsync` (`Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`) and `GrantPermissionAsync` share a new private `EnsureCanManageAsync(Permission)`:
- The caller needs `Administrator` on the access point.
- Granting or revoking `Owner` needs `Owner`.
- A caller without an account or without a membership gets `AccessDeniedException`.

This replaces the inverted `if (creator.AccessMap.TryGetValue(...)) throw`, which let a caller with no membership revoke anyone, and refused every legitimate administrator.

Revoke also has new and changed rules:
- **Last owner:** removing the last `Owner` throws `ConflictException` with `ErrorCode` `LastOwner`.
- **Elevated role:** revoking a lower role than the stored one now throws `ConflictException` (`ElevatedRole`) instead of `InvalidOperationException`.
- **Missing records:** a missing access point or target account throws `NotFoundException`.

Grant keeps its "only raise a role" semantics and returns `false` for the same or a lower role. The role `None` throws `ArgumentException`, which the HTTP layer rejects first (section E1.3).

### E1.2 403 for authorization failures

- New domain exceptions in `Backend.Core/src`:
  - `StorytellerException`, an abstract base with `ErrorCode`.
  - `AccessDeniedException`, `NotFoundException` and `ConflictException`.
  - `ErrorCodes`, holding the stable values `AccessDenied`, `NotFound`, `Conflict`, `AccountExists`, `AccessPointExists`, `LastOwner`, `ElevatedRole`, `PatchInvalid` and `PatchTestFailed`.
- `HttpRequestDataExtensions.CheckAccessToAsync` calls a new public `EnsureRole(actual, minimal, key)`, which throws `AccessDeniedException`. `SecurityTokenException` is still used for missing or invalid credentials, missing scopes, and every machine check, and still answers `401`.
- `EnsureRole` was extracted so it can be unit tested. Debug builds define `DEV_AUTH`, which skips the role check entirely.

### E1.3 Domain error mapping

- `Api.Functions/src/ErrorHandling/ErrorResponseMapping.cs` maps exceptions to status codes and error codes:

  | Exception | Status |
  | --- | --- |
  | `AccessDeniedException` | 403 |
  | `NotFoundException` | 404 |
  | `ConflictException` | 409 |
  | `JsonPatchException`, `TestFailed` | 412 |
  | Any other `JsonPatchException` | 400 |

  `TryMap` builds an `ErrorResponse` with `Message`, `ErrorCode` and `Hint`. `Error` (the exception wrapper with the stack trace) is never set. `ToActionResult` is for functions that catch the exception themselves.
- `ExceptionHandlingMiddleware` has a new `catch (Exception) when (ErrorResponseMapping.TryMap(...))` before the generic 500 handler. It logs at warning level. The 401 and 422 paths are unchanged.
- `ExceptionExtensions.TryGetErrorCode` also reads `StorytellerException.ErrorCode` and `JsonPatchException.ErrorCode`.
- `CosmosAccessService` now throws the domain exceptions:
  - `CreateAccountAsync`: an existing account → `ConflictException` (`AccountExists`).
  - `CreateAccessPointAsync`: not the organization owner → `AccessDeniedException`; the project exists → `ConflictException` (`AccessPointExists`).
  - `GetAccessPointsAsync`: unknown account → `NotFoundException`.
  - `ResetMachineAccessAsync`: unknown machine → `NotFoundException`. It used to be `500`; now it is `404`.
- Other `InvalidOperationException` sites outside this service are unchanged, as the spec says.
- `AccessHttp.PostGrantPermission` and `PostRevokePermission` validate the body first, with a new `TryValidatePermission`. A missing body, the role `None`, an empty `AccountId` or an empty `AccessPointKey` returns `400` without calling the service.

### E1.4 OpenAPI

- New `OpenApi/Filters/ForbiddenResponseDocumentFilter`, registered in `OpenApiConfigurationOptions`. It adds a `403` response with an `ErrorResponse` body to every operation secured by the `manual` or `integrated` scheme. Machine-only operations (`mtls`) and anonymous ones are skipped. An operation that already documents `403` keeps its own response.
- **Deviation:** the spec planned a `403` attribute on every function that calls `CheckAccessTo*Async`. That is about 60 attributes, and every future endpoint would need one. The filter also covers grant, revoke and create access point, where the service raises `403`.
- `Definitions.Descriptions`:
  - New `ResponseForbidden` and `ResponsePreconditionFailed`.
  - `ResponseUnauthorized` now reads "Authentication issues: missing or invalid credentials, or a missing scope."
- The OpenAPI JSON files in `sdk.typescript/` and `Sdk.NSwag/` were **not** re-exported. The spec bumps the API to 0.9 once phase 0c is done.

### E13 JSON Patch errors

- New `Backend.Core/src/Configuring/JsonPatchException.cs`, with `Kind` (`Invalid`, `TestFailed`, `OperationFailed`), `OperationIndex` and `ErrorCode`. It derives from `InvalidOperationException`, so existing catch blocks and the existing test `PatchConfigurationAsync_InvalidPath_Throws` keep working.
- `JsonExtensions.ApplyPatch` (`Backend.CosmosDb/src/JsonExtensions.cs`):
  - A failed operation is classified with `PatchResult.Operation`, the index of the failing operation: a `test` op is `TestFailed`, any other op is `OperationFailed`.
  - Deserialization errors are turned into `Invalid`: `System.Text.Json.JsonException` for an unknown op or a missing path, and `Json.Pointer.PointerParseException` for a malformed pointer. Before, these escaped as unmapped exceptions and became `500`.
  - `ApplyPatchRequested` throws `Invalid` when `$patch` is not an array.
- `ConfigurationHttp.SetConfiguration` and `PatchConfiguration`, and `TemplateHttp.SetTemplate` and `PatchTemplate`, catch `JsonPatchException` before `InvalidOperationException` and return `ErrorResponseMapping.ToActionResult` (`412` or `400`, with `ErrorCode`). All four document `412`. The POST and PUT endpoints are included because `$patch` inside the body runs the same code.
- **Deviations from the spec:**
  - The spec said a failed patch returned `500`. In fact, configuration and template patches already returned `400` for any failure, because the functions catch `InvalidOperationException`. Only malformed documents could reach `500`. Phase 0a separates `412` from `400`.
  - The spec's `PathNotFound` is named `OperationFailed`, because a non-test operation can fail for reasons other than a missing path (for example, `move` from a missing location, or an array index out of range).

## 3. Additional Fixes Found During the Phase

1. **Grant, revoke and project creation stored API models instead of Cosmos entities.** `UpsertItemAsync(accessPoint, …)` and `UpsertItemAsync(account, …)` wrote `AccessPoint` and `Account` records. Those have no lowercase `id` and no `PartitionKey`, so the writes could not succeed against Cosmos. As a result, grant, revoke, and creating a second project for an existing account all failed at runtime. New `ToEntity()` mappings in `AccessMappings.cs` and a private `SaveMembershipAsync` now write `AccessPointEntity` and `AccountEntity`.
2. **`GetAccessPointsAsync` used an invalid query.** `SELECT * FROM ap WHERE ap.Id IN @ids` is a Cosmos syntax error (`SC1001`), so `GET v1/access/points` always failed. It is now `ARRAY_CONTAINS(@ids, ap.id)` with a materialized list, and returns an empty list early when the account administers nothing.
3. **`CreateAccountAsync` checked for an existing account by user name.** The read used the user name instead of the account ID, so the duplicate check never found anything. It now reads by `IdentityId`.
4. **Configuration writes required only `Reader`.** `SetConfiguration`, `PatchConfiguration` and `DeleteConfiguration` called `CheckAccessToProjectAsync` without a role, which defaults to `Reader`. Every other write (annotations, templates, schemas, machines) requires `Contributor`, and so does the admin UI's role model. The three endpoints now require `Contributor`. **This changes behaviour:** a member with only `Reader` can no longer write configurations.

## Tests

| Project | New or changed tests | Result |
| --- | --- | --- |
| `Api.Functions.UnitTests` | `ErrorResponseMappingTests` (every mapping, unmapped types, no `Error` details, `ToActionResult`); `ForbiddenResponseDocumentFilterTests` (manual and integrated get 403, mtls and anonymous do not, an existing 403 is kept, `412` attributes on the four patch-capable operations); `PermissionHttpTests` (invalid bodies → 400 without a service call, the caller becomes `CreatedById`, `AccessDeniedException` propagates to the middleware); `HttpRequestDataExtensionsTests.EnsureRole_*` | 105 passed |
| `Backend.CosmosDb.UnitTests` (Cosmos emulator) | `CosmosAccessServiceTests`, 19 tests: grant by owner, administrator and outsider; Owner only by Owner; same or lower role → `false`; unknown target → 404; `None` → `ArgumentException`; revoke by owner, outsider (regression for the inverted check) and contributor; elevated role; not a member → `false`; last owner; one of two owners; second project updates the owner account (regression for the model upsert); duplicate project; non-owner project; duplicate account; `GetAccessPoints` for an unknown account. `JsonPatchTests`, 8 cases with no Cosmos needed. `CosmosConfigurationServiceTests.PatchConfigurationAsync_FailedTest_ThrowsTestFailedAndKeepsTheVersion`. | 147 passed |
| `Access.Certificates.IntegrationTests` | none (uses `CreateAccessPointAsync`) | 23 passed |

Every Platform project in `42.mono.slnx` builds. The build ran with `-p:UseArtifactsOutput=true -p:ArtifactsPath=<temp>`, because a running Functions host locked `Api.Functions/src/bin/Debug`. `dotnet build src/Platform/Directory.Build.proj` fails before reaching any project changed here, because the traversal glob picks up a stale `Supervisor/Api.Functions/src/obj/Debug/net9.0/WorkerExtensions/WorkerExtensions.csproj`. That is a pre-existing problem, unrelated to this phase.

## Documentation

- `docs/Platform/Storyteller/authentication.md`: new section "401 and 403", with the role needed per operation group and the access-management error codes.
- `docs/Platform/Storyteller/templating.md`: `412` and `PatchInvalid` added to the write status codes.
- `docs/42for.net/platform/configuration.md`: optimistic concurrency with `test` operations (`412 PatchTestFailed`), `400 PatchInvalid`, and the `Contributor` role for configuration writes.

## Follow-ups

- Export `open.api.v0.9.x.json` and regenerate `Sdk.NSwag` and the TypeScript SDK after phase 0c.
- Grant, revoke and project creation still write the access point and the account in two separate upserts, without ETags. Phase 0b (E2, members) adds `IfMatchEtag` and a retry.
- `sform` shows `403` as a generic API error. A clearer message ("your role on X is too low") would help, and belongs in the CLI follow-up for members and invitations.
- The traversal build glob (`**\*.*?proj`) should exclude `obj` and `bin` folders.
