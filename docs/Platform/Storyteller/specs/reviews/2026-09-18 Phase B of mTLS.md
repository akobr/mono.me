# Phase B of mTLS — Key Vault CA (Completed 2026-09-18)

## Overview

Phase B adds production-grade CA key storage by introducing the `Access.Certificates.Azure.KeyVault` project. The CA private key lives as a non-exportable RSA-4096 key in Azure Key Vault; signing happens remotely via `CryptographyClient`. This means compromise of the app or database no longer yields the CA key — an attacker can mint certificates only while they hold the app identity, and every signature is logged by Key Vault.

The full plan is described in `2026-09-16 mTLS Per-Machine Authentication for Storyteller API.md`.

Phase A summary: `2026-09-17 Phase A of mTLS.md`.

---

## What Was Done

### 1. Extended `ICertificateAuthorityStore` Interface

**`Backend.Core/src/Accessing/ICertificateAuthorityStore.cs`** — added two methods:
- `GetActiveRecordAsync()` → `CertificateAuthorityRecord?` — returns the active CA record with certificate data, optional PKCS#12, optional Key Vault key identifier, and version
- `StoreCertificateAsync(byte[] certificateData, string version, string? keyVaultKeyIdentifier)` — stores a CA certificate without PKCS#12 data (for Key Vault CA where the private key never leaves the vault)

**`Backend.Core/src/Accessing/Model/CertificateAuthorityRecord.cs`** — new record type:
```csharp
public record CertificateAuthorityRecord(
    byte[] CertificateData,
    byte[]? Pkcs12Data,
    string? KeyVaultKeyIdentifier,
    string Version);
```

### 2. `KeyVaultSignatureGenerator`

**`Access.Certificates.Azure.KeyVault/src/KeyVaultSignatureGenerator.cs`**

Extends `X509SignatureGenerator` (~40 lines of logic):

- **`GetSignatureAlgorithmIdentifier(hash)`** — returns DER-encoded `AlgorithmIdentifier` bytes by delegating to a throwaway `X509SignatureGenerator.CreateForRSA(RSA.Create(2048), RSASignaturePadding.Pkcs1)` instead of hand-rolling ASN.1
- **`SignData(data, hash)`** — hashes locally via `IncrementalHash`, then calls `CryptographyClient.Sign()` with `RS256`/`RS384`/`RS512`. Synchronous by contract; the Azure SDK provides sync overloads, so no sync-over-async
- **`BuildPublicKey()`** — returns the cached `PublicKey` from the Key Vault key

The generator must only be used on the issuance/renewal path — never on the validation hot path. The validator uses only the CA's cached public certificate.

### 3. `KeyVaultCertificateAuthorityProvider`

**`Access.Certificates.Azure.KeyVault/src/KeyVaultCertificateAuthorityProvider.cs`**

Implements `ICertificateAuthorityProvider`. Uses `CachedAsync<T>` with configurable refresh interval (same pattern as `LocalCertificateAuthorityProvider`).

**Loading flow:**
1. Read active CA record from `ICertificateAuthorityStore` via `GetActiveRecordAsync()`
2. If found with `KeyVaultKeyIdentifier`: parse the key identifier URI via `KeyVaultKeyIdentifier`, create `CryptographyClient` via `KeyClient.GetCryptographyClient(name, version)`, wrap in `KeyVaultSignatureGenerator`, return `CertificateAuthorityMaterial`

