# Phase 4 of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

This review covers Phase 4 (the documents and the deployment, sections 16-18) of the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md), commit `4871da3f9`. Both Bicep entry points and both parameter files build with `az bicep` 0.49.2 without errors or warnings (with the dummy owner values of the CI workflow), and the VitePress site builds without dead links. MFA in the production WorkOS environment was `Off` when this phase was done; with the owner's approval it was set to `Required` on 2026-11-17 (staging unchanged).

## What Was Done

### 16. Deployment settings

- `main.bicep` and `modules/functions.bicep`: `stepUpEnabled` (`''`, `'true'` or `'false'`, `@allowed`) and `stepUpMaxAge` (`''` or a `TimeSpan`), passed to the module and emitted as `Auth__StepUp__Enabled` and `Auth__StepUp__MaxAge` only when not empty, in the union of the app settings, as `sentryDsnSetting`.
- `parameters/prod.bicepparam` sets both to `''`, with a comment that names `'false'` as the escape hatch (as `cosmosConnectionKey`); the README lists both and a "Later runs" bullet.
- **Deviation:** 24 hours is written `1.00:00:00`: `24:00:00` parses as 24 days and would stop the API.

### 17. Rollout and limits

`authentication.md` has "Rollout of the step-up" (the admin UI with or before the API, older `sform` versions, the escape hatch) and "Limits of the step-up" (a recent sign-in is not MFA; code in the admin UI's origin can start the sign-in itself for SSO users and anyone with a live provider session; the device-code approval in a signed-in browser is undocumented; the two policies are not guarded). The deploy order is in the cookbook (step 16), the runbook (phase 11) and the Deployment README too.

### 18. Documents

- `docs/Platform/Storyteller/authentication.md`: "Recent sign-in (step-up)" (the settings, the providers and builds, what is withheld and what answers 401 with the exact body and header, restore and purge, the withheld resolved read, the order of the checks, the discovery, who is judged, the remedies of each client) and "MFA policy" (Required in production for every sign-in without SSO; SSO is the customer's identity provider's; the limit of `auth_time`; `Off` as of 2026-11-15; staging stays without MFA; the ISO 27001 notes outside the repository record it); "The time of the sign-in" and the `auth_time` rows of the claims tables; the Storyteller user tokens; the 401 row and the sentence that only the step-up 401 has a body; the `StepUpRequired` row of the access-management errors; "Not available yet"; the MFA item of the dashboard setup; the discovery examples.
- `access.md`, `key-values.md`, `inheritance.md` (five rights values, the header table), `mcp.md` ("A recent sign-in"), `configuration-provider.md` (the three reasons and remedies), `Sdk.NSwag/package-readme.md` and the `Sdk.NSwag` README.
- `docs/42for.net`: `platform/admin-ui.md` ("Signing in again to confirm"; the heading avoids the apostrophe of the dialog's title for its anchor), `platform/using-the-platform.md`, `cli/sform.md` (the window, the message, the remedy, exit 44, the withheld hints), `cli/github-action.md` and the GitHub Action README.
- `docs/Platform/Deployment`: the cookbook (8.3, step 13 with MFA Required and impersonation off, 16, 17), the runbook (phases 5, 11, 12), `decisions.md` (the window can change any time, the escape hatch).
- `docs/Platform/development.md`: Debug builds skip the step-up, and how to try it locally.
- Both automation-test documents: a run longer than the window meets the step-up on the guarded cases; the pools stay in staging without MFA.
- **Deviation:** the documents do not say that the API refuses impersonated tokens on its routes (that is 424-262, not merged); they say that such a token never counts as a recent sign-in.
- `conformance.md` and `docs/github/workflows.md` had nothing that became false.
