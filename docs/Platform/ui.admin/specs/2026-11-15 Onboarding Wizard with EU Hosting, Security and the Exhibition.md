# Onboarding Wizard with EU Hosting, Security and the Exhibition

Linear: [424-267](https://linear.app/424fun/issue/424-267/admin-ui-onboarding-wizard-eu-hosting-security-and-federated-auth-join) — Admin UI: onboarding wizard (EU hosting, security and federated auth, join the exhibition, create an organization).

## Problem

A user who signs in without an account sees one page: a form that creates an organization and its first project. The page says nothing about where the data lives, how the platform treats security, or what the platform looks like when it holds data. The first minute of a new user is a blank catalog.

The page becomes a short wizard of four steps:

1. **Welcome**: greets the user by name and states that all customer data, configuration and secrets are stored and processed only in the EU, with the list of sub-processors.
2. **Security**: security is the main focus; machines and pipelines connect with federated auth, people sign in through AuthKit, or through the customer's own Entra ID or Keycloak in a deployment of its own.
3. **Exhibition** (optional): a checkbox that makes the newcomer a **Reader** of the deployment's demo project, the exhibition.
4. **Organization**: the form of today, which creates the account with an organization and a project.

Step 3 needs something the API does not have: nothing lets a user join a project they were not invited to.

## Current State

Paths are relative to the repository root; `UI` is `src/Platform/ui.admin/src`, `ST` is `src/Platform/Storyteller`.

### Admin UI

- `UI/app/pages/onboarding.vue` (44 lines): `definePage({ meta: { layout: 'bare', allowWithoutAccount: true } })`; a `UCard` with the heading `Welcome, <name>`, the paragraph "Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.", `OrganizationForm` with the label **Create and continue** and the link **Sign out**. `submit` calls `createAccount({ Organization, Project, ProjectDescription })`, `session.setAccount(...)` and `router.replace('/{org}/{project}/default/annotations')`.
- `UI/app/components/orgs/OrganizationForm.vue`: owns the form and its values (`useForm`, no initial values, nothing exposed), an `#actions` slot left of its submit button, `autofocus` on the first input, and the error mapping: any `409` of `submit` → "This organization already exists, or you are not its owner." on the organization field, `InvalidName` → the field, anything else → an alert. **New organization** on `/orgs` uses the same form in `OrganizationCreateDialog.vue` with `createAccessPoint`.
- `UI/app/stores/session.ts`: `accountStatus` is `missing` after a `404` of `GET v1/access/account`; `setAccount` makes it `ready`; `accessMap` holds the roles by `org` and `org.project`.
- `UI/app/router/guards/auth.ts:32–42`: a user without an account passes to a route with `allowWithoutAccount`, else goes to `/invitations` (pending invitations) or `/onboarding`; a user with an account who opens `/onboarding` goes to `/`. The guard runs on every navigation, also on one that changes only the query. The other guards (`context.ts`, `page-memory.ts`, `telemetry.ts`) do nothing for a query change of `/onboarding`; telemetry counts a page view only when the path changes.
- `UI/app/boot/auth.ts:70–72`: a deployment whose provider is not AuthKit is `unsupported`, and every route shows `/unsupported`. So the wizard exists only where people sign in through AuthKit.
- `UI/app/App.vue:17` picks the layout from `route.meta.layout`; `UI/app/layouts/BareLayout.vue` has no script: a centered column of `max-w-md` (448 px) under the logo, shared by login, callback, unsupported, invitations and `/`. `test/unit/components/brand.test.ts:45–53` mounts it without a router.
- `UI/app/pages/invitations/index.vue:103–110`: the link **Create my own organization instead** opens `/onboarding` for an invited user without an account.
- A member of a project without a role in its organization is an exercised state (`northwind/portal` of the mock): `/orgs` shows the organization with the badge **Project member** and the project link `<project> Reader`.
- The admin UI locks **Modeling** (schemas, templates, deleted annotations) below Contributor (`pages/[org]/[project]/[view]/modeling.vue:7–21`, `utils/navigation.ts:116`), and shows **Members** and **Monitor** to Administrators only. The API lets a Reader read schemas and templates.
- Nuxt UI 4.11.3 has `UStepper`, but it announces the steps from zero ("Step 0 of 4": `Stepper.vue:70–75` binds a 0-based index to Reka's live region), its triggers react to `mousedown` only, and four titles are at the limit of a 360 px phone. The house pattern of a step list is an `<ol aria-label="Steps">` (`components/key-values/KeyValueImportDialog.vue:217–221`).
- The only documentation link of the UI is in `components/configuration/BindingReference.vue:218–227` (`UButton` with `to`, `target="_blank"`, `trailing-icon="i-lucide-external-link"`, `variant="link"`). Toasts with a link action exist (`components/transfer/TransferToaster.vue:36–54`).

### Mock

- `UI/app/mock/db.ts`: the `MockDb` is JSON in `sessionStorage` (`2s-admin:mock-db:2`). The scenarios `new-user` and `invited` take the `default` seed, remove Ada from every access point and set `account: null`. `scopeSeed` generates annotations for four scopes only. The mock profile of the automation tests and Playwright use `default`.
- `UI/app/mock/handlers.ts:92–126`: `GET` and `POST */api/v1/access/account`; the `POST` answers `409 AccountExists` when the account exists (the API answers `400` without a body).
- `UI/app/mock/access-handlers.ts:391–413`: accepting an invitation raises the role on the access point, creates the account when missing and calls the private `syncAccount`.
- `UI/app/mock/browser.ts`: `window.storytellerMock` has `reset`, `changeConfiguration` and `addMachine`, assigned when the mock has started. The generated Faker handlers of the SDK follow the hand-written ones, so an SDK operation without a hand-written handler is answered with random data.
- `UI/test/unit/mock/start-scenario.test.ts:8` reads the key `2s-admin:mock-db` (without `:2`), so its `toBeNull()` passes whatever is stored.

### API

- `ST/Api.Functions/src/V1/AccessHttp.cs:80–141`: `POST v1/access/account` (`CreateAccount`) refuses an existing account with `400` without a body, and creates an account without memberships for `{}`. A missing name claim is `401` (`GetIdentityProfileAsync`). So an account without an own organization is a supported state; `CosmosInvitationService.AcceptInvitationAsync` relies on it (`EnsureAccountAsync`, then `JoinAccessPointAsync`).
- `ST/Backend.Core/src/Accessing/IAccessService.cs:42–44`: `JoinAccessPointAsync(accessPointKey, accountId, role)` is trusted: it adds the account or raises its role without checking an actor, never lowers a role (`CosmosAccessService.cs:469–482`) and needs an existing access point and an existing account (`UpdateMembershipAsync`, `:817–825`). It replaces both documents in one batch under ETags and tries twice; then it answers `409 Conflict`.
- `CosmosAccessService.CreateAccountAsync` (`:49–95`) throws `ConflictException(AccountExists)` only when its own read finds the account; two requests that both pass the read meet at `CreateItemAsync`, whose `CosmosException` nothing translates (`500`).
- Anyone who signs in may register any organization name that is not reserved and becomes its Owner (`CosmosAccessService.cs:161–193`). The seed runs the same name rules (`DbCreator/src/DataSets/DataSetProvisioner.cs:42`), so a reserved name would refuse the seed too.
- `GET v1/access/points/{key}/members` needs `Administrator` (`CosmosAccessService.GetMembersAsync`, `:367–375`). Any member may remove themselves (`RemoveMemberAsync`, `:446–452`); the admin UI offers that only on the **Members** page.
- Options that can be wrong stop the start: `Api.Functions/src/ConditionalReads/ConditionalReadsEntryPoint.cs` (`AddOptions().Bind().Validate().ValidateOnStart()`, called in `Program.cs:115`). A thrown `NotFoundException` logs a Warning per request; routes that answer "this deployment has none" return `NotFoundResult` (`AccessHttp.cs:569–572`).
- The OpenAPI document takes `required` of a property from `[JsonProperty(Required = Required.Always)]` (`V1/Models/MachineTokenResponse.cs`), not from the C# `required`.
- `ST/Api.Functions/test/OpenApiExportTests.cs` requires every operation in one of its tables (`UserOnlyOperations`, `:30–79`) and `OpenApi/OperationAccess.cs` to match; it also exports the OpenAPI document (`STORYTELLER_OPENAPI_EXPORT`), from which both SDKs are generated (`ST/sdk.typescript`, `ST/Sdk.NSwag`, snapshot `open.api.v0.10.34.json`). No workflow runs `Api.Functions/test`.
- Deployment: the app settings of the Function App are in `src/Platform/Deployment/modules/functions.bicep:83–220`; an optional setting follows the `sentryDsn` pattern. A merge to `main` that touches `src/Platform/ui.admin/**` publishes the admin UI to production (`.github/workflows/Platform.UI.Admin.yml`, job `deploy`); the API is published by hand (runbook 11.1).
- The data set `exhibit` (`ST/DbCreator/src/data-sets/exhibit`) targets `fourth-wall` / `toons-and-tyrants` / `default`; `2sInitDb seed exhibit --organization <o> --project <p> --owner <account>` seeds it elsewhere and makes the owner the Owner of what it creates. The seed ignores the set's `key-values.json` until 424-265 (a resolved read of many of its configurations answers `422` without the pairs), and the type names of a project live in the browser until 424-266.

### Tests and test cases

- `UI/test/e2e/onboarding.spec.ts`: two tests fill the form straight after opening `/onboarding`. `accessibility.spec.ts:94–97` runs axe on `/onboarding`; `visual.spec.ts:56` has the screen `onboarding` (four baselines, made in the Linux container).
- `UI/test/unit/router/guards.test.ts` covers the guard; no unit test mounts the page. `test/helpers.ts:123–143`: `mountScreen` always sets an account and installs no guard.
- `src/Platform/UI.Admin.AutomationTests/src/Tests/Access/SignInTests.cs:179–220`: ACC-SGN-06 (Testiny 21), mock only, with `Pages/OnboardingPage.cs`. NAV-RTE-03 (Testiny 120) relies on `/onboarding` → `/` for a user with an account. Every pool user has an account, so onboarding is tested on the mock only.
- Testiny `2SP` has 233 cases; the counts are written in `CLAUDE.md`, `docs/Platform/UI.Admin.AutomationTests/test-cases.md` and `automation-tests.md`.

### The copy and what it relies on

The issue gives the text of step 1 and asks to verify it before it is written. What the repository says:

- **The data.** All Azure resources are in `swedencentral`; only the metadata of the Static Web App is in `eastus2` and the admin UI is served from the global edge (`docs/Platform/Deployment/cookbook-azure-production.md:34,362–368`, `decisions.md:15,72`). Sentry is `de.sentry.io`, PostHog `eu.i.posthog.com`. The statement is about the data, not about the static files. True today.
- **WorkOS.** The text says WorkOS "is used only to sign in administrators of the 2S Admin UI". Production also sends WorkOS the email address of every invited person: with the management key (`parameters/prod.bicepparam:33`, `authKitApiKeyEnabled = true`) the API creates a WorkOS invitation and WorkOS sends the email (`Access.AuthKit/src/AuthKitInvitationSender.cs:35–37`, `docs/Platform/Storyteller/access.md:157`). `sform` and the MCP server sign the same people in through AuthKit.
- **The three safeguards.** "Impersonation is blocked" (424-262), "access to secrets requires a recent sign-in" (424-264) and "every access change is in an append-only audit log" (424-263), and so "recorded" in "made and recorded", are in progress, not on `main`: 424-262 has code on its branch, 424-263 a plan, 424-264 nothing yet. The issue is blocked by all three.
- **An own identity provider.** The decision record of 424-262 (`docs/Platform/Deployment/decisions.md` on `fix/424-262`) words it for the trust page: a dedicated deployment signs people in with "a Keycloak server, which can be hosted in the European Union, or their own Microsoft Entra ID tenant. Such a deployment uses one identity provider, and the admin web application is not available in it." It also notes that Microsoft is a US company, though the tenant and its admins are the customer's.

## Proposed Changes

### 1. The exhibition in the API

#### 1.1 Settings (`ST/Api.Functions/src/Exhibitions/ExhibitionOptions.cs`, `ExhibitionEntryPoint.cs`)

| Setting | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Exhibition:Organization` | `Exhibition__Organization` | empty | The organization of the exhibition project |
| `Exhibition:Project` | `Exhibition__Project` | empty | The exhibition project |
| `Exhibition:Owner` | `Exhibition__Owner` | empty | The id of the account that owns the exhibition project: the `--owner` of its seed |

```csharp
public class ExhibitionOptions
{
    public const string SectionName = "Exhibition";

    public string? Organization { get; set; }

    public string? Project { get; set; }

    public string? Owner { get; set; }

    // All three are set: the deployment has an exhibition.
    public bool IsConfigured { get; }

    // None is set, or all three are and both names are valid names (NameRules.IsValidName).
    public bool IsValid { get; }

    // "organization.project"; only for a configured exhibition.
    public string ProjectKey { get; }
}
```

All three, or none: without them the deployment has no exhibition. `services.AddExhibition(configuration)` (called in `Program.cs` next to `AddConditionalReads`) binds the section and validates it at the start (`ValidateOnStart`): one or two of the three, or a name that is no valid name, stops the start with a message that names the settings. The names are compared as written; a valid name is lower-case.

**Why an owner.** The join makes every caller a member of whatever project the settings name, and anyone may register a free organization name. Settings that are live before the seed (a wrong order of the runbook, a typo, a self-hosted deployment) would hand "the exhibition" to the first person who registers the names: their description would show in every newcomer's wizard, and they would list every joiner's name and email. With `Exhibition:Owner` both routes work only when that account is the **Owner** of the project, so a project somebody else created is never offered or joined. A reserved name is no alternative: the seed could not create it either.

#### 1.2 Routes (`ST/Api.Functions/src/V1/ExhibitionHttp.cs`, new)

| Method | Route | Operation | Answer |
| --- | --- | --- | --- |
| GET | `v1/access/exhibition` | `GetExhibition` | `200` `Exhibition`, or `404` |
| POST | `v1/access/exhibition/join` | `JoinExhibition` | `200` `Account`, or `404`, `409` |

`access` is a reserved organization name, so the literals meet no organization route. Both are user-only operations (`request.EnsureUser()`: a machine gets `403`), with the two `OpenApiSecurity` attributes, the `401` of `ResponseUnauthorizedUserOnly` and the `403` of `ResponseForbiddenUserOnly`, as every user-only operation. Neither has a request body. `Definitions.cs`: `Routes.Access.V1.Exhibition`, `Routes.Access.V1.ExhibitionJoin`, `RouteIds.Access.GetExhibition`, `RouteIds.Access.JoinExhibition`. `OpenApi/OperationAccess.cs`: both in `UserOnly`.

`Exhibition` (`ST/Api.Functions/src/V1/Models/Exhibition.cs`); `Organization` and `Project` carry `[JsonProperty(Required = Required.Always)]`, so the generated types have them without `?`:

```csharp
public record class Exhibition
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    // The description of the project, when it has one.
    public string? Description { get; init; }
}
```

**The exhibition of a request.** Both routes resolve it the same way:

1. Not configured → `new NotFoundResult()` (`404` without a body, nothing logged: this is every newcomer of a deployment without an exhibition).
2. `GetAccessPointAsync(ProjectKey)`. No such project, or `Exhibition:Owner` is not its `Owner` → the same `404`, and a Warning that names the settings (the deployment is misconfigured, or not seeded yet).

`GetExhibition` then answers `200` with the two names and the description of the project. The exhibition is the view `default` of the project; the seed puts it there.

`JoinExhibition`, after `EnsureUser()` and the two steps above (so nothing is written for an exhibition that cannot be joined):

1. `accountId = request.GetRegistrationAccountId(provider)`, the id a registration uses (as `PostAccount` and the acceptance of an invitation).
2. No account: `CreateAccountAsync(new AccountCreate { IdentityId, UserName, Name })` without an organization, with the names of `GetIdentityProfileAsync`. A token without the names is `401`, as for `PostAccount`, and nothing is created (an account with empty names would be a blank row of the member list, and has no stored user name to compare a later token with, 424-262). When the create throws, the handler reads the account again and goes on when it exists now (a concurrent request created it: the `ConflictException` of the service's own read, or the `CosmosException` of the write); it rethrows only when the account is still missing.
3. `JoinAccessPointAsync(ProjectKey, accountId, AccountRole.Reader)`: idempotent, and a higher role stays. A `ConflictException` with `ErrorCodes.Conflict` (the document changed twice while the service tried) is tried again up to three more times, each after a short random delay (50–250 ms); then the `409` goes out. Every newcomer writes the same document, so the two attempts of the service, sized for an administrator's change, are thin at a launch peak.
4. `200` with `GetAccountAsync(accountId)`, the account with the membership.

Steps 2 and 3 are two writes. A join that fails after its account was created leaves an account without memberships; a second call joins and creates nothing. The wizard handles that state (3.6).

The role and the key never come from the request: `JoinAccessPointAsync` is trusted, and the route gives exactly `Reader` on exactly the configured project. The membership is the project's only: the account gets no role in the organization.

The orchestration stays in the HTTP class; `IAccessService` and `CosmosAccessService` do not change.

Through an actor (a Storyteller user token, the MCP server) the join works as for the person: an actor may grant `Reader`, and its token names an account that exists.

#### 1.3 What a visitor can read, and known limits

- **A Reader of the exhibition reads**, through the API: annotations and their history, deleted annotations, configurations (resolved without secret values), schemas, templates, the binding catalog, key/value pairs without secret values with their versions, and the key/value policy of the project, which names the key of the deployment's vault (`KeyVaultKeyProvider.cs:218`: a URI and a key name, no key material). The version lists carry the name and the account id of whoever changed something. The Reader does not read members, machines, the monitor or secret values. So the exhibition is changed only by its seed (actor `System`): a change by hand publishes the editor's name to every visitor. `access.md` documents the list.
- **In the admin UI** a Reader does not see **Modeling**: the exhibition cannot show its schemas and templates to a visitor there. Opening Modeling to Readers is its own change; the copy of step 3 promises only what a Reader sees (3.5).
- **A visitor cannot leave in the admin UI**: the action is on the **Members** page, which Administrators see. `DELETE v1/access/points/{key}/members/{own account id}` removes the membership. A leave action for a member below Administrator is its own change.
- **Size.** Every visitor adds one entry to the `AccessMap` of the project (about 50 bytes; a Cosmos DB item holds 2 MB), every join rewrites the whole document, and the member list is not paged. At the item limit a join fails with `500`. Fine for a demo of thousands of visitors; not reviewed beyond that.
- **The demo is as complete as its seed**: until 424-265 the key/value pairs come from `sform story kv import`, and until 424-266 a visitor sees the default type names.

#### 1.4 Tests

- `ST/Api.Functions/test/ExhibitionHttpTests.cs` (new, with a recording fake of `IAccessService` as `MembersHttpTests`): no settings → `404` without a body on both routes and no call of the service; a configured project that does not exist → `404`, nothing written; a project whose Owner is another account → `404`, nothing written; `GetExhibition` answers the names and the description; a user without an account → a bare account under the registration id with the names of the token, then the join as `Reader`, and the account in the answer; a user with an account → no create; a create that throws while the account appears → the join goes on; a create that throws while the account stays missing → the exception; a token without names → `401`, nothing created; `Conflict` twice, then the join → `200` after three calls; `Conflict` four times → `409`; a machine is refused (`UserOnlyRoutes.ShouldRefuseMachineAsync`); under Entra ID an id that is not `{tid}:{oid}` fails as for `PostAccount`.
- `ST/Api.Functions/test/ExhibitionOptionsTests.cs` (new): none and all three are valid; one or two are not; an upper-case or dotted name is not; the start validation of `AddExhibition` fails for them.
- `OpenApiExportTests`: both operations in `UserOnlyOperations`, their paths, tag and response codes, and `Organization` and `Project` required in the schema `Exhibition`. `RouteTableTests`: the two routes are registered.
- `ST/Backend.CosmosDb/test/CosmosAccessServiceTests.cs`: one test of the state the wizard creates: a bare account joins a project as `Reader`; `GetProjectsAsync` lists it, `GetAccessPointsAsync` is empty, and a second join changes nothing.

### 2. OpenAPI and the SDKs

After the API commit: export the document (`STORYTELLER_OPENAPI_EXPORT`, no `Auth__*` variables set), copy it as `open.api.v0.10.<n>.json` to `ST/Sdk.NSwag` and `ST/sdk.typescript` (the superseded TypeScript copy is removed), regenerate the TypeScript SDK (`npm run generate`: `getExhibition`, `joinExhibition`, the model `Exhibition`, their Vue Query, Zod and MSW counterparts) and `ApiSdk.g.cs` with NSwag 14.6.3 (`GetExhibitionAsync`, `JoinExhibitionAsync`), bump the file name in both READMEs and add the two functions to the table of `sdk.typescript/src/test/msw.test.ts`.

`sform` and the MCP server get nothing: a command of `sform` would need cases in Testiny `2SC` (`CommandCoverageTests`), and neither creates accounts for newcomers.

### 3. The wizard (`UI/app/pages/onboarding.vue`)

#### 3.1 Steps and their address

The steps are `welcome`, `security`, `exhibition` and `organization`. The page asks `getExhibition()` once, with a timeout of 5 seconds, and shows `LoadingState` until the answer, so the list of steps never changes under the user:

- `200`: four steps.
- `404`: three steps, without `exhibition`.
- Anything else (an error, the timeout): four steps; step 3 shows `ErrorState` with **Retry** in place of its content, and nothing can be checked until the exhibition loads.

The current step is in the query by name: `?step=security`, `?step=exhibition`, `?step=organization`; no value is `welcome`, and an unknown name, or `exhibition` in a deployment without one, is the first step. The choice of step 3 is next to it (`join=1`), written with `router.replace`. **Next**, **Back** and the step list call `router.push({ query: { ...route.query, step } })`. So the browser's back and forward move between the steps (the back gesture of a phone included), a reload keeps the step and the choice, and a test opens `/onboarding?step=organization` whatever the number of steps. The guard lets every such navigation pass while the account is missing; `mock-scenario` stays in the query, which the mock reads once per page load.

The step list is the page's own, in the house pattern: `<nav aria-label="Onboarding steps"><ol>` of buttons, one per step, with its number in a circle and its name (`Welcome`, `Security`, `Exhibition`, `Organization`); the accessible name is `Step <n>: <name>`, the current one has `aria-current="step"`. Below `sm` only the current step shows its name; the others are their numbers. Choosing an item goes to that step: steps 1 to 3 hold nothing to validate, so every step is reachable at any time. `UStepper` is not used (Current State).

The card of every step: the step list, the content with an `h2` heading, and the buttons **Back** (not on the first step) and **Next** (not on the last, where the form has **Create and continue**). The page's `h1` above the card is the welcome line, on every step: `Welcome, <name>`, or `Welcome` without a user (`ph-no-capture`: the name is personal data). Below the card, on every step: the link **Sign out**, as today.

Focus: after a change of step the `h2` of the new step gets the focus (`tabindex="-1"`), so a screen reader announces it; on the organization step the first field of the form gets it instead. The first load focuses nothing, on any step: `OrganizationForm` gets the prop `autofocus` (default `true`, for the dialog), and the wizard passes `false`.

**History.** Each visited step is an entry of the history. After the wizard ends, the browser's back opens such an entry, which the guard answers with `/` and the last context: the user stays where they are, once per step they visited. One more back leaves the app as today. Going back over the entries before the final navigation would put more navigation into the one moment that must not fail; the limit is accepted and tested (back never shows the wizard again).

#### 3.2 The wide column (`App.vue`, `BareLayout.vue`, `router/meta.d.ts`)

`RouteMeta.wide?: boolean`. `App.vue` passes it to the bare layout as a prop (`:wide="route.meta.wide === true"` on the bare layout only); `BareLayout` gets `defineProps<{ wide?: boolean }>()` and its column is `max-w-2xl` instead of `max-w-md`. The layout stays free of the router. Only onboarding sets it; the other bare pages keep their width.

#### 3.3 Step 1: Welcome (`components/onboarding/OnboardingWelcome.vue`)

- The main message is the heading of the step and cannot be missed: a callout in the primary color with a shield icon (`i-lucide-shield-check`), the `h2` **Customer data stays in the EU** and, in bold, "All customer data, configuration and secrets are stored and processed only in the EU (Sweden)."
- The rest of the text as a paragraph (see the table below for its two forms).
- The sub-processors as a list of four entries (`<ul aria-label="Sub-processors">`): the name, a badge with the region (`EU only` in the success color, `USA` in neutral) and the purpose. A list, as the issue asks, not a table: three columns do not fit a phone, and the purpose of WorkOS is long.

| Sub-processor | Purpose | Region |
| --- | --- | --- |
| Microsoft Azure | Hosting, database, storage, secrets | EU only |
| Sentry | Error reporting | EU only |
| PostHog | Product analytics | EU only |
| WorkOS | Sign-in of the administrators of the 2S Admin UI and their invitation emails (name, email, IP address, session data) | USA |

- A link in the style of `BindingReference`: **Details of the EU hosting** → `https://claude.ai/share/c9fbe93d-61c4-466c-8490-2ba028fb1999`, the discussion the issue names.

The texts, the sub-processors and every link of the wizard are constants of one module, `UI/app/utils/onboarding.ts`, so a change of the wording is one file. The copy never says that WorkOS cannot gain access: whoever controls the identity provider can sign in as a user; the safeguards make that visible and limited.

**The text, sentence by sentence.** It differs from the issue's text where the verification of Current State found the text ahead of the facts:

| Sentence of the issue | In the wizard | Why |
| --- | --- | --- |
| "All customer data, configuration and secrets are stored and processed only in the EU (Sweden)." | The same, in bold. The heading is **Customer data stays in the EU**. | "Your data" would be wider than the sentence: the reader's own name and email go to WorkOS. |
| "WorkOS (USA) is used only to sign in administrators of the 2S Admin UI and processes only their name, email, IP address and session data." | "WorkOS (USA) is used only to sign in administrators of the 2S Admin UI and to send their invitation emails. It processes only their name, email, IP address and session data, and the email address of a person they invite." | Production sends WorkOS the address of every invited person. |
| "Every authorization decision is made and recorded by 2S in the EU. Impersonation is blocked, access to secrets requires a recent sign-in, and every access change is in an append-only audit log." | While `safeguardsLive` is `false`: "Every authorization decision is made by 2S in the EU." Once it is `true`: the issue's two sentences. | The three safeguards are not in production (8). |
| "Customers that need an EU-only identity provider can use Entra ID or Keycloak." | The same. | Step 2 says what such a deployment is. |

#### 3.4 Step 2: Security (`components/onboarding/OnboardingSecurity.vue`)

Heading **Security first**. The text:

- "Security is the main focus of the 2S Platform."
- **Machines and pipelines** — "They need no stored secret. A machine exchanges its own platform token, for example a GitHub Actions OIDC token or a Kubernetes service account token, for a short-lived 2S token."
- **People** — "This platform signs you in with WorkOS AuthKit. A dedicated deployment can sign people in with an identity provider of your own instead:" then the list "**Entra ID** in your own tenant" and "**Keycloak**, self-hosted, for example in the EU", and "Sign-in is then fully under your control, and WorkOS is not involved. Such a deployment is used through sform and the API; the Admin UI is not available in it today."

The issue's bullet ends "no US identity provider is involved". The wizard says "WorkOS is not involved" and names the limit, in step with the decision record of 424-262: Microsoft is a US company too, and the application that shows this text does not run in such a deployment.

Links (new tab, external-link icon):

| Label | Address |
| --- | --- |
| Authentication: Entra ID and Keycloak | `https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#selecting-the-provider` |
| Federated auth of machines | `https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-access-with-workload-identity-federation` |
| sform | `https://42for.net/cli/sform` |
| GitHub Action | `https://42for.net/cli/github-action` |

#### 3.5 Step 3: Exhibition (`components/onboarding/OnboardingExhibition.vue`)

Heading **Join the exhibition**. The text: "The exhibition is a demo project with the data of a fictional company. It shows the core concepts of the platform at work. You join as a Reader: you can browse its annotations, their configurations and the graph, and you cannot change anything." Under it, the description of the project when the API gives one.

A `UCheckbox` (unchecked at first): label **Join the exhibition**, description "Adds you to `<organization>/<project>` as a Reader. Its administrators see your name and email in the list of members; other visitors do not."

A note: "This step is optional. To join only the exhibition and create your own organization later, check the box and choose **Continue with the exhibition only** in the last step."

Nothing is sent here. The choice is applied when the wizard ends (3.6), so going back and unchecking has nothing to undo.

#### 3.6 Step 4: Organization, and the end of the wizard

Heading **Create your organization**, the paragraph of today ("Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.") and `OrganizationForm` with **Create and continue** and **Back** in its `#actions` slot. The form stays mounted while another step shows (`v-show`), so its values survive **Back** and **Next**.

When the exhibition is checked, under the form: "You are joining the exhibition." and the button **Continue with the exhibition only**.

| The user | Calls | Then |
| --- | --- | --- |
| submits the form, exhibition unchecked | `createAccount` (as today) | `/{org}/{project}/default/annotations` |
| submits the form, exhibition checked | `createAccount`, then `joinExhibition` | the same, with the toast "You joined the exhibition" and its action **Open the exhibition** |
| **Continue with the exhibition only** | `joinExhibition` (it creates the account) | `/{exhibition org}/{exhibition project}/default/annotations`, with the toast "You joined the exhibition" |

`createAccount` comes first: the join would create a bare account, and `createAccount` refuses an existing one.

**States.** The page is `idle`, `working` or `joinFailed`:

- `working` (a request of the end is in flight): the step list, **Back**, the form's submit (a new prop `disabled` of `OrganizationForm`) and **Continue with the exhibition only** are disabled, so the two ways to end cannot run at once.
- The account that `createAccount` or `joinExhibition` answers is kept in the page. `session.setAccount` is called once, immediately before the final `router.replace`: an account in the session would make the guard send the next navigation of the wizard to `/`.
- A failed `createAccount` shows in the form as today (the page rethrows it into `OrganizationForm`); nothing was joined, the page is `idle` again.
- A failed `joinExhibition` after a created account never reaches the form (its `409` would read "This organization already exists…"). The page becomes `joinFailed`: in place of the step list and the steps, an alert **Joining the exhibition failed** ("Your account is ready." and the message of the error) with **Try again** (`joinExhibition`, then the end of the table) and **Continue without the exhibition** (to the new project).
- A failed `joinExhibition` of **Continue with the exhibition only**: the page reads `getAccount()`. An account exists (the join failed after it was created, 1.2) → `joinFailed`, where **Continue without the exhibition** leads to `/orgs`. No account → an alert under the button with the message; the page is `idle`, and the button tries again.
- In `joinFailed` a reload ends the wizard on `/` (the account exists), without the exhibition.

A user who joined only the exhibition has an account: `/onboarding` sends them to `/` from then on (the guard is unchanged), and they create an organization with **New organization** on `/orgs`, which exists.

**Known limit.** The exhibition is offered here only. A user who ends the wizard without it (unchecked, **Continue without the exhibition**, a reload in `joinFailed`) has no way to join later in the admin UI; `POST v1/access/exhibition/join` works for an existing account. An entry on `/orgs` is its own change.

#### 3.7 Tests of the admin UI

- Unit (`UI/test/unit/onboarding/onboarding-page.test.ts`, new). The page is mounted through the routes on the mock handlers (`resetDb('new-user')`), with the boot state of a signed-in user (`setBootState`, as `guards.test.ts`), no account in the session and `authGuard` installed: `mountScreen` gets the options `account` (`null`: none) and `guards`. Cases: four steps with the exhibition, three after a `404`, four with **Retry** after an error; **Next**, **Back**, the step list and the query names; an unknown step; the choice in the query; the form keeps its values over **Back**; the focus after a change of step and none on load; create alone, create and join (the order of the two requests), join only; each failure of 3.6, with a step change that the guard would redirect; the two requests cannot run at once; the name in the heading; both forms of the safeguards sentence (`utils/onboarding.ts`).
- Unit: `brand.test.ts` gets the wide column (`props: { wide: true }`); `organization-form.test.ts` the props `autofocus` and `disabled`; `guards.test.ts` is unchanged.
- e2e `onboarding.spec.ts`: the two tests of today walk through the steps first; new: the steps forward and back with the buttons, the step list and the browser's back, and back after the end never shows the wizard; a join with an own organization (on `/orgs` the organization of the exhibition with **Project member** and the project link `exhibition Reader`; in the project, rows of the catalog and none of **New**, **New annotation** and **Bulk create**); a join only, followed by **New organization**; three steps without an exhibition (`openApp('/onboarding', 'new-user')`, `window.storytellerMock.setExhibition(null)`, `goto('/onboarding')`).
- `accessibility.spec.ts`: axe on each of the four steps (light and dark, desktop and phone).
- `visual.spec.ts`: the screens `onboarding` (step 1), `onboarding-security`, `onboarding-exhibition` and `onboarding-organization` by their addresses; baselines from `npm run test:visual:docker -- --update`.

### 4. The mock

- `MockDb.exhibition?: { Organization: string; Project: string } | null` (`UI/app/mock/db.ts`); the storage key becomes `2s-admin:mock-db:3` and is exported, and `start-scenario.test.ts` uses it (a tab with data under the previous key starts from the new seed).
- The legacy seed (`default`, and so `new-user`, `invited` and `large`) gets the organization `exhibition` and the project `exhibition.exhibition` (Owner Grace Hopper, a description, Ada not a member) with `exhibition: { Organization: 'exhibition', Project: 'exhibition' }`, and `scopeSeed` generates a small catalog for `exhibition/exhibition/default`. The names are the ones production will use (5). The showcase has no exhibition: its user has an account, and its projects follow rules of their own.
- `UI/app/mock/access-handlers.ts`, next to the acceptance of an invitation: `GET */api/v1/access/exhibition` (`404` without the setting or the access point; else the names and the description) and `POST */api/v1/access/exhibition/join` (the same `404`; raises the role to `Reader` and never lowers one, creates the account when missing, `syncAccount`, answers the account).
- `window.storytellerMock.setExhibition(value | null)` (`browser.ts`, and its type in `test/e2e/support.ts`): a test turns the exhibition off. The wizard asks once per load, so the test calls the hook and opens the page again.
- Unit tests (`UI/test/unit/mock/exhibition.test.ts`, new): both handlers in the states above; `legacy.test.ts` expects the fourth project.

### 5. Deployment and local development

- `src/Platform/Deployment/main.bicep` and `modules/functions.bicep`: the parameters `exhibitionOrganization`, `exhibitionProject` and `exhibitionOwner` (default `''`), and the three settings only when all three are set (the `sentryDsn` pattern). `parameters/prod.bicepparam` does not set them in this change.
- **Turning the exhibition on in production** is a step of the runbook, in this order: (1) `2sInitDb seed exhibit --organization exhibition --project exhibition --owner <the operator's account id>`; (2) the key/value pairs, `sform story kv import` of the set's `key-values.json` until the seed brings them (424-265); (3) one resolved read of a configuration as a check; (4) the three parameters in `prod.bicepparam`, what-if and deployment. The names are `exhibition` / `exhibition`, as the issue calls them.
- Local development: the three settings in the user secrets or the environment of the API, after `2sInitDb seed exhibit` (its own names `fourth-wall` / `toons-and-tyrants`, or others); `local.settings.json` stays without them, because the owner is the developer's own account.

### 6. Test cases and automation

Testiny `2SP`, folder **Access > Sign In and Account** (23). All eight cases are mock only: `[MockOnly]`, Role Any, Mode M, type Functional, status Ready, automation Automated; each builds its own `new App(Page, "new-user")`. They are automated in `SignInTests`, in a new partial file `SignInTests.Onboarding.cs` (the first test class in two files; still one class per sub-folder). The seven new cases are created in Testiny first: their ids go into `[TestCase(id)]` and `test-cases.md`.

`<name>` is the mock's user, Ada Lovelace. "The last step" is reached with **Next** from the first.

**ACC-SGN-06** (Testiny 21, edited) · Medium · `Onboarding_NewUser_CreatesFirstOrganization` — A new user passes the onboarding steps and creates the first organization and project. Precondition: generated organization and project names.

1. Open `/` with the scenario → URL `/onboarding`; heading 'Welcome, Ada Lovelace'; the steps 'Step 1: Welcome' (current), 'Step 2: Security', 'Step 3: Exhibition', 'Step 4: Organization'; heading 'Customer data stays in the EU'.
2. Click 'Next' three times → headings 'Security first', 'Join the exhibition', 'Create your organization'; the text 'Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.'; button 'Create and continue'.
3. Click 'Create and continue' with empty fields → field errors 'Enter a organization name.' and 'Enter a project name.'
4. Type 'invitations' in 'Organization' and click 'Create and continue' → field error 'This organization name is reserved.'
5. Fill 'Organization' and 'First project' with the generated names and 'Project description' with a text that has spaces around it; click 'Create and continue' → URL `/<org>/<project>/default/annotations`; 'No annotations in this view'.
6. Open `/<org>` → a project card '`<project>`' with the badge 'Owner', the key '`<org>.<project>`' and the description without its surrounding spaces.

**ACC-SGN-07** (new) · Medium · `Onboarding_Welcome_StatesEuHostingAndSubProcessors` — The first onboarding step states the EU hosting and lists the sub-processors.

1. Open `/onboarding` → heading 'Customer data stays in the EU'; the text 'All customer data, configuration and secrets are stored and processed only in the EU (Sweden).'; the sentences about WorkOS and about the authorization decisions (3.3).
2. Read the list 'Sub-processors' → four entries: 'Microsoft Azure' 'EU only' 'Hosting, database, storage, secrets'; 'Sentry' 'EU only' 'Error reporting'; 'PostHog' 'EU only' 'Product analytics'; 'WorkOS' 'USA' 'Sign-in of the administrators of the 2S Admin UI and their invitation emails (name, email, IP address, session data)'.
3. Read the link 'Details of the EU hosting' → it opens `https://claude.ai/share/c9fbe93d-61c4-466c-8490-2ba028fb1999` in a new tab.

**ACC-SGN-08** (new) · Low · `Onboarding_Security_ExplainsFederatedAuthAndLinksToDocs` — The security step of onboarding explains federated auth and links to the documentation.

1. Open `/onboarding?step=security` → heading 'Security first'; 'Security is the main focus of the 2S Platform.'; the paragraphs 'Machines and pipelines' and 'People' with 'Entra ID in your own tenant' and 'Keycloak, self-hosted, for example in the EU'.
2. Read the links → 'Authentication: Entra ID and Keycloak', 'Federated auth of machines', 'sform' and 'GitHub Action' with the addresses of 3.4, each in a new tab.

**ACC-SGN-09** (new) · Medium · `Onboarding_Steps_MoveBackAndForthAndKeepInput` — Back, Next, the step list and the browser's back move between the onboarding steps and keep what was entered.

1. Open `/onboarding` → no 'Back'; 'Step 1: Welcome' is the current step.
2. Click 'Next' → URL `/onboarding?step=security`; 'Step 2: Security' is current. Click 'Back' → heading 'Customer data stays in the EU'.
3. Click 'Step 3: Exhibition' and check 'Join the exhibition' → URL with `step=exhibition` and `join=1`.
4. Click 'Step 4: Organization' and type an organization name; click 'Back', then 'Next' → the name is still in 'Organization'; 'You are joining the exhibition.' shows.
5. Go back in the browser's history → heading 'Join the exhibition', the box checked. Reload → the same step, the box checked.

**ACC-SGN-10** (new) · High · `Onboarding_JoinExhibitionWithOrganization_OpensItAsReader` — A new user joins the exhibition with an own organization and opens it as a Reader. Precondition: generated organization and project names.

1. Open `/onboarding?step=exhibition` → heading 'Join the exhibition'; the box 'Join the exhibition' unchecked, with 'Adds you to exhibition/exhibition as a Reader.'
2. Check the box, click 'Next', fill 'Organization' and 'First project' and click 'Create and continue' → URL `/<org>/<project>/default/annotations`; toast 'You joined the exhibition' with 'Open the exhibition'.
3. Open `/orgs` → the organization '`<org>`' with the badge 'Owner'; the organization 'exhibition' with the badge 'Project member' and the project link 'exhibition Reader'.
4. Open `/exhibition/exhibition/default/annotations` → annotations of the catalog; none of the buttons 'New', 'New annotation' and 'Bulk create'.

**ACC-SGN-11** (new) · Medium · `Onboarding_JoinOnlyExhibition_CreatesOrganizationLater` — A new user joins only the exhibition and creates an organization later. Precondition: generated organization and project names.

1. Open `/onboarding?step=organization` → no button 'Continue with the exhibition only'.
2. Click 'Step 3: Exhibition', check 'Join the exhibition', click 'Next' → 'You are joining the exhibition.' and the button 'Continue with the exhibition only'.
3. Click 'Continue with the exhibition only' → URL `/exhibition/exhibition/default/annotations`; toast 'You joined the exhibition'; 'User menu for Ada Lovelace'.
4. Open `/onboarding` → it is replaced by the exhibition: URL `/exhibition/exhibition/default/annotations`.
5. Open `/orgs`, click 'New organization', fill the names and create → URL `/<org>/<project>/default/annotations`; on `/orgs` the organization '`<org>`' with the badge 'Owner' next to 'exhibition'.

**ACC-SGN-12** (new) · Medium · `Onboarding_NoExhibition_HasThreeSteps` — Onboarding has no exhibition step when the deployment has none. Precondition: the mock's exhibition is turned off (`setExhibition(null)`) after the first load, then `/onboarding` is opened again.

1. Open `/onboarding` → the steps 'Step 1: Welcome', 'Step 2: Security' and 'Step 3: Organization'; no step 'Exhibition'.
2. Click 'Next' twice → heading 'Create your organization'; no 'Continue with the exhibition only'.
3. Open `/onboarding?step=exhibition` → heading 'Customer data stays in the EU' (the first step).

**ACC-SGN-13** (new) · Low · `Onboarding_SignOut_ReturnsToSignIn` — Sign out on onboarding returns to the sign-in page.

1. Open `/onboarding` → link 'Sign out' below the card.
2. Click 'Sign out' → URL `/login`; heading 'Sign in to 2S Admin'.

**Not automated, and why.** Onboarding and the join on a deployed target: every pool user has an account (as ACC-SGN-06 before). The failures of the join (`409` under contention, an account without the membership): the mock has no failing join; unit tests of the page cover them. The full safeguards sentence: it shows once `safeguardsLive` is on (8). The real routes are covered by `ExhibitionHttpTests` and the Cosmos DB test, not through the UI.

**Code.** `Pages/OnboardingPage.cs`: the step list, **Next**, **Back**, the step headings, the EU callout and the sub-processor list, the links, the checkbox, **Continue with the exhibition only**, **Sign out**; `CreateAsync` walks to the last step first. `Pages/AnnotationsPage`: `ExpectNoCreateActionsAsync` moves there from `AnnotationRoleTests`. `Support/MockApi.ACC.cs`: `SetExhibitionAsync`, used through a `MockApi` of the test's own `App`.

**Counts.** 233 → 240 cases, Access 33 → 40, mock only 7 → 14, Role Any 29 → 36: `CLAUDE.md`, `docs/Platform/UI.Admin.AutomationTests/test-cases.md` (the header names this spec as the source of the new steps) and `automation-tests.md` (the catalogue, the roles, the mode table, the hooks of `MockApi`). The description of folder 23 names the wizard.

NAV-RTE-03 (120), ACC-MYI-02 (106), ORG-ORG-01 (8) and ORG-ORG-02 (23) stay as they are: the guard, the link of the invitations page and the texts of the form do not change.

### 7. Documentation

- `docs/42for.net/platform/admin-ui.md`, **Signing in and the first project**: the wizard, its steps and the exhibition.
- `src/Platform/ui.admin/README.md`: the scenario `new-user`, the mock's exhibition and its hook, the e2e list.
- `docs/Platform/Storyteller/access.md`: the two routes, the section **Exhibition** (the settings and why an owner, what a join does, what a visitor reads, the limits of 1.3), the account that comes from a join.
- `docs/Platform/Storyteller/data-sets.md` and `playground.md`: `exhibit` in the table of built-in sets, and seeding it as the exhibition.
- `src/Platform/Deployment/README.md`, `docs/Platform/Deployment/cookbook-azure-production.md` and `runbook-azure-production.md`: the three parameters and the step that turns the exhibition on (5). `decisions.md`: the exhibition (names, owner, order), and the wizard's texts as a place that repeats the hosting and sign-in wording, to change together with it.
- `docs/Platform/development.md` and `docs/Platform/Aspire.Host/local-development.md`: the exhibition locally.
- `docs/Platform/UI.Admin.AutomationTests/*` and `CLAUDE.md` (the counts; `exhibit` among the built-in sets) as in 6.
- `docs/github/workflows.md` needs no change: no workflow or path filter changes.

### 8. Release gate, and the order with 424-262, 424-263 and 424-264

A merge of this change publishes the admin UI at once, while the API is published by hand. So the text cannot wait for the three issues by the order of merges alone.

- `safeguardsLive` in `UI/app/utils/onboarding.ts` is `false`: the wizard shows "Every authorization decision is made by 2S in the EU." and nothing about impersonation, the recent sign-in or the audit log.
- It becomes `true`, a change of one line, when all of this holds: 424-262, 424-263 and 424-264 are done; the production API runs a build with them (runbook 11.1); the WorkOS dashboard check of 424-262 is recorded. Then the wizard shows the issue's sentences.
- Both forms are tested (3.7).

Whichever of the four changes merges later is rebased on the others. For this one that means: 424-263 gives the writes of `IAccessService` an audit caller, so `ExhibitionHttp` passes it to `CreateAccountAsync` and `JoinAccessPointAsync` (the join is then recorded as a member who joined, with the joiner as the actor); the OpenAPI snapshot, both generated SDKs and the visual baselines are made again from the rebased code, never merged by hand.

### Commits

One pull request; the commits follow the sections. Every subject ends with `(424-267)`.

| Commit | Checks |
| --- | --- |
| `docs(platform)`: this plan | — |
| `feat(platform)`: the exhibition routes of the API (1) | `Api.Functions/test`, the one Cosmos DB test in a container |
| `chore(platform)`: regenerate the SDKs from OpenAPI 0.10.`<n>` (2) | the SDK's `typecheck`, `lint`, `test`; the build of `Sdk.NSwag` and its users |
| `feat(platform)`: the mock's exhibition (4) | Vitest |
| `feat(platform)`: the wizard (3) | `typecheck`, `lint`, Vitest, e2e, the visual baselines |
| `feat(platform)`: the exhibition parameters of the deployment (5) | `az bicep build`, `build-params` |
| `test(platform)`: the cases of the wizard (6) | the automation tests on the mock profile |
| `docs(platform)`: the documentation (7) | — |
| `docs(platform)`: the review | — |

The review is `docs/Platform/ui.admin/specs/reviews/<date> Onboarding Wizard with EU Hosting, Security and the Exhibition.md`.

### Out of scope

- A way to join the exhibition after onboarding, and a way for a Reader to leave it, in the admin UI (3.6, 1.3).
- **Modeling** for Readers (1.3).
- A public page about hosting and sub-processors on 42for.net; the wizard links to the discussion the issue names.
- Seeding production and setting its three parameters: steps of the operator, after this change (5).
- A command of `sform` and a tool of the MCP server for the exhibition.
- Turning `safeguardsLive` on (8).
