# Require a Recent Sign-in for Policy Changes that Widen Secret Delivery

Follow-up of Linear [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and) (PR #203). This plan builds on the branch `feat/424-264`: it reuses its step-up guard (`EnsureRecentSignIn`, `401 StepUpRequired`). No application code is changed by this document.

## Problem

424-264 requires a recent sign-in (Auth:StepUp, 10 minutes by default) before a user reads or writes secrets, changes roles or members, or gives out a machine credential. Two Administrator routes were left out, and the review of that branch recorded them as an accepted risk (`authentication.md`, "Limits of the step-up"):

- **The key/value policy** (`PUT v1/{org}/{project}/key-values/policy`, `SetKeyValuePolicy`). Turning `RequireMtlsForSecretDelivery` off lets every machine with `Secrets.Read` or `Secrets.Write` get secret values without its client certificate: API keys, client-credentials tokens and federated tokens alone are then enough.
- **The machine authentication policy** (`PUT v1/access/points/{key}/machine-authentication`, `PutMachineAuthentication`). Enabling a credential kind again (`EnabledCredentialKinds`) brings back the machines of that kind, whose credentials were refused while the kind was off.

So an attacker with a stolen, old Administrator session cannot create a credential (that is guarded), but can widen what an already leaked machine credential does. The step-up should cover exactly the changes that widen secret delivery or credential acceptance, and nothing that only tightens.

## Current State

- `KeyValuePolicyHttp.SetKeyValuePolicy` (`Api.Functions/src/V1/KeyValuePolicyHttp.cs`): `CheckAdministratorAsync`, the body (`KeyValuePolicyUpdate`: `AllowedProtections`, `DefaultSecretProtection`, `RequireMtlsForSecretDelivery`, `HsmRotationInterval`, `Byok`, `ExpectedVersion`; every member optional), then `IKeyValuePolicyService.UpdateAsync`. The service applies only the members that differ (`CosmosKeyValuePolicyService.cs:102-104` for the mTLS rule). The handler does not read the stored policy before the update; the cached copy is `IKeyValuePolicyStore.GetAsync`, the current one `GetCurrentAsync`.
- `AccessHttp.PutMachineAuthentication`: the Administrator check, a 501 without the store, the project key, the body (`MachineAuthenticationPolicy`: `EnabledCredentialKinds`, `CredentialKind`, `CertificateLifetimeDays`), `MachineAuthenticationPolicies.Validate`, then `Normalize` and `SetAsync`. The effective policy of the stored one and the deployment's `MachineAuth` options is `MachineAuthenticationPolicies.Resolve(stored, options)`.
- The step-up guard of 424-264: `HttpRequestDataExtensions.EnsureRecentSignIn` (machines pass, a user without a recent sign-in gets `StepUpRequiredException`, 401 with the RFC 9470 challenge); `OperationAccess.StepUp` lists the guarded operations, and `OpenApiExportTests.Document_StepUpOperations_DocumentTheChallengeInTheir401` checks that exactly those document the 401 body and header.
- Clients that call the two routes: the admin UI (`SecretsPolicyCard.vue` and `ByokCard.vue` call `setKeyValuePolicy`; `settings.vue` calls `setMachineAuthentication`), `sform story kv policy` (`Cli/src/Commands/KeyValues/KeyValuePolicyCommand.cs`), and the MCP tools `set_key_value_policy` and `set_machine_authentication_policy` (`Mcp/src/Tools/KeyValueTools.cs`, `MachineTools.cs`). Every one of them already handles `StepUpRequired` through the SDK hook or `IsStepUpRequired` (424-264), except that the admin UI's proactive check is not wired to these screens.

## Proposed Changes

### 1. What counts as widening

Only a change that gives more than the stored state needs the step-up. Tightening, and every other member, stays as it is, so an Administrator with an old sign-in can still lock things down at once (as the deletes and the recipient removal of 424-264 stay open as kill switches).

| Route | Needs a recent sign-in when | Does not need it |
| --- | --- | --- |
| Key/value policy | `RequireMtlsForSecretDelivery` goes from `true` to `false` | turning it on; `AllowedProtections`, `DefaultSecretProtection`, `HsmRotationInterval`, `Byok`; an update that leaves the rule unchanged |
| Machine authentication policy | the effective enabled kinds gain a kind they did not have | removing kinds; `CredentialKind` (the default of new machines, which need the role and the step-up when they are created anyway); `CertificateLifetimeDays` |

Comparing the effective policies (after `Resolve`) and not the stored bodies keeps a body that only spells out the deployment default from counting as widening.

### 2. The key/value policy

In `SetKeyValuePolicy`, after the body is read and validated:

```csharp
if (update.RequireMtlsForSecretDelivery is false)
{
    var current = await _policyStore.GetCurrentAsync(organization, project, request.FunctionContext.CancellationToken);

    if (current.RequireMtlsForSecretDelivery)
    {
        request.EnsureRecentSignIn();
    }
}
```

- `GetCurrentAsync` (not the cached copy), so the comparison uses the stored policy the update is applied to; it is one point read, paid only when the body turns the rule off.
- A race (another request turns the rule on between the read and the update) at worst asks for no step-up for a change that then still turns it off. `UpdateAsync` already guards concurrent changes with `ExpectedVersion`; the admin UI and `sform` send it. Moving the check into the service is not worth a domain dependency on HTTP: noted, not done.

### 3. The machine authentication policy

In `PutMachineAuthentication`, after `Validate` and before `SetAsync`:

```csharp
var before = MachineAuthenticationPolicies.Resolve(await _policyStore.GetAsync(organization, project), _machineOptions);
var after = MachineAuthenticationPolicies.Resolve(normalized, _machineOptions);

if (after.EnabledCredentialKinds!.Except(before.EnabledCredentialKinds!).Any())
{
    request.EnsureRecentSignIn();
}
```

`MachineAuthenticationPolicies.Widens(before, after)` (a static helper in `Backend.Core`) holds the comparison, so it is tested on its own.

### 4. OpenAPI and the documents

- `OperationAccess.StepUp` gains `SetKeyValuePolicy` and `SetMachineAuthentication`; both routes document the step-up 401 (`ResponseUnauthorizedStepUpUserOnly`, `StepUpChallengeHeaders`) and their descriptions say when it applies. A new snapshot `open.api.v0.10.<n>.json` and the regenerated SDKs follow, as in 424-264.
- `authentication.md`: the table of "Recent sign-in (step-up)" gets the two rows with their condition; "Limits of the step-up" and "Not available yet" drop the accepted risk.
- `key-values.md` (Who may do what, the policy section), `access.md` (the machine authentication policy), `mcp.md` (the two tools can meet the step-up).

### 5. Clients

- **Admin UI:** `SecretsPolicyCard` calls the step-up store's `ensureRecentSignIn()` before it saves a change that turns "Require mTLS for secret delivery" off, and `settings.vue` before it saves a policy that enables a kind. The 401 itself already opens the dialog through the SDK hook; the proactive check only spares the person a lost edit. Vitest for both.
- **sform:** `story kv policy` and the machine authentication command already print the step-up text through `ApiRefusal` and `WriteApiError`; a test each.
- **MCP:** `set_key_value_policy` and `set_machine_authentication_policy` get the step-up text through `ApiErrors.ToResult`; their descriptions say that widening needs a recent sign-in; a test each.

### 6. Tests

- `Api.Functions/test`: for each route, with a stale sign-in: widening is `401 StepUpRequired` and nothing is stored; tightening and the other members pass; an update that leaves the mTLS rule as it is passes; with a recent sign-in widening passes; machines are refused as before (both routes are for users only). The key/value case reads the current policy only when the body turns the rule off.
- `Backend.Core/test`: `MachineAuthenticationPolicies.Widens` with stored and default policies.
- `OpenApiExportTests`: the step-up set of section 4.

### Decisions

- **D1. Guard the widening, not the route.** A stale Administrator keeps every tightening, including turning mTLS on and disabling a kind.
- **D2. Compare effective policies.** Deployment defaults do not count as a change.
- **D3. The check stays in the handler.** As every other step-up guard; the race of section 2 is accepted.

### Out of scope

- Re-checking the step-up inside the services, and the BYOK settings (they change which key protects values, not who receives them).

### Phases and commits

1. `docs(platform): plan the step-up of policy changes that widen secret delivery (424-264)`: this spec.
2. `feat(platform): require a recent sign-in for policy changes that widen secret delivery (424-264)`: sections 1-3 and 6.
3. `chore(platform): regenerate the SDKs from OpenAPI 0.10.<n> (424-264)` and the clients and documents of sections 4 and 5.
4. The review.
