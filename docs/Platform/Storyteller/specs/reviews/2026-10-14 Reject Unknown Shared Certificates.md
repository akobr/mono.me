# Reject Unknown Shared Certificates

## Overview

A shared certificate is now accepted only when the project has a stored, non-revoked record for its thumbprint. Validation looks the certificate up with a cached point read instead of loading the project's list. This implements [Reject Unknown Shared Certificates](../2026-10-14%20Reject%20Unknown%20Shared%20Certificates.md) in full.

## What Was Done

### 1. Point lookup in the store

- `ISharedCertificateStore.GetByThumbprintAsync` was added.
- `CosmosSharedCertificateStore.GetByThumbprintAsync` reads `scr.{THUMBPRINT}` with `TryReadItemAsync`. It caches a hit for 5 minutes and a miss for 30 seconds, wrapped in a `CachedCertificate` record so that a cached miss can be told apart from a missing cache entry.
- A private `Evict` removes both the list entry and the thumbprint entry. `StoreAsync` and `RevokeAsync` call it, and `RevokeAsync` also calls it when the certificate is not found.
- The mapping from entity to model moved into a shared `ToModel` helper.

### 2. Validator

`ClientCertificateValidator` rejects a shared certificate with no record, logging the organization and the project, and still rejects a revoked one. The result for an accepted certificate is unchanged.

### 3. Tests

- `ClientCertificateValidatorTests`:
  - the default shared store mock returns no record;
  - the helper `SharedStoreWith` sets up a stored or revoked record for a test certificate;
  - two new tests were added: `ValidateAsync_SharedCertNotInStore_ReturnsNull` and `ValidateAsync_RevokedSharedCert_ReturnsNull`.
- `CosmosSharedCertificateStoreTests.GetByThumbprint_FindsIssuedCertificates_AndFollowsRevocation` was added.
- These suites pass: `Access.Certificates` unit tests (62) and integration tests (23), `Api.Functions` unit tests (296), `Backend.Core` unit tests (42), and `Backend.CosmosDb` unit tests (337).

### 4. Documentation

`docs/Platform/Storyteller/authentication.md` now describes the point lookup, the rejection of unknown and revoked shared certificates, and both cache lifetimes.
