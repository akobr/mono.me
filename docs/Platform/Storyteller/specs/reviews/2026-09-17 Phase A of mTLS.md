# Phase A of mTLS — Foundation (Completed 2026-09-17)

## Overview

Phase A implements the foundation layer for mTLS per-machine authentication in the Storyteller API. It introduces certificate-based machine authentication alongside the existing API key mechanism, with per-project policy control over which credential kind(s) are required. The system defaults to `ApiKey` mode, so existing behavior is unchanged until a project explicitly opts into certificates.

The full plan is described in `2026-09-16 mTLS Per-Machine Authentication for Storyteller API.md`.

---

## What Was Done

### 1. New Enums and Models in `Abstractions.Access`

**`MachineCredentialKind`** (`Abstractions.Access/src/MachineCredentialKind.cs`)
- `ApiKey` (default), `Certificate`, `CertificateAndApiKey`

**`MachineAuthenticationPolicy`** (`Abstractions.Access/src/Model/MachineAuthenticationPolicy.cs`)
- Per-project policy record: `CredentialKind` + optional `CertificateLifetimeDays`
- Stored on the access point; `null` means inherit deployment defaults

**Extended `IMachineAccess` / `MachineAccess`** with:
- `MachineCredentialKind CredentialKind`
- `string? CertificateThumbprint`
- `string? Certificate` (base64 PKCS#12, response-only)
- `string? CertificatePassword` (one-time, response-only)
- `DateTimeOffset? LastRenewalAt`

**Extended `IAccessPoint` / `AccessPoint`** with:
- `MachineAuthenticationPolicy? MachineAuthentication`

### 2. Backend.Core Abstractions (14 new files)

All under `Backend.Core/src/Accessing/`:

| File | Purpose |
|---|---|
| `ICertificateAuthorityProvider.cs` | `GetActiveAsync()` (CA cert + signer), `GetTrustedAsync()` (all valid CAs) |
| `ICertificateAuthorityStore.cs` | Persist/retrieve CA PKCS#12 material |
| `IClientCertificateAuthority.cs` | Issue per-machine and shared certificates |
| `IClientCertificateValidator.cs` | Validate a presented `X509Certificate2` |
| `IClientCertificateStore.cs` | Store/retrieve/revoke per-machine certificate records |
| `IMachineAuthenticationPolicyStore.cs` | Get/set per-project `MachineAuthenticationPolicy` |
| `MachineAuthenticationOptions.cs` | Deployment-wide config: `DefaultCredentialKind`, `CertificateHeaderName`, `TrustDomain`, lifetime bounds, renewal windows |
| `CertificateAuthorityOptions.cs` | CA config: `Kind` (Cosmos/Configuration/KeyVault), `RefreshInterval`, `IsAutoBootstrapEnabled` |
| `CertificateAuthorityKind.cs` | Enum: `Cosmos`, `Configuration`, `KeyVault` |

Models under `Backend.Core/src/Accessing/Model/`:

| File | Purpose |
|---|---|
| `ClientCertificateKind.cs` | `Machine` or `Shared` |
| `ClientCertificateValidationResult.cs` | Validation output: org, project, machineAccessId, scope, thumbprint, kind, annotationKey |
| `IssuedCertificate.cs` | Issuance output: pkcs12, password, thumbprint, serial, notBefore/notAfter |
| `CertificateAuthorityMaterial.cs` | CA cert + `X509SignatureGenerator` |
| `CertificateRecord.cs` | Persistent record: thumbprint, previous thumbprint, serial, validity, revoked flag, scope, annotationKey, lastRenewalAt |

**`IMachineAccessService`** gained `ExtendMachineAccessAsync(MachineAccess existing, MachineAccessCreate model)` with a default `NotSupportedException` implementation.

**`MachineAccessCreate`** gained `int? CertificateLifetimeDays`.

### 3. `CachedAsync<T>` in `Libraries/Utils/Async`

File: `Libraries/Utils/Async/src/CachedAsync.cs`

A time-based caching primitive that wraps an async factory, caches the result for a configurable TTL, and re-evaluates on the next access after expiry. Thread-safe via `SemaphoreSlim`. On refresh failure when a previous value exists, the stale value is kept and the error is reported via a callback — the next access retries.

Used by `LocalCertificateAuthorityProvider` to cache the CA certificate/key material.

### 4. `Access.Certificates` Project (10 files)

New project: `src/Platform/Storyteller/Access.Certificates/`
- Assembly: `42.Platform.Storyteller.Backend.Certificates`
- References: `Backend.Core`, `Access.ApiKeys`, `Libraries/Utils/Async`

| File | Purpose |
|---|---|
| `CertificateIdentity.cs` | Parses/generates SPIFFE URI SANs: `spiffe://{trustDomain}/org/{org}/project/{proj}/machine/{id}` or `.../shared/{label}`. Extracts URIs from SAN extension via ASN.1 parsing (context-specific tag 6). Validates path segments against `[a-zA-Z0-9._-]`. |
| `LocalCertificateAuthority.cs` | Implements `IClientCertificateAuthority`. Issues RSA-2048 leaf certificates with: `DigitalSignature` key usage, `clientAuth` EKU, SPIFFE URI SAN, SKI, AKI, random 16-byte serial. Clamps lifetime to CA remaining validity. |
| `LocalCertificateAuthorityProvider.cs` | Implements `ICertificateAuthorityProvider`. Auto-bootstraps an RSA-4096 self-signed CA (BasicConstraints CA, KeyCertSign+CrlSign, SKI) when the store is empty. Uses `CachedAsync<T>` with configurable refresh interval. |
| `ClientCertificateValidator.cs` | Implements `IClientCertificateValidator`. Validates via `X509Chain` with `CustomRootTrust`, `NoCheck` revocation, `clientAuth` application policy. Checks validity window, parses SAN identity, does thumbprint pinning (current or previous during overlap) and revocation check. |
| `CertificateMachineAccessService.cs` | Creates certificates for machine access (new or extending existing), stores certificate records. Resolves lifetime: request value > project policy > deployment default, clamped to bounds. |
| `PolicyAwareMachineAccessService.cs` | Implements `IMachineAccessService`. Resolves the project's `MachineAuthenticationPolicy` and dispatches: `ApiKey` delegates to `ApiKeyMachineAccessService`, `Certificate` delegates to `CertificateMachineAccessService`, `CertificateAndApiKey` does two-phase create with compensation (API key first, then extend with certificate; rolls back on failure). |
| `ConfigurationCertificateAuthorityStore.cs` | In-memory `ICertificateAuthorityStore` for tests/CI. |
| `InMemoryClientCertificateStore.cs` | In-memory `IClientCertificateStore` for tests/dev. |
| `EntryPoint.cs` | `AddCertificateMachineAccess(IConfiguration)`: binds `MachineAuthenticationOptions`, registers all services, replaces `IMachineAccessService` with `PolicyAwareMachineAccessService`, ensures `IApiKeyValidator` is registered. Runtime check that `AddApiKeyMachineAccess()` was called first. |

### 5. Backend.CosmosDb Changes

**Consolidated `MachineAccessEntity`** (`Entities/Access/MachineAccessEntity.cs`):
- Absorbed fields from `ApiKeyHashEntity`: `HashedSecret`
- Added certificate fields: `CredentialKind`, `CertificateThumbprint`, `PreviousThumbprint`, `PreviousValidUntil`, `CertificateSerialNumber`, `NotBefore`, `NotAfter`, `IsRevoked`, `LastRenewalAt`
- Added `ETag` for CAS operations (used in Phase C renewal)

**New entities:**
- `CertificateAuthorityEntity.cs` — CA material in `core` container, partition `certificates`, id `cau.{version}`
- `SharedCertificateEntity.cs` — shared certificates in `org.{organization}`, partition `{project}.access`, id `scr.{thumbprint}`

**New stores:**
- `CosmosMergedApiKeyHashStore.cs` — replaces `CosmosApiKeyHashStore`. Reads/writes `HashedSecret` from consolidated `MachineAccessEntity`. Falls back to old `akh.*` document format on read for backward compatibility during transition.
- `CosmosCertificateAuthorityStore.cs` — stores/retrieves CA certificates and PKCS#12 data. Uses `CreateItemAsync` with conflict catch for race-safe bootstrap.
- `CosmosMachineAuthenticationPolicyStore.cs` — reads `MachineAuthenticationPolicy` from `AccessPointEntity.MachineAuthentication`. Uses `IMemoryCache` with 60-second per-entry expiration.

**Updated `AccessPointEntity`** — added `MachineAuthentication` property.

**Updated `EntryPoint.cs`:**
- Replaced `CosmosApiKeyHashStore` registration with `CosmosMergedApiKeyHashStore`
- Added `CosmosCertificateAuthorityStore`, `CosmosMachineAuthenticationPolicyStore` registrations
- Added `AddMemoryCache()`
- Updated AutoMapper: `MachineAccessEntity -> MachineAccess` maps `CertificateThumbprint`; reverse map ignores `HashedSecret` and `ETag`

**Fixed `CosmosAccessService.CreateMachineAccessAsync`:**
1. Now persists `MachineAccessEntity` (with `PartitionKey`) instead of `MachineAccess` model (which had no `PartitionKey`, causing silent data loss)
2. Masks `AccessKey` defensively: `accessKey.Length >= 3 ? accessKey[..3] + "***" : "***"` (previously would throw on empty/short keys)

### 6. `MachineAuthenticationMiddleware` (replaces `ApiKeyAuthenticationMiddleware`)

File: `Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`

The middleware owns the complete machine authentication flow:

1. **Pass-through**: when neither certificate header nor `Authorization: ApiKey` is present, calls `next()` immediately — user/bearer requests are never evaluated
2. **Certificate extraction**: reads the configured header (default `X-ARR-ClientCert`); rejects if the header appears more than once (prevents header-append bypass)
3. **Credential validation**: calls `IClientCertificateValidator` and/or `IApiKeyValidator` as appropriate
4. **Identity reconciliation**:
   - Both cert + API key present with per-machine cert: triples (org, project, machineAccessId) must match exactly
   - Shared cert + API key: org/project must match; identity comes from API key
   - Shared cert alone: rejected (API key mandatory)
   - API key alone: identity from API key
5. **Policy check**: loads `MachineAuthenticationPolicy` from `IMachineAuthenticationPolicyStore` (cached), checks if presented credentials satisfy `ApiKey | Certificate | CertificateAndApiKey`
6. **Claims synthesis**: maps scope to role claims via `MachineScopeClaims`, adds `azp`/`sub` claims, carries `annotationKey` into claims, sets `FunctionContextItemKeys.MachineIdentity`

**`MachineScopeClaims.cs`** — extracted the scope-to-claims mapping (verbatim from old middleware) into a static helper.

### 7. `CheckScope` Logic Bug Fix

File: `Api.Functions/src/Security/HttpRequestDataExtensions.cs`

**Before (line 76):** `scopes.All(scope => !allScopes.Contains(scope))` — rejected only when **none** of the required scopes were present ("match any" semantics)

**After:** `scopes.Any(scope => !allScopes.Contains(scope))` — rejects when **any** required scope is missing ("match all" semantics)

### 8. API Endpoints

**`GET v1/access/certificate-authority`** — returns the CA public certificate as PEM. **Unauthenticated** — machines need it before they can authenticate.

**`PUT v1/access/points/{key}/machine-authentication`** — sets a project's `MachineAuthenticationPolicy`. Requires `User.Impersonation` scope and `Administrator` role.

### 9. Wiring Changes

**`Program.cs`:**
- Middleware: `ExceptionHandlingMiddleware` then `MachineAuthenticationMiddleware`
- Services: `AddApiKeyMachineAccess()` then `AddCertificateMachineAccess(context.Configuration)`

**`Api.Functions.csproj`** — added project reference to `Access.Certificates`

**`local.settings.json`** — added `MachineAuth:*` dev defaults (DefaultCredentialKind=ApiKey, IsCertificateHeaderFromClientAllowed=true for dev, TrustDomain=2s.platform, Authority:Kind=Cosmos)

**`Definitions.cs`** — new routes (`CertificateAuthority`, `MachineAuthentication`), route IDs, `SecuritySchemas.Mtls`

**`FunctionContextItemKeys.cs`** — added `MachineIdentity`

**`42.mono.slnx`** — added `Access.Certificates` project

**`Directory.Packages.props`** — added `Microsoft.Extensions.Caching.Memory` 10.0.5

---

## Files Created

```
src/Platform/Storyteller/Abstractions.Access/src/MachineCredentialKind.cs
src/Platform/Storyteller/Abstractions.Access/src/Model/MachineAuthenticationPolicy.cs

src/Platform/Storyteller/Backend.Core/src/Accessing/ICertificateAuthorityProvider.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/ICertificateAuthorityStore.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/IClientCertificateAuthority.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/IClientCertificateValidator.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/IClientCertificateStore.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/IMachineAuthenticationPolicyStore.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/MachineAuthenticationOptions.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/CertificateAuthorityOptions.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/CertificateAuthorityKind.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/ClientCertificateValidationResult.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/IssuedCertificate.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/CertificateAuthorityMaterial.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/ClientCertificateKind.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/CertificateRecord.cs

src/Libraries/Utils/Async/src/CachedAsync.cs

src/Platform/Storyteller/Access.Certificates/src/Access.Certificates.csproj
src/Platform/Storyteller/Access.Certificates/src/EntryPoint.cs
src/Platform/Storyteller/Access.Certificates/src/LocalCertificateAuthority.cs
src/Platform/Storyteller/Access.Certificates/src/LocalCertificateAuthorityProvider.cs
src/Platform/Storyteller/Access.Certificates/src/ClientCertificateValidator.cs
src/Platform/Storyteller/Access.Certificates/src/CertificateMachineAccessService.cs
src/Platform/Storyteller/Access.Certificates/src/PolicyAwareMachineAccessService.cs
src/Platform/Storyteller/Access.Certificates/src/CertificateIdentity.cs
src/Platform/Storyteller/Access.Certificates/src/ConfigurationCertificateAuthorityStore.cs
src/Platform/Storyteller/Access.Certificates/src/InMemoryClientCertificateStore.cs

src/Platform/Storyteller/Backend.CosmosDb/src/Entities/Access/CertificateAuthorityEntity.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Entities/Access/SharedCertificateEntity.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosMergedApiKeyHashStore.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosCertificateAuthorityStore.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosMachineAuthenticationPolicyStore.cs

src/Platform/Storyteller/Api.Functions/src/Security/MachineAuthenticationMiddleware.cs
src/Platform/Storyteller/Api.Functions/src/Security/MachineScopeClaims.cs
```

## Files Modified

```
src/Platform/Storyteller/Abstractions.Access/src/Model/IMachineAccess.cs
src/Platform/Storyteller/Abstractions.Access/src/Model/MachineAccess.cs
src/Platform/Storyteller/Abstractions.Access/src/Model/IAccessPoint.cs
src/Platform/Storyteller/Abstractions.Access/src/Model/AccessPoint.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/IMachineAccessService.cs
src/Platform/Storyteller/Backend.Core/src/Accessing/Model/MachineAccessCreate.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Entities/Access/MachineAccessEntity.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Entities/Access/AccessPointEntity.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosAccessService.cs
src/Platform/Storyteller/Backend.CosmosDb/src/EntryPoint.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Backend.CosmosDb.csproj
src/Platform/Storyteller/Api.Functions/src/Security/HttpRequestDataExtensions.cs
src/Platform/Storyteller/Api.Functions/src/Program.cs
src/Platform/Storyteller/Api.Functions/src/Api.Functions.csproj
src/Platform/Storyteller/Api.Functions/src/Definitions.cs
src/Platform/Storyteller/Api.Functions/src/FunctionContextItemKeys.cs
src/Platform/Storyteller/Api.Functions/src/V1/AccessHttp.cs
src/Platform/Storyteller/Api.Functions/src/local.settings.json
42.mono.slnx
Directory.Packages.props
```

## Implementation Notes for Phase B

### What Phase B Adds
`Access.Certificates.Azure.KeyVault` project with `KeyVaultSignatureGenerator` and `KeyVaultCertificateAuthorityProvider`. The CA private key lives as a non-exportable RSA-4096 key in Azure Key Vault; signing happens remotely via `CryptographyClient`.

### Key Interfaces Phase B Must Implement
- `ICertificateAuthorityProvider` (replaces the `LocalCertificateAuthorityProvider` registration)
- Phase B adds `AddKeyVaultCertificateAuthority(IConfiguration)` extension that overrides the `ICertificateAuthorityProvider` registered by `AddCertificateMachineAccess()`

### Key Dependencies Phase B Will Need
- `Azure.Security.KeyVault.Keys` (add to `Directory.Packages.props`)
- `Microsoft.Extensions.Azure` (already catalogued)
- The existing `AzureKeyVaults` configuration section from `Binding.Azure.KeyVault`

### Important Design Decisions Already Made
- `X509SubjectAlternativeNameExtension.EnumerateUris()` is NOT available on .NET 10.0.204 — `CertificateIdentity` uses manual ASN.1 parsing via `AsnReader` (context-specific tag 6 for URI SANs)
- `X509CertificateLoader.LoadPkcs12()` is used instead of the obsolete `new X509Certificate2(byte[])` constructors
- The `KeyVaultSignatureGenerator` is synchronous by contract (`SignData` method) — Azure SDK provides sync overloads for `CryptographyClient.Sign()`, so no sync-over-async
- A guard assertion must verify at construction time that the generator is never injected into the validator (hot path) — only into issuance/renewal services

### Configuration Section
```json
{
  "MachineAuth": {
    "Authority": {
      "Kind": "KeyVault",
      "KeyVault": {
        "VaultName": "default",
        "KeyName": "storyteller-machine-ca"
      }
    }
  }
}
```

### Enabling Key Vault in `Program.cs`
```csharp
services
    .AddCertificateMachineAccess(context.Configuration)      // local-key provider by default
    .AddKeyVaultCertificateAuthority(context.Configuration); // overrides to Key Vault
```
