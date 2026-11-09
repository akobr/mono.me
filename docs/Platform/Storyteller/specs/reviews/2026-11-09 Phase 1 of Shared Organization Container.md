# Phase 1 of Shared Organization Container

## Overview

Phase 1 of the spec [Shared Organization Container](../2026-11-09%20Shared%20Organization%20Container.md) (424-230) covers sections 1 to 3 and 7: the shared container `organizations` with the hierarchical partition key `/Organization`, `/PartitionKey`, the `Organization` property on every stored item, the organization-scoped repository `OrganizationContainer`, every call site of `Backend.CosmosDb` moved to its keys, and the removal of the two unused stored procedures. It was delivered together with phase 2 in the commit `refactor(platform): store all organizations in one shared Cosmos DB container`, as the spec's section 12 foresaw: the call sites only build and pass their tests together with the queries and the organization changes of phase 2.

## What Was Done

### 1. One shared container

- `CosmosConstants`: `OrganizationsContainerName = "organizations"`; the prefix of the earlier layout stays only as `LegacyOrganizationContainerNamePrefix = "org."`, for DbCreator's refusal (phase 2).
- `ContainerFactory.CreateContainerIfNotExistsAsync("organizations")` delegates to the new `EnsureOrganizationsContainerAsync()`: `ContainerProperties` with `OrganizationsPartitionKeyPaths` (`/Organization`, `/PartitionKey`, from `OrganizationKeys`), the settings of `ApplySettings` (TTL `-1`, `/*` indexed, `/Entries/*` and the audit stamps excluded), and `EnsureContainerSettingsAsync` on an existing container. Any other container (`core`) keeps the single path `/PartitionKey` and no settings. `BuildDatabase` uses `CosmosConstants.DatabaseName` instead of the literal.
- `IContainerFactory`: `EnsureOrganizationContainersAsync` is gone; new are `EnsureOrganizationsContainerAsync()` and `FindLayoutProblemsAsync()` (phase 2); `GetContainerNamesAsync()` stays (the structure tests use it).

### 2. The organization on every item

- `public required string Organization { get; init; }` on `Entity` (and so on every annotation, configuration, schema and template entity), `ViewEntity`, `KeyValueEntity`, `KeyValueVersionEntity`, `KeyValueAuditEntity`, `KeyValueUsageEntity`, `ViewTransferEntity`, `FederatedIssuerEntity`, `MachineAccessEntity`, `MachineCertificateEntity` and `SharedCertificateEntity`. The core types stay without it.
- The compiler named every construction (`CS9035`); the mappings and factories set it: `AnnotationMappings.ToEntity(annotation, organization)`, `Create.*(organization, ...)` (only `Data` calls them), `MachineCertificateEntity.FromRecord(organization, ...)`, `SchemaMappings`/`TemplateMappings.ToHistory` (copied from the stored item), and the initializers in the stores. Raw JSON written to `organizations` (the test plants) carries it too. Entities rewritten with `with` keep the `Organization` they were read with.
- `EntityOrganizationTests` (reflection over the assembly) checks that every stored entity type except the five core types has a required `Organization`, serialized under that name by both serializers (System.Text.Json and Newtonsoft with the options of `JsonSerializationOptions`).
- **Deviation:** the spec's "test that captures the documents written through the test interceptors" was not written: `RecordedBatch` and `RecordedOperation` keep only the kind, the id and the ETag of an operation, and `InterceptedContainer` passes point writes through. Cosmos DB refuses a document whose `Organization` differs from the key of the write (`400`), so a missed write fails the suites, which ran green on three emulators.

### 3. An organization-scoped repository

- `OrganizationContainer` (`Backend.CosmosDb/src/OrganizationContainer.cs`) as specified: `Organization`, `Container` (the shared one), `Key(partition)`, `Prefix`, `QueryOptions(partition?, maxItemCount?)`, `WithOrganization(query)` and the constant `OrganizationParameter = "@organization"`. The constructor refuses a blank organization.
- `OrganizationKeys.Key(organization, partition)` and `Prefix(organization)` build the keys with `PartitionKeyBuilder`; `FullKey.GetCosmosPartitionKey()` returns the full key of the key's organization; `PartitionKeys.GetCosmos*` take the organization first.
- `IContainerRepositoryProvider.GetOrganizationContainer` returns `OrganizationContainer`; `ContainerRepositoryProvider` caches the two SDK containers and creates the small scope per call.
- The call sites (77 in 23 files, migrated by area: access, annotations, configurations, key/values and transfers, DbCreator) take every key of organization data from the scope; the remaining `new PartitionKey(...)` in `Backend.CosmosDb/src` are on `core` (`access`, `certificates`, `signing-keys`). Internal types follow: `AnnotationItemStore(OrganizationContainer, ...)` with `Repository` instead of `Container`, `BatchPlan(OrganizationContainer)` (it keeps the partition value per unit and builds each batch with `Key(value)`), the `Place` of `CosmosKeyValueService` with the full key, `ConfigurationCacheInvalidator` and the private helpers of the configuration services on `OrganizationContainer`, `RequestProcessModel.PartitionKey` the full key.
- The constructor of `CosmosAccessService` lost its `IContainerFactory` (it only created containers).
- `ContainerRepositoryProviderTests` (one shared container, a scope per organization) and `OrganizationKeysTests` (keys, prefix, query options, the parameter, the blank organization).

### 7. Removing the stored procedures

- `CosmosStoredProcedureNames.cs`, both procedure bodies in `ContainerFactory`, and `ContainerExtensions.UpsertStoredProcedureAsync`, `RemoveArrayValueAsync`, `DeleteUsageAndExecutionsAsync` with `DeleteResponse` are deleted. Nothing called them; no runtime behaviour changed, and creating a container no longer upserts two procedures. `audit-operations.md` notes that the "before" side's procedures are gone.

### Not in this phase

- Reading the parents of a delete or restore in one `ReadMany` stays [424-229](https://linear.app/424fun/issue/424-229/read-the-parents-of-a-soft-delete-and-a-restore-in-one-readmany-call).
