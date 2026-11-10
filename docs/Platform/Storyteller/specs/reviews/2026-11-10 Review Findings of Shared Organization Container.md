# Review Findings of Shared Organization Container

## Overview

The findings of the review of pull request #182, which delivers the spec [Shared Organization Container](../2026-11-09%20Shared%20Organization%20Container.md) (424-230). Each finding was checked against the code of the branch. The valid ones are fixed, and the others are left with a reason. The spec stays as written. Its open points are recorded here and in the docs.

## What Was Done

### 1. An enforced predicate for organization-wide queries

- **The finding.** Isolation rests on the predicate `c.Organization = @organization` written by hand in every organization-wide query. On the emulators, the prefix key alone keeps other organizations out, so no test failed when a query lacked the predicate. **Valid.**
- **Done:** `OrganizationQueryGuard` (`Backend.CosmosDb/test`), a `RequestHandler` in the pipeline of the suite's Cosmos client.
  - It refuses every query on `organizations` that does not carry one full key, unless the query filters on the organization.
  - Both forms count: the SQL predicate with the `@organization` parameter, and LINQ's `root["Organization"] = "..."`.
  - Because it sits at the request level, it sees the SQL queries and the LINQ queries (`GetAnnotationsAsync`, `QueryAffectedConfigurations`, the cache invalidation) alike. A wrapper of `Container` would miss LINQ, whose feed iterator runs on the SDK's own container.
  - A query with a full key carries `x-ms-documentdb-partitionkey` with two values. An organization-wide query carries the range of the prefix (`x-ms-start-epk`, `x-ms-end-epk`) and no key header.
  - `CosmosClientProvider` takes the `RequestHandler`s registered in the services and adds them to the client's pipeline. The API registers none. The test `Startup` registers the guard, so the whole suite (all isolation tests included) runs every query through it.
  - A `ReadMany` over several partitions reaches the guard as the SDK's own query, which has the full key of every item (`c.id = @param_id{n} AND c["Organization"] = @param_pk{n}0 AND ...`). The guard lets it through when every item has its organization. The first full run refused it in `BindingDependencyValidator`, the export and the import before that rule existed.
  - A test that looks across organizations on purpose (`TransferIsolationTests.OrganizationsOfItemAsync`, which shows that an item exists only in its own organization) runs its query in `OrganizationQueryGuard.AllowCrossOrganization()`.
  - `OrganizationQueryGuardTests`: a prefix query without the predicate is refused, in SQL and in LINQ. With the predicate, it runs. A full-key query runs without one. A table checks the recognized texts, the `ReadMany` form included.
  - The guard found no production query without the predicate.
- **Not done:** a production `OrganizationContainer` method that writes or asserts the predicate, or a LINQ helper. The guard covers every call site, the future ones included, without changing them.

### 2. A pull request trigger for `storyteller-cosmos.yml`

- **Valid.** The workflow ran only by hand or when called.
- It now also runs on pull requests that touch `src/Platform/Storyteller/**`, the build-wide files or the workflow itself.
- On a pull request, the classic job runs only `--filter-trait Emulator=Classic` of `Backend.CosmosDb` and skips `Access.Certificates/testint`, which has no such test (a module without tests fails the run). Run by hand or called, it runs the whole suites as before. The whole suites on the classic emulator take long and the emulator restarts under load, so a pull request gets the quick run.

### 3. Pinned emulator images

- **Valid.** Both `CosmosTestEmulator` copies used `vnext-latest` and `latest`.
- They now use `vnext-EN20261019` and `2.14.28`, the releases with the same digests as the `vnext-latest` (`sha256:db53613d…`) and `latest` (`sha256:9a4f6912…`) the suites ran green on.

### 4. Unknown organizations

