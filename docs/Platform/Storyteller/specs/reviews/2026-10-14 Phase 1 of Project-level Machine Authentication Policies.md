# Phase 1 of Project-level Machine Authentication Policies

## Overview

Phase 1 covers the API and the SDKs (sections 1–7) of [Project-level Machine Authentication Policies with Per-machine Selection](../2026-10-14%20Project-level%20Machine%20Authentication%20Policies%20with%20Per-machine%20Selection.md), Linear 424-14. A project's policy now lists the enabled machine credential kinds, a default kind and a default certificate lifetime. A new machine picks one of the enabled kinds, gets the project's default lifetime unless it asks for its own, and keeps its kind, lifetime and certificate expiry. Machine calls are checked against the machine's own kind, which the project must still enable. `GET v1/access/points/{key}/machine-authentication` is new, `PUT` validates and normalizes. The OpenAPI document was exported as `open.api.v0.9.2.json`, and both SDKs were regenerated. Phase 2 (sform and the admin UI) builds on them.

## What Was Done

### 1. Policy model

- `Abstractions.Access/src/Model/MachineAuthenticationPolicy.cs`: `EnabledCredentialKinds` (`IReadOnlyList<MachineCredentialKind>?`) is new. `CredentialKind` became `MachineCredentialKind?` and now means the default kind. Stored documents and old request bodies with only `CredentialKind` keep their meaning: that kind alone is enabled and is the default.

### 2. Effective policy

- `Backend.Core/src/Accessing/MachineAuthenticationPolicies.cs`: `Resolve`, `GetEffectiveAsync`, `Validate`, `Normalize`, `IsEnabled`, `IssuesCertificate` and `IsSatisfiedBy`, as planned.
- `Resolve` drops undefined kinds; when nothing is left it enables the deployment default. The default kind is the stored `CredentialKind` when it is enabled, else the first *listed* enabled kind (not the first in enum order), so an admin's ordering decides when no default was sent. The lifetime is clamped to the deployment bounds.
- `Normalize` stores the kinds distinct and in enum order and fills in the default. A missing lifetime stays null, so the project keeps following `MachineAuth:CertificateLifetimeDays`.
- Tests: `Backend.Core/test/MachineAuthenticationPoliciesTests.cs` (32 cases).

### 3. API

- `AccessHttp.GetMachineAuthentication` (`GET v1/access/points/{key}/machine-authentication`, `User.Impersonation`, Contributor). Without a policy store it resolves from the options only. An organization key returns 400.
- `AccessHttp.PutMachineAuthentication` validates (400 with `ErrorResponse`), stores `Normalize(...)` and returns the resolved policy. The key split moved to `TrySplitProjectKey`, shared by both.
- `AccessHttp` takes an optional `IOptions<MachineAuthenticationOptions>` as the last constructor parameter, so existing tests that construct it are unchanged.
- `Definitions.RouteIds.Access.GetMachineAuthentication` was added. `RouteTableTests.MachineAuthenticationRoutes_AreRegistered` covers both verbs. `MachineHttpTests` covers the defaults, a legacy policy, normalization on PUT, a rejected PUT and an organization key.

### 4. Creating a machine

- `MachineAccessCreate.CredentialKind` (`MachineCredentialKind?`) was added.
- `PolicyAwareMachineAccessService.CreateMachineAccessAsync` resolves the effective policy, picks `model.CredentialKind ?? policy.CredentialKind`, throws `MachineAccessNotSupportedException` for a kind that is not enabled (`PostMachines` maps it to 400), and fills `CertificateLifetimeDays` from the project only for kinds with a certificate. The old `ResolveCredentialKindAsync` was removed.
- **Fixed on the way:** the project's `CertificateLifetimeDays` existed before but was never read on the server; new certificates always got the deployment lifetime. It is applied now.
- Tests: `Access.Certificates/test/PolicyAwareCredentialKindTests.cs` builds the router with the real API key and certificate services over mocked stores and authority (default kind and lifetime, an enabled kind, `CertificateAndApiKey`, a requested lifetime, clamping, a kind that is not enabled, no stored policy, a legacy policy).

### 5. Lifetime and expiry on the machine

- `MachineAccess` / `IMachineAccess`: `CertificateLifetimeDays` and `CertificateNotAfter`.
- `CertificateMachineAccessService` returns both on create and extend.
- `MachineAccessEntity.CertificateLifetimeDays` is new. `CosmosAccessService.CreateMachineAccessAsync` writes it and `NotAfter`; `AccessMappings.ToMachineAccess` maps them back.
- `CosmosCertificateRenewalService` renews with the machine's lifetime, falling back to `MachineAuth:CertificateLifetimeDays`.
- **Fixed on the way:** `NotAfter` was never written when a machine was created, so `RenewAsync` answered "Machine access has no certificate expiry information" for every machine that had not been renewed by other means. It is written at creation now.

