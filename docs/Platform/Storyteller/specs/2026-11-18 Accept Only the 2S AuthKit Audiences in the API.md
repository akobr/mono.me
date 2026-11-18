# Accept Only the 2S AuthKit Audiences in the API

Linear: [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api).

Line numbers refer to the tree this plan was written against (`ffafd30f5`). No application code is changed by this document.

## Problem

All products of 42for.net share one WorkOS production environment, so users have one login and one account everywhere ([decisions](../../Deployment/decisions.md#one-workos-environment-for-the-products-of-42fornet)). One environment has one JWT template, so every access token it issues carries the same `aud` (`https://2s.42for.net`). The 2S API accepts an AuthKit user token by its issuer, its `aud` and its signature, and gives every signed-in user `User.Impersonation`. Every application of a WorkOS environment issues its session tokens with the same `iss` (WorkOS: "The `iss` claim is the same for all applications in the environment"), and the template's `aud` lands in all of them. So once a second application exists in the environment, a token that **another 42for.net product** gets for a user is a full token of that user for the 2S API. If that product leaks its tokens or hands them on, the leak reaches 2S.

WorkOS recommends "one audience per API". With **Multiple Applications** enabled, each product gets its own WorkOS Application, and its tokens identify the application. The 2S API then has to accept a **list** of audiences (those of the 2S applications) in place of the one `Auth:AuthKit:Audience`, so that the shared audience can leave the list when Multiple Applications is switched on.

## Current State

### The token check

- `Access.AuthKit/src/AuthKitBearerTokenValidator.cs:51-54` builds its `AuthKitTokenSource` with `Auth:AuthKit:Issuer` and **one** audience: `Auth:AuthKit:Audience`, or `null` when it is blank. `null` turns the audience check off.
- `Access.AuthKit/src/AuthKitTokenSource.cs:23-32` takes `string? audience` and keeps it as `Audience`. Its own-audience overload (`ValidateTokenAsync(rawToken, ct)`, lines 119-122) wraps it into a one-element list. The overload with an explicit list (lines 126-170) sets `ValidateAudience = audiences is not null` and `ValidAudiences = audiences` (lines 154-155). So the token check already takes a list, but the source does not.
- IdentityModel's default audience rule applies: a token passes when **any** of its `aud` values equals **any** valid audience, compared ordinally (case-sensitive), ignoring one trailing `/` (`IgnoreTrailingSlashWhenValidatingAudience` is `true` by default). A token without `aud` fails while the check is on.
- Three places build a source (`new AuthKitTokenSource(`):
  - the bearer validator (above);
  - `AuthKitMachineTokenValidator.cs:40`: issuer `AuthKitDomain`, audience `ClientId` (M2M tokens carry the environment's client id; WorkOS does not let an M2M application choose it);
  - `AuthKitUserSubjectTokenValidator.cs:42-45`: the exchange's `AdditionalIssuers`, `audience: null`. The subject validator always calls the explicit-list overload with the actor's `SubjectAudiences` (line 78), so the source's own audience is never used there.

### The options

- `Backend.Core/src/Accessing/AuthKitOptions.cs:13-14`: `public string? Audience { get; set; }`, "Null means the audience is not validated".
- `Backend.Core/src/Accessing/UserAuthenticationOptionsValidator.cs:29-49` (`ValidateAuthKit`) checks `ClientId`, `Issuer` and the JWKS URL, and returns at the first failure. `ValidateKeycloak` (lines 51-91) collects every failure, including "`AllowedClientIds` entries must not be blank".
- `Backend.Core/src/Accessing/UserAuthenticationEntryPoint.cs:15-44` binds `Auth` once (idempotent through a marker), so an array such as `DefaultUserScopes` is not appended twice when the user and the machine registrations both call it.

### Where else the API audience counts

- `Backend.Core/src/Accessing/TokenExchangeOptionsValidator.cs:323-342` (`GetApiAudiences`) adds `Auth:AuthKit:Audience` for AuthKit. `ValidateSubjectAudiences` (lines 185-206) refuses an actor's `SubjectAudiences` entry that names an API audience (trimmed, without a trailing `/`, ignoring case): a token issued for the API would become exchangeable, and the middleware would refuse the API's own users as tokens issued to an actor.
- `Api.Functions/src/Security/BearerAuthenticationMiddleware.cs:213-216` refuses a bearer token whose `aud` names an actor's `SubjectAudiences` (`TokenExchangeOptions.IsIssuedToAnActor`).
- Nothing else reads `AuthKitOptions.Audience`. The discovery endpoint (`Api.Functions/src/V1/AuthConfigurationHttp.cs:48`) returns `ClientId`, not the audience.

### The 2S clients

- The admin UI (`ui.admin/src/app/boot/auth.ts:93`) and `sform` (`Cli/src/Authentication/AuthenticationConfigurationResolver.cs`) both take the AuthKit client id from `GET v1/auth/configuration`, which is `Auth:AuthKit:ClientId`. So today both sign in through **one** WorkOS application (the environment's default application). `sform` uses the device authorization flow, the admin UI authkit-js with PKCE; both get their tokens from `api.workos.com/user_management/authenticate`.

### The issuer today

- Without the custom AuthKit domain, the WorkOS API issues user tokens with the issuer `https://api.workos.com/user_management/<client id>`, and the API reads the keys from `/sso/jwks/<client id>` (`main.bicep` `authKitCustomDomain = false`, `functions.bicep:88-90`). [424-261](https://linear.app/424fun/issue/424-261) reads this as a per-application issuer that keeps other products' tokens out until the custom domain. WorkOS's Applications doc says otherwise: the `iss` "is the same for all applications in the environment. It will reference the default application's client id", and the default application of production is the 2S Platform. So the issuer separates environments, not applications: the isolation ends when the second application is created, with or without the custom domain. This change has to be in production before that.
- AuthKit machine access is off in production: `functions.bicep` sets no `Auth__AuthKit__MachineOrganizationId`.

### Deployment

- `src/Platform/Deployment/main.bicep:52-53`: `param authKitAudience string = 'https://${trustDomain}'`, passed to the module (line 266).
- `src/Platform/Deployment/modules/functions.bicep:43-44` (`param authKitAudience string`), line 107 (`Auth__AuthKit__Audience: authKitAudience`). Lists become indexed settings with `toObject(range(...))` (`MachineAuth__DisabledCredentialKinds__${i}`, lines 128-132), merged with `union(...)` (line 238). The template owns the whole app-settings collection.
- `parameters/prod.bicepparam:27-28` keeps the default audience, "an open point until confirmed".

### Documentation

- `docs/Platform/Storyteller/authentication.md`: the AuthKit settings table (line 39), the example (line 52), "How AuthKit tokens are checked" (line 87), "AuthKit dashboard setup" step 3 (line 217), the token exchange's `SubjectAudiences` row (line 841).
- `docs/Platform/Deployment/decisions.md:44-45` (points 3 and 4 of the plan), `cookbook-azure-production.md` (8.3 Application settings, lines 287, 303, 327; step 13, line 492; open points, line 620), `runbook-azure-production.md:232` (step 5, WorkOS).

### WorkOS behavior

From WorkOS's docs, blog and SDKs (read 2026-11-18), and the WorkOS workspace through its API, read-only:

| Question | Answer | Source |
|---|---|---|
| `aud` of a session token without Multiple Applications | None. It comes only from the JWT template. | Blog [Verify WorkOS access tokens in your own API](https://workos.com/blog/verify-workos-access-tokens-in-your-own-api): "AuthKit session access tokens do not carry an `aud` claim by default"; the [access token reference](https://workos.com/docs/reference/authkit/session-tokens/access-token) has none. |
| `aud` with Multiple Applications | "Tokens carry an `aud` identifying the application." The value (the client id, a URL, an array) is **not documented**. WorkOS's emulator mints `aud = client_id`. | The same blog; [workos/emulate](https://github.com/workos/emulate) (`src/workos/routes/auth.ts`). |
| Template `aud` and application `aud` together | **Not documented.** `aud` is not a reserved template key (`iss`, `sub`, `exp`, `iat`, `nbf`, `jti`, `act` are), so a template may set it. The emulator lets template claims win, derived from the docs, not from production. | [JWT templates](https://workos.com/docs/authkit/jwt-templates); emulator PR #32. |
| Scope of the template | One per environment, none per application. It renders into every token of a sign-in or consent flow: session tokens of every application **and** Connect tokens of OAuth applications and MCP clients. | [JWT templates](https://workos.com/docs/authkit/jwt-templates). |
| `iss` | The same for all applications; it references the default application's client id: `https://api.workos.com/user_management/<default client id>`, or the custom auth domain's equivalent. | [Applications](https://workos.com/docs/authkit/applications), "Access token claims"; authkit-nextjs README. |
| `client_id` | "Will reflect the `client_id` for the current application context": it differs per application. | [Applications](https://workos.com/docs/authkit/applications). |
| Keys | One key pair per environment for every token family (session, Connect, M2M, agent); the JWKS URL is keyed by a client id. A valid signature does not prove a 2S session token. | [JWKS reference](https://workos.com/docs/reference/authkit/session-tokens/jwks); workos-node `user-management.ts`. |
| Authorization code, refresh, device code | One endpoint (`user_management/authenticate`) and one token family for a client id; the same `aud` is likely, not documented. | [CLI Auth](https://workos.com/docs/authkit/cli-auth). |
| Single sign-on across applications | "A unified login experience"; not stated outright, and session policy is per application. | [Applications](https://workos.com/docs/authkit/applications). |
| Resource indicators | A Connect (MCP) feature: the token's `aud` is the requested `resource`. The User Management endpoints have no `resource` parameter, so the default indicator should not change session tokens (inferred). | [MCP](https://workos.com/docs/authkit/mcp). |
| Turning it on | Applications are created in the dashboard; the changelog and the blog still say "reach out" to WorkOS to manage several. | [Changelog](https://workos.com/changelog/multiple-applications). |

The workspace (checked 2026-11-18, read-only):

- **Production**: one AuthKit application, "2S Platform", the default, its client id the environment's (`client_01M3MMVAP9HG798GFH9WS2NB8J`). The JWT template sets `"aud": "https://2s.42for.net"`. No Connect applications, no resource indicators, no custom domain.
- **Staging**: one AuthKit application, the default (redirects to `localhost:5173` and `localhost:7071`). The JWT template has **no `aud`**. One dynamically registered Connect client (MCP Inspector) and one default resource indicator `https://localhost:7180/mcp`.
- No read operation shows a "Multiple Applications" switch; neither environment has a second AuthKit application.

What this means here:

- The audience list is the WorkOS-recommended gate, but the production value is unknown until a real token is decoded. Two things are open: the application `aud`'s value, and whether the environment template's fixed `aud` overrides it. If the template wins, every product's tokens carry `https://2s.42for.net`, and no audience list can separate them. Then either the template drops `aud` (if WorkOS then adds the application's) or the `client_id` fallback is needed.
- `client_id` is the only per-application claim WorkOS documents. So it is the fallback the issue names, and it does not depend on the template.
- The staging check has to copy production's template `aud` into the staging template first, or it cannot observe the combination.

## Proposed Changes

### Phase 1: A list of audiences in the API

#### 1. `AuthKitOptions` (Backend.Core)

- New `public string[] Audiences { get; set; } = [];`: the audiences of the 2S applications. A user token must carry one of them.
- `Audience` stays, documented as the single value of earlier releases.
- New `public IReadOnlyList<string> GetAudiences()`, the **effective** audiences, one rule for every reader:
  1. `Audiences` has a non-blank entry: its non-blank entries, trimmed, without duplicates (ordinal). `Audience` is then ignored.
  2. Otherwise a non-blank `Audience`: `[Audience]`, as configured (not trimmed, as today).
  3. Otherwise empty: the audience is not checked (as today).

#### 2. `UserAuthenticationOptionsValidator`

- `ValidateAuthKit` collects its failures, as `ValidateKeycloak` does, and adds: "`Auth:AuthKit:Audiences` entries must not be blank." A blank entry is an operator mistake (an empty `Auth__AuthKit__Audiences__1`): failing the start beats silently dropping it.
- No other rule: an audience may be a URL or a client id (`client_…`), so its shape is not checked.

#### 3. `AuthKitTokenSource`

- The constructor takes `IReadOnlyCollection<string>? audiences` in place of `string? audience`; the property becomes `Audiences`. `null` or empty: the audience is not checked. The own-audience overload passes `Audiences` on.
- Callers: the bearer validator passes `GetAudiences()` (or `null` when empty); the machine validator passes `[authKit.ClientId]`; the exchange's additional issuers pass `null`. The machine and subject rules do not change.

#### 4. `AuthKitBearerTokenValidator`

- Builds its source with `authKit.GetAudiences()`. The header comment names `Auth:AuthKit:Audiences` and the legacy `Audience`.

#### 5. `TokenExchangeOptionsValidator.GetApiAudiences`

- For AuthKit, adds every effective audience **and** a non-blank `Audience` (trimmed). The legacy value stays an API audience for this check even while the list overrides it: the deployment keeps it (section 8), it is the shared template audience, and an actor that listed it in `SubjectAudiences` would make the tokens of every application with that `aud` exchangeable. The comparison stays as it is (trimmed, without a trailing `/`, ignoring case).

#### 6. Not changed

- `AuthKitMachineTokenValidator` (M2M tokens keep the `ClientId` audience; they do not come from a sign-in or the JWT template, and `sub == client_id` and the machine organization already bind them), `AuthKitUserSubjectTokenValidator` (the actor's `SubjectAudiences`), the middleware's actor check, the discovery endpoint, the MCP server (its own `Mcp:Authentication:Audience`).
- No `client_id` allow-list: see Decisions.

#### 7. Tests

- `Access.AuthKit/test/AuthKitBearerTokenValidatorTests.cs` (signed RS256 tokens, the existing fixture; `aud` as an array through the raw-payload helper):
  - a listed audience passes; an unlisted one fails; a token without `aud` fails;
  - the legacy `Audience` alone behaves as today (the existing theory stays);
  - the list wins: with both set, a token with only the legacy audience fails, and one with a listed audience passes;
  - an `aud` array with one listed member passes; one with no listed member fails;
  - a listed audience with or without a trailing `/` matches (IdentityModel's default, pinned); another case does not.
- `Access.AuthKit/test/AuthKitRegistrationTests.cs`:
  - `GetAudiences()`: list, trimming and duplicates, the legacy fallback, empty;
  - a host starts with `Auth:AuthKit:Audiences:0` and `:1`, and both validators registered bind the list once;
  - a blank entry stops the start with the message; the AuthKit failures are reported together.
- `Backend.Core/test/TokenExchangeOptionsTests.cs`: an actor's `SubjectAudiences` naming a listed audience (also with a trailing `/` or another case) fails; naming the legacy `Audience` fails while a list is set; another audience passes.
- `Api.Functions/test/AuthKitAudienceAuthenticationTests.cs` (new, on the pattern of `AuthKitImpersonationAuthenticationTests`: the real AuthKit validator, the validating path in every build, a shared issuer): a `user_…` token of another application's audience gets **401**, `next` is not called, and the AuthKit machine validator of the shared issuer does not take it; a token of a 2S audience is accepted.

### Phase 2: Deployment

#### 8. Bicep

- `main.bicep`: `param authKitAudiences string[] = []` ("the audiences of the 2S applications; when set, the API ignores `authKitAudience`"), passed to the Functions module.
- `modules/functions.bicep`: `param authKitAudiences string[]`; `Auth__AuthKit__Audiences__${i}` built with `toObject(range(0, length(authKitAudiences)), …)` and merged with `union(...)`. `Auth__AuthKit__Audience` stays: a rollback to a build that reads only the single value then keeps checking an audience instead of none.
- With the default `[]` the compiled app settings are those of today.
- `parameters/prod.bicepparam`: keeps the default, with a comment: the list is set in the release that enables Multiple Applications, from the staging findings.
- Validated as the `Platform.Deployment` workflow does: `az bicep build` of both entry points and `build-params` of both parameter files.

### Phase 3: Documentation

#### 9. `docs/Platform/Storyteller/authentication.md`

- Settings table: a row for `Auth:AuthKit:Audiences:*` (the list wins over `Audience`; blank entries refused; empty both: no check); `Audience` as the single value of earlier releases.
- Example values with `Auth:AuthKit:Audiences:0` and the app setting `Auth__AuthKit__Audiences__0`.
- "How AuthKit tokens are checked": any `aud` of the token must be one of the effective audiences; one listed value suffices; the list must not hold the shared JWT template audience once Multiple Applications is on.
- "AuthKit dashboard setup", step 3: the JWT template's `aud` and Multiple Applications.
- The `SubjectAudiences` row of the token exchange: names `Auth:AuthKit:Audiences` and the legacy value.
- A short section "Several WorkOS applications": what to list, the shared issuer of all applications, the template's `aud`, the `client_id` claim, machine tokens out of scope.

#### 10. `docs/Platform/Deployment`

- `decisions.md`, point 4 of the plan: the API reads `Auth:AuthKit:Audiences` (424-239), what staging has to confirm, and the order.
- `runbook-azure-production.md`: step 5 (WorkOS) points to a new section **"Enabling Multiple Applications in WorkOS"**: the staging check (section 11), then in one release: deploy the API with this change; set `authKitAudiences` in `prod.bicepparam` and deploy the Bicep (review what-if); enable Multiple Applications in WorkOS production; change the JWT template if staging says so; verify the admin UI and `sform` after a fresh sign-in and after a refresh; verify that another application's token gets `401`. Rollback: keep the single value; a build that reads only `Audience` refuses the new tokens if WorkOS drops the template's `aud`.
- `cookbook-azure-production.md`: 8.3 (the settings table row of `Auth__AuthKit__Audiences__*`), step 13, and the open point of the shared WorkOS environment (step 19).
- `src/Platform/Deployment/README.md`: the parameter.

#### 11. The staging check (by hand, before the production release)

The issue's step 1 needs WorkOS dashboard changes and real sign-ins, so it is an operator task, not part of the code. The runbook section lists it as a checklist, and its findings go into the issue:

1. Add production's `"aud": "https://2s.42for.net"` to the **staging** JWT template (it has none today), so staging behaves like production. Sign in once to the local admin UI and note the token's `aud` before anything else changes.
2. Enable Multiple Applications in staging (the dashboard, or WorkOS support if the dashboard offers no second application) and create a second application ("another product").
3. Sign in to the local admin UI (`npm run dev` against staging) and with `sform account` (a clean `SFORM_HOME`); decode both access tokens without logging them whole: `iss`, `aud` (a string or an array), `client_id`, `exp`.
4. Get a token of the second application and decode it the same way.
5. Refresh both 2S tokens and decode again (also a session started before step 2). Remove the template's `aud`, sign in again, decode again.
6. Sign in to the second application with the same user: is the WorkOS session reused?
7. With the template's `aud` in place, get an MCP token of the staging resource indicator (`https://localhost:7180/mcp`): does it carry the resource or the template's `aud`? (For [424-287](https://linear.app/424fun/issue/424-287), which needs the resource.)
8. If machine access is on in that environment, check that an M2M token still carries the environment client id as `aud`.
9. Result: the 2S audience list, whether the template keeps `aud`, and whether the `client_id` fallback is needed (when another application's token still carries a value of the 2S list).

## Decisions

- **The list wins over the single value; no union.** A union would keep the shared template audience accepted while the deployment still emits it, so another product's token carrying that value would pass. With precedence, setting the list is the switch, and the deployment can keep the single value for a rollback.
- **No audience, no check, as today.** The acceptance criteria require unchanged behavior with the single value, and local Aspire runs without an audience. Production always sets one through Bicep.
- **Any-match, IdentityModel's default.** A token passes when one of its `aud` values is listed. The isolation then rests on the list's content: it must not hold the shared template audience after the switch. Requiring every `aud` of a token to be listed would refuse the 2S tokens if WorkOS adds the template's `aud` next to the application's.
- **Machine tokens keep their own rule.** They do not come from a sign-in or the JWT template; their audience, `sub == client_id` and machine organization checks already bind them to Storyteller's M2M applications. AuthKit machine access is off in production.
- **The `client_id` fallback is not built now.** The issue makes it conditional on the staging findings, and WorkOS's verification guidance checks `aud`, not `client_id`. It is still the only per-application claim WorkOS documents, so it is the likely answer if staging shows that the template's fixed `aud` wins over the application's. Then it is a follow-up of a few lines: an `Auth:AuthKit:AllowedClientIds` list checked after the signature, issuer and audience, as `Auth:Keycloak:AllowedClientIds` checks `azp`, with its indexed Bicep settings.
- **The legacy value counts in the exchange's collision check.** See section 5.
- **Against the CodeRabbit plan** (attached to the issue): its precedence rule, empty default, Bicep keeping the single value and the conditional fallback are taken. Changed: `AuthKitTokenSource` does change, because its constructor takes one audience (the plan says it already takes a list); the legacy value stays in the collision check; the middleware test runs the validating path in every build, as `AuthKitImpersonationAuthenticationTests` does, instead of only in Release.
- **One `Issuer`, one JWKS, as today.** WorkOS gives every application of the environment the default application's issuer, and one key pair signs every token of the environment, so a second 2S application (for example one of its own for `sform`) needs only its audience in the list. [424-261](https://linear.app/424fun/issue/424-261)'s list of token sources is not needed for it; that the JWKS of another client id serves the same keys is inferred, which is why the API keeps reading the default application's.

## Phases and commits

1. `docs(platform): plan accepting only the 2S AuthKit audiences in the API (424-239)`: this spec.
2. `feat(platform): accept a list of AuthKit audiences in the API (424-239)`: Phase 1.
3. `build(platform): pass the AuthKit audiences to the API's app settings (424-239)`: Phase 2.
4. `docs(platform): document the AuthKit audiences and the Multiple Applications rollout (424-239)`: Phase 3.
5. `docs(platform): review accepting only the 2S AuthKit audiences (424-239)`: the review.

## Out of scope

- The staging check itself and the production rollout (dashboard changes, by hand; section 11).
- The `client_id` allow-list (see Decisions).
- Several issuers and key sets for several 2S applications ([424-261](https://linear.app/424fun/issue/424-261)).
- The MCP server's audience and its actor ([424-287](https://linear.app/424fun/issue/424-287)); this change only keeps the MCP audience out of the API's list through the existing collision check.
