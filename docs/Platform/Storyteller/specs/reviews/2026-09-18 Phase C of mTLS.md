# Phase C of mTLS — Renewal and Shared Certificates (Completed 2026-09-18)

## Overview

Phase C adds the self-service certificate renewal endpoint with ETag-guarded idempotency, plus issue/list/revoke for project-scoped shared certificates. This completes the certificate lifecycle: machines can now renew their own certificates before expiry without administrator intervention, and projects can issue shared certificates for multi-instance deployments.

The full plan is described in `2026-09-16 mTLS Per-Machine Authentication for Storyteller API.md`.

Previous phases: `2026-09-17 Phase A of mTLS.md`, `2026-09-18 Phase B of mTLS.md`.

---

## What Was Done

### 1. Certificate Renewal Service

**`Backend.Core/src/Accessing/ICertificateRenewalService.cs`** — interface:
```csharp
Task<CertificateRenewalResult> RenewAsync(
    string organization, string project,
    string machineAccessId, string presentingThumbprint);
```

**`Backend.Core/src/Accessing/Model/CertificateRenewalResult.cs`** — result record with `Outcome`, `Pkcs12`, `Password`, `Thumbprint`, `LastRenewalAt`, `Message`.

**`Backend.Core/src/Accessing/Model/CertificateRenewalOutcome.cs`** — enum: `Success`, `AlreadyRenewed`, `NotInRenewalWindow`, `NotFound`.

**`Backend.CosmosDb/src/Accessing/CosmosCertificateRenewalService.cs`** — implements `ICertificateRenewalService`. Handles the complete renewal flow:

1. **Read `MachineAccessEntity`** from Cosmos with ETag
2. **Check if already renewed** — if presenting certificate's thumbprint matches `PreviousThumbprint`, return `AlreadyRenewed` with the descriptive message including `LastRenewalAt`
3. **Verify presenting certificate** — must match current `CertificateThumbprint`
4. **Check renewal window** — `DateTimeOffset.UtcNow` must be >= `NotAfter - RenewalWindowDays`
5. **Issue new certificate** via `IClientCertificateAuthority.IssueCertificateAsync`
6. **Compute `PreviousValidUntil`** — `min(old NotAfter, now + RenewalOverlapDays)`
7. **ETag-guarded CAS write** — `ReplaceItemAsync` with `IfMatchEtag`. Updates:
   - `CertificateThumbprint` = new thumbprint
   - `PreviousThumbprint` = old thumbprint
   - `PreviousValidUntil` = computed overlap
   - `CertificateSerialNumber`, `NotBefore`, `NotAfter` = from new certificate
   - `LastRenewalAt` = `DateTimeOffset.UtcNow`
8. **On CAS conflict (HTTP 412)** — single re-read. If presenting cert now matches `PreviousThumbprint`, return `AlreadyRenewed`. No retry loop needed — after one successful CAS the entity is stable.

### 2. Shared Certificate Store

**`Backend.Core/src/Accessing/ISharedCertificateStore.cs`** — interface:
- `ListAsync(org, project)` → `IReadOnlyList<SharedCertificate>`
- `StoreAsync(org, project, SharedCertificate)`
- `RevokeAsync(org, project, thumbprint)` → `bool`
- `LabelExistsAsync(org, project, label)` → `bool`

