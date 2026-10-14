# Reject Unknown Shared Certificates

## Problem

`ClientCertificateValidator` accepts a shared certificate whose thumbprint is not in the project's shared certificate store. The only check is that the certificate is not marked revoked. Any certificate signed by Storyteller's CA with a well-formed `/shared/{label}` SAN therefore passes, even if the project never issued it, or its record was lost.

The project's shared certificate list is also cached for up to 5 minutes (see [Cache Shared Certificate Lists per Project](2026-10-14%20Cache%20Shared%20Certificate%20Lists%20per%20Project.md)). Rejecting thumbprints that are missing from that cached list would refuse a newly issued certificate on other instances until their cached list expires.

## Current State

- `src/Platform/Storyteller/Access.Certificates/src/ClientCertificateValidator.cs`: the shared branch calls `ListAsync`, finds the presented thumbprint, and rejects the certificate only when the record exists and `IsRevoked` is set.
- `src/Platform/Storyteller/Backend.Core/src/Accessing/ISharedCertificateStore.cs`: exposes `ListAsync`, `StoreAsync`, `RevokeAsync` and `LabelExistsAsync`, but no single-certificate lookup.
- `src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs`: a shared certificate document has the id `scr.{thumbprint}` in partition `{project}.access`.

## Proposed Changes

### 1. Point lookup in the store

- Add `Task<SharedCertificate?> GetByThumbprintAsync(string organization, string project, string thumbprint)` to `ISharedCertificateStore`.
- In `CosmosSharedCertificateStore`, implement it as a point read of `scr.{THUMBPRINT}`, with the thumbprint upper-cased as .NET produces it.
- Cache the result the way `CosmosClientCertificateStore` does: a found certificate for **5 minutes**, a miss for **30 seconds**, under `scert:thumb:{organization}.{project}:{THUMBPRINT}`.
- `StoreAsync` and `RevokeAsync` evict this entry together with the project's list entry.

### 2. Validator

The shared branch calls `GetByThumbprintAsync`:
- When no record exists, it rejects the certificate (`null`) and logs a warning.
- When the record is revoked, it rejects the certificate, as before.
- Otherwise it returns the same `Shared` result as today.

Validation then no longer loads the whole project list. A certificate issued on one instance is accepted on every other one at once, unless that instance looked up the same thumbprint during the last 30 seconds.

### 3. Tests

- `ClientCertificateValidatorTests`:
  - the default shared store knows no certificates;
  - a valid shared certificate needs a stored record;
  - new cases cover a certificate that is not in the store and one that is revoked.
- `CosmosSharedCertificateStoreTests`: a point lookup misses before the certificate is stored, finds it afterwards in any letter case, follows its revocation, and keeps projects isolated.

### 4. Documentation

Update the shared certificate sentence in `docs/Platform/Storyteller/authentication.md`.
