# Phase 2 of Shared Organization Container

## Overview

Phase 2 of the spec [Shared Organization Container](../2026-11-09%20Shared%20Organization%20Container.md) (424-230) covers sections 4 to 6 and the tests of section 10: the organization-wide queries with the predicate and the key prefix, organizations without containers, DbCreator, the tenant-isolation tests per area and the structure tests. The code is in the commit `refactor(platform): store all organizations in one shared Cosmos DB container` (with phase 1), the new tests in `test(platform): guard the isolation of organizations in the shared container`, and one fix found by the audit in `fix(platform): refuse an API key with a blank organization, project or machine`. Everything ran first on the classic Linux emulator, as planned.

## What Was Done

### 4. Organization-wide queries

Every query on `organizations` that names no single partition has `c.Organization = @organization` written in its `WHERE` clause, the parameter from `WithOrganization`, and the prefix key (`QueryOptions()`), with its `MaxItemCount` and continuation tokens as before:

| Place | Query |
| --- | --- |
| `AnnotationItemStore.QueryPageAsync` | the runner of the annotation listings (`GetViewAnnotationsAsync`, the deleted annotations of `CosmosAnnotationService.Deletion`); it adds the parameter and the prefix, the callers write the predicate |
| `AnnotationItemStore.QueryScopeAsync` | descendants over several partitions: predicate and prefix when the scope has no partition |
| `CosmosAnnotationService.GetAnnotationsAsync` (LINQ) | `PartitionKey = partitionKey ?? repository.Prefix` and `.Where(r => r.Organization == organization)`, also in one partition (harmless, one code path) |
| `CosmosViewService.DiscoverAsync` | `SELECT DISTINCT VALUE c.ViewName ... WHERE c.Organization = @organization AND ...` |
| `CosmosConfigurationService` | the configuration listing, the histories of a view, the deleted annotation keys |
| `CosmosConfigurationSchemaService.QueryAffectedConfigurations` (LINQ) | the prefix and `.Where` on `Organization` |
| `ConfigurationCacheInvalidator.CollectAsync` (LINQ) | targets without a partition (the descendants of a subject or context, the templates) get the prefix; the predicate is added to every target |
| `CosmosKeyValueMaintenanceService` (re-wrap), `CosmosKeyValuePolicyService.CountInUseAsync`, `CosmosKeyValueUsageService` (the three snapshot queries) | predicate and prefix |

Every other query names one partition and runs with its full key. The adversarial audit (five auditors, one per area, and three skeptics per finding) found no query or point operation that crosses organizations.

### 5. Organizations without containers

- `CosmosAccessService.CreateAccessPointAsync` creates no container; an organization is its access point in `core`.
- `CosmosAccessService.VerifyAccessForMachineAsync` dropped `ReadContainerStreamAsync`: the point read of the machine with the full key answers it.
- `CosmosOrganizationCatalog.ListAsync()` reads the organization access points (`apt.{organization}` without a dot in the key) from the partition `access` of `core`; `CosmosKeyValueUsageService.TakeSnapshotAsync` uses it instead of container names, and its constructor takes the catalog instead of `IContainerFactory`. **Deviation:** the catalog is `public sealed`, not internal, because the public constructor of the usage service takes it.
- **Deviation:** a missing container `organizations` does not answer `503` (the spec's last bullet of section 5). Reads of a missing container look like missing items (stream reads answer `404`), writes fail with the Cosmos DB `404` as a `500`, and the two `404` handlers of the access area (`FindMachinesUsingIssuerAsync`, `CosmosFederatedIssuerStore`) read it as no data. That is how a missing `core` has always behaved; DbCreator runs before the API in a deployment (the deployment cookbook) and once per fresh emulator locally. Telling the container-level `404` (sub-status 1003) apart from a missing item in every stream read is left for a later change.

### 6. DbCreator

- `CoreDbStructureBuilder.BuildAsync` first calls `IContainerFactory.FindLayoutProblemsAsync()` (a read-only listing of the containers of `42.Platform.2S`; a missing database is fine). Containers named `org.*`, or an `organizations` with other partition key paths, stop it with `DatabaseLayoutException` (`Problems`) before anything is written; `InitDbCommand` prints the problems and the re-seed (delete the database or use a new emulator volume, then run DbCreator again) in the error style of the base account check and returns the new `ExitCodes.ERROR_DATABASE_LAYOUT` (3).
- Then it creates `core` and `organizations`, seeds `42dotnet` with `Data.CreateCoreAnnotations(new OrganizationContainer(...))`, and seeds the access of the staging accounts into `core`; the UI test organization needs no container any more.
- **Deviation:** the Aspire host's API does not wait for DbCreator (`WaitForCompletion`): `db-creator` is started by hand (`WithExplicitStart`), so the API would wait forever, and the API never created `core` either. The local development page says to run `db-creator` once per fresh volume.

### 10. Tests (the phase 2 part)

- One isolation class per area, each with two organizations of identical project, view and key names and distinct values: `AnnotationIsolationTests` (point reads, listings and paging, descendants, views, soft delete, restore, purge, history), `ConfigurationIsolationTests` (reads, resolution, composition, listings, histories, writes and invalidation, soft delete and purge, version numbers, conditional-read dependencies), `SchemaAndTemplateIsolationTests` (reads, deletes, compliance of affected configurations, imports, template invalidation), `KeyValueIsolationTests` (reads, versions, diffs, export and import, delete, restore, purge, `CountInUseAsync`, the re-wrap, the usage snapshot, the audit), `AccessIsolationTests` (API key and federated machines, the hash store, client and shared certificates, issuers, renewal) and `TransferIsolationTests` (jobs, exports, imports, the organization catalog). "Unchanged" compares every item of B (id, partition, ETag) before and after each operation through A.
- `OrganizationsContainerStructureTests`: exactly `core` and `organizations` after DbCreator and after creating organizations, the partition key paths, an idempotent second run, and the refusal of an `org.legacy-*` container.
- `StructuredApiKeyTests`: the audit found that an API key whose organization is blank (`" "`) reached `GetOrganizationContainer`, whose scope refuses a blank organization: a `500` instead of the `401` it got before. `StructuredApiKey.TryParse` now refuses blank segments.
- Results on the classic Linux emulator (in chunks, as before; the reused emulator restarted once under load, as it did before this change): `Backend.CosmosDb/test` all green (528 tests of the collection plus the unit tests), `Access.Certificates/testint` 73 of 73; `Api.Functions/test` 2831, `Backend.Core/test` 1362 and `Access.Certificates/test` 119, all green; the whole `src/Platform` builds.
- The existing tests that read or wrote the container directly use the full keys and carry `Organization` in their raw documents; `ConfigurationResolutionTests` mocks the provider with an `OrganizationContainer`.
