# Shared Certificate List Cache

## Overview

This change caches the shared certificate list of a project in the process, so a call that presents a shared certificate no longer queries Cosmos every time. It implements [Cache Shared Certificate Lists per Project](../2026-10-14%20Cache%20Shared%20Certificate%20Lists%20per%20Project.md) in full.

## What Was Done

### 1. Cache in `CosmosSharedCertificateStore`

- `Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs` takes `IMemoryCache` in its constructor. `AddCosmosDbAnnotations` already calls `AddMemoryCache()`, so the DI registration did not change.
- `ListAsync` caches the result per project under `scert:list:{organization}.{project}`: for 5 minutes when the list has certificates, and for 30 seconds when it is empty.
- `StoreAsync` evicts the entry after the document is created. `RevokeAsync` evicts it after the replace, and also when the certificate is not found.
- `LabelExistsAsync` is not cached, as planned.

### 2. Consistency

As planned: a change is visible at once on the instance that made it, and other instances see it within 5 minutes. The admin list endpoint reads through the same cache.

### 3. Tests

- New `Backend.CosmosDb/test/CosmosSharedCertificateStoreTests.cs`, against the Cosmos test container:
  - `List_IsCachedAndEvictedByWritesOnTheSameInstance` checks that a write evicts the cached list on the writing instance, that a second instance keeps its stale list, and that a fresh instance sees the revocation.
  - `Registration_UsesTheCosmosStore` checks the DI registration.
- These suites pass: `Backend.CosmosDb` unit tests (336), `Access.Certificates` unit tests (60) and integration tests (23), and `Api.Functions` unit tests (296).

### 4. Documentation

The scalability paragraph on certificates in `docs/Platform/Storyteller/authentication.md` now describes the cache and its 5-minute revocation bound.
