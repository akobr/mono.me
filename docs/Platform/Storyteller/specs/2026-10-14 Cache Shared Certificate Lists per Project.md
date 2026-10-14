# Cache Shared Certificate Lists per Project

## Problem

Every call that presents a shared client certificate runs a Cosmos query in the project's access partition. `ClientCertificateValidator` calls `ISharedCertificateStore.ListAsync`, which loads every shared certificate of the project. The thumbprint lookup and the revocation check then run on that list. Machine certificate lookups are cached in the process, but shared certificate lookups are not. A busy client that uses a shared certificate therefore spends request units and adds Cosmos latency on every call.

## Current State

- `src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs`: `ListAsync` queries `scr.`-prefixed documents in partition `{project}.access` with no cache. `StoreAsync` creates a document and `RevokeAsync` sets `IsRevoked`.
- `src/Platform/Storyteller/Access.Certificates/src/ClientCertificateValidator.cs`: for a shared certificate, it calls `ListAsync(organization, project)` and finds the presented thumbprint in the result.
- `src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosClientCertificateStore.cs`: machine certificates are cached in `IMemoryCache`, hits for 5 minutes and misses for 30 seconds. The instance that writes a change evicts its own cache entries.
- `services.AddMemoryCache()` is already called in `AddCosmosDbAnnotations`, where the shared store is registered.

## Proposed Changes

### 1. Cache in `CosmosSharedCertificateStore`

- Inject `IMemoryCache` into the store.
- `ListAsync` caches the list per project under the key `scert:list:{organization}.{project}`:
  - A list with at least one certificate is kept for **5 minutes**, the same as a machine certificate hit.
  - An empty list is kept for **30 seconds**, the same as a machine certificate miss, so a project's first certificate is picked up quickly on other instances.
- `StoreAsync` and `RevokeAsync` evict the project's entry after a successful write. `RevokeAsync` evicts it even when the certificate is not found.
- `LabelExistsAsync` stays uncached. It runs only when a certificate is issued and must see the latest labels.

### 2. Consistency

The cache is in the process. The instance that issues or revokes a certificate sees the change at once. Other instances see a revocation within 5 minutes, the same bound as for machine certificates. The admin list endpoint uses the same `ListAsync`, so on another instance it can show a stale list for that long.

### 3. Tests

- A unit test in `Backend.CosmosDb/test` (against the Cosmos test container used by `CosmosClientCertificateStoreTests`) covers:
  - a second read is served from the cache;
  - a write on the same instance evicts the entry;
  - a separate instance with its own cache sees the revocation once its own entry expires or is absent.

### 4. Documentation

Update the scalability paragraph in `docs/Platform/Storyteller/authentication.md` to describe the cache.
