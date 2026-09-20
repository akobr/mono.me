# Testing of mTLS — Unit Tests (2026-09-18)

## Overview

Unit test project `Access.Certificates.UnitTests` covering the mTLS certificate infrastructure implemented in Phases A–C. Uses **xUnit** + **Shouldly** + **Moq**, matching the conventions specified in the plan (Shouldly instead of FluentAssertions for new tests).

**Test results: 55 passed, 0 failed, 0 skipped.**

---

## Test Project

**`src/Platform/Storyteller/Access.Certificates/test/Access.Certificates.UnitTests.csproj`**
- `net10.0`, `IsTestProject=true`, `IsPackable=false`
- References: `Access.Certificates`, `Access.Certificates.Azure.KeyVault`
- Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Shouldly`, `Moq`

---

## Test Classes and Coverage

### 1. `CertificateIdentityTests` (7 tests)

Tests SPIFFE URI generation and SAN round-trip parsing.

| Test | What it verifies |
|---|---|
| `ToSpiffeUri_Machine_ProducesExpectedUri` | Machine SPIFFE URI format |
| `ToSpiffeUri_Shared_ProducesExpectedUri` | Shared SPIFFE URI format |
| `SanRoundTrip_Machine_ParsesCorrectly` | Issue a cert with machine SAN, parse it back — org, project, machineAccessId match |
| `SanRoundTrip_Shared_ParsesCorrectly` | Issue a cert with shared SAN, parse it back — org, project, label match |
| `TryParse_MissingSan_ReturnsNull` | Cert without SAN extension returns null |
| `TryParse_WrongTrustDomain_ReturnsNull` | Cert with different trust domain returns null |
| `IsValidSpiffePathSegment_*` | Validates `[a-zA-Z0-9._-]` regex (5 valid, 5 invalid via Theory) |

### 2. `LocalCertificateAuthorityTests` (7 tests)

Tests CA-issued leaf certificates.

| Test | What it verifies |
|---|---|
| `IssueCertificate_ProducesValidLeaf` | Non-empty PKCS#12, password, thumbprint, serial; valid time range |
| `IssueCertificate_LeafHasSanWithCorrectSpiffeUri` | SAN round-trips through PKCS#12 export/import |
| `IssueCertificate_LeafChainsToCA` | X509Chain validates leaf → CA with CustomRootTrust |
| `IssueCertificate_LeafHasClientAuthEku` | EKU contains OID `1.3.6.1.5.5.7.3.2` (clientAuth) |
| `IssueCertificate_ClampsLifetimeToCaValidity` | 3650-day request clamped to CA's ~365 remaining days |
| `IssueSharedCertificate_HasSharedSan` | Shared cert has `ClientCertificateKind.Shared` SAN |
| `IssueCertificate_Pkcs12CanBeLoaded` | PKCS#12 loads via `X509CertificateLoader.LoadPkcs12`, has private key |

### 3. `ClientCertificateValidatorTests` (10 tests)

Tests the certificate validation pipeline.

| Test | What it verifies |
|---|---|
| `ValidateAsync_ValidMachineCert_ReturnsResult` | Valid cert → organization, project, machineAccessId, scope in result |
| `ValidateAsync_ValidSharedCert_ReturnsSharedResult` | Shared cert → `ClientCertificateKind.Shared`, no machineAccessId |
| `ValidateAsync_ForeignCaCert_ReturnsNull` | Cert signed by a different CA is rejected |
| `ValidateAsync_ExpiredCert_ReturnsNull` | Cert past `NotAfter` is rejected |
| `ValidateAsync_MissingSan_ReturnsNull` | Cert without SAN is rejected |
| `ValidateAsync_ThumbprintMismatch_ReturnsNull` | Cert thumbprint doesn't match store record |
| `ValidateAsync_RevokedRecord_ReturnsNull` | Cert with `IsRevoked=true` in store is rejected |
| `ValidateAsync_NoRecord_ReturnsNull` | Machine cert with no store record is rejected |
| `ValidateAsync_PreviousThumbprintDuringOverlap_Succeeds` | Old cert accepted during overlap window |
| `ValidateAsync_PreviousThumbprintAfterOverlapExpiry_ReturnsNull` | Old cert rejected after overlap expires |
| `ValidateAsync_AnnotationKey_CarriedInResult` | AnnotationKey from store record appears in result |

### 4. `LocalCertificateAuthorityProviderTests` (4 tests)

Tests CA bootstrap and caching.

| Test | What it verifies |
|---|---|
| `GetActiveAsync_EmptyStore_BootstrapsCA` | Empty store → new CA created with expected subject |
| `GetActiveAsync_ExistingStore_LoadsWithoutBootstrap` | Second provider loads same CA (matching thumbprint) |
| `GetActiveAsync_AutoBootstrapDisabled_Throws` | `IsAutoBootstrapEnabled=false` with empty store → exception |
| `GetTrustedAsync_ReturnsAllCertificates` | Returns at least one certificate after bootstrap |

### 5. `KeyVaultSignatureGeneratorTests` (4 tests)

Tests the two-step self-signing dance and external generator behavior.

| Test | What it verifies |
|---|---|
| `GetSignatureAlgorithmIdentifier_MatchesLocalRsaGenerator` | All RSA PKCS1 SHA256 generators produce identical DER AlgorithmIdentifier |
| `SelfSignedCa_ViaLocalSimulation_VerifiesAgainstOwnPublicKey` | Two-step dance: `CertificateRequest(publicKey)` + `Create(issuerDn, generator)` produces valid self-signed CA with BasicConstraints CA=true |
| `LeafSignedByExternalGenerator_ChainsToSelfSignedCa` | Leaf signed by external generator chains to CA via X509Chain CustomRootTrust |
| `PublicKeyFromSubjectPublicKeyInfo_RoundTrips` | `RSA.ExportSubjectPublicKeyInfo()` → `PublicKey.CreateFromSubjectPublicKeyInfo()` round-trips |

### 6. `SharedCertificateServiceTests` (3 tests)

Tests shared certificate issuance and lifecycle.

| Test | What it verifies |
|---|---|
| `IssueAsync_ReturnsValidCertificate` | Returns certificate with label, thumbprint, PKCS#12, password; store called |
| `IssueAsync_DuplicateLabel_AppendsTimestamp` | Duplicate label → label starts with original + timestamp suffix |
| `RevokeAsync_DelegatesToStore` | Revocation delegates to store and returns result |

### 7. `PolicyAwareMachineAccessServiceTests` (5 tests)

Tests policy resolution and credential kind dispatch.

| Test | What it verifies |
|---|---|
| `CreateMachineAccess_DefaultApiKeyPolicy_UsesApiKeyKind` | Null policy → `DefaultCredentialKind` (ApiKey) |
| `CreateMachineAccess_CertificatePolicy_ReturnsCertificateKind` | Certificate policy → `Certificate` kind |
| `CreateMachineAccess_CertificateAndApiKeyPolicy_ReturnsCombinedKind` | CertificateAndApiKey policy → `CertificateAndApiKey` kind |
| `DefaultOptions_DefaultCredentialKind_IsApiKey` | Default config starts with ApiKey |
| `CertificateMachineAccessService_ExtendWithCertificate_PreservesExistingId` | `with` expression preserves Id/ObjectId when extending |

### 8. `CachedAsyncTests` (5 tests)

Tests the `CachedAsync<T>` caching primitive.

| Test | What it verifies |
|---|---|
| `GetValueAsync_FirstCall_InvokesFactory` | First call invokes factory exactly once |
| `GetValueAsync_WithinTtl_ReturnsCachedValue` | Second call within TTL returns same value without re-invoking factory |
| `GetValueAsync_AfterInvalidate_RefreshesValue` | `Invalidate()` forces re-evaluation on next access |
| `GetValueAsync_FactoryThrows_WithExistingValue_ReturnsStale` | Stale-on-error: factory failure returns previous value, reports error via callback |
| `GetValueAsync_FactoryThrows_WithNoExistingValue_Throws` | No stale value available → exception propagates |

---

## Test Helper

**`TestCertificateHelper.cs`** — shared utility for all test classes:
- `CreateCa(lifetimeYears)` — creates RSA-2048 CA (smaller than production 4096 for test speed) with BasicConstraints, KeyUsage, SKI
- `IssueMachineLeaf(...)` — issues leaf with machine SPIFFE URI SAN
- `IssueSharedLeaf(...)` — issues leaf with shared SPIFFE URI SAN
- `IssueLeafWithCustomSan(...)` — configurable: custom SAN URI, time range, EKU toggle
- `CreateCaMaterial()` — convenience for `CertificateAuthorityMaterial`
- `DefaultOptions` — standard `MachineAuthenticationOptions` for tests

---

## Files Created

```
src/Platform/Storyteller/Access.Certificates/test/Access.Certificates.UnitTests.csproj
src/Platform/Storyteller/Access.Certificates/test/GlobalUsings.cs
src/Platform/Storyteller/Access.Certificates/test/TestCertificateHelper.cs
src/Platform/Storyteller/Access.Certificates/test/CertificateIdentityTests.cs
src/Platform/Storyteller/Access.Certificates/test/LocalCertificateAuthorityTests.cs
src/Platform/Storyteller/Access.Certificates/test/ClientCertificateValidatorTests.cs
src/Platform/Storyteller/Access.Certificates/test/LocalCertificateAuthorityProviderTests.cs
src/Platform/Storyteller/Access.Certificates/test/KeyVaultSignatureGeneratorTests.cs
src/Platform/Storyteller/Access.Certificates/test/SharedCertificateServiceTests.cs
src/Platform/Storyteller/Access.Certificates/test/PolicyAwareMachineAccessServiceTests.cs
src/Platform/Storyteller/Access.Certificates/test/CachedAsyncTests.cs
```

## Files Modified

```
42.mono.slnx — added test project
```

---

## Coverage Summary

| Area | Tests | Status |
|---|---|---|
| SPIFFE URI SAN generation & parsing | 7 | All pass |
| CA bootstrap & leaf issuance | 7 | All pass |
| Certificate validation (chain, EKU, revocation, thumbprint overlap) | 10 | All pass |
| CA provider bootstrap & caching | 4 | All pass |
| Key Vault signature generator (two-step dance, AlgorithmIdentifier) | 4 | All pass |
| Shared certificate service (issue, duplicate label, revoke) | 3 | All pass |
| Policy-aware service (credential kind resolution) | 5 | All pass |
| CachedAsync (TTL, invalidation, stale-on-error) | 5 | All pass |
| **Total** | **55** | **All pass** |

---

## Not Covered (Deferred)

### Integration Tests
Full pipeline integration tests with `Testcontainers.CosmosDb` are deferred as they require Docker and a CosmosDB emulator. The plan describes an `Access.Certificates.IntegrationTests.csproj` covering:
- Full middleware pipeline tests (API key, certificate, combined, shared cert without key, duplicate header)
- Policy enforcement end-to-end
- CA endpoint unauthenticated access
- Renewal with real Cosmos (ETag contention, idempotency)

### CheckScope Tests
The `CheckScope` fix tests (match-all semantics, `App.` prefix stripping) would require referencing `Api.Functions` which is an exe project. These should be tested in a separate API-level test project.

### Middleware Policy-Matrix Tests
Full `MachineAuthenticationMiddleware` policy-matrix tests require the Azure Functions middleware infrastructure (`FunctionContext`, `HttpRequestData`). These are better suited for the integration test project.