**Auto-bootstrap flow:**
1. Create RSA-4096 key in Key Vault via `KeyClient.CreateRsaKeyAsync()` (non-exportable, Sign+Verify operations)
2. Build `PublicKey` from the key's `JsonWebKey.ToRSA()` → `ExportSubjectPublicKeyInfo()` → `PublicKey.CreateFromSubjectPublicKeyInfo()`
3. Create `CryptographyClient` via `KeyClient.GetCryptographyClient(keyName, version)`
4. Build `CertificateRequest` with CA extensions (BasicConstraints CA, KeyCertSign+CrlSign, SKI)
5. **Two-step self-signing dance**: `request.Create(subjectDn, generator, notBefore, notAfter, serial)` — using the same subject DN as both subject and issuer, signed by `KeyVaultSignatureGenerator`. This produces a valid self-signed certificate because the issuer DN equals the subject DN and the signature is made by the subject's own key (held in Key Vault)
6. Store certificate + key identifier in Cosmos via `StoreCertificateAsync()` (conditional create — may lose to a race)
7. **Always re-read from store** after the store attempt to get the definitive record (ours or the winner's). This ensures all instances use the key version that matches the stored CA certificate

**Race handling:**
- Multiple instances may create keys in Key Vault (each creates a new version)
- Only one stores the CA certificate in Cosmos (conditional create)
- Losers read the winner's record
- Orphaned key versions are harmless and, on Standard, not billed
- The re-read-after-store pattern ensures correctness without returning mismatched material

**Constructor warning:**
- Logs a warning if `MachineAuth:Authority:Kind` is not `KeyVault`, so the DI switch and the config switch cannot disagree silently

### 4. `KeyVaultCertificateAuthorityOptions`

**`Access.Certificates.Azure.KeyVault/src/KeyVaultCertificateAuthorityOptions.cs`**

```csharp
public class KeyVaultCertificateAuthorityOptions
{
    public string VaultName { get; set; } = "default";   // key into AzureKeyVaults config section
    public string KeyName { get; set; } = "storyteller-machine-ca";
}
```

Bound from `MachineAuth:Authority:KeyVault` configuration section.

### 5. Entry Point

**`Access.Certificates.Azure.KeyVault/src/EntryPoint.cs`** — `AddKeyVaultCertificateAuthority(IConfiguration)`:

- Binds `KeyVaultCertificateAuthorityOptions` from `MachineAuth:Authority:KeyVault`
- Registers a named `KeyClient` via `AddAzureClients` using the vault URI from `AzureKeyVaults:{VaultName}`
- Replaces the `ICertificateAuthorityProvider` registration (overrides `LocalCertificateAuthorityProvider` from Phase A)

### 6. Store Implementations Updated

**`CosmosCertificateAuthorityStore`** — implemented `GetActiveRecordAsync()` and `StoreCertificateAsync()`:
- `GetActiveRecordAsync()`: queries for the active CA entity, returns `CertificateAuthorityRecord` with all fields including `KeyVaultKeyIdentifier`
- `StoreCertificateAsync()`: creates entity with `Pkcs12Data = null` and the given `KeyVaultKeyIdentifier`, uses `CreateItemAsync` with conflict catch for race safety

**`ConfigurationCertificateAuthorityStore`** (in-memory) — implemented both new methods:
- Supports both PKCS#12 and certificate-only storage
- Fixed obsolete `new X509Certificate2(byte[])` → `X509CertificateLoader.LoadCertificate()`

### 7. Wiring Changes

**`Program.cs`** — added commented-out line for production Key Vault override:
```csharp
services.AddCertificateMachineAccess(context.Configuration);
// Override to Key Vault CA in production (comment out for local dev)
//services.AddKeyVaultCertificateAuthority(context.Configuration);
```

**`Api.Functions.csproj`** — added project reference to `Access.Certificates.Azure.KeyVault`

**`42.mono.slnx`** — added `Access.Certificates.Azure.KeyVault` project

**`Directory.Packages.props`** — added `Azure.Security.KeyVault.Keys` 4.7.0

---

## Configuration

### Key Vault CA (production)

```json
{
  "AzureKeyVaults": {
    "default": "https://my-vault.vault.azure.net/"
  },
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

### Enabling in `Program.cs`

```csharp
services
    .AddCertificateMachineAccess(context.Configuration)      // local-key provider by default
    .AddKeyVaultCertificateAuthority(context.Configuration); // overrides to Key Vault
```

### Required Azure Permissions

- **Key Vault Crypto Officer** — needed for auto-bootstrap (create key)
- Can be downgraded to **Key Vault Crypto User** (sign only) after first successful bootstrap

---

## Files Created

```
src/Platform/Storyteller/Access.Certificates.Azure.KeyVault/src/Access.Certificates.Azure.KeyVault.csproj
src/Platform/Storyteller/Access.Certificates.Azure.KeyVault/src/KeyVaultSignatureGenerator.cs
src/Platform/Storyteller/Access.Certificates.Azure.KeyVault/src/KeyVaultCertificateAuthorityProvider.cs
src/Platform/Storyteller/Access.Certificates.Azure.KeyVault/src/KeyVaultCertificateAuthorityOptions.cs
src/Platform/Storyteller/Access.Certificates.Azure.KeyVault/src/EntryPoint.cs

src/Platform/Storyteller/Backend.Core/src/Accessing/Model/CertificateAuthorityRecord.cs
```

## Files Modified

```
src/Platform/Storyteller/Backend.Core/src/Accessing/ICertificateAuthorityStore.cs
src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosCertificateAuthorityStore.cs
src/Platform/Storyteller/Access.Certificates/src/ConfigurationCertificateAuthorityStore.cs
src/Platform/Storyteller/Api.Functions/src/Program.cs
src/Platform/Storyteller/Api.Functions/src/Api.Functions.csproj
42.mono.slnx
Directory.Packages.props
```

---

## Architecture

### Key Vault is Never on the Hot Path

Validating a request needs only the CA's **public** certificate, which is cached via `CachedAsync<T>`. A Key Vault outage blocks issuing and renewing machines but never breaks authentication for machines that already hold a certificate.

### Two Provider Families

Selected by `MachineAuth:Authority:Kind`:

| Kind | Provider | Key Storage | Signing |
|---|---|---|---|
| `Cosmos` | `LocalCertificateAuthorityProvider` | PKCS#12 in Cosmos | In-process `CreateForRSA` |
| `Configuration` | `LocalCertificateAuthorityProvider` | In-memory | In-process `CreateForRSA` |
| `KeyVault` | `KeyVaultCertificateAuthorityProvider` | Non-exportable key in Key Vault | Remote via `CryptographyClient` |

### DI Registration Order

```
AddApiKeyMachineAccess()                    // registers ApiKeyMachineAccessService
AddCertificateMachineAccess(config)         // registers LocalCertificateAuthorityProvider (default)
AddKeyVaultCertificateAuthority(config)     // replaces with KeyVaultCertificateAuthorityProvider
AddAzureKeyVaultBindings(config, env)       // registers SecretClient (credential shared via AddAzureClients)
```

### Entity: `CertificateAuthorityEntity`

Already has `KeyVaultKeyIdentifier` field (added in Phase A anticipating Phase B):
- For `Cosmos`/`Configuration` kinds: `Pkcs12Data` is populated, `KeyVaultKeyIdentifier` is `null`
- For `KeyVault` kind: `Pkcs12Data` is `null`, `KeyVaultKeyIdentifier` stores the full versioned key URI (e.g., `https://vault.vault.azure.net/keys/storyteller-machine-ca/abc123`)

---

## Implementation Notes for Phase C

### What Phase C Adds

Self-service renewal endpoint with ETag-guarded idempotency, plus issue/list/revoke for project-scoped shared certificates.

### Key Endpoints Phase C Must Implement

- `POST v1/{organization}/{project}/access/machines/{id}/certificate/renew` — self-service renewal, authenticated by the machine's own still-valid certificate
- `GET|POST v1/{organization}/{project}/access/certificates` — list/issue shared certificates
- `DELETE v1/{organization}/{project}/access/certificates/{thumbprint}` — revoke shared certificate

### Key Concepts for Phase C

- **Renewal idempotency**: ETag-guarded compare-and-swap on `MachineAccessEntity`. Exactly one caller wins the race. Subsequent callers presenting a certificate whose thumbprint matches `PreviousThumbprint` get an "already renewed" response
- **Renewal window**: `RenewalWindowDays` (30) — how early before expiry a machine can initiate renewal
- **Overlap window**: `RenewalOverlapDays` (7) — how long after renewal the old certificate is still accepted via `PreviousThumbprint`/`PreviousValidUntil`
- **Shared certificates**: stored in `SharedCertificateEntity`, project-scoped, label uniqueness enforced by appending `-YYYY-MM-DD-HH-mm-ss` on duplicate
- **No CRL/OCSP**: revocation is record-based (flag in Cosmos), takes effect on the next request

### Key Dependencies Phase C Will Need

- The `IClientCertificateStore` implementations (Cosmos) for storing certificate records
- ETag support on `MachineAccessEntity` (already has `ETag` property from Phase A)
- `CosmosAccessService` changes for renewal endpoint wiring
