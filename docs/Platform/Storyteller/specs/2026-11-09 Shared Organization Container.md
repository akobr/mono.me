# Shared Organization Container

## Problem

Every organization gets its own Cosmos DB container (`org.{organization}`). That layout cannot carry the platform the way it is meant to be used: any developer may sign up and create organizations, so there will be more than 25 from the start, and the number has no planned limit.

- A database with shared throughput holds 25 containers. Past that, every container raises the minimum throughput of the database: by 1000 RU/s of autoscale maximum, or by 100 RU/s manual. The free tier of the production account (1000 RU/s) ends at about 31 organizations with manual throughput, and at 25 with autoscale.
- An account holds at most 500 databases and containers, and this limit can't be raised, in any capacity mode.
- Creating a container is a control-plane operation (500 creations per 5 minutes per account, 240 RU/s for all metadata operations), so a wave of sign-ups is throttled.
- Microsoft's multitenancy guidance does not recommend a container per tenant. It recommends a partition key per tenant, with hierarchical partition keys when there are many tenants, and an account of their own for the few tenants that need physical isolation.

The production environment ([Azure Production Environment of the 2S Platform](../../Deployment/specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md)) has not been deployed yet, so the layout can change now without a data migration ([decisions](../../Deployment/decisions.md), item 9).

Two smaller problems are solved on the way:

- `ContainerFactory` registers two stored procedures on every container, and nothing calls them since the soft delete of annotations (commit `8823d9d44`, review [Phase 1 of Soft-delete and audit of annotations](reviews/2026-10-11%20Phase%201%20of%20Soft-delete%20and%20audit%20of%20annotations.md), "Deviation"). They are the only reason the Aspire host and the tests run the classic Linux emulator.
- The classic emulator is heavy (3.65 GB image, one to three minutes to start, x64 only) and unstable under the full Cosmos test suite, which therefore runs in chunks.

## Current State

### Containers and keys

- `Backend.CosmosDb/src/CosmosConstants.cs`: the database `42.Platform.2S`, the container `core`, and the prefix `org.` of organization containers.
- `ContainerRepositoryProvider.GetOrganizationContainer(organization)` returns an `IContainerRepository` for `org.{organization}`. `IContainerRepository` only exposes the SDK `Container`, so every caller builds its own `PartitionKey` and queries. 77 calls in 23 files of `Backend.CosmosDb/src`, plus the tests.
- `ContainerFactory.CreateContainerIfNotExistsAsync(name)` creates a container with the partition key path `/PartitionKey`, applies the settings of organization containers (`ApplySettings`: TTL on without a default, `/*` indexed, `/Entries/*` and the audit stamp paths excluded) and upserts both stored procedures on every container, `core` included.
- `ContainerFactory.EnsureOrganizationContainersAsync` and `GetContainerNamesAsync` list the containers of the database (`GetContainerQueryIterator`).
- `CosmosAccessService.CreateAccessPointAsync` (line 187) creates `org.{organization}` when an organization is created.
- `CosmosAccessService.VerifyAccessForMachineAsync` (line 683) reads the container (`ReadContainerStreamAsync`) to decide whether the organization exists.
- `DbCreator/src/Logic/CoreDbStructureBuilder.cs` creates `org.42dotnet` with the core annotations (`Data.CreateCoreAnnotations(container)`), `core` with the access, `org.{UI_TESTS_ORGANIZATION}` with the staging seed, and finally updates the settings of every organization container.
- Partition key values have no organization: `PartitionKeys` (`{project}.rst.{name}`, `{project}.sbt.{name}`, `{project}.template`, `{project}.kv.{view}`, `{project}.transfer`), `FullKey.GetCosmosPartitionKey()`, and literals such as `{project}.access`, `{project}.audit`, `{project}.usage`. There are 104 `new PartitionKey(…)` in `Backend.CosmosDb/src`.
- The entities (`Entities/`) have `PartitionKey` and `id`, but no organization. The organization is implied by the container.

### What lives where