### 6. Enforcement by the machine's own kind

- `ApiKeyHashEntry`, `ApiKeyValidationResult`, `CertificateRecord` and `ClientCertificateValidationResult` got a trailing `MachineCredentialKind? CredentialKind = null`, so existing call sites and tests compile unchanged.
- `CosmosMergedApiKeyHashStore` returns the entity's kind; `ApiKeyMachineAccessService.ValidateAsync` and `ClientCertificateValidator` pass it on. `CertificateMachineAccessService` records `Certificate` or `CertificateAndApiKey` on the certificate record. The unused `CosmosApiKeyHashStore` was left as it is; it returns no kind, so the middleware falls back.
- `MachineAuthenticationMiddleware` resolves the machine kind (machine certificate, then API key, then the shape of the presented credentials) and returns 401 unless the kind is enabled and `IsSatisfiedBy` holds.
- **Deviation:** to unit test the middleware, `RespondUnauthorizedAsync` now goes through a `protected virtual AssignResponse`, the same seam `BearerAuthenticationMiddleware` has. `MachineAuthenticationMiddlewareTests` (10 cases) cover each kind, two kinds side by side, a disabled kind, a `CertificateAndApiKey` machine with only its key, a `ClientCredentials` project, the fallback and the deployment default.
- `MachineCredentialPolicy.EnsureAllowedAsync` accepts an identity provider token when `ClientCredentials` is enabled. The message now lists the enabled kinds. `MachineAccessRulesTests` gained a multi-kind accept and reject case.

### 7. OpenAPI document and SDKs

- **New (not in the spec):** `Api.Functions/src/OpenApi/Filters/MachineCredentialKindDocumentFilter.cs`. The OpenAPI extension writes the first enum value as `default` on every enum property. For the two nullable `CredentialKind` properties, NSwag turned that into `= ApiKey` initializers, so a C# client that left the kind empty would have asked for `ApiKey` instead of the project default (and got 400 in a project without `ApiKey`). The filter removes those two defaults and lets the items of `EnabledCredentialKinds` reference a shared `MachineCredentialKind` schema, so NSwag generates `ICollection<MachineCredentialKind>` instead of an enum named `EnabledCredentialKinds`. Tested in `MachineCredentialKindDocumentFilterTests`.
- **Export.** As in the binding catalog work, the Chocolatey `func` cannot start the .NET 10 worker. Rider's core tools (`AzureFunctionsTools/Releases/4.109.0/cli_x64/func.exe`) served a temporary build on port 7072. `/api/openapi/v3.json` was saved after the API commit, so the document reports `0.9.2.24105`; `servers` was set back to `http://localhost:7071/api`. The diff against `open.api.v0.9.1.json` contains only this change.
- **NSwag:** `ApiSdk.g.cs` regenerated; the README points to `open.api.v0.9.2.json`. New: `GetMachineAuthenticationAsync`, `MachineCredentialKind`, `MachineAccessCreateCredentialKind`, nullable `MachineAuthenticationPolicy.CredentialKind`.
- **TypeScript SDK:** `npm run generate` set the package version to `0.9.2`. New: `getMachineAuthentication`, `useGetMachineAuthentication`, the MSW handler, `MachineCredentialKind` and `MachineAccessCreateCredentialKind`. Every generated file also changed its `OpenAPI spec version` header. `npm test` (59 passed, 1 skipped), `typecheck` and `build` pass.
- **Merge note:** the unmerged binding catalog branch (`feat/424-10`) also adds `open.api.v0.9.2.json`. Whichever branch merges second has to export the document again and regenerate both SDKs.

### Documentation

- `docs/Platform/Storyteller/authentication.md`: a new "Machine authentication policy" section (kinds, properties, GET/PUT and validation, creating a machine, enforcement), the client-credentials and scale sections now speak of enabled kinds, the `MachineAccess` table has the two new properties, and the role summary says reading the policy needs `Contributor`. The spec had this in phase 2; it was moved here because it describes the API.

### Tests run

| Project | Result |
|---|---|
| `Backend.Core/test` | 66 pass |
| `Access.Certificates/test` | 68 pass |
| `Api.Functions/test` | 199 pass |
| `Access.AuthKit/test`, `Access.AzureAd/test`, `Access.Keycloak/test` | 132, 55, 24 pass |
| `Cli/test` | 110 pass (unchanged commands against the regenerated SDK) |
| `Backend.CosmosDb/test` | 72 pass; 191 Testcontainers tests could not run (Docker was not running) |
| `Access.Certificates/testint` | builds; not run (needs Docker). New: `SetPolicy_WithEnabledKinds_IsReadBackFromCosmos` (reads through an empty cache) and `MachinesOfTheEnabledKinds_KeepTheirKindLifetimeAndExpiry`. |

The Cosmos round trip of `EnabledCredentialKinds` and of the new entity fields is therefore covered by tests that have not run yet.
