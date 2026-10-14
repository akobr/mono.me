# Shared Certificate Control in the Machine Authentication Policy

## Problem

The project-level machine authentication policy (`EnabledCredentialKinds`, Linear 424-14) controls which credential kinds a project's machines may use, but it does not cover shared certificates:

- **Shared certificates ignore the policy.** In `MachineAuthenticationMiddleware`, a shared certificate gives the machine no credential kind. The kind comes from the API key presented with it. An `ApiKey` machine that sends any shared certificate of its project therefore passes a project that enables only `ApiKey`. Disabling `Certificate` and `CertificateAndApiKey` does not stop shared certificates.
- **They count as mTLS for secret delivery.** The middleware records the thumbprint of a validated shared certificate (`PresentingCertificateThumbprint`). An `ApiKey` machine with a secrets scope can therefore satisfy `RequireMtlsForSecretDelivery` with a certificate that does not identify it.
- **Issuance ignores the policy.** `POST …/access/certificates` checks only the Administrator role, so a project without any certificate kind can still issue shared certificates.
- **A machine cannot be created for a shared certificate.** `CertificateAndApiKey` always issues a machine certificate. A machine meant to use the project's shared certificate still gets its own certificate, which is never used and still expires and shows up for renewal.

## Current State

- `src/Platform/Storyteller/Abstractions.Access/src/Model/MachineAuthenticationPolicy.cs` holds `EnabledCredentialKinds`, `CredentialKind` and `CertificateLifetimeDays`, with no shared-certificate setting.
- `src/Platform/Storyteller/Backend.Core/src/Accessing/MachineAuthenticationPolicies.cs` provides `Resolve`, `IsEnabled` and `IsSatisfiedBy(kind, hasCertificate, hasApiKey)`. `CertificateAndApiKey` is satisfied by any validated certificate together with the key.
- `src/Platform/Storyteller/Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`:
  - rejects a shared certificate without an API key;
  - with a key, takes the identity from the key, and the kind from `apiKeyResult.CredentialKind` (`ResolveMachineKind`);
  - then checks `policy.IsEnabled(kind) && kind.IsSatisfiedBy(...)`.
- `src/Platform/Storyteller/Access.Certificates/src/SharedCertificateService.cs`: `IssueAsync` does not read the policy.
- `src/Platform/Storyteller/Access.Certificates/src/PolicyAwareMachineAccessService.cs`: `CreateCertificateAndApiKeyAsync` creates the API key, then always calls `ExtendWithCertificateAsync`.
- `MachineAccessCreate` (`Backend.Core/src/Accessing/Model`), `MachineAccess` (`Abstractions.Access/src/Model`) and `MachineAccessEntity` (`Backend.CosmosDb/src/Entities/Access`) have no notion of a shared certificate.
- Admin UI:
  - `components/access/MachineCreateDialog.vue` selects the kind and the lifetime;
  - `pages/[org]/[project]/certificates.vue` always offers "Issue certificate".
- `sform machine create` has `--credential-kind` and `--lifetime-days`.

## Proposed Changes

### 1. Shared certificates belong to `CertificateAndApiKey`

A shared certificate is only ever half of a "certificate and API key" login, so the existing kind is the switch and the policy gets no new property:

- **Allowed:** a project allows shared certificates exactly when `EnabledCredentialKinds` contains `CertificateAndApiKey`.
- **New helper:** `MachineAuthenticationPolicies.AllowsSharedCertificates(this MachineAuthenticationPolicy policy)` returns `policy.IsEnabled(MachineCredentialKind.CertificateAndApiKey)`.

The rejected alternative was a separate `AllowSharedCertificates` flag. It would allow combinations that make no sense (shared certificates without `CertificateAndApiKey`), and it would need validation, migration and UI of its own.

### 2. Authentication

In `MachineAuthenticationMiddleware`, after the kind is resolved:

- **Kind required:** when the presented certificate is a shared one, the machine's kind must be `CertificateAndApiKey`. Otherwise the call gets 401 with a warning logged ("Shared certificate presented by a {kind} machine").
- **Policy check:** the existing check `policy.IsEnabled(kind)` then makes the shared certificate follow the project policy. Disabling `CertificateAndApiKey` stops every shared-certificate login at once, with no revocation needed.
- **Old machines:** a machine whose stored kind is unknown (created before kinds were recorded) keeps the current fallback. Its credentials form `CertificateAndApiKey`, which is then checked against the policy.
- **Breaking:** an `ApiKey` machine that sends a shared certificate with its key is now rejected. Before, the certificate was silently accepted. This also closes the secret-delivery gap above, because an `ApiKey` machine never reaches the point where `PresentingCertificateThumbprint` is set.
- **Unchanged:** a `CertificateAndApiKey` machine that has its own certificate may still present a shared certificate of the project instead (documented as "its own, or a shared certificate of the project"). Binding such machines to their own certificate is left for later.

### 3. Issuing shared certificates