| Container | Partitions | Content |
| --- | --- | --- |
| `core` | `access`, `certificates`, `signing-keys` | accounts, access points (organizations and projects), invitations, machine authentication and key/value policies, the machine CA, the signing key of Storyteller tokens |
| `org.{organization}` | `{project}.rst.{name}`, `{project}.sbt.{name}` | annotations, their audits and configurations with history |
| | `{project}.template`, schema partitions | templates and schemas with history |
| | `{project}.kv.{view}` | key/value pairs and versions |
| | `{project}.access` | machines, machine certificates, shared certificates, trusted issuers, API key hashes |
| | `{project}.audit`, `{project}.usage` | key/value audit events, usage days |
| | `{project}.transfer` | view export and import jobs |

`core` is unaffected by this spec.

### Queries over a whole organization

Most queries name one partition. These do not, and rely on the container to stay inside the organization:

| Place | Query |
| --- | --- |
| `Annotating/CosmosAnnotationService.cs` `GetAnnotationsAsync` (line 834), `AnnotationItemStore.QueryPageAsync` (line 281, from `CosmosAnnotationService.cs:141` and `CosmosAnnotationService.Deletion.cs:70`) | annotation listings and the deleted annotations, without a partition key when the request names no responsibility or subject |
| `Annotating/AnnotationItemStore.cs` `QueryScopeAsync` (line 385) | descendants, with `scope.PartitionKey` null for scopes over several partitions |
| `Annotating/CosmosViewService.cs` `DiscoverAsync` (line 143) | the views of a project (`STARTSWITH(c.PartitionKey, '{project}.')`) |
| `Configuring/CosmosConfigurationService.cs` (lines 110, 605, 1598) | configuration listing, histories of a view, deleted annotation keys |
| `Configuring/CosmosConfigurationSchemaService.cs` `QueryAffectedConfigurations` (line 870) | configurations affected by a schema (LINQ without a partition key) |
| `KeyValues/Maintenance/CosmosKeyValueMaintenanceService.cs` (line 227) | envelopes to re-wrap |
| `KeyValues/Policy/CosmosKeyValuePolicyService.cs` `CountInUseAsync` (line 295) | keys of a level per view |
| `KeyValues/Usage/CosmosKeyValueUsageService.cs` (line 252) | the daily gauges per project |

`CosmosKeyValueUsageService.TakeSnapshotAsync` (line 80) finds the organizations by listing the containers whose name starts with `org.`.

### Stored procedures

`CosmosStoredProcedureNames` (`removeValueFromArray`, `deleteUsageAndExecutions`), their bodies in `ContainerFactory.CreateContainerIfNotExistsAsync`, and `ContainerExtensions.UpsertStoredProcedureAsync`, `RemoveArrayValueAsync` and `DeleteUsageAndExecutionsAsync`. Nothing calls the two `Execute` wrappers (`git grep` over `src`). The soft delete computes the relation arrays in memory and writes them in transactional batches with ETags (`CosmosAnnotationService.Deletion.cs`, `BatchPlan`).

### Emulators

- `Aspire.Host/src/AppHost.cs` line 25: `AddAzureCosmosDB("cosmos").RunAsClassicEmulator(…)` with the data volume `platform-cosmos-data`, "which supports the stored procedures the backend registers (the vNext emulator does not)". `Cosmos:Mode=local` uses the Windows emulator. In `Aspire.Hosting.Azure.CosmosDB` 13.6.4, `RunAsEmulator` is the vNext emulator.
- `Backend.CosmosDb/test/Startup.cs` and `Access.Certificates/testint/CosmosFixture.cs`: `CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")`, `AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10` because the containers the tests create otherwise run out of partitions (503, substatus 1007), a wait for the log line `Started`, and a cleanup that deletes every database (`GetDatabaseQueryIterator`).

### The probe

[Hierarchical Partition Key Probe](../cookbooks/hierarchical-partition-key-probe.md) (`src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs`) creates a container keyed by `/Organization`, `/PartitionKey` and runs every kind of operation the backend uses. On 2026-11-09 it passed on the classic Linux emulator, the vNext emulator and the Windows emulator 2.14.27 (without `/EnablePreview`): 19 checks each, none failed. The vNext emulator has no stored procedures and reports a flat 1 RU per request; listing containers and databases works on all three. The run against the real account is planned after the account exists.

## Proposed Changes

### 1. One shared container

