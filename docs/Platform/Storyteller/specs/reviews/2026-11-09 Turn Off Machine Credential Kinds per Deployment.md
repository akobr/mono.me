# Turn Off Machine Credential Kinds per Deployment

## Overview

This review covers the whole of the spec [Turn Off Machine Credential Kinds per Deployment](../2026-11-09%20Turn%20Off%20Machine%20Credential%20Kinds%20per%20Deployment.md): the setting `MachineAuth:DisabledCredentialKinds`, which the first Azure deployment sets to `Certificate` and `CertificateAndApiKey` while it has no mTLS. It was implemented as planned, in the single place that resolves and validates machine authentication policies, plus startup validation.

## What Was Done

### 1. The setting

`Backend.Core/src/Accessing/MachineAuthenticationOptions.cs`: `public MachineCredentialKind[] DisabledCredentialKinds { get; set; } = [];`, documented in the class.

### 2. The effective policy

`Backend.Core/src/Accessing/MachineAuthenticationPolicies.cs`: `Resolve` filters the requested kinds with `Enum.IsDefined(kind) && !options.IsTurnedOff(kind)` before choosing the default; the fallback to `DefaultCredentialKind` is unchanged. New extension `IsTurnedOff(this MachineAuthenticationOptions, MachineCredentialKind)`. No consumer was changed: machine creation, shared certificates, the middleware, `MachineCredentialPolicy` and the federation exchange all resolve through `Resolve` or `GetEffectiveAsync`.

### 3. A requested policy

`Validate` checks the turned-off kinds after the unknown kinds and before the default kind, with "The machine credential kind {kind} is turned off in this deployment.".

### 4. Startup validation

- `MachineAuthenticationPolicies.ValidateOptions` returns "MachineAuth:DisabledCredentialKinds names {n}, which is not a machine credential kind." or "MachineAuth:DefaultCredentialKind {kind} is turned off by MachineAuth:DisabledCredentialKinds.".
- New `Access.Certificates/src/MachineAuthenticationOptionsValidator.cs`; `Access.Certificates/src/EntryPoint.cs` now binds with `AddOptions<MachineAuthenticationOptions>().Bind(…).ValidateOnStart()` and registers the validator with `TryAddEnumerable`.

### 5. Out of scope

As planned: no client-visible list of turned-off kinds and no OpenAPI change, so `Sdk.NSwag` and `sdk.typescript` are not regenerated. The `PUT` 400 description ("…a kind doesn't exist…") does not name the new reason; the message itself does.

### 6. Tests and docs

- `Backend.Core/test/MachineAuthenticationPoliciesTests.cs`: five tests (filtering, fallback, `Validate`, both `ValidateOptions` failures).
- `Access.Certificates/test/PolicyAwareCredentialKindTests.cs`: creation of `Certificate` and `CertificateAndApiKey` refused, the default kind turned off; `SharedCertificateServiceTests.cs`: issuing refused; new `MachineAuthenticationOptionsValidatorTests.cs`: binding from the configuration, the empty default, and the startup failure.
- Runs: `Access.Certificates/test` 121 passed, `Backend.Core/test` (`*MachineAuthenticationPoliciesTests`) 59 passed, `Api.Functions/test` (`*Machine*`, `*Certificate*`) 1,282 passed.
- `docs/Platform/Storyteller/authentication.md`: the paragraph **Kinds a deployment turns off** in *Machine authentication policy*.
