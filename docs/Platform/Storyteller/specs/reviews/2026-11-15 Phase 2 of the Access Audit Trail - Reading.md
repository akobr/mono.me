# Phase 2 of the Access Audit Trail: Reading

Linear: [424-263](https://linear.app/424fun/issue/424-263/record-an-append-only-audit-trail-for-role-membership-and-machine).

## Overview

This review covers phase 2 of the spec [Append-Only Access Audit Trail](../2026-11-15%20Append-Only%20Access%20Audit%20Trail.md): the query of the trail, its route, the OpenAPI document and both SDKs, their tests and documentation (sections 6, 7, 9 and 10). Commits: `731b42d90` (the query, the route and their tests), `ffe920b3d` (the SDKs from OpenAPI `0.10.40`) and `789cecbf9` (documentation). It was implemented as planned, with the deviations below. Phase 1, the recording, is reviewed in [Phase 1 of the Access Audit Trail: Recording](2026-11-15%20Phase%201%20of%20the%20Access%20Audit%20Trail%20-%20Recording.md).

## What Was Done

### 6. The read API

- `Backend.CosmosDb/src/Accessing/CosmosAccessAuditLog.cs` implements `IAccessAuditLog`, registered in `AddCosmosDbAnnotations`. One query per source, `core` (partition `access`) and `organizations` (the organization as the key prefix, so `OrganizationQueryGuard` accepts it), each `SELECT TOP @take … WHERE STARTSWITH(c.id, 'aca.') AND c.Organization = @organization … ORDER BY c.id DESC` with the optional `c.Project`, `c.Action`, `c.Target.Id`, `c.Time` bounds and `c.id < @before`, read concurrently, merged by id and cut at the page size. A missing `organizations` container reads as no records.
- The continuation token is base64url of `1:{last id}`; `DecodeToken` refuses anything that is not an `aca.` id of the record shape (`InvalidQuery`). The token is not bound to an organization: used with another organization, it only pages below that id, and the predicate on `c.Organization` still isolates.
- `Api.Functions/src/V1/AccessAuditHttp.cs`, `GET v1/access/points/{key}/audit` (`Definitions.Routes.Access.V1.AccessAudit`, route id `GetAccessAudit`, tag `Access`, user-only in `OperationAccess`), with the parameters `project`, `action`, `target`, `from`, `to` (new: `Definitions.Parameters.Action` and `Target`) and `continuationToken`. `EnsureUser`, then `CheckUserAccessToAsync(key, Administrator)`; a project key sets the project, and another `?project=` is `400 InvalidQuery`; `action` and the times use the key/value audit's `KeyValueQuery`.

**Deviation:** `action` is typed `string` in the OpenAPI document, with the actions in its description, as the key/value audit's `operation`: the generator gives an enum-typed query parameter the default of its first value, and an SDK would then filter by `MemberGranted` unasked. The response's `Action` stays the enum.

**Deviation, from the tests:** `v1/access/points/{key}/audit` and the key/value audit `v1/{organization}/{project}/key-values/audit` both took `GET v1/access/points/key-values/audit` (`RouteDispatchTests.NoTwoRoutesOfOneMethod_TakeTheSameRequest`). The key/value audit route now has the organization constraint of the views and bindings (`^(?!access$)`, `access` is a reserved organization name); the OpenAPI document strips route constraints, so the path there is unchanged.

### 7. OpenAPI and the SDKs

The document was exported with `OpenApiExportTests` (`0.10.40.62838`) to `Sdk.NSwag/open.api.v0.10.40.json` and `sdk.typescript/open.api.v0.10.40.json` (the TypeScript SDK keeps only the newest, so `v0.10.34` was removed). NSwag 14.6.3 added `GetAccessAuditAsync` to `IAccessApiClient` and the models (336 lines, nothing else changed); Orval regenerated `generated/` (the new route and models, and the document's version line of every file), the package version is `0.10.40`, and typecheck, lint, the 356 tests and the build pass. The READMEs name the new file, and the TypeScript README describes `getAccessAudit` with the paging helpers.

### 9. Tests

- `Backend.CosmosDb/test/CosmosAccessAuditLogTests.cs` (17 methods, 47 cases): ten records written through the services into both sources, newest first across them, every field, each filter (inclusive bounds), keyset paging with page sizes 1 to 100 including page boundaries between the sources, records written between pages, isolation of `x` and `x2`, an empty organization, 17 bad tokens and the token's form.
- `Api.Functions/test/AccessAuditHttpTests.cs` (37 cases): the organization and project keys, the project filter, the mismatch, the action, the pass-through, machines, every role with `UserRoleChecks.Enforce()`, the exact role on the key, a user through an actor; `RouteDispatchTests` and `RouteTableTests` for the route and its neighbours. The whole project: 3,147 tests.

### 10. Documentation

`access-audit.md` ("Reading the trail"), `access.md` (the route, and reading the audit in the table of roles) and `mcp.md` (the access audit among what the MCP server does not offer).

## Notes

- `?action=3` is read as the fourth action, as `operation` of the key/value audit is: both go through `Enum.TryParse`; an undefined number is refused.
- The Cosmos DB suites of two worktrees share one reused emulator container (its label is per module, not per worktree), and each suite deletes the database when it starts. A run in this worktree removed that container while a run of another worktree was using it; that run may have to be repeated. A label per worktree would keep them apart (not changed here).
