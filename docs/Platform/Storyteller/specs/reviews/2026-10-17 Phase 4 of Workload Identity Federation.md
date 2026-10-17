# Phase 4 of Workload Identity Federation

Linear: [424-46 Add workload identity federation](https://linear.app/424fun/issue/424-46/add-workload-identity-federation)

## Overview

Phase 4 covers the admin UI (section 15 and `admin-ui.md`) of [Workload Identity Federation for Machine Access](../2026-10-17%20Workload%20Identity%20Federation%20for%20Machine%20Access.md). Administrators manage trusted issuers on the Machines page, Contributors create `Federated` machines with a rule editor and edit their rules on the machine detail, and a setup dialog replaces the one-time secret. It uses the TypeScript SDK regenerated in [Phase 2](2026-10-17%20Phase%202%20of%20Workload%20Identity%20Federation.md). A reviewer and a skeptic went over the result; all four findings were confirmed and fixed.

| Suite (`src/Platform/ui.admin/src`) | Result |
|---|---|
| `npm run build` (vue-tsc + vite), `npm run lint` | pass |
| `npm test` | 489 passed (461 before) |
| `npm run test:e2e` (desktop and mobile, mocked API, axe) | 237 passed, 5 skipped (skips predate this work) |
| `npm run test:visual:docker` | 60 passed; the four `machines-*` baselines were regenerated, the other 56 are unchanged |

## What Was Done

### 15. Admin UI

- **Helpers.** `utils/machines.ts`: `hasSecret`, `isFederated`, kind labels (`credentialKindLabels`, `credentialKindLabel`) and `apiBaseUrl()`. New `utils/federation.ts`: claim rows and claim maps, rule drafts with a completeness check, `projectTokenEndpoint`, `issuerPlatform` (classifies an issuer by host as the API does), the seven templates of the spec table with issuer, key source, lifetime, required claims and help, per-platform rule hints, `formatLifetime`.
- **Trusted issuers** (`TrustedIssuersTable.vue`, `TrustedIssuerDialog.vue`, `ClaimRowsEditor.vue`): an Administrator section `#trusted-issuers` below Shared certificates with name, issuer, key source, max lifetime and required claims, Edit and Delete (typed-name confirmation), and an alert when the policy does not enable `Federated`. The dialog's template select fills the issuer, key source, lifetime and required-claim rows; a JWKS URI field or JWKS text area follows the key source; server 400/409 messages show inline.
- **New machine** (`MachineCreateDialog.vue`, `FederationRuleEditor.vue`): with `Federated`, rules (issuer select, audience prefilled with the project's token endpoint, subject, claim rows, platform hints) replace the certificate fields; a single issuer is preselected; Create waits until every rule is complete. Without issuers, Administrators get a link to `#trusted-issuers` and Contributors are told to ask an Administrator.
- **Setup dialog** (`FederatedSetupDialog.vue`) after creating a `Federated` machine: machine id (`client_id`), token endpoint, rules, a `curl` exchange and a GitHub Actions step, all copyable.
- **Machines list:** Reset hidden for `Federated` machines, kind labels.
- **Machine detail** (`machines/[id].vue`): kind label; for `Federated` the certificate rows are hidden, the rules are listed with an "issuer deleted" badge, Contributors edit them (`FederationRulesDialog.vue`, rules `PUT`, inline server messages), and a Setup button reopens the setup dialog.
- **Mock API** (`app/mock/federation.ts`, `access-handlers.ts`, `db.ts`): `Federated` machines without a secret and with a token endpoint, the reset refusal, the rules `PUT`, issuer CRUD with the API's rules (route name wins, immutable issuer, 20 issuers, 409 `FederatedIssuerInUse` naming machines, Administrator only) and the main validation messages. Seeds: `acme.billing` and `acme.shipping` enable `ApiKey` and `Federated` and trust a `github` issuer (`repository_owner_id` 65); `acme.billing` has the `Federated` machine `mach-release-pipeline`.
- **Deviations:**
  - Kind labels in the list and the detail only; the policy checkboxes and the create-dialog select keep the API names, as the e2e tests, docs and sform do (and the Settings baselines stay unchanged).
  - Templates only when adding an issuer (the issuer cannot change), GitHub Actions preselected; a `<placeholder>` left in the issuer or a subject blocks saving; a name already used in the project is refused on the client, because a `PUT` on it would replace that issuer.
  - The page waits for the certificate and issuer lists before scrolling to `#shared-certificates` / `#trusted-issuers`.
  - The create dialog takes up to 10 rules, like the rules dialog, and widens while `Federated` is selected.
  - The audience prefill is built from the SDK's configured `baseUrl`, not the boot state, so unit tests see it too.
  - Mock machine ids keep the `mach-…` format; the ported validation simplifies the IP-literal and IDN checks.

### 16. Documentation (Phase 4 part)

- `docs/42for.net/platform/admin-ui.md`: the Machines row, the machine authentication paragraph, a section on federated machines, trusted issuers and rules, where the audience prefill comes from, and that federated machines have no one-time secret.
- `src/Platform/ui.admin/README.md`: the test list.

## Review fixes

| Finding | Fix |
|---|---|
| The setup snippets used the first rule's audience whatever its issuer (an Azure rule's app id in the GitHub step) | The dialog reads the trusted issuers: the GitHub step uses the first GitHub rule and is hidden without one; the `curl` comment lists one audience per issuer; a 403 falls back to the rule whose audience is the token endpoint |
| The audience prefill (the UI's API address) and the API's `TokenEndpoint` could differ behind a proxy, and the two dialogs disagreed | The rules dialog prefills the audience of the machine's first non-Azure rule, else `TokenEndpoint`; the detail and the setup dialog flag a non-Azure rule whose audience is not `TokenEndpoint`; `admin-ui.md` explains it |
| After "Add a trusted issuer", the closing modal took focus back from `#trusted-issuers` | `onCloseAutoFocus` is suppressed for that exit only; e2e asserts the section is focused and in view (it failed on desktop and mobile without the fix) |
| Several new behaviours were untested, and one e2e covered less than its name said | The template e2e walks Vercel and Azure before Kubernetes; new e2e deletes an issuer, sees the "issuer deleted" badge and follows the New machine link; unit tests for the disabled-kind alert and the delete flow |

## Verification

```
cd src/Platform/ui.admin/src
npm run build
npm run lint
npm test
npm run test:e2e
npm run test:visual:docker
```

- No visual baselines exist for the machine detail page or the federation dialogs; they are covered by the e2e and axe checks.
- The mock's validation is a hand port of `FederationValidation`; if the API's messages change, it can drift.

## Left for later

- The first `Federated` machine of a project created in the UI behind a proxy (or with a different `PublicBaseUrl`) still gets the UI-derived audience, because the API reports the token endpoint only on machines; the UI flags such rules. An endpoint that names the project's token endpoint before any machine exists would close this.
