# Onboarding Wizard with EU Hosting, Security and the Exhibition

Linear: [424-267](https://linear.app/424fun/issue/424-267/admin-ui-onboarding-wizard-eu-hosting-security-and-federated-auth-join) — Admin UI: onboarding wizard (EU hosting, security and federated auth, join the exhibition, create an organization).

## Overview

This review covers the whole of the spec [Onboarding Wizard with EU Hosting, Security and the Exhibition](../2026-11-15%20Onboarding%20Wizard%20with%20EU%20Hosting,%20Security%20and%20the%20Exhibition.md), delivered in one pull request. A user who signs in without an account now goes through a wizard of four steps: **Welcome** (customer data stays in the EU, the sub-processors), **Security** (federated auth of machines, sign-in of people), **Exhibition** (an optional join of the deployment's demo project as a Reader) and **Organization** (the form of before). The API got the two routes of the exhibition, both SDKs were regenerated, the mock got an exhibition, the deployment templates got three optional parameters, and Testiny `2SP` got seven new cases with their automation.

Three things differ from the issue's text on purpose and are the owner's to confirm; they are listed under **Decisions for the owner** at the end.

## What Was Done

### 1. The exhibition in the API

As planned, with one rule made stronger and one validation added.

- `Api.Functions/src/Exhibitions/ExhibitionOptions.cs` and `ExhibitionEntryPoint.cs`: the section `Exhibition` with `Organization`, `Project` and `Owner`, all three or none, validated at the start (`AddExhibition` in `Program.cs`, after `AddConditionalReads`). The message names the settings, never their values.
- `Api.Functions/src/V1/ExhibitionHttp.cs`: `GET v1/access/exhibition` (`GetExhibition`) and `POST v1/access/exhibition/join` (`JoinExhibition`), with `V1/Models/Exhibition.cs`, the routes and route ids in `Definitions.cs` and both operations in `OperationAccess.UserOnly`. `IAccessService` and `CosmosAccessService` are unchanged.
- The join: the registration account id, an account without an organization when the caller has none (a token without names is `401`), `JoinAccessPointAsync` as `Reader`, up to three more attempts after a `Conflict` with a random delay of 50 to 250 ms, then the account. A create that throws is followed by a read of the account; the failure of the create goes out only when the account is still missing, also when that read fails too.

**Deviations from the spec.**

- **The owner rule is stronger.** The spec offered the exhibition when `Exhibition:Owner` is the project's `Owner`. The review of the code found that this proves nothing: an `Owner` makes any existing account an `Owner` without its consent (`POST v1/access/grant`), so whoever registered the names first could add the configured account to their own project and keep managing it. The routes now also refuse while any other account is an `Administrator` or an `Owner` of the project (a third Warning). So a second administrator of the exhibition switches it off until that role is lowered. What somebody wrote into a project before leaving it stays: the access map does not say who created a project. That gap needs a creator stamp on the access point, which is a change of the service and was left out.
- **An owner with white space around it stops the start**, like a name that is not valid: such a value would start and never match.
- The exhibition is resolved before the account id is read, so the Entra ID test of a foreign id records one read of the access point.

**Tests.** `ExhibitionHttpTests` and `ExhibitionOptionsTests` (71 tests), `Document_ExhibitionOperations_ArePublished` in `OpenApiExportTests`, `ExhibitionRoutes_AreRegistered` in `RouteTableTests`, and `JoinAccessPoint_BareAccountAsReader_ListsTheProjectAndNoAccessPoint` in `CosmosAccessServiceTests`.

Runs:

- `dotnet test --project src/Platform/Storyteller/Api.Functions/test`: 3159 passed, 0 failed.
- The same in Release (no `DEV_AUTH`) for the exhibition, OpenAPI and route classes: 236 passed.
- The Cosmos DB test, with `STORYTELLER_TESTS_USE_CONTAINER=true`: passed on the vNext emulator and on the classic one. Its first try failed in the collection fixture, because the reused vNext container is shared by every worktree of the machine and another session's suite was writing to it; that session may have lost a chunk to this run's database delete. The whole Cosmos DB suites were not run.

### 2. OpenAPI and the SDKs

As planned. The document is `0.10.40` (`open.api.v0.10.40.json` in `Sdk.NSwag` and `sdk.typescript`; the TypeScript copy of `0.10.34` is removed). It differs from the previous one by the two operations and the schema `Exhibition` only. `npm run generate` gave `getExhibition`, `joinExhibition` and the model with their Vue Query, Zod and MSW counterparts; NSwag 14.6.3 added `GetExhibitionAsync` and `JoinExhibitionAsync` (265 lines, no rewrite). Both READMEs name the new file, and `msw.test.ts` has the two functions in its table.

Runs: the SDK's `typecheck`, `lint` and `test` (358 passed, 1 skipped); builds of `Sdk.NSwag`, `sform` and the MCP server (0 errors).

### 3. The wizard

- `app/pages/onboarding.vue`: the wizard. `app/composables/useOnboardingEnd.ts`: its end (the states `idle`, `working`, `joinFailed`). `app/components/onboarding/`: `OnboardingSteps`, `OnboardingWelcome`, `OnboardingSecurity`, `OnboardingExhibition`. `app/utils/onboarding.ts`: every text, the sub-processors, every link, and `safeguardsLive`.
- `App.vue`, `layouts/BareLayout.vue`, `router/meta.d.ts`: the wide column (`meta.wide`, a prop of the layout).
- `components/orgs/OrganizationForm.vue`: the props `autofocus`, `disabled` and `validateOnBlur`.

**Deviations from the spec.**

- **The first step has no `step` in the address.** **Back** to it gives `/onboarding`; `?step=welcome` still opens it.
- **The accessible name of a step is an `aria-label`** (`Step <n>: <name>`). A name built from hidden spans reads "Step 1 : Welcome" in Playwright.
- **The wizard's form does not check a field when the user leaves it** (`validateOnBlur`, new). The automation tests found the reason on WebKit with a phone's viewport: a change of step puts the focus in **Organization**; a press on **Back** or **Continue with the exhibition only** blurred the empty field, its one-line error replaced the two-line help, the buttons moved up 20 px under the pointer, and the press missed them (2 of 4 runs). A change and the submit still check the fields. The dialog of **New organization** is unchanged.
- **In `joinFailed` the focus goes to Try again** (new): the form is gone there, and the focus with it.
- `OrganizationForm.disabled` also stops the submit handler, not only the button.
- The `ErrorState` of step 3 is its compact form (an alert with **Retry**); the full one has an `h2` of its own.
- A **Retry** that answers `404` makes the wizard three steps and goes on to the organization, the step that followed the exhibition.
- A last navigation that fails (a page that did not load) ends the wizard with a full load of the target: by then the session has the account, and the guard would send every further step to `/`.
- **Try again** and **Continue with the exhibition only** take the focus back after a failure; they drop it while they work.
- After a failed join of the exhibition only, an account that cannot be read (not a `404`) counts as none: the button tries again, and the join is idempotent.
- The alerts of a failed join have `role="alert"`; the toast with **Open the exhibition** stays 10 seconds; **Sub-processors** is a visible heading above its list.
- One bug found by the e2e tests and fixed before the commit: on the organization step the focus did not reach the first field, because the watcher ran before `v-show` displayed the step.

**Tests.**

- Unit: `test/unit/onboarding/` (`onboarding-page.test.ts`, `onboarding-end.test.ts`, `onboarding-texts.test.ts`: 68 tests), `components/app-layout.test.ts`, and more cases in `brand.test.ts` and `organization-form.test.ts`. `test/helpers.ts`: `mountScreen` takes `account` and `guards`. The whole suite: 2066 passed (112 files).
- e2e: `onboarding.spec.ts` (the two tests of before walk through the steps; seven new ones), `accessibility.spec.ts` (six onboarding states per theme: the four steps, the exhibition step checked, the organization step with the second button), `visual.spec.ts` (four screens). The whole suite passed on desktop and phone (432 passed, 14 skipped) before the last changes of the form and the focus; after them the onboarding and accessibility specs passed again (180).
- The unit tests cannot see a dropped focus: their DOM keeps the focus on a disabled button. The two focus fixes were checked in Chromium with a temporary Playwright spec (deleted), on desktop and phone.
- Visual baselines from the Linux container: the four `onboarding-*` changed and twelve are new; the other 64 comparisons passed unchanged.

On this machine (Node 26; `.nvmrc` says 24) Vitest runs with `NODE_OPTIONS=--no-experimental-webstorage`: without it 16 tests fail on an untouched tree, because Node's own `localStorage` displaces happy-dom's.

**Reviews.** Three reviewers read the plan before it was committed; the owner setting, the release switch, the step names in the address and the own step list come from them. The API and the mock were each reviewed and fixed before their commits. The two automated reviews of the uncommitted wizard did not run to their end, so that review was done by hand, and two reviewers read the committed branch afterwards: they found the three rare paths above (the failed last navigation, the Retry that answers `404`, the focus after a failed join) and "your data" in the admin UI docs, all fixed.

**Review of what a person sees.** Done by hand: the code against sections 3.1 to 3.6, and a temporary Playwright spec (deleted) that opened fourteen states of the wizard at 1440, 390, 360 and 320 px in light and dark, measured them and walked them by keyboard. No state overflows or clips, no text of the wizard is below the contrast of WCAG AA, the keyboard walk had no finding, and the screenshots were read one by one. Not changed, because they are not the wizard's:

- The hovered primary button has a contrast of 4.16 in dark mode (the app's token).
- The bare layout has no `main` landmark (axe: moderate, on every bare page).
- The box of the checkbox is 16 px; its label is part of the target.

### 4. The mock

As planned: `MockDb.exhibition`, the storage key `2s-admin:mock-db:3` (exported), the organization `exhibition` and the project `exhibition.exhibition` in the legacy seed with a small catalog, the two handlers in `access-handlers.ts`, and `window.storytellerMock.setExhibition`.

**Deviations.** The join and the acceptance of an invitation share one helper (`joinAccessPoint`). `POST access/account` now refuses an organization that exists with `409 AccessPointExists`: before, a new user who named their organization `exhibition` took the exhibition over in the mock. The API refuses that too, with `403`.

Tests: `test/unit/mock/exhibition.test.ts` (27 tests, through the SDK's functions), `legacy.test.ts` (the fourth project), `start-scenario.test.ts` (it reads the exported key now, and a tab with data under the previous key starts fresh).

### 5. Deployment and local development

As planned, with the third parameter: `exhibitionOrganization`, `exhibitionProject` and `exhibitionOwner` in `main.bicep` and `modules/functions.bicep` (the settings only when all three are set). `parameters/prod.bicepparam` and `local.settings.json` are unchanged. `az bicep build` of both entry points and `build-params` of both parameter files: exit 0, no warning. Nothing was deployed.

### 6. Test cases and automation

- Testiny `2SP`, folder **Access > Sign In and Account**: case 21 (ACC-SGN-06) has its new title and steps; the new cases are 492 (ACC-SGN-07), 493 (ACC-SGN-08), 494 (ACC-SGN-09), 495 (ACC-SGN-10), 496 (ACC-SGN-11), 497 (ACC-SGN-12) and 498 (ACC-SGN-13); the description of the folder names the wizard.
- `Tests/Access/SignInTests.Onboarding.cs` (new, the second file of `SignInTests`): the eight tests. `Pages/OnboardingPage.cs` for the wizard, `Support/MockApi.ACC.cs` (`SetExhibitionAsync`), `Pages/AnnotationsPage.cs` (`ExpectNoCreateActionsAsync`, moved from `AnnotationRoleTests`).
- The catalogue has 240 cases (Access 40, mock only 14, Role Any 36): `test-cases.md`, `automation-tests.md`, `CLAUDE.md`.

Runs against the mock build (`UITESTS_PROFILE=mock`, a preview on `http://localhost:4274`):

- The eight onboarding tests: 8 of 8 on desktop, on the phone profile, on Firefox and on WebKit; on WebKit with the phone profile three times in a row after the fix of the form.
- The Access area: 40 of 40 on desktop and on the phone profile, again with the last build of the wizard. `MyInvitationTests`, `NavigationRoutingTests`, `OrganizationTests` and `AnnotationRoleTests` passed.
- The whole catalogue was not run, and nothing was reported to Testiny.

**Not automated**, as planned: onboarding on a deployed target (every pool user has an account), and the failures of the join, which the unit tests of the page cover.

### 7. Documentation

`docs/42for.net/platform/admin-ui.md` and `src/Platform/ui.admin/README.md` (the wizard, the mock's exhibition and hook, the e2e list); `docs/Platform/Storyteller/access.md` (the section **Exhibition**), `data-sets.md` and `playground.md` (`exhibit`); `src/Platform/Deployment/README.md`, the cookbook, the runbook (**Turning the exhibition on**) and `decisions.md`; `docs/Platform/development.md` and `Aspire.Host/local-development.md`; `docs/Platform/UI.Admin.AutomationTests/*`; `CLAUDE.md`.

### 8. Release gate

`safeguardsLive` in `app/utils/onboarding.ts` is `false`: the wizard says "Every authorization decision is made by 2S in the EU." and nothing about impersonation, a recent sign-in or an audit log. Both forms of the text are unit-tested. Turning it on is one line, once 424-262, 424-263 and 424-264 run in the production API.

At the time of this review 424-262 has code on its branch, 424-263 a plan, and 424-264 nothing; none is on `main`. 424-263 changes the signatures of `IAccessService`: whichever of the two changes merges later passes the audit caller in `ExhibitionHttp` and makes the OpenAPI snapshot, both SDKs and the visual baselines again.

## Decisions for the owner

1. **The WorkOS sentence.** The issue's text says WorkOS "is used only to sign in administrators of the 2S Admin UI". Production also sends WorkOS the email address of every invited person (`AuthKitInvitationSender`, with `authKitApiKeyEnabled = true`). The wizard says "…to sign in administrators of the 2S Admin UI and to send their invitation emails. It processes only their name, email, IP address and session data, and the email address of a person they invite.", and the WorkOS entry of the sub-processors names the invitation emails.
2. **The heading** is "Customer data stays in the EU", not "Your data…": the reader's own name and email go to WorkOS.
3. **Step 2** says "WorkOS is not involved" where the issue says "no US identity provider is involved", and adds "Such a deployment is used through sform and the API; the Admin UI is not available in it today.", in step with the decision record of 424-262 (Microsoft is a US company too, and the admin UI signs in through AuthKit only).
4. **The link of step 1** opens the shared discussion on claude.ai that the issue names. The page could not be read from here, so its content is not checked against the copy. A page of the docs site would be a steadier target.
5. **The exhibition in production** needs the seed, the key/value pairs (424-265), and then the three parameters; the runbook has the steps. Until then the wizard has three steps there.

All five are constants of `app/utils/onboarding.ts` or parameters; none needs a change of code elsewhere.

## Known limits

- The exhibition is offered in onboarding only. A user who ends the wizard without it has no way to join later in the admin UI, and a Reader has no way to leave it there; the API has both.
- **Modeling** is locked for Readers in the admin UI, so a visitor does not see the exhibition's schemas and templates there.
- Every join rewrites the access point of the exhibition project, and its member list is not paged.
- Each visited step is an entry of the browser's history. After the wizard ends, the browser's back stays on the page, once per visited step, before it leaves the app.

## Rework after the first review (2026-11-16)

The owner reworked the texts and the layout of the wizard by hand after this review; the tests and the docs followed. This section supersedes what the sections above say about the steps, their texts, the links and `safeguardsLive`.

### The five steps

- **Welcome** (new, `OnboardingWelcome.vue`): the heading "Meet the 2S Platform" with a rocket icon, the motto, a paragraph about the pricing (free while it takes shape, then based on usage, not on seats, announced well in advance) and the link **Documentation** to `https://42for.net/platform/introduction`.
- **Residency** (the former Welcome, `OnboardingResidency.vue`, `?step=residency`): the callout "Your platform's data stays in the EU"; its lead names machine access too, followed by the sentence about AI processing. The purpose of Microsoft Azure is "Hosting, storage, secrets, logs". The entry of WorkOS has two paragraphs: its purpose (`workosPurpose`: what it does and receives) and a note, "Every authorization decision is made and recorded by 2S in the EU." The sentence about an EU-only identity provider moved to the security step.
- **Security**: a new lead and four sections with icons, in this order: **Secrets**, **Machines and pipelines**, **People** (WorkOS AuthKit signs people in and 2S alone decides; the three safeguards; a second paragraph about an EU-only identity provider) and **Security testing**. The list of Entra ID and Keycloak and the limit of such a deployment are gone.
- **Exhibition**: a sparkles icon, new texts (Fourth Wall Software, and what the demo shows), the checkbox **Show me the exhibition**, and a fixed description under it: "Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only." The description of the project from the API is no longer shown, and `exhibitionChoiceDescription` ignores its parameter.
- **Organization**: an icon on its heading, and a line under the form: "By creating an organization or joining the exhibition, you agree to the Customer License Agreement and the Data Processing Agreement.", each a link to `42for.software/legal/` (`agreementLinks`).

The step labels are "Step 1: Welcome" to "Step 5: Organization"; without an exhibition the wizard has four steps, and **Organization** is "Step 4: Organization".

### `safeguardsLive` is gone

The switch, `hostingStatement` and their two forms of the text are removed: the security step names the three safeguards always. The platform opens to the public only once 424-262, 424-263 and 424-264 run in production, so no user sees the claim before it holds. Section 8 above no longer applies; `docs/Platform/Deployment/decisions.md` says so.

### Links

`hostingLink` (the discussion on claude.ai) is gone. The residency step has `residencyLinks`: **Cloud hosting** (`https://42for.net/platform/cloud`) and **Sub-processors list** (`https://42for.software/legal/sub-processors`). The security step's `securityLinks` are **Security details**, **Authentication of machines** and **Self-hosting option** on `42for.net/platform/`, in place of the four links to GitHub and the CLI docs. Every link opens in a new tab. Of the `42for.net/platform/` pages, only `introduction` exists in `docs/42for.net` today: `cloud`, `security`, `auth-machine` and `self-hosting` are not written yet.

### Layout

- The step list (`OnboardingSteps.vue`) is a container (`@container`) and switches to the compact names (only the current step named) by a container query on its own width (`@min-[37rem]`), not by the viewport, and does not wrap.
- The card has one padding, `p-5`, at every width, so nothing in it moves when the step list switches.

### Tests and docs

- Unit: `onboarding-texts.test.ts` and `onboarding-page.test.ts` rewritten for the five steps, the new texts and exports, with new cases for the welcome step (motto, pricing, **Documentation** with `target="_blank"`), the residency step and its links, the order of the security sections, the exhibition step without the project's description, and the agreements line; every behaviour case stays (failures, focus, query). 72 tests in `test/unit/onboarding/`; the whole suite 2070 passed (112 files). `typecheck`, `lint` and Prettier clean.
- e2e: `onboarding.spec.ts` walks the five steps (four without an exhibition), `accessibility.spec.ts` has the residency step (seven onboarding states per theme), `visual.spec.ts` has the screen `onboarding-residency`. `npx playwright test onboarding accessibility --project=desktop --project=mobile`: 184 passed. The visual project was not run: the five `onboarding-*` baselines need to be made again in the Linux container, `onboarding-residency` for the first time.
- Automation: `OnboardingPage.cs` has the headings and texts of the five steps as constants, `Topics`, `Agreements`, `ExhibitionChoiceDescription` (now a locator) and `ExpectLinksAsync`. In `SignInTests.Onboarding.cs` the eight tests keep their ids; three are renamed: `Onboarding_WelcomeAndResidency_StateEuHostingAndSubProcessors` (492, now the welcome step too), `Onboarding_Security_ExplainsSafeguardsAndLinksToDetails` (493) and `Onboarding_NoExhibition_HasFourSteps` (497); case 21 checks the agreements line. Against `npm run dev:mock` on `http://localhost:5174`: 8 of 8 with `UITESTS_PROFILE=mock` and 8 of 8 with `mock,mobile`. The titles of ACC-SGN-07 and ACC-SGN-08 in `test-cases.md` follow; the cases in Testiny are not updated yet.
- Docs: `docs/42for.net/platform/admin-ui.md` (the five steps), `src/Platform/ui.admin/README.md` (the e2e list), `docs/Platform/Deployment/decisions.md` (the wording and the safeguards) and `docs/Platform/Storyteller/access.md` (the description of the project no longer shows in onboarding).
