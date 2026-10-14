# Project-level Machine Authentication Policies with Per-machine Selection

Linear: [424-14](https://linear.app/424fun/issue/424-14/project-level-machine-authentication-policies-with-per-machine).

## Problem

A project has exactly one machine credential kind. Every new machine gets that kind, and every machine call has to satisfy it, so a project cannot run an API-key service next to an mTLS one. Changing the kind locks out every machine created with the previous one.

The issue asks for:

- project settings that only say **which** machine authentication policies (credential kinds) are enabled;
- each machine picks one of the enabled kinds when it is created;
- a **default certificate lifetime** in the project settings, preselected whenever a machine with a certificate is created.

## Current State

- **Model.** `MachineAuthenticationPolicy` (`Abstractions.Access/src/Model/MachineAuthenticationPolicy.cs`) has `CredentialKind` and `CertificateLifetimeDays`. It is stored on the access point document (`AccessPointEntity.MachineAuthentication`) by `CosmosMachineAuthenticationPolicyStore`, cached for 60 seconds. Without a stored policy the deployment default `MachineAuth:DefaultCredentialKind` applies.
- **Setting the policy.** `PUT v1/access/points/{key}/machine-authentication` (`AccessHttp.PutMachineAuthentication`, Administrator) stores the body as it is, without validation. There is no GET; the policy is read from `GET v1/access/points/{key}` (`AccessPoint.MachineAuthentication`), which needs Administrator.
- **Creating a machine.** `PolicyAwareMachineAccessService.CreateMachineAccessAsync` (`Access.Certificates`) reads the project kind and routes to the API key, certificate, certificate-and-API-key or identity provider service. `MachineAccessCreate` has no kind. The created `MachineAccess.CredentialKind` is stored on `MachineAccessEntity` and kept when the policy changes; reset and delete follow it.
- **Certificate lifetime.** `CertificateMachineAccessService.ResolveLifetimeDays` uses `MachineAccessCreate.CertificateLifetimeDays ?? MachineAuth:CertificateLifetimeDays`, clamped to `MinCertificateLifetimeDays..MaxCertificateLifetimeDays`. **The project's `CertificateLifetimeDays` is never read on the server.** Renewal (`CosmosCertificateRenewalService`) always uses `MachineAuth:CertificateLifetimeDays`. `CosmosAccessService.CreateMachineAccessAsync` does not write `NotAfter` onto `MachineAccessEntity`, so the entity of a new machine has no expiry.
- **Enforcement.**
  - `MachineAuthenticationMiddleware` validates the presented API key and/or certificate, then compares them with the **project** kind (`ApiKey` → key, `Certificate` → certificate, `CertificateAndApiKey` → both, `ClientCredentials` → never). The machine's own kind is not consulted.
  - `MachineCredentialPolicy.EnsureAllowedAsync` accepts an identity provider machine token only when the project kind is `ClientCredentials`.
  - `ApiKeyValidationResult`, `ApiKeyHashEntry`, `CertificateRecord` and `ClientCertificateValidationResult` carry no credential kind.
- **sform.** `sform machine machine-auth --credential-kind <kind> [--lifetime-days n]` sets the single kind. `sform machine create [--lifetime-days n]` cannot choose a kind.
- **Admin UI.**
  - `settings.vue` has a single "Credential kind" select and a lifetime (1–825 days) shown for certificate kinds.
  - `MachineCreateDialog.vue` gets `issuesCertificates` from `machines.vue`, which reads the policy through `useGetAccessPoint` (Administrator only, so a Contributor never sees the lifetime field). The lifetime input always starts at 90.
  - `certificateDaysLeft` estimates the expiry from `LastRenewalAt` plus the project lifetime.
- **Docs.** `docs/Platform/Storyteller/authentication.md` describes one kind per project ("Projects with the `ClientCredentials` policy in turn reject API keys and certificates", "`CredentialKind` … taken from the project policy at creation").

## Proposed Changes

### 1. Policy model (`Abstractions.Access`)

```csharp
public record MachineAuthenticationPolicy
{
    // Kinds the machines of the project may use. Null or empty: only CredentialKind (policies stored before 424-14).
    public IReadOnlyList<MachineCredentialKind>? EnabledCredentialKinds { get; init; }

    // Default kind: preselected for a new machine and used when a create request names none. One of the enabled kinds.
    public MachineCredentialKind? CredentialKind { get; init; }

    // Default certificate lifetime of new machines, in days. Null: MachineAuth:CertificateLifetimeDays.
    public int? CertificateLifetimeDays { get; init; }
}
```

`CredentialKind` keeps its name and wire format, so stored documents and old clients (`{ "CredentialKind": "Certificate" }`) still mean "only `Certificate`, and it is the default". It becomes nullable, so a new client can send only the enabled kinds.

### 2. Effective policy (`Backend.Core/Accessing/MachineAuthenticationPolicies.cs`)

A static helper used by the API, the router and the middleware:

```csharp
public static class MachineAuthenticationPolicies
{
    // The stored policy, or the deployment default without one, with every property filled:
    // EnabledCredentialKinds (distinct, in enum order), CredentialKind (an enabled kind) and
    // CertificateLifetimeDays (clamped to the deployment's min/max).
    public static MachineAuthenticationPolicy Resolve(MachineAuthenticationPolicy? stored, MachineAuthenticationOptions options);

    public static Task<MachineAuthenticationPolicy> GetEffectiveAsync(
        this IMachineAuthenticationPolicyStore store, string organization, string project, MachineAuthenticationOptions options);

    // Null when valid, else a message for 400.
    public static string? Validate(MachineAuthenticationPolicy requested, MachineAuthenticationOptions options);

    // The requested policy as it is stored: kinds distinct and in enum order, the default filled in.
    public static MachineAuthenticationPolicy Normalize(MachineAuthenticationPolicy requested);

    public static bool IsEnabled(this MachineAuthenticationPolicy effective, MachineCredentialKind kind);

    public static bool IssuesCertificate(this MachineCredentialKind kind); // Certificate, CertificateAndApiKey

    // Whether the presented credentials are what a machine of this kind must send.
    public static bool IsSatisfiedBy(this MachineCredentialKind kind, bool hasCertificate, bool hasApiKey);
}
```

Resolution rules:

| Stored | Enabled | Default |
|---|---|---|
| none | `[MachineAuth:DefaultCredentialKind]` | the same |
| `CredentialKind` only (legacy) | `[CredentialKind]` | `CredentialKind` |
| `EnabledCredentialKinds` (+ optional `CredentialKind`) | the list | `CredentialKind`, or the first listed kind |

Validation (`PUT`) rejects with 400:

- no kind at all (`EnabledCredentialKinds` empty and `CredentialKind` null);
- a value that is not a defined `MachineCredentialKind`;
- a `CredentialKind` that is not in a non-empty `EnabledCredentialKinds`;
- a `CertificateLifetimeDays` outside `MachineAuth:MinCertificateLifetimeDays..MaxCertificateLifetimeDays`.

### 3. API (`Api.Functions/src/V1/AccessHttp.cs`)

- **New `GET v1/access/points/{key}/machine-authentication`** (`GetMachineAuthentication`, `User.Impersonation`, **Contributor** on the project, because Contributors create machines). Returns the effective policy (section 2) with every property filled. Without a policy store it resolves from the deployment options only. An organization key returns 400, like PUT.
- **`PUT`** validates (400 with an `ErrorResponse` message), stores `Normalize(requested)` (`CertificateLifetimeDays` stays null when not given, so the project keeps following the deployment default), and returns the effective policy.
- `GET v1/access/points/{key}` keeps returning the stored policy unchanged.
- `Definitions.RouteIds.Access.GetMachineAuthentication`, `RouteTableTests` and the OpenAPI document get the new operation.

### 4. Creating a machine

- `MachineAccessCreate` (`Backend.Core/Accessing/Model`) gets `MachineCredentialKind? CredentialKind`.
- `PolicyAwareMachineAccessService.CreateMachineAccessAsync`:
  1. reads the effective policy;
  2. `kind = model.CredentialKind ?? policy.CredentialKind`;
  3. a kind that is not enabled throws `MachineAccessNotSupportedException` ("The credential kind X is not enabled in the project org.project."), which `PostMachines` already maps to 400;
  4. for certificate kinds, `CertificateLifetimeDays = model.CertificateLifetimeDays ?? policy.CertificateLifetimeDays`; other kinds get null;
  5. routes by `kind` as today.
- `CertificateMachineAccessService` keeps clamping the lifetime.

### 5. The machine remembers its certificate lifetime and expiry

- `MachineAccess` / `IMachineAccess` get `int? CertificateLifetimeDays` and `DateTimeOffset? CertificateNotAfter`.
- `CertificateMachineAccessService` fills both on create and extend.
- `MachineAccessEntity` gets `CertificateLifetimeDays`. `CosmosAccessService.CreateMachineAccessAsync` stores it and `NotAfter`. `ToMachineAccess` maps `CertificateLifetimeDays` and `CertificateNotAfter = NotAfter`.
- `CosmosCertificateRenewalService` renews with the machine's `CertificateLifetimeDays`, falling back to `MachineAuth:CertificateLifetimeDays` for machines created before this change. Because `NotAfter` is now stored at creation, a new machine can be renewed in its renewal window.

### 6. Enforcement by the machine's own kind

The machine must present what **its** kind requires, and that kind must still be enabled in the project. Disabling a kind therefore stops the machines that use it, as changing the single kind did before.

- `ApiKeyHashEntry`, `ApiKeyValidationResult`, `CertificateRecord` and `ClientCertificateValidationResult` get a trailing optional `MachineCredentialKind? CredentialKind = null`:
  - `CosmosMergedApiKeyHashStore.GetAsync` returns the entity's kind; `ApiKeyMachineAccessService.ValidateAsync` passes it on;
  - `CertificateMachineAccessService` records `Certificate` (create) or `CertificateAndApiKey` (extend); `ClientCertificateValidator` passes it on for machine certificates (shared certificates have no machine).
- `MachineAuthenticationMiddleware`, after reconciling the identity:
  1. `machineKind` = the machine certificate's kind, else the API key's kind, else the kind the presented credentials form (both → `CertificateAndApiKey`, certificate → `Certificate`, key → `ApiKey`) for stores that do not know it;
  2. 401 unless `policy.IsEnabled(machineKind) && machineKind.IsSatisfiedBy(hasCert, hasApiKey)`.
- `MachineCredentialPolicy.EnsureAllowedAsync` accepts an identity provider machine token when `ClientCredentials` is enabled in the project.

| Machine kind | Must present | Accepted when |
|---|---|---|
| `ApiKey` | API key | `ApiKey` is enabled |
| `Certificate` | its certificate | `Certificate` is enabled |
| `CertificateAndApiKey` | certificate (own or shared) and API key | `CertificateAndApiKey` is enabled |
| `ClientCredentials` | identity provider token | `ClientCredentials` is enabled |

### 7. OpenAPI document and SDKs

Export the document from the running Functions host into `open.api.v{version}.json` in `sdk.typescript/` and `Sdk.NSwag/`, regenerate the NSwag client (`ApiSdk.g.cs`) and the TypeScript SDK (`npm run generate`). New in the SDKs: `getMachineAuthentication` / `useGetMachineAuthentication`, `MachineAuthenticationPolicy.EnabledCredentialKinds`, `MachineAccessCreate.CredentialKind`, `MachineAccess.CertificateLifetimeDays` and `CertificateNotAfter`.

### 8. sform

- `sform machine machine-auth`:
  - `-k|--credential-kind` becomes repeatable (also comma separated): the enabled kinds. Kebab case keeps working (`client-credentials`).
  - New `-d|--default-kind`: the default kind; without it the first `-k`, or the current default when `-k` is not given.
  - `-l|--lifetime-days`: the default certificate lifetime.
  - Options that are not given keep the current values (read through the new GET). Without any option the command prints the current policy.
- `sform machine create` gets `-k|--credential-kind` (one kind; default: the project default). `-l` stays; without it the server uses the project default lifetime. The printed JSON adds `CertificateLifetimeDays` and `CertificateNotAfter`.
- `sform machine list` adds a `Kind` column.

### 9. Admin UI (`ui.admin`)

- `utils/machines.ts`: `credentialKinds` (ordered), `issuesCertificates(kind)` takes a kind, `certificateDaysLeft(machine, fallbackLifetimeDays?, now?)` uses `CertificateNotAfter` first, then `LastRenewalAt + (machine.CertificateLifetimeDays ?? fallback)`.
- **Settings → Machine authentication** (Administrator): a checkbox per kind with its description ("Enabled credential kinds"), a "Default for new machines" select among the enabled kinds, and "Default certificate lifetime (days)" when an enabled kind issues certificates. It reads `useGetMachineAuthentication`. Saving sends all three; at least one kind has to be checked.
- **New machine dialog**: receives the effective policy. A "Credential kind" select lists only the enabled kinds, preselected with the default. "Certificate lifetime (days)" appears when the selected kind issues certificates and is preselected with the project default each time the dialog opens. The request sends `CredentialKind` and the lifetime.
- `machines.vue` reads the policy with `useGetMachineAuthentication` when the user can manage machines (Contributor), so Contributors get the kinds and the lifetime too.
- Machine detail shows the certificate lifetime and expiry.
- Mock API: `GET` and `PUT` of the policy follow the server rules; create honours `CredentialKind` (400 when not enabled) and the lifetime; reset keeps the machine's own kind. Seeds use `EnabledCredentialKinds`.
- Tests: unit tests for the helpers and the dialog, and the e2e flow "creates a machine and downloads its certificate" enables `Certificate` in Settings and picks it in the dialog.

### 10. Documentation

- `docs/Platform/Storyteller/authentication.md`: a "Machine authentication policy" section (enabled kinds, default kind, default lifetime, GET/PUT, validation, enforcement by the machine's kind, sform), and the `MachineAccess` table gets the two new properties.
- `docs/42for.net/platform/admin-ui.md`: Settings and Machines rows.

### Phases

1. **Phase 1 — API and SDKs:** sections 1–7, with unit tests (`Access.Certificates/test`, `Api.Functions/test`) and the Cosmos-free parts of the integration tests.
2. **Phase 2 — sform and admin UI:** sections 8–10.

### Out of scope

- Validating that a deployment can issue an enabled kind (for example `ClientCredentials` without an identity provider). Creating such a machine keeps failing with 400 as today.
- Changing the kind of an existing machine; delete it and create a new one.
- A durable `IClientCertificateStore` (the in-memory store is documented in `authentication.md`).
