# Phase 2 of Project-level Machine Authentication Policies

## Overview

Phase 2 covers the clients (sections 8–10) of [Project-level Machine Authentication Policies with Per-machine Selection](../2026-10-14%20Project-level%20Machine%20Authentication%20Policies%20with%20Per-machine%20Selection.md), Linear 424-14, on top of the API and SDKs of [Phase 1](2026-10-14%20Phase%201%20of%20Project-level%20Machine%20Authentication%20Policies.md). `sform` enables several credential kinds per project and picks one per machine. In the admin UI, Settings enables the kinds and sets the default kind and certificate lifetime, and the New machine dialog lists the enabled kinds with the project defaults preselected. The mock API follows the server's rules.

## What Was Done

### 8. sform

- `Cli/src/Commands/MachineAccess/CredentialKinds.cs` (new): parses a kind in any case or in kebab case into any of the SDK's kind enums, rejecting numbers (`Enum.IsDefined`), as the old command did inline.
- `MachineAuthSetCommand` (`sform machine machine-auth`):
  - `-k|--credential-kind` is repeatable and also takes comma-separated kinds; duplicates are dropped and the order is kept.
  - `-d|--default-kind` is new. It must be one of the `-k` kinds, otherwise the command fails before calling the server.
  - `-l|--lifetime-days` stays.
  - Without options the command prints the policy from `GetMachineAuthenticationAsync`.
  - **Deviation:** the spec said options that are not given keep the current values. The command sets the whole policy instead, as before: `-k` is required as soon as any option is given, a missing `-d` makes the first `-k` the default, and a missing `-l` lets the project follow the deployment default. Keeping the lifetime would have needed the raw stored policy: the GET returns the deployment default filled in, and sending that back would have fixed it into the project.
- `MachineCreateCommand` (`sform machine create`): `-k|--credential-kind` is new. Without it `CredentialKind` stays null, and NSwag's `NullValueHandling.Ignore` leaves it out of the JSON, so the project default applies. Phase 1's document filter keeps NSwag from initializing it to `ApiKey`; a test serializes the request and checks that the property is absent. The printed JSON adds `CertificateLifetimeDays` and `CertificateNotAfter`.
- `MachineListCommand` (`sform machine list`): a `Kind` column.
- Tests: `MachineAuthSetCommandTests` (spellings, several kinds, the default kind, a default that is not enabled, options without kinds, unknown kinds, printing the policy) and `MachineCreateCommandTests` (no kind, each kind with a lifetime, an unknown kind). `Cli/test`: 121 pass.

### 9. Admin UI

- `utils/machines.ts`:
  - `credentialKindDescriptions` is keyed by `MachineCredentialKind`;
  - `credentialKinds` lists the kinds in the API's order;
  - `issuesCertificates(kind)` takes a kind instead of a policy;
  - `certificateDaysLeft(machine, fallbackLifetimeDays?, now?)` uses `CertificateNotAfter`, then `LastRenewalAt` plus the machine's own lifetime, then the fallback.
- `pages/[org]/[project]/settings.vue`, **Machine authentication** card (Administrator), reading `useGetMachineAuthentication`:
  - "Enabled credential kinds" is a `UCheckboxGroup` with a description per kind. `components.d.ts` registers the component.
  - "Default for new machines" is a select of the enabled kinds. Unchecking the default moves it to the first enabled kind.
  - "Default certificate lifetime (days)" shows when an enabled kind issues certificates (1–825 days, as before).
  - Save is disabled when no kind is checked, and the field shows "Enable at least one kind."
  - The old note "The policy applies to machines created or reset after the change" was wrong (the API already enforced the policy on every call). It now says that machines keep their kind and that disabling a kind stops its machines.
  - The page still uses `useGetAccessPoint` for the member count.
- `components/access/MachineCreateDialog.vue`:
  - takes the effective `policy` instead of `issuesCertificates`;
  - "Credential kind" lists the enabled kinds (disabled when there is only one) with the description as help;
  - each time the dialog opens, the default kind and the project lifetime are preselected;
  - "Certificate lifetime (days)" shows only for kinds with a certificate. The request sends `CredentialKind`, and the lifetime only for those kinds.
- `pages/[org]/[project]/machines.vue` reads the policy with `useGetMachineAuthentication` when the user can manage machines (Contributor). It no longer reads the access point, which needed Administrator. Before, Contributors never saw the lifetime field.
- `pages/[org]/[project]/machines/[id].vue` shows "Certificate lifetime" and "Certificate expires" when present.
- Mock API (`mock/access-handlers.ts`, `mock/db.ts`):
  - GET and PUT of the policy apply the server's resolution and its 400 messages;
  - create honours `CredentialKind` (400 when the kind is not enabled) and the lifetime, and returns `CertificateLifetimeDays` and `CertificateNotAfter`;
  - reset keeps the machine's own kind; before, it switched the machine to the project's current kind;
  - the seeds carry `EnabledCredentialKinds`, and the certificate machine its lifetime and expiry. Its expiry badge still shows 20 days, so the visual baselines are unaffected.
- `invalidateAccess` already covers `/v1/access/points/{key}/machine-authentication`, so saving the policy refreshes both pages.
- The UI's `node_modules` held a copied SDK build of another branch (`install-links=true`). `npm ci` reinstalled it from the regenerated SDK; nothing in the lock file changed.
- Tests:
  - `test/unit/access/access.test.ts`: helper cases; the mock API enabling two kinds, creating a machine of each, and rejecting a kind that is not enabled, a default that is not enabled and an empty policy; `MachineCreateDialog` preselecting the project default kind and lifetime, and asking no lifetime for API keys.
  - `test/e2e/access.spec.ts`: "creates a machine and downloads its certificate" now checks **Certificate** in Settings and picks **Certificate** in the dialog.
  - Results: `npm test` 312 pass. `npm run lint` and `vue-tsc --build` are clean. `access.spec.ts` and `accessibility.spec.ts` (settings and machines included) pass on desktop and mobile: 30 tests.
  - The visual suite was not run (it needs the Docker container).
  - Checked by hand in `npm run dev:mock`: checking **Certificate** on Settings shows the lifetime field, preselected with the seeded 90 days.

### 10. Documentation

- `docs/42for.net/platform/admin-ui.md`: the Machines row and a paragraph on **Machine authentication** and **New machine**.
- `docs/Platform/Storyteller/authentication.md` (updated in phase 1) gained the `sform` commands for the policy and for picking a kind.