- **Valid.** The storage no longer checks implicitly that an organization exists: a missing `org.{organization}` container used to answer `404`.
- `annotation-lifecycle.md#containers` now says:
  - an unknown organization reads nothing and its writes succeed;
  - such data is not in the organization catalog, so not in the key/value usage snapshot;
  - the access checks are the gate (the account's access map for users, the point read of the machine for machines);
  - code that writes outside them must make sure the organization exists first.
- No code change: no API path reaches the storage for an organization without an access point.

### 5. Shared constants of the access points

- **Valid.** `CosmosOrganizationCatalog` repeated `"access"` and `"apt."`.
- `AccessPointEntity` has `Partition` and `IdPrefix`, used by its own `PartitionKey` and `Id`, and by the catalog.
- The older literals of the access services (accounts and invitations share the partition) are left as they were. Changing them is not part of this change.

### 6. Nits

- **The DbCreator message.** "has the layout before the shared container" was wrong for an `organizations` with other partition key paths. `DatabaseLayoutException`, `InitDbCommand` and the comment of `ExitCodes` now say "does not have the layout of the shared container"; each problem keeps its own sentence.
- **`FindLayoutProblemsAsync` does not check `core`.** Skipped: `core` keeps its layout (`/PartitionKey`), which this change does not touch, and `OrganizationsContainerStructureTests` checks it.
- **`CosmosTestEmulator` in two projects.** Skipped: the two test projects share no code, and a shared test library for one class is more than this change needs.
- **Explicit `Ensure` methods instead of `CreateContainerIfNotExistsAsync(name)`.** Skipped: `EnsureOrganizationsContainerAsync` already exists. The name-based method keeps DbCreator's one loop over the containers.
- **Throughput per organization.** Recorded in `annotation-lifecycle.md#containers`, not in the spec, which stays as written:
  - all organizations share the database's throughput, and a busy one can make the others wait (`429`);
  - Cosmos DB has no throughput per key prefix;
  - an organization that needs its own moves to a container or an account of its own.

### 7. The inline comment on `Containers_HaveItemLevelTimeToLiveEnabled`

- **The finding.** Move the check of the bare container (`EnsureContainerSettingsAsync`) into a classic-only test.
- **Not valid:** the test passes on the vNext emulator. The container settings are stored and read back there, as on the classic emulator. Moving it would only drop it from the vNext runs. Left as it is.

### 8. The first pull request run

- The vNext job of the first run on the pull request failed in one test, `CosmosKeyValueServiceTests.Listing_Pages`: the first page had 1,050 items instead of 1,000.
- **Not related to the inline comment of section 7:** `Containers_HaveItemLevelTimeToLiveEnabled` passed in the same job (777 of 778 passed).
- The cause is the emulator together with the client's OS:
  - On Linux, the SDK gets the query plan from the gateway.
  - For that single-partition query (full key, `x-ms-max-item-count: 1000`), the vNext emulator returns all 1,050 items with no continuation.
  - From Windows, the same emulator container returns 1,000 items and a continuation.
  - The failure reproduced every time from the .NET SDK container `mcr.microsoft.com/dotnet/sdk:10.0` against the local emulator, and the captured request and response showed the cause.
  - The service and the classic emulator keep the page size.
- `Listing_Pages` now carries the trait `Emulator=Classic` and skips itself on vNext. The skip reason of `CosmosTestEmulator` names the page size. On the classic emulator it passes (the 5 classic tests, 5 of 5).
- The rest of the vNext suite passed on Linux in that job, so no other test depends on the page size.

### 9. A later pull request run

- The vNext job of run 3 ([42726450097](https://github.com/akobr/mono.me/actions/runs/42726450097), commit `fc41cc2`) failed in one test, `CosmosKeyValueServiceTests.ExpiredDeletedKey_IsGoneAndANewCreateContinuesAfterItsVersions`: the recreate of the expired key threw `ConflictException` (changed concurrently 8 times). Run 2 (commit `b06a392`, the same code) had passed it.
- The cause is the vNext emulator, not the shared container:
  - After the deleted key's time to live, a point read answers 404, but `CreateItemAsync` with the same id answers 409 Conflict for about 0.95 seconds more. A throwaway probe measured it in every round, with the hierarchical key and with a plain `/PartitionKey` container alike.
  - `SetAsync` reads 404, creates, gets 409 and retries; its 8 attempts back off about 0.6 seconds in all, so it gives up inside that second.
  - The test polls the read every 2 seconds and sees the 404 about 4 seconds after the delete, right at the end of the 3-second retention and the second after it. Whether the recreate lands before or after the id is freed is a matter of timing, so the test passes or fails from run to run.
  - The classic emulator, the engine of the service, frees the id with the expiry: the same probe created the item again within 20 ms of the first 404, with no conflict.
- The test now carries the trait `Emulator=Classic` and skips itself on vNext, so it runs in the classic job of every pull request. The test and its assertions are unchanged. The skip reason and the vNext notes of `CosmosTestEmulator` name the late id. `SetAsync` stays as it is: it is right on the service.

### Validation

- The affected projects build: `DbCreator`, `Api.Functions`, `Api.Web` and both Cosmos DB test projects.
- On the pinned vNext emulator:
  - `Backend.CosmosDb/test` without the classic trait, with the guard: 778 of 778;
  - `Access.Certificates/testint`: 73 of 73.
- On the pinned classic emulator:
  - the `Emulator=Classic` tests: 4 of 4, 5 of 5 with `Listing_Pages`, and 6 of 6 with `ExpiredDeletedKey_IsGoneAndANewCreateContinuesAfterItsVersions`;
  - the guard tests and the isolation tests of annotations, configurations and transfers: 35 of 35.
- The pull request trigger of the workflow runs once this branch is pushed.
