# Shared Certificate Control in the Machine Authentication Policy

## Overview

Shared certificates now follow the `CertificateAndApiKey` kind of the project's machine authentication policy:

- issuing one needs that kind to be enabled;
- one is accepted only with the API key of a `CertificateAndApiKey` machine while the project enables that kind;
- a new `CertificateAndApiKey` machine can use a shared certificate instead of getting its own.

The change covers the API, both SDKs, `sform` and the admin UI. It implements [Shared Certificate Control in the Machine Authentication Policy](../2026-10-14%20Shared%20Certificate%20Control%20in%20the%20Machine%20Authentication%20Policy.md) on top of PR #87 (Linear 424-14). It follows the shared certificate list cache and the rejection of unknown shared certificates, which were moved onto the same branch.

## What Was Done

### 1. Shared certificates belong to `CertificateAndApiKey`

`MachineAuthenticationPolicies.AllowsSharedCertificates(policy)` (`Backend.Core`) returns `policy.IsEnabled(CertificateAndApiKey)`. A legacy policy that holds only `CredentialKind` follows its one kind. As planned, `MachineAuthenticationPolicy` got no new property.

### 2. Authentication

`MachineAuthenticationMiddleware` resolves the machine kind as before. A shared certificate now also requires the kind to be `CertificateAndApiKey`; otherwise the call gets 401 and a warning naming the kind is logged. The check runs before the policy check and before `PresentingCertificateThumbprint` is set. So an `ApiKey` machine never gets a certificate thumbprint into the invocation, and it can no longer satisfy the mTLS rule of secret delivery with a shared certificate.

A machine without a stored kind still falls back to `CertificateAndApiKey` when it sends both credentials, and the policy then decides. A `CertificateAndApiKey` machine with its own certificate may still present a shared certificate instead, as documented.

### 3. Issuing shared certificates

`SharedCertificateService` now takes `IMachineAuthenticationPolicyStore`. `IssueAsync` reads the effective policy and throws `MachineAccessNotSupportedException` when the policy does not allow shared certificates. `CertificatesHttp.PostSharedCertificate` maps that exception to 400 with an `ErrorResponse`, and declares the 400 in its OpenAPI attributes. List and revoke are unchanged.

### 4. Machines that use a shared certificate

- `MachineAccessCreate.UseSharedCertificate` (`bool?`) was added, along with `MachineAccess.UsesSharedCertificate` (`bool`, also on `IMachineAccess`) and `MachineAccessEntity.UsesSharedCertificate`. The flag is written by `CosmosAccessService.CreateMachineAccessAsync` and read by `AccessMappings.ToMachineAccess`.
- `PolicyAwareMachineAccessService.CreateMachineAccessAsync` rejects `UseSharedCertificate` with any kind other than `CertificateAndApiKey`, including a project default of another kind, and clears the certificate lifetime when the flag is set.
- `CreateCertificateAndApiKeyAsync` returns the API-key machine with the `CertificateAndApiKey` kind and `UsesSharedCertificate = true`, without calling `ExtendWithCertificateAsync`. No certificate record is written. The API key's validation result carries `CertificateAndApiKey`, from the stored entity.
- Reset and delete need no change: reset renews the key, and delete's best-effort certificate revocation finds nothing.

### 5. OpenAPI and SDKs

The plan said to overwrite `open.api.v0.9.9.json`. The build now reports 0.9.11, so the document was re-exported as `open.api.v0.9.11.json` in `Sdk.NSwag/` and `sdk.typescript/`. The unreleased v0.9.9 files of this branch were renamed to it rather than kept next to it.

The NSwag client, the TypeScript SDK (`npm run generate`) and its `package.json` version (0.9.11) were regenerated. The NSwag README points at the new file.

### 6. `sform`

- `sform machine create -s|--shared-certificate` sends `UseSharedCertificate = true` with the kind `CertificateAndApiKey`, which `-s` implies.
- `-s` with another `-k` fails before calling the API, with `ERROR_INPUT_PARSING`.
- `--lifetime-days` and `--output` are ignored with a note.
- The output lists `UsesSharedCertificate` and says that the machine signs in with a shared certificate of the project (`sform account shared-cert`). The plan named the command `sform shared-cert`; it is a subcommand of `account`.