- A new constant `CosmosConstants.OrganizationsContainerName = "organizations"`. The prefix `org.` is removed.
- The container `organizations` has the hierarchical partition key `["/Organization", "/PartitionKey"]` (kind `MultiHash`, version 2) and the settings of `ApplySettings`. It shares the throughput of the database `42.Platform.2S` with `core`. The database holds those two containers, whatever the number of organizations.
- A logical partition is (organization, today's partition key value), for example (`acme`, `billing.kv.default`). The values of `PartitionKey` and every `id` stay as they are. An `id` is unique per full key, so the same ids in two organizations don't collide (probe check 1).
- The limits per logical partition stay: 20 GB and 10,000 RU/s per (organization, partition). An organization as a whole can exceed 20 GB and spread over several physical partitions.

### 2. The organization on every item

- Every entity stored in `organizations` gets `public required string Organization { get; init; }`, serialized as `Organization` like the other PascalCase properties. These are the entities of the "org" rows of the table above: `Entity` and the annotation, configuration, schema and template entities derived from it (or `ExtendableEntity`), `AnnotationAuditEntity`, `ViewEntity`, the key/value entities (`KeyValueEntity`, `KeyValueVersionEntity`, `KeyValueAuditEntity`, `KeyValueUsageEntity`), `MachineAccessEntity`, `MachineCertificateEntity`, `SharedCertificateEntity`, `FederatedIssuerEntity`, the API key hash items of `CosmosMergedApiKeyHashStore`, and `ViewTransferEntity`. Item types of `core` (`AccessPointEntity`, `AccountEntity`, `InvitationEntity`, `CertificateAuthorityEntity`, `TokenSigningKeyEntity`) don't get it.
- The mappings (`AnnotationMappings`, `ConfigurationMappings`, `SchemaMappings`, `TemplateMappings`, `KeyValueMappings`, `AccessMappings`) set it from the `FullKey` or the organization of the call. The JSON serializers of the stream APIs (`_serializer.WriteAnnotation`, `DeserializeSystemTextJson`, Newtonsoft) need no change.
- `required` makes the compiler name every construction that misses it. Cosmos DB enforces the rest: a document whose `Organization` is missing or differs from the partition key of the write is refused (`400`), so a missed write fails in the first test that runs it instead of storing an item in the wrong place.
- A unit test asserts, by reflection over the assembly, that every entity type written to `organizations` has `Organization`, and a test captures the documents written through the test interceptors and checks that each carries it.

### 3. An organization-scoped repository

`GetOrganizationContainer(organization)` returns a new `OrganizationContainer : IContainerRepository` instead of a plain `ContainerRepository`. It is a thin scope over the shared container: the call sites keep the SDK calls they have, and take every partition key and every organization-wide query option from it.

```csharp
public sealed class OrganizationContainer : IContainerRepository
{
    public const string OrganizationParameter = "@organization";

    public OrganizationContainer(string organization, Container container);

    public string Organization { get; }

    public Container Container { get; }      // the shared container

    // (organization, partition): every point operation, single-partition query, ReadMany item and transactional batch
    public PartitionKey Key(string partitionKey);

    // (organization): the key prefix of an organization-wide query
    public PartitionKey Prefix { get; }

    // QueryRequestOptions with PartitionKey = Key(partitionKey), or Prefix when partitionKey is null
    public QueryRequestOptions QueryOptions(string? partitionKey = null, int? maxItemCount = null);

    // adds the parameter @organization with the organization to a query that has "c.Organization = @organization"
    public QueryDefinition WithOrganization(QueryDefinition query);
}

public static class OrganizationKeys
{
    public static PartitionKey Key(string organization, string partitionKey);   // PartitionKeyBuilder: both levels
    public static PartitionKey Prefix(string organization);                     // PartitionKeyBuilder: the first level
}
```

- The interface `IContainerRepositoryProvider.GetOrganizationContainer` returns `OrganizationContainer`; `GetCore` stays `IContainerRepository`. The provider caches one `Container` for `organizations` and creates the small `OrganizationContainer` per call (no cache per organization, so the cache no longer grows with the organizations). Test doubles (`InterceptedRepositoryProvider`) build an `OrganizationContainer` over their intercepted container.
- No call site builds a `PartitionKey` for organization data itself: every `new PartitionKey(value)` on an organization container becomes `repository.Key(value)`. `FullKey.GetCosmosPartitionKey()` builds the full key itself (`OrganizationKeys.Key(OrganizationName, GetPartitionKey())`), since a `FullKey` carries its organization; `EntityIndices`, a query projection that is never stored, needs no `Organization`. The `PartitionKeys.GetCosmos*` helpers take the organization as their first parameter. The string helpers (`PartitionKeys.Get*`) stay, because they are the value of `PartitionKey` in the documents. `core` keeps its single `/PartitionKey` path and its `new PartitionKey(…)` calls.
- A wrapper with its own read, write and query methods (all SDK operations in terms of the partition string) was considered and not taken: it would rewrite every call site's SDK usage for the same guarantee, which the `required` property, Cosmos DB's key check and the isolation tests give.
- `BatchPlan` keeps the partition key *value* per unit and builds the batch with `Key(value)` of the organization it was created for (`new BatchPlan(container)` becomes `new BatchPlan(organizationContainer)`).
- `ReadMany` calls (`BindingDependencyValidator`, `AnnotationItemStore.ReadManyAsync`, `ConfigurationCacheInvalidator`, the API key hash store) pass the full keys.

### 4. Organization-wide queries

Every query of the table in Current State that names no single partition:

- gets `c.Organization = @organization` in its `WHERE` clause, written out in the query text, with the parameter from `OrganizationContainer.WithOrganization` (SQL text is not rewritten by a helper: the queries add their own clauses, `ORDER BY`, `GROUP BY` and `DISTINCT`, and an injected predicate would be fragile), **and**
- runs with `OrganizationContainer.QueryOptions()` (the organization as the key prefix).

The `WHERE` clause is the isolation: Microsoft documents only that the prefix does the routing, so isolation must not depend on it (the probe's informational check showed the prefix alone kept the other organization out on all three emulators, but that is not a contract). The prefix routes the query to the physical partitions of the organization instead of all of them. LINQ queries (`QueryAffectedConfigurations`, `GetAnnotationsAsync`) add `.Where(entity => entity.Organization == organization)` and use the same options.

Costs measured by the probe on the classic emulator: a point read 1 RU, an organization query of 6 items about 3 RU, `GROUP BY` and `ORDER BY` about 3 RU, the same as a query inside one partition today. At the free-tier size everything is in one physical partition (up to 50 GB), so nothing gets slower. Later the cost of an organization's queries grows with its own size, not with the size of all organizations.

### 5. Organizations without containers

- `CosmosAccessService.CreateAccessPointAsync` no longer creates a container. Creating an organization is its access point in `core`, and immediate.
- `CosmosAccessService.VerifyAccessForMachineAsync` drops `ReadContainerStreamAsync`: a missing organization has no machine, so the point read of the machine answers it.
- `ContainerFactory.EnsureOrganizationContainersAsync` and `GetContainerNamesAsync` are replaced by `EnsureOrganizationsContainerAsync()`, which creates `organizations` when it is missing and applies `ApplySettings` to it. A container created with another partition key definition (a database from before this change) fails the startup of `DbCreator` with a message that names the old containers and the re-seed (section 9).
- A new `CosmosOrganizationCatalog.ListAsync()` (internal to `Backend.CosmosDb`) reads the organization access points (`apt.{organization}`, keys without a dot) from the `access` partition of `core`, one single-partition query. `CosmosKeyValueUsageService.TakeSnapshotAsync` uses it instead of the container names. `core` is the record of which organizations exist.
- The API needs no right to create containers any more. `organizations` is created by `DbCreator` before the API starts; a missing container answers `503` from the first request with a log entry, instead of being created on demand.

### 6. DbCreator

- **Before anything is written**, `CoreDbStructureBuilder.BuildAsync` checks the database: when it holds containers named `org.*` (listed with `GetContainerQueryIterator`, which works on the classic, vNext and Windows emulators and in Azure; probe check "list the containers"), or a container `organizations` whose partition key paths are not `/Organization`, `/PartitionKey`, it stops with a message that names them and explains the re-seed (delete the database, or start the emulator with a new volume, and run DbCreator again). `InitDbCommand` reports it with the existing error output and exit code.
- Then it creates `core` and `organizations` (`EnsureOrganizationsContainerAsync`), and seeds `42dotnet` (`Data.CreateCoreAnnotations(OrganizationContainer)`) and, with the staging seed, the UI test organization, both in `organizations`.
- The Aspire host starts the API after DbCreator has finished (`WaitForCompletion(dbCreator)`), since the API no longer creates the container it uses.
- The final step "settings of N organization containers" becomes "settings of the container `organizations`".
- `Data.CreateCoreAnnotations` takes the `OrganizationContainer` of `42dotnet`, so the seeded items carry `Organization`.

### 7. Removing the stored procedures

- Delete `CosmosStoredProcedureNames.cs`, both procedure bodies in `ContainerFactory`, and `ContainerExtensions.UpsertStoredProcedureAsync`, `RemoveArrayValueAsync` and `DeleteUsageAndExecutionsAsync` with `DeleteResponse`.
- No runtime behaviour changes: nothing calls them. Creating a container no longer upserts two procedures.
- The soft delete keeps its in-memory arrays and ETag batches. Reading the parents in one `ReadMany` is tracked separately ([424-229](https://linear.app/424fun/issue/424-229/read-the-parents-of-a-soft-delete-and-a-restore-in-one-readmany-call)) and is not part of this spec.

### 8. The vNext emulator for development and tests

- **Aspire** (`AppHost.cs`): `RunAsClassicEmulator` becomes `RunAsEmulator` (the vNext emulator in Aspire 13.6) with `--protocol https` if Aspire does not set it, `WithDataExplorer()` (vNext only), a new data volume `platform-cosmos-vnext-data` (the classic volume's format is not readable by vNext), and the persistent lifetime. The comment about the stored procedures goes. `Cosmos:Mode=local` (the Windows emulator) stays.
- **Tests** (`Backend.CosmosDb/test/Startup.cs`, `Access.Certificates/testint/CosmosFixture.cs`): an emulator selector `STORYTELLER_TESTS_EMULATOR` (`vnext`, the default, or `classic`) next to `STORYTELLER_TESTS_USE_CONTAINER`.
  - vNext runs from a generic Testcontainers `ContainerBuilder`, not `CosmosDbBuilder`: the 4.16 builder builds an `http://` connection string, waits for the classic log line `Started` and exposes only port 8081. The fixture uses the image `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest`, the command `--protocol https`, the ports 8081 and 8080, a wait for `GET /ready` on port 8080, an `https://` endpoint on the mapped port 8081 with the well-known key, and reuse labels of its own.
  - classic keeps today's `CosmosDbBuilder` configuration (`PARTITION_COUNT=10`, the wait for `Started`).
  - The cleanup deletes the database `42.Platform.2S` by name (tolerating `404`) instead of every database of the emulator.
- **The tests become one organization per test class** where they used one container per test class, so parallel test classes keep apart by organization, which is what production does too.
- **What vNext can't check:** request charges (a flat 1 RU), range and composite indexes (a policy is accepted but not applied, so a query `ORDER BY` an excluded path passes on vNext and fails in Azure), and replacing container settings (a no-op). The tests that assert those carry the trait `Emulator=Classic` and skip themselves on vNext through the fixture's emulator kind; a classic run selects them with `--filter-trait "Emulator=Classic"`.
- **CI**: a new workflow `.github/workflows/storyteller-cosmos.yml` (by hand, `workflow_dispatch`, or called, `workflow_call`, like the other Platform workflows) with a vNext job that runs `Backend.CosmosDb/test` and `Access.Certificates/testint` without the classic trait, and a classic job on an x64 runner that runs only the classic trait. Both set `STORYTELLER_TESTS_USE_CONTAINER=true`.
- The local Windows emulator stays a supported option and needs no flag (2.14.27).

### 9. Local data

There is no production data. Development data in the classic emulator's volume `platform-cosmos-data`, in the Windows emulator, and in the Testcontainers emulators is not migrated: `DbCreator` refuses to start on a database with `org.*` containers (section 5) and names the command that deletes the database, then seeds again. The documentation of the Aspire host says so. A copy tool is not provided; if it is needed later, it reads each `org.{organization}`, sets `Organization` and writes into `organizations` with the same ids.

### 10. Tests

- **Tenant isolation**: a test base that seeds two organizations with the same project, view, annotation keys, configurations, schemas, templates, key/values, machines and transfers, runs every service against one of them and asserts that nothing of the other one appears or changes: listings, histories, descendants, deleted annotations, views, re-wrap, counts per view, usage, export and import, purge. One test class per area (annotations, configurations, schemas and templates, key/values, access, transfers).
- **Entity coverage**: the reflection test of section 2.
- **Partition keys**: unit tests of `OrganizationKeys.Key` and `Prefix`, `OrganizationContainer.QueryOptions` and `WithOrganization`, and of `ContainerRepositoryProvider` (one shared container, a scope per organization).
- **Structure**: after DbCreator, the database has exactly `core` and `organizations`; creating organizations creates no container; DbCreator refuses a database with an `org.*` container.
- **The existing Cosmos suites** run on vNext; `--max-parallel-test-modules 1` should no longer be required, which the review confirms or not.
- **Classic emulator suite**: the tests with the trait `Emulator=Classic` (the container settings and indexing assertions, and a request-charge test of a point read and an organization-wide query with the prefix), run in the classic job of `storyteller-cosmos.yml`, so request charges and the indexing policy are checked like in Azure.
- **Azure**: the probe against `prod-2s-42fornet-cdb` before `DbCreator` runs there (cookbook, run 4); its charges go into the review.

### 11. Documentation

- Every current document that names the organization container, the stored procedures or the classic emulator: in `docs/Platform/Storyteller/` `annotation-lifecycle.md` (Containers), `key-values.md` (Storage: "the organization container (`org.{organization}`)" becomes the shared container; the TTL sentence about every container), `access.md` (creating an organization), `authentication.md` (machine certificates and issuers in the organization's partition), `audit-operations.md`, `binding.md`, `definitions.md`, `export-import.md`, `inheritance.md`, `mcp.md`, `templating.md` and `aspire.md`; `docs/Platform/Aspire.Host/local-development.md` and `docs/Platform/development.md` (the vNext emulator, the Data Explorer, the new volume, the re-seed); `CLAUDE.md` (the test notes about the Cosmos emulator, the emulator selector and the classic suite, the new workflow).
- The deployment spec's section 3 already names the shared container.

### 12. Order of the work

1. Sections 1 to 3 and 7 with the unit tests (no emulator needed).
2. Section 4 and 5, then 6, with the tenant isolation tests on the classic emulator first (the tests as they run today).
3. Section 8: the tests and Aspire on vNext; the classic suite in CI.
4. Section 11, and the probe against Azure once the account exists (not part of this change: the account does not exist yet; the cookbook of the probe has the run).

Each phase is its own commit (or commits) and gets its own review in `specs/reviews/`. Phases 1 and 2 only build together: the call sites move to the new keys in phase 1, and their tests run on an emulator in phase 2.

### 13. Decisions

- **D1.** The hierarchical key is `/Organization`, `/PartitionKey`, two levels. A third level (`/id`) would let a single partition exceed 20 GB, but transactional batches need the full key, and the key/value writes, the annotation batches and the purge rely on a batch per partition.
- **D2.** The organization is a property of every item, not part of the `PartitionKey` value: the existing values, ids, `STARTSWITH(c.PartitionKey, …)` filters and the dependency manifests of conditional reads stay valid.
- **D3.** Isolation is the `WHERE c.Organization = @organization` predicate written in every organization-wide query, never the key prefix alone. The prefix is the routing.
- **D4.** Moving an organization to a container or account of its own later (a premium or enterprise organization) is out of scope. The layout makes it a copy of the organization's items, plus one record that says where the organization lives; `GetOrganizationContainer` is the one place that would read it.
- **D5.** Per-organization request units are not measured by this spec. Cosmos DB reports metrics per container; the key/value usage meters are the existing per-organization measurement.
- **D6.** Deleting an organization is not a feature today. When it comes, it is a background job that queries the organization's items and deletes them in batches per partition (Cosmos DB has no delete by a key prefix; the probe measured about 8 RU per item).
- **D7.** `OrganizationContainer` is a thin scope (keys, query options, the parameter), not a wrapper of every SDK operation (section 3).
- **D8.** The CodeRabbit coding plan of 424-230 was read as a second opinion before the implementation. Taken from it: the generic `ContainerBuilder` for vNext, the cleanup by database name, the classic trait and the CI workflow with two jobs, the validation of the partition key paths of an existing `organizations` container, `core` as the record of organizations, and a fuller documentation list. Not taken: stamping `Organization` inside the repository on every write (the `required` property and Cosmos DB's key check give the same guarantee without touching the stream writes), and a fallback that reads `org.{organization}` containers by name when the container feed is missing (the feed works on all three emulators and in Azure, probe check "list the containers").
