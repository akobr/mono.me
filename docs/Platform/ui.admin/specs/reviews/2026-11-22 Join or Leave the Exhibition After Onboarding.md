# Join or Leave the Exhibition After Onboarding

Linear: [424-271](https://linear.app/424fun/issue/424-271/admin-ui-join-or-leave-the-exhibition-after-onboarding) — Admin UI: join or leave the exhibition after onboarding.

## Overview

This review covers the whole spec, [Join or Leave the Exhibition After Onboarding](../2026-11-22%20Join%20or%20Leave%20the%20Exhibition%20After%20Onboarding.md), delivered in one pull request. Two things change in the admin UI:

- **Organizations** ends with a card of the deployment's exhibition for a user who has an account and is no member of it. **Join the exhibition** makes the user a Reader and opens the project.
- **Settings** has **Leave project** under the Project card for every member. The only Owner sees it disabled, with the reason.

The mock now lets any member leave, as the API does. Each change has unit and e2e tests. Testiny `2SP` has five new cases (510 to 514), each with its automation test. The API and the SDKs are unchanged.

## What Was Done

### 1. The mock lets any member leave, as the API does

`app/mock/access-handlers.ts`, `DELETE …/members/:accountId` (commit "fix(platform): let any member leave …"). The checks follow the API's order:

1. Leaving needs no role.
2. Removing someone else needs Administrator.
3. A missing member is `404 MemberNotFound`.
4. Removing an Owner needs Owner.
5. The last Owner stays (`409 LastOwner`).
6. Only removing someone else needs a recent sign-in.

**Deviation.** The spec gave the refusal of an Owner's removal as `403 ElevatedRole`. The API throws an `AccessDeniedException` without a code there (`CosmosAccessService.cs:423–426`), so it answers `403 AccessDenied` with "Only an owner can remove an owner of '{key}'.". The mock now answers the same. The review of the code found this.

**Tests.** `test/unit/mock/members.test.ts` (new), 15 tests:

- a Reader, a Contributor, a ContributorWithSecrets and an Administrator each leave and keep their other memberships;
- a leave with a stale sign-in;
- the last Owner;
- an Owner leaves while another Owner stays;
- `MemberNotFound` for a member who left already, and for an Administrator who removes an account that is no member;
- the `MemberRemoved` audit record;
- a Contributor who removes someone else (`403`, before the lookup of the member);
- an Administrator who removes an Owner (`403 AccessDenied`);
- an Owner who removes another member and another Owner;
- leaving the exhibition, which stays offered, and joining it again.

### 2. Join the exhibition on `/orgs`

As planned, with one addition to the cache.

- `app/composables/useExhibition.ts` (new):
  - `useExhibition()` returns `exhibition`, `projectKey`, `member`, `offered`, `isExhibition` and `refetch`. It wraps `useGetExhibition` with `meta.silent`, `staleTime` of five minutes and a request `timeoutMs` of 5000. The exhibition counts only when the query's status is `success`, and any failure hides the offer.
  - `useJoinExhibition(offer)` keeps the account of the join's answer (`session.setAccount`, no second request), then shows the toast "You joined the exhibition" and pushes the project's address. A `404` shows "The exhibition is no longer offered." and refetches, which hides the card. Any other failure shows "Joining the exhibition failed" with the message, and the card stays.
- **Addition (review): a 404 is kept like an answer.** vue-query fetches a query in error again on every mount, and `staleTime` keeps data only. Without the option, every visit to Organizations or Settings asked again, and the API logs a Warning for each request of an exhibition that is configured but not offered. `retryOnMount` is now false for a `404` only. Other failures are asked again by the next page.
- `app/components/orgs/ExhibitionOfferCard.vue` (new):
  - a subtle `UCard` with `data-exhibition-offer` and the heading **The exhibition** (level 2);
  - the wizard's text about Fourth Wall Software and `exhibitionChoiceDescription()`;
  - the button **Join the exhibition**, a button and not a link.
- `app/pages/orgs/index.vue`: the card is the last item of the grid. Without organizations, the grid holds only the card, below the empty state.
- `app/utils/exhibition.ts` (new) holds the four texts that the wizard does not have.
- `projectPath` moved from `useOnboardingEnd.ts` to `app/utils/memberships.ts`, next to the new `withoutMembership(account, key)`. Both have tests in `test/unit/utils/shell.test.ts`.

**Tests.** `test/unit/orgs/exhibition-offer.test.ts` (new), 10 tests:

- the card for a non-member, after the organizations and without a link;
- no card for a member, without an exhibition (the showcase) or after a `503`, and no error shown;
- the card below the empty state;
- the join: one POST, no read of the account, the session, the address and the toast;
- the `404` and the `409` of the join;
- a `404` that a second mount does not ask again, and a `503` that it does.

`test/e2e/membership.spec.ts` (new) has three join tests:

- the join from Organizations, read-only in the exhibition, then **Project member** and `exhibition Reader`;
- no card with the exhibition turned off, the card back when it is on again, and no card after a join from another tab;
- no card in the showcase. This test was not in the spec.

### 3. Leave a project from its Settings

As planned, with changes from the review.

- `app/composables/useLeaveProject.ts` (new). `leave(key)` runs these steps:
  1. `removeMember(key, own id)`, without a step-up. `MemberNotFound` counts as left. `LastOwner` and other failures show "You did not leave the project" with the reason, and the membership stays.
  2. `getAccount()`, or the account without the key when that read fails.
  3. `setAccount`.
  4. The refresh of the projects, the toast "You left {key}", and the navigation to `/orgs`.
- `app/components/access/ProjectLeave.vue` (new):
  - the footer text, and the button **Leave project** (outline, error color);
  - for the only Owner, the button disabled and "You are the only Owner of this project. Make another member an Owner before you leave.";
  - `ConfirmDialog` "Leave {key}?". The description names the role through `roleLabel` and says how to come back: "To come back, you need a new invitation.", or for the exhibition "You can join the exhibition again from Organizations.".
- `app/utils/roles.ts`: `roleLabel(role)`, which `RoleBadge.vue` now uses too, and `ownerCount(accessMap)`.
- `app/pages/[org]/[project]/settings.vue`: the footer of the Project card. `lastOwner` holds when the caller is `Owner` and the loaded access point has no other Owner. `isExhibition` comes from `useExhibition()`.

**Changes after the review of the code** (commit "fix(platform): address the review …"):

- **A draft no longer blocks the leave.** Take an Administrator with an unsaved change of the project settings who left. The page's guard asked "Discard changes?" after the leave. "Keep editing" stopped the navigation: `router.replace` resolves with a `NavigationFailure` and does not throw, so the fallback never ran, and the user stayed on Settings of a project they had left. Two changes fix it:
  - `settingsDirty` counts a draft only while it can be saved (`canManageSettings`);
  - a navigation that fails or is stopped ends in a full load of `/orgs`, after `leaveWithoutPrompt()`.

  When the page has unsaved changes, the confirmation adds "Your unsaved changes on this page are lost.".
- **The confirmation stays until the page goes.** `setAccount` takes the role away before the navigation. The footer's `v-if` on the role then unmounted the open dialog. Focus fell to the body, and the user saw no dialog or toast while the refresh of the projects was awaited. Three changes fix it:
  - the footer now depends on the project key only;
  - `ProjectLeave` keeps what the dialog was opened for (key, role, exhibition, unsaved changes), so its text stays and the leave is for the project of the click;
  - `leaving` stays true until the navigation is done, and the refresh is no longer awaited.

**Tests.** `test/unit/access/project-leave.test.ts` (new), 13 tests:

- Cancel sends nothing;
- the leave reads the account after the DELETE and ends on `/orgs` with the toast;
- `MemberNotFound` counts as left;
- a failed read of the account drops the key locally;
- a `500` keeps the membership;
- the only Owner sees the disabled button and its text;
- an Owner leaves while another Owner stays;
- `LastOwner` from the API while the members are not loaded;
- the exhibition's sentence, and the label of the long role;
- no step-up;
- the dialog kept while Organizations loads slowly;
- a dirty draft ends without "Discard changes?".

Settings is mounted as a route here, so its guard runs. Each test of the review fixes was run once against the old code, and failed there.

`test/e2e/membership.spec.ts` has three leave tests:

- a Contributor: Cancel, the leave, `/orgs` without the project, then "You have no access here";
- a visitor of the exhibition, whom Organizations offers it again;
- the only Owner of `acme/billing`.

`accessibility.spec.ts` checks the settings of a Contributor and the open dialog with axe.

### 4. Unit and e2e tests of the admin UI

The new tests are listed in sections 1 to 3. The visual baselines `organizations-*` (4) were made again in the Playwright Linux image. The other 84 screens are unchanged: the screenshot `settings` opens at `#secrets`, below the Project card.

Runs:

- `npm run typecheck`, and oxlint and ESLint on every changed file: clean. Prettier reports one long line of `access-handlers.ts` (the audit's continuation token), which was there before this change and is left as it was.
- Vitest: 129 files, 2489 tests, all passed (`NODE_OPTIONS=--no-experimental-webstorage`).
- Playwright, every spec on desktop and mobile, before the review fixes: 473 passed, 14 skipped, 3 failed. The machine was loaded by a parallel run of the automation tests. The three failures were the mock's hooks not yet ready, a theme color read early, and a timeout. All 26 tests of their two specs (`settings.spec.ts`, `theme.spec.ts`) passed on a rerun.
- After the review fixes: `membership`, `settings`, `onboarding` and `accessibility` on desktop and mobile, 226 passed.
- `npm run test:visual:docker -- --update`: 88 passed. Only `organizations-*` changed.

### 5. Test cases and automation

The five cases are in Testiny `2SP` as planned:

| Id | Reference | Title |
| --- | --- | --- |
| 510 | ORG-ORG-03 | A user with an account joins the exhibition from the Organizations page and opens it as a Reader |
| 511 | ORG-ORG-04 | The Organizations page offers the exhibition only to a user who is not its member, on a deployment that has one |
| 512 | ACC-MEM-05 | A Contributor leaves a project from its Settings and loses access to it |
| 513 | ACC-MEM-06 | A visitor leaves the exhibition from its Settings, and the Organizations page offers it again |
| 514 | ACC-MEM-07 | The only Owner of a project cannot leave it |

The descriptions of the folders Access (11), Organizations (12) and Members (25) name the new cases. The tests are `OrganizationTests` (510, 511) and `MemberTests` (512 to 514). All but ACC-MEM-07 are `[MockOnly]`.

**Deviations in the code.**

- The members of the offer are in `Pages/OrganizationsPage.ORG.cs`, the partial of the ORG cases. The spec planned an ACC partial.
- `Pages/OrganizationsPage.ACC.cs` (new) has only `LinksInto(org, project)`. ACC-MEM-05 checks that no link of the page leads into the project, the navigation's included.
- `JoinExhibitionAsync()` is in `Support/MockApi.ORG.cs` (new), because only ORG-ORG-04 uses it.
- `ProjectSettingsPage.LeaveDialog` is a property built from the page's routes, not a method.

**Counts**, counted again from `test-cases.md`:

- 240 → 245 cases; Access 40 → 43; Organizations and Projects 10 → 12;
- roles: Any 36 → 39, Contributor 10 → 11, Owner 54 → 55;
- modes: M 14 → 18, B,P 41 → 42, P 48 → 49. The disposable projects of a run are 49 on desktop and 42 on a phone.

They are written in `CLAUDE.md`, `test-cases.md` and `automation-tests.md`.

**Runs** against `npm run dev:mock` (the mock profile), desktop and phone:

- after the review fixes: `MemberTests` 7/7 and `OrganizationTests` 4/4 on each;
- before them, each on both:
  - `SettingsProjectTests` 4/4;
  - `SignInTests` 13/13;
  - the Navigation area: 22 passed and 1 skipped on desktop, 18 and 5 on the phone;
  - `ProjectTests` 3/3;
  - `MyInvitationTests` 3/3.

One phone run lost the existing ACC-MEM-02 to a toast missed under load in `CreateDisposableProjectAsync`. It passed alone and in two full reruns.

`dotnet build` of the project: 0 errors. Its warnings (SA1143, SA1515) are in files this change does not touch.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the onboarding paragraph no longer says the exhibition is offered only there;
  - a new paragraph describes the card on Organizations;
  - the Settings row has **Leave project**.
- `docs/Platform/Storyteller/access.md`: the exhibition is offered on Organizations too, and **Leaving** describes **Leave project**.
- `docs/Platform/development.md`: Organizations offers the exhibition, and has no card without it.
- `src/Platform/ui.admin/README.md`:
  - the mock's exhibition;
  - `setExhibition` and Organizations asking once per page load;
  - the e2e bullet of `membership.spec.ts`;
  - the axe list.
- The automation documents and `CLAUDE.md`, see section 5.

## Decisions for the owner

- **Leave project is offered to every member, not only below Administrator.** The API lets any member leave. An Administrator had no way to leave in the UI, because the members table refuses their own row. The acceptance criterion "the last Owner still cannot" concerns Owners too. Limiting it to Readers and Contributors is a change of one condition in `settings.vue`.
- **The offer is the last card of the Organizations grid**, under the heading **The exhibition**, with the wizard's text. It shows as long as the deployment offers the exhibition and the user is not its member. It cannot be dismissed.
- **A 404 of the exhibition is kept** until vue-query drops it: ten minutes after the last page that used it, or at sign-out. An exhibition turned on in the meantime shows up after that.

## Known limits

- The members table keeps its rule for the caller's own row. An Administrator leaves through Settings, not through Members. An Owner who removes their own row there is not sent away from the page, as before this change.
- Some checks that the offer is absent (ORG-ORG-04 and the e2e test of the same name) can pass before the request for the exhibition has answered. The checks where the offer is present are real.
- While the answer about the exhibition is pending or has failed, the confirmation of a visitor says "To come back, you need a new invitation.". The text is right again once the exhibition loads, and it comes from the same cached answer as the card on Organizations.
- Leaving an organization, and a leave on Members, stay out of scope.