### 7. Admin UI

- **`MachineCreateDialog.vue`:**
  - for `CertificateAndApiKey`, a "Use a shared certificate of the project" checkbox appears; its help text says whether a certificate is issued;
  - when it is checked, the lifetime field is hidden and the request sends `UseSharedCertificate: true` with `CertificateLifetimeDays: null`;
  - the checkbox resets each time the dialog opens.
- **`OneTimeSecretDialog.vue`:** has a `sharedCertificate` flag (mapped by `machineSecret`) that shows an info alert in place of the certificate download.
- **`machines.vue`:** shows a "shared" badge in the certificate column.
- **`machines/[id].vue`:** shows "A shared certificate of the project".
- **`certificates.vue`:**
  - reads the project policy;
  - while the policy does not enable `CertificateAndApiKey`, shows "Shared certificates are disabled" and disables "Issue certificate";
  - while the policy loads, issuing stays enabled, because the API refuses it anyway.
- **`utils/machines.ts`:** has `allowsSharedCertificates`, and the `CertificateAndApiKey` description mentions shared certificates.
- **`mock/access-handlers.ts`:**
  - follows the API: the 400s, no certificate on create or reset of a shared-certificate machine, and no shared certificate issued without the kind;
  - the seed is unchanged, so the seeded `acme.billing` shared certificate shows as disabled until `CertificateAndApiKey` is enabled in Settings.

### 8. Tests

- **`Backend.Core`:** `AllowsSharedCertificates` for each kind set and for a legacy policy.
- **`Access.Certificates` unit tests:**
  - creation with a shared certificate, explicit or through the project default;
  - the flag rejected for `ApiKey`, `Certificate` and a `Certificate` default;
  - a `CertificateAndApiKey` machine without the flag still gets its own certificate;
  - issuing refused and revoking allowed without the kind.
- **`Access.Certificates` integration tests:**
  - the shared certificate tests enable `CertificateAndApiKey` for their project;
  - new tests cover issuing without the kind, and a shared-certificate machine stored in Cosmos without a certificate, whose API key validates as `CertificateAndApiKey`.
- **`Api.Functions` (`MachineAuthenticationMiddlewareTests`), with a shared certificate:**
  - accepted with a `CertificateAndApiKey` key;
  - refused with an `ApiKey` key, with no thumbprint recorded;
  - refused after the kind is disabled;
  - refused without a key;
  - the unknown-kind fallback follows the policy.
- **`Cli`:** `-s` with no kind and with `certificate-and-api-key`, `-s` with another kind, and no flag without `-s`.
- **Admin UI:**
  - unit tests for the helper, the secret mapping, the mock API rules, the dialog request with and without the checkbox, and the secret dialog note;
  - a Playwright flow (desktop and mobile): disabled Certificates page, then enabling `CertificateAndApiKey`, then creating a shared-certificate machine and finding the "shared" badge.

Results on 2026-10-14:

- **.NET:** the solution builds. All test projects pass (`dotnet test 42.mono.slnx -m:1`), including `Access.Certificates` unit tests (78) and integration tests (27), `Api.Functions` (322), `Backend.Core` (79) and `Cli` (178). The one failure, `KeyValueEndToEndTests.Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos`, was an SSL error from the Key Vault emulator. It passed when run again on its own.
- **TypeScript SDK:** 99 tests pass (1 skipped).
- **Admin UI:**
  - Vitest: 379 tests pass;
  - Playwright e2e: 123 pass (3 skipped);
  - visual suite in Docker: 52 pass, with no baseline changes.

### 9. Documentation

`docs/Platform/Storyteller/authentication.md` now covers:

- the rule that shared certificates follow `CertificateAndApiKey`, under the kind table;
- `UseSharedCertificate` when a machine is created;
- `sform machine create -s`;
- `UsesSharedCertificate` and the `CertificateThumbprint` exception in the `MachineAccess` table;
- the shared-certificate case of `CertificateAndApiKey` in "What each call does".

The admin UI has no user documentation beyond its specs.