**`Backend.Core/src/Accessing/Model/SharedCertificate.cs`** — record with:
- `Thumbprint`, `Label`, `NotBefore`, `NotAfter`, `IsRevoked`
- `Certificate` (base64 PKCS#12, response only)
- `CertificatePassword` (one-time, response only)

**`Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs`** — implements `ISharedCertificateStore`:
- Stores in `org.{organization}` / partition `{project}.access` / id `scr.{thumbprint}`
- `ListAsync`: queries with `STARTSWITH(c.id, 'scr.')`
- `RevokeAsync`: reads entity, updates `IsRevoked = true`
- `LabelExistsAsync`: count query filtered by label

### 3. Shared Certificate Service

**`Access.Certificates/src/SharedCertificateService.cs`** — orchestrates shared cert lifecycle:
- **`IssueAsync`**: resolves lifetime (request > config default, clamped to bounds), enforces label uniqueness by appending `-YYYY-MM-DD-HH-mm-ss` on duplicate, issues via `IClientCertificateAuthority.IssueSharedCertificateAsync`, stores via `ISharedCertificateStore`
- **`ListAsync`**: delegates to store
- **`RevokeAsync`**: delegates to store, logs revocation

### 4. API Endpoints

New endpoint class: **`Api.Functions/src/V1/CertificatesHttp.cs`**

**`POST v1/{organization}/{project}/access/machines/{id}/certificate/renew`**
- **Authentication**: machine's own still-valid certificate (via `MachineAuthenticationMiddleware`)
- **Authorization**: route `{id}` must match authenticated `MachineIdentity` from middleware
- Uses `PresentingCertificateThumbprint` from context (set by middleware)
- Returns:
  - `200 OK` with PKCS#12 on success
  - `200 OK` with descriptive "already renewed" message on idempotent retry
  - `400 Bad Request` if not in renewal window
  - `404 Not Found` if machine access not found or cert mismatch
  - `401 Unauthorized` if no machine identity or ID mismatch

**`GET v1/{organization}/{project}/access/certificates`**
- **Scope**: `User.Impersonation`, **Role**: `Administrator`
- Lists all shared certificates for the project

**`POST v1/{organization}/{project}/access/certificates`**
- **Scope**: `User.Impersonation`, **Role**: `Administrator`
- Body: `{ "Label": "...", "LifetimeDays": 365 }`
- Returns the issued shared certificate with PKCS#12 data

**`DELETE v1/{organization}/{project}/access/certificates/{thumbprint}`**
- **Scope**: `User.Impersonation`, **Role**: `Administrator`
- Revokes a shared certificate (record-based, takes effect on next request)

### 5. Middleware Update

**`MachineAuthenticationMiddleware`** — now passes the presenting certificate's thumbprint into `FunctionContextItemKeys.PresentingCertificateThumbprint`. This is needed by the renewal endpoint to match the presenting cert against `MachineAccessEntity`.

### 6. Definitions Update

**New routes** in `Definitions.Routes.Access.V1`:
- `MachineCertificateRenew` — `v1/{organization}/{project}/access/machines/{id}/certificate/renew`
- `SharedCertificates` — `v1/{organization}/{project}/access/certificates`
- `SharedCertificate` — `v1/{organization}/{project}/access/certificates/{thumbprint}`

**New route IDs** in `Definitions.RouteIds.Access`:
- `RenewMachineCertificate`, `GetSharedCertificates`, `IssueSharedCertificate`, `RevokeSharedCertificate`

**New parameter**: `Definitions.Parameters.Thumbprint`

### 7. DI Registration

**`Backend.CosmosDb/src/EntryPoint.cs`**:
- `ICertificateRenewalService` → `CosmosCertificateRenewalService`
- `ISharedCertificateStore` → `CosmosSharedCertificateStore`

**`Access.Certificates/src/EntryPoint.cs`**:
- `SharedCertificateService` (concrete class)

---

## Files Created

```
src/Platform/Storyteller/Backend.Core/src/Accessing/ICertificateRenewalService.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/CertificateRenewalResult.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/CertificateRenewalOutcome.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/ISharedCertificateStore.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/SharedCertificate.cs

src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosCertificateRenewalService.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs

src/Platform/Storyteller/Access.Certificates/src/SharedCertificateService.cs

src/Platform/Storyteller/Api.Functions/src/V1/CertificatesHttp.cs
src/Platform/Storyteller/Api.Functions/src/Models/SharedCertificateCreate.cs
```

## Files Modified

```
src/Platform/Storyteller/Api.Functions/src/Definitions.cs
src/Platform/Storyteller/Api.Functions/src/FunctionContextItemKeys.cs
src/Platform/Storyteller/Api.Functions/src/Security/MachineAuthenticationMiddleware.cs
src/Platform/Storyteller/Backend.CosmosDb/src/EntryPoint.cs
src/Platform/Storyteller/Access.Certificates/src/EntryPoint.cs
```

---

## Renewal Flow Detail

### Timeline Diagram

```
Certificate issued ─────────────── Renewal window opens ── Renewed ── Overlap ends ── Old cert expires
      │                                    │                   │            │               │
      │← lifetime (e.g. 365 days) ────────→│← 30 days ────────→│←─ 7 days ─→│               │
      │                                    │                   │            │               │
      │                           RenewalWindowDays    Winner gets      Old cert        NotAfter
      │                             before NotAfter     new PKCS#12    no longer
      │                                                                accepted
```

### Renewal Idempotency

Once renewed, **no second certificate is ever minted during a renewal window**:

| Presenting cert thumbprint | State | Response |
|---|---|---|
| Matches current `CertificateThumbprint` | Not yet renewed, in window | **Success** — issue new cert, CAS write |
| Matches current `CertificateThumbprint` | Not in window | **NotInRenewalWindow** |
| Matches `PreviousThumbprint` | Already renewed | **AlreadyRenewed** — descriptive message |
| Matches new `CertificateThumbprint` | Already renewed, presenting new cert | **NotInRenewalWindow** (new cert's window is ~335 days away) |
| Neither | Unknown cert | **NotFound** |

### ETag CAS Retry

On HTTP 412 (Precondition Failed):
1. Single re-read of entity
2. Presenting cert now matches `PreviousThumbprint` (winner updated entity)
3. Return `AlreadyRenewed` — no retry loop needed

---

## Shared Certificate Lifecycle

### Label Uniqueness

If a label already exists within the project, the system appends `-YYYY-MM-DD-HH-mm-ss`:
- First issue: `my-service`
- Duplicate: `my-service-2026-09-18-14-30-00`

### Revocation

Record-based (flag in Cosmos). Takes effect on the very next request — no CRL/OCSP needed.

### Storage

`SharedCertificateEntity` in `org.{organization}` / partition `{project}.access` / id `scr.{thumbprint}`:
- `Label`, `Thumbprint`, `NotBefore`, `NotAfter`, `IsRevoked`

---

## Implementation Notes for Phase D

### What Phase D Adds

CLI commands for certificate management: `MachineCreateCommand` gains credential-kind, lifetime, and PKCS#12 encryption options; new commands for renew, machine-authentication policy, shared-certificate issue/list/revoke, and CA certificate download.

### Key CLI Commands Phase D Must Implement

- `sform machine create` — gains `--credential-kind`, `--lifetime-days`, `--pkcs12-encryption` options; writes PKCS#12 to file
- `sform machine renew` — self-service renewal via the API endpoint
- `sform machine-auth set` — set project's `MachineAuthenticationPolicy`
- `sform shared-cert issue` — issue shared certificate
- `sform shared-cert list` — list shared certificates
- `sform shared-cert revoke` — revoke shared certificate
- `sform ca download` — download CA certificate as PEM

### Key Dependencies

- The CLI uses `Platform/Sdk` (RestSharp-based HTTP client) which already exposes `Configuration.ClientCertificates`
- New API endpoints are available and documented via OpenAPI
- PKCS#12 file writing needs `System.IO` (the CLI already uses `IFileSystem` abstraction)