- `SharedCertificateService.IssueAsync` reads the effective policy (`IMachineAuthenticationPolicyStore.GetEffectiveAsync`). When the policy does not allow shared certificates, it throws `MachineAccessNotSupportedException` ("The project {org}.{project} does not enable CertificateAndApiKey, which shared certificates belong to.").
- `CertificatesHttp.PostSharedCertificate` maps that exception to 400 with an `ErrorResponse`.
- Listing and revoking stay allowed in every policy, so an administrator can still clean up after disabling the kind.

### 4. Machines that use a shared certificate

- `MachineAccessCreate.UseSharedCertificate` (`bool?`): only with `CredentialKind` `CertificateAndApiKey`, given explicitly or as the project default. Any other kind with `UseSharedCertificate: true` gets 400 ("Only CertificateAndApiKey machines can use a shared certificate.").
- `PolicyAwareMachineAccessService.CreateCertificateAndApiKeyAsync` creates only the API key when `UseSharedCertificate` is true. The result has:
  - `CredentialKind = CertificateAndApiKey`;
  - `UsesSharedCertificate = true`;
  - no certificate, password, thumbprint, lifetime or expiry. `CertificateLifetimeDays` in the request is ignored and stored as null.
- `MachineAccess.UsesSharedCertificate` (`bool`) is stored on `MachineAccessEntity` and mapped back by `AccessMappings.ToMachineAccess`. Older documents read as `false`.
- At login, such a machine presents a shared certificate of its project and its API key. Its key alone fails (`IsSatisfiedBy` needs a certificate). It has no own certificate, so renewal returns "not found" as for any machine without one.
- **Reset:** reset renews the API key, as for every `CertificateAndApiKey` machine.
- **Delete:** delete removes the key. The best-effort revocation of a machine certificate finds nothing.

### 5. OpenAPI and SDKs

- The new properties `MachineAccessCreate.UseSharedCertificate` and `MachineAccess.UsesSharedCertificate` appear in a re-exported `open.api.v0.9.9.json`. That version is not released yet; it was introduced by this PR.
- The NSwag client (`Sdk.NSwag/src/ApiSdk.g.cs`) and the TypeScript SDK (`npm run generate`) are regenerated from it.

### 6. `sform`

- `sform machine create --shared-certificate` (`-s`) sends `UseSharedCertificate = true`. The kind defaults to `CertificateAndApiKey` when `--credential-kind` is not given, and the command fails before calling the API when another kind is given. `--lifetime-days` and `--output` are ignored, with a note.
- The command output says that the machine has no certificate of its own and must present one of the project's shared certificates (`sform shared-cert list`).

### 7. Admin UI

- **`MachineCreateDialog.vue`:**
  - with `CertificateAndApiKey` selected, a checkbox "Use a shared certificate of the project" appears;
  - when it is checked, the lifetime field is hidden and the request sends `UseSharedCertificate: true` with no lifetime;
  - the help text says that no certificate is issued for the machine.
- **One-time secret dialog:** after creating such a machine, it shows the API key and a note to use a shared certificate from the Certificates page.
- **Machines list and detail:** a shared-certificate machine shows "Shared certificate" in the certificate column and field instead of "–".
- **`certificates.vue`:** when the project policy does not enable `CertificateAndApiKey`, an info alert says that shared certificates are disabled by the machine authentication policy and that existing ones no longer authenticate. "Issue certificate" is then disabled. Listing and revoking stay available.
- **`utils/machines.ts`:** the `CertificateAndApiKey` description mentions shared certificates.
- **MSW mock** (`mock/access-handlers.ts`):
  - machine creation honours `UseSharedCertificate`, with the same 400 for other kinds;
  - issuing a shared certificate returns 400 when the mock policy does not enable `CertificateAndApiKey`.

### 8. Tests

- **`Backend.Core`:** `AllowsSharedCertificates` for each kind set.
- **`Access.Certificates` unit tests:**
  - `PolicyAwareMachineAccessService`: a shared-certificate machine gets no certificate, other kinds reject the flag, and the project default `CertificateAndApiKey` accepts it;
  - `SharedCertificateService`: issue is refused when the policy does not enable `CertificateAndApiKey`.
- **`Api.Functions`** (`MachineAuthenticationMiddlewareTests`), with a shared certificate:
  - with the key of an `ApiKey` machine: 401;
  - with the key of a `CertificateAndApiKey` machine while the project enables it: accepted;
  - with the same key after the project disables it: 401;
  - with no key: 401, unchanged.
- **Admin UI:**
  - unit tests for the dialog request;
  - an e2e flow that creates a shared-certificate machine and finds "Issue certificate" disabled when the policy lacks `CertificateAndApiKey`.

### 9. Documentation

- **`docs/Platform/Storyteller/authentication.md`:** the table of credential kinds, the shared certificate paragraph, `UseSharedCertificate` and `UsesSharedCertificate` in the machine field table, and the rule that shared certificates follow `CertificateAndApiKey`.
- **The `sform` and admin UI docs**, where they describe machine creation and certificates.
