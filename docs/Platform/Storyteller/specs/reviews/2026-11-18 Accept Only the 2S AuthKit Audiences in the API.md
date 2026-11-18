# Accept Only the 2S AuthKit Audiences in the API

Linear: [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api).

## Overview

This review covers all three phases of [the spec](../2026-11-18%20Accept%20Only%20the%202S%20AuthKit%20Audiences%20in%20the%20API.md). The API reads `Auth:AuthKit:Audiences`, the audiences of the 2S WorkOS applications. When that list has entries, it overrides the single `Auth:AuthKit:Audience`, so the JWT template's shared `aud` can leave the check. With neither set, nothing changes. Bicep passes the list as `Auth__AuthKit__Audiences__<n>` and keeps the single value for a rollback. The docs explain why only the audience separates the products of one WorkOS environment, and the runbook gives the staging check and a production order without a `401` window. Before the plan, a read of WorkOS's docs and the workspace (read-only) found that every application of an environment has the same `iss`. So the per-client issuer does not keep other products out today, contrary to [424-261](https://linear.app/424fun/issue/424-261)'s reading. The staging check itself, the `client_id` fallback, and the production values stay open (see Left for later).

## What Was Done

### 1. `AuthKitOptions`

`Backend.Core/src/Accessing/AuthKitOptions.cs`:

- `public string[] Audiences { get; set; } = [];`.
- `Audience` is now documented as the single value of earlier releases.
- `GetAudiences()` returns:
  - the non-blank `Audiences`, trimmed and distinct (ordinal);
  - otherwise a non-blank `Audience` as configured (not trimmed, as before);
  - otherwise nothing.

### 2. `UserAuthenticationOptionsValidator`

`ValidateAuthKit` now collects its failures, as `ValidateKeycloak` does, so a host with several AuthKit mistakes reports them together. It adds "`Auth:AuthKit:Audiences` entries must not be blank." The existing messages are unchanged.

### 3. `AuthKitTokenSource`

- The constructor takes `IReadOnlyCollection<string>? audiences`.
- `Audiences` is `null` when the list is null or empty, and then `aud` is not checked.
- The own-audience overload passes `Audiences` to the explicit-list overload, which is unchanged.

Callers:

- `AuthKitMachineTokenValidator` passes `[authKit.ClientId]`, the same as before.
- `AuthKitUserSubjectTokenValidator`'s additional issuers pass `audiences: null`, the same as before.

### 4. `AuthKitBearerTokenValidator`

It builds its source with `authKit.GetAudiences()`. The header comment says why the audience, and not the issuer, keeps other products out.

### 5. `TokenExchangeOptionsValidator.GetApiAudiences`

For AuthKit, it now adds every effective audience **and** a non-blank `Audience`, even while the list overrides it. The comparison is unchanged: trimmed, without a trailing `/`, ignoring case.

### 6. Not changed

The middleware, the machine and subject rules, the discovery endpoint and the MCP server are not changed. No `client_id` allow-list was added.

### 7. Tests

- `Access.AuthKit/test/AuthKitBearerTokenValidatorTests.cs`, signed RS256 tokens:
  - listed audiences pass; unlisted ones and a missing `aud` fail;
  - the list overrides the single value;
  - an `aud` array passes with one listed member and fails with none or with `[]`;
  - entries are trimmed; `aud` is compared ordinally, and only one trailing `/` may differ;
  - a blank single value with an empty list checks nothing. The existing single-value theory is unchanged.
- `Access.AuthKit/test/AuthKitRegistrationTests.cs`:
  - `GetAudiences`: precedence, trimming and duplicates, the fallback to the single value, blank entries;
  - blank entries refused, and the AuthKit failures reported together;
  - a host that starts with `Auth:AuthKit:Audiences:0` and `:1` next to `Audience`, and a bearer validator source that carries the list;
  - the existing bind-once test now also checks that `Audiences` is not appended twice.
- `Backend.Core/test/TokenExchangeOptionsTests.cs`: `SubjectAudiences` refused when they name a listed audience or the single value (with case and trailing-`/` variants); other audiences pass.
- `Api.Functions/test/AuthKitAudienceAuthenticationTests.cs` (new) uses the real AuthKit user and machine validators of one shared issuer and keys:
  - another application's `user_…` token gets **401**, both with its own `aud` and with the template's shared `aud` that the list overrides. `next` is not called, and the machine validator does not take the token;
  - tokens with a 2S audience are users.
  - The validating path runs in every build, as `AuthKitImpersonationAuthenticationTests` does.

### 8. Bicep

- `main.bicep`: `param authKitAudiences string[] = []`, passed to the module. The description of `authKitAudience` says it is ignored while the list is set.
- `modules/functions.bicep`: the parameter, and `authKitAudienceSettings` built with `toObject(range(...))` as the `MachineAuth__DisabledCredentialKinds__${i}` settings are. Merged with `union(...)`. `Auth__AuthKit__Audience` stays.
- `parameters/prod.bicepparam`: a comment only. The list stays empty until the staging check gives its values.
- `src/Platform/Deployment/README.md`: a row in the parameter table.

### 9. `authentication.md`

- The settings table: the `Audiences` row, and the `Audience` row reworded.
- The flat-key and app-setting example.
- The `aud` rule in "How AuthKit tokens are checked".
- Step 3 of the dashboard setup: the template renders into every application's tokens.
- The `SubjectAudiences` row.
- A new section, **Several WorkOS applications**: the shared issuer, keys and template, what to list, reading the values from real tokens, never listing the template's `aud`, the switch order, machine tokens, and the exchange.

### 10. Deployment docs

- `runbook-azure-production.md` has a new section, **Enabling Multiple Applications in WorkOS**:
  - a local JWT payload decoder;
  - where the admin UI's and `sform`'s tokens are (`SFORM_TOKEN_STORE=file`);
  - the local API as a Release build;
  - the staging checklist (8 steps plus a local check);
  - the production order (7 steps);
  - the rollback.
  
  Phase 5 now points to it.
- `decisions.md`: the shared issuer also without the custom domain, and points 3 and 4 of the plan.
- `cookbook-azure-production.md`: step 13, the 8.3 settings table, and the open point.

Deviations from the spec's sections 10 and 11:

- **The production order.** The spec followed the issue's one-step switch. The runbook instead:
  1. lists the new audience **next to** `https://2s.42for.net`;
  2. enables Multiple Applications and checks a decoded token;
  3. only then changes the template;
  4. removes the old value before the second application is created.

  Setting only the new value before the switch would refuse every 2S token until WorkOS changes the `aud`. Setting it after the switch would leave the shared value accepted for a while.
- **The staging check** runs the local API as a Release build. It also decodes every token without the template's `aud`, and it records what each value is made of, so that production lists its own value (see Review).

## Review

A review of the branch read it in four ways: correctness and security, tests, deployment and rollout, and the docs. A skeptic then tried to refute each finding. The correctness and security reader found nothing. The other three reported ten findings: two were refuted, and eight stand, three of which describe the same problem. All of them are in the docs or a test comment, so the code passed the review unchanged. Fixed in `test(platform): say why the machine validator refuses …` and `docs(platform): fix the staging check and the production order …`:

1. **The staging check would prove nothing on a Debug API** (medium). A local API is a Debug build, and `DEV_AUTH` decodes AuthKit tokens without checking them, so a wrong list still lets everyone in, and the other application's token gets `200`. The runbook now runs the API as a Release build for those checks, and says how to tell one: a token with a changed signature gets `401`.
2. **A staging value isn't a production value** (medium; three reviewers). If an application's `aud` is its client id, the value staging measures is staging's client id. Staging step 8 now records what a value is made of. Production step 2 lists production's own value (for example `authKitClientId`), never a staging one. The README row, the `prod.bicepparam` comment, `decisions.md` and the cookbook say the same.
3. **Production changed the template in the same step as the switch** (medium). It now enables Multiple Applications first and decodes a token. A wrong first entry can then be fixed while the second entry still lets everyone in. The template changes after that.
4. **The rollback could drop the list while the 2S tokens no longer carry the single value** (medium). It now restores the template's `aud` first, and prefers listing the values that the tokens carry now.
5. **The staging decision rule ignored the case without the template's `aud`** (low). Step 6 now decodes all three tokens without it, and step 8 takes the values from the template as it is decided.
6. **The test comment claimed that `org_id` takes the machine validator to its user-or-machine check** (low). Another application's `aud` is never the client id, so the machine validator stops at its audience check. The comment now says so, and `org_id` is gone. That validator refusing `user_` tokens is already `AuthKitImpersonationAuthenticationTests`' job.
7. **"Set it once the environment has several applications"** (low) contradicted "before the second application exists". The settings rows of `authentication.md` and the cookbook now say "before".

Refuted:

- That the old audience leaves the list without a check of sessions from before the switch. Staging step 5 decodes such sessions, and production step 5 checks a refresh.
- That `authentication.md` overstates what WorkOS documents. The sentence quotes WorkOS, and the uncertainty is stated right next to it.

## Validation

- `dotnet test --project src/Platform/Storyteller/Access.AuthKit/test`: 233 passed.
- `dotnet test --project src/Platform/Storyteller/Backend.Core/test`: 1409 passed.
- `dotnet test --project src/Platform/Storyteller/Api.Functions/test`: 3172 passed.
- In Release (no `DEV_AUTH`), the AuthKit, bearer, Keycloak and machine-fallback classes: 49 passed.
- `dotnet test --project src/Platform/Storyteller/Access.AzureAd/test` (the options binding): 140 passed.
- `dotnet build src/Platform`: no errors.
- A mutation check: `GetAudiences` changed to the union of the list and the single value. Five of the new tests failed, among them the middleware test with the template's shared `aud`. The change was reverted.
- `az bicep build` of `main.bicep` and `domains.bicep`, and `az bicep build-params` of both parameter files, as the `Platform.Deployment` workflow runs them: no errors, no warnings. `prod.bicepparam` was built again after the review changed its comment.
- After the review: `AuthKitAudienceAuthenticationTests`, 4 passed.
- Not checked locally: that an empty `authKitAudiences` compiles to no setting. It uses the same `toObject(range(0, length(...)))` as `MachineAuth__DisabledCredentialKinds`, and the what-if of the next production run shows it (expect no `Auth__AuthKit__Audiences__*`).

## Left for later

- **The staging check** (the issue's step 1, the runbook's checklist) changes WorkOS staging, so it is the operator's step. Its findings go into 424-239. They decide the 2S audience list, whether the template keeps `aud`, and whether the `client_id` fallback is needed.
- **The `client_id` allow-list** (`Auth:AuthKit:AllowedClientIds`), if staging shows that no application `aud` separates the products. `client_id` is the only per-application claim that WorkOS documents.
- **The production release**: setting `authKitAudiences` and enabling Multiple Applications, as the runbook orders it.
- **[424-261](https://linear.app/424fun/issue/424-261)**: its premise (a per-client issuer) does not match WorkOS's docs. A second 2S application needs only its audience in the list.
- **[424-287](https://linear.app/424fun/issue/424-287)**: the template's `aud` also renders into Connect (MCP) tokens. Staging step 7 checks that a resource indicator still sets the MCP token's `aud`.
