# Turn Off Machine Credential Kinds per Deployment

## Problem

The first Azure deployment of the platform runs without mTLS (see [Azure Production Environment of the 2S Platform](../../Deployment/specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md)): the API's front end passes no client certificate, and `MachineAuth:IsCertificateHeaderFromClientAllowed` stays off. An Administrator can still enable `Certificate` or `CertificateAndApiKey` in a project's machine authentication policy, create such machines and issue shared certificates. Those machines get a certificate that can never sign in, and nothing tells the Administrator why.

The deployment needs a setting that turns kinds off for every project, so they can't be enabled, created or used.

## Current State

- `Backend.Core/src/Accessing/MachineAuthenticationOptions.cs` (`MachineAuth`) has `DefaultCredentialKind`, the certificate header and lifetimes, but nothing that limits the kinds.
- `Backend.Core/src/Accessing/MachineAuthenticationPolicies.cs` is the single place that turns a stored policy into the effective one (`Resolve`) and validates a requested one (`Validate`). Every consumer goes through it:
  - `GET` and `PUT v1/access/points/{key}/machine-authentication` (`Api.Functions/src/V1/AccessHttp.cs`, `Validate`, `Normalize`, `Resolve`);
  - machine creation (`Access.Certificates/src/PolicyAwareMachineAccessService.cs`, `GetEffectiveAsync` then `IsEnabled`);
  - shared certificates (`Access.Certificates/src/SharedCertificateService.cs`, `AllowsSharedCertificates`);
  - sign-in of API-key and certificate machines (`Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`), of bearer-token machines (`Api.Functions/src/Security/MachineCredentialPolicy.cs`) and the federation exchange (`Access.Federation/src/FederatedTokenExchangeService.cs`).
- `Access.Certificates/src/EntryPoint.cs` binds `MachineAuth` with `services.Configure`, without validation.

## Proposed Changes

### 1. The setting

`MachineAuthenticationOptions.DisabledCredentialKinds` (`MachineCredentialKind[]`, empty by default), bound from `MachineAuth:DisabledCredentialKinds` (`MachineAuth__DisabledCredentialKinds__0=Certificate`, …). An array with an empty default, so the configuration binder never appends to a non-empty default list.

### 2. The effective policy

`MachineAuthenticationPolicies.Resolve` leaves the turned-off kinds out of `EnabledCredentialKinds`, before the default kind is chosen. A stored policy keeps them stored: when the setting no longer lists them, they work again without a change of any project. When no kind is left, the project enables `DefaultCredentialKind`, as without a stored policy.

Because every consumer uses `Resolve`, this alone makes creation (`400 MachineAccessNotSupported`), shared certificates (`400 MachineAccessNotSupported`), and the sign-in of existing machines of a turned-off kind (refused like a kind the project disables) follow the setting. `GET …/machine-authentication` shows the policy without them.

### 3. A requested policy

`MachineAuthenticationPolicies.Validate` refuses a policy that enables a turned-off kind with "The machine credential kind {kind} is turned off in this deployment." (`400`, no error code, like the other policy validation messages).

### 4. Startup validation

`MachineAuthenticationPolicies.ValidateOptions(options)` returns why the settings are inconsistent: a turned-off kind that doesn't exist, or `DefaultCredentialKind` turned off. `Access.Certificates` registers `MachineAuthenticationOptionsValidator : IValidateOptions<MachineAuthenticationOptions>` and binds the options with `AddOptions().Bind(…).ValidateOnStart()`, so the Functions host fails at startup with the message.

### 5. Out of scope

- Exposing the turned-off kinds to clients (the discovery endpoint publishes no machine settings; the admin UI shows the API's `400` message). A later change can add them to the policy response together with the SDK regeneration.
- The OpenAPI descriptions are not changed, to keep the generated SDKs as they are.

### 6. Tests and docs

- `Backend.Core/test/MachineAuthenticationPoliciesTests.cs`: `Resolve` leaves out turned-off kinds and falls back to the default; `Validate` refuses them; `ValidateOptions`.
- `Access.Certificates/test`: creating a machine of a turned-off kind, the default kind turned off, issuing a shared certificate, and the binding and startup validation of the setting.
- `docs/Platform/Storyteller/authentication.md`, section *Machine authentication policy*.
