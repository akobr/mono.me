# Join or Leave the Exhibition After Onboarding

Linear: [424-271](https://linear.app/424fun/issue/424-271/admin-ui-join-or-leave-the-exhibition-after-onboarding) — Admin UI: join or leave the exhibition after onboarding.

## Problem

The exhibition, the deployment's demo project, is offered only in the onboarding wizard (424-267). Two gaps follow:

1. **No later join.** A user who ends the wizard without the exhibition, or who got the account through an invitation, can never join it in the admin UI.
2. **No way out.** A visitor who joined cannot leave. The only way to remove yourself is the members table of the **Members** page, which only Administrators see. Even there, only an Owner who is not the last one can remove themselves.

The API already supports both. `POST v1/access/exhibition/join` works for a caller who has an account. `DELETE v1/access/points/{key}/members/{own account id}` lets any member remove themselves, without a recent sign-in.

## Current State

Paths are relative to the repository root. `UI` is `src/Platform/ui.admin/src` and `ST` is `src/Platform/Storyteller`.

### Admin UI

- `UI/app/pages/orgs/index.vue` (75 lines) builds its list from the account only: `groupMemberships(session.accessMap)` (`UI/app/utils/memberships.ts:24–49`). It shows `EmptyState` "No organizations yet" (28–35), or a grid of `UCard`s, one per organization (37–70), and has **New organization** in the top bar. It makes no API call.
- `UI/app/pages/onboarding.vue:37–58` reads the exhibition with `getExhibition({ timeoutMs: 5_000 })`. Only a `404` means "the deployment offers none"; any other failure keeps the step with Retry.
- `UI/app/composables/useOnboardingEnd.ts` joins with `joinExhibition()`. The answer is the caller's `Account`, which goes into `session.setAccount` before `router.replace` to `/{org}/{project}/default/annotations` (`projectPath`, :18). After a `404` of the join it shows the warning toast `texts.end.notOffered`. The join toast is `texts.end.joined` ("You joined the exhibition", icon `i-lucide-party-popper`).
- `UI/app/utils/onboarding.ts` holds the texts of the wizard: `onboardingTexts.exhibition` (`heading`, `text`, `promise`, …) and `exhibitionChoiceDescription()` ("Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.").
- The SDK generates `useGetExhibition` with the key `["v1","access","exhibition"]` (`ST/sdk.typescript/src/generated/vue-query/access/access.ts:2703–2760`). The admin UI does not use it yet.
- `UI/app/pages/[org]/[project]/settings.vue` is open to every member. The sidebar entry **Settings** has no `minRole` (`UI/app/utils/navigation.ts:136`). Its first card, **Project** (466–505), shows the access point, **Your role** (`RoleBadge`), the number of **Members** (only when `useGetAccessPoint` loaded, which is enabled for `canAdmin` only, 332–334) and the description. The page has no leave or delete action.
- `UI/app/components/access/MembersTable.vue:106–122` removes a member after `ConfirmDialog` "Remove <name>?" and reloads the account when the member is the caller. It does not navigate away. `UI/app/utils/roles.ts:53–55` (`memberRules`) lets only an Owner remove their own row. The API also lets an Administrator do so.
- `UI/app/components/common/ConfirmDialog.vue` takes `title`, `description`, `confirmLabel`, `color`, `loading` and `v-model:open`, and emits `confirm`.
- After a change of the access map, the navigation menu drops a project the user left: `navigationScope` passes the remembered context through `accessibleContext` (`UI/app/utils/navigation.ts:59–84`). `/` does the same (`UI/app/pages/index.vue:11`). `context.access` is computed only by the context guard, on the next navigation. So a page that removes the caller's own access must navigate away.
- Telemetry captures only `$pageview` and the clicks on links and buttons with their labels (`UI/app/boot/posthog.ts:29–30`). The labels of new buttons must not name a project.

### Mock

- `UI/app/mock/access-handlers.ts:435–465` handles `DELETE …/members/:accountId`, which differs from the API:
  - It first requires `Administrator` of every caller (`requireRole`, :438). So in the mock, a Reader or Contributor who leaves gets `403`, where the API answers `204`.
  - It lacks the API's rule that only an Owner removes another Owner (`ST/Backend.CosmosDb/src/Accessing/CosmosAccessService.cs:423–426`, `403 ElevatedRole`).
  - It already skips the step-up for the caller's own account (:450–451) and answers `409 LastOwner`.
- `GET v1/access/exhibition` (:587–601) and `POST v1/access/exhibition/join` (:603–613) answer as the API does. The GET ignores whether the caller is a member, and so does the API (`ST/Api.Functions/src/V1/ExhibitionHttp.cs:70–80`).
- In the `default` dataset (`UI/app/mock/db.ts:188–233`), Ada is:
  - Owner of `acme` and the only Owner of `acme.billing`;
  - Contributor of `acme.shipping`, whose Owner is Grace;
  - Reader of `northwind.portal`.

  She is not a member of the exhibition `exhibition.exhibition` (Grace is its Owner), so `GET v1/access/exhibition` answers. The showcase has no exhibition.
- `window.storytellerMock.setExhibition(value | null)` turns the exhibition on or off (`UI/app/mock/browser.ts:81–85`).

### API

- `CosmosAccessService.RemoveMemberAsync` (`:405–431`) lets any member leave: `isLeaving` skips the Administrator check and the Owner check, and `EnsureNotLastOwner` refuses the last Owner (`409 LastOwner`). Removing a project membership changes only that key: the account and its organization role stay. `MembersHttp.cs:113–117` asks for a recent sign-in only for the removal of someone else.
- The join (`ExhibitionHttp.cs:92–114`) keeps an existing account and a higher role, and answers with the `Account`.

### Tests and test cases

- e2e: `UI/test/e2e/onboarding.spec.ts` covers the joins of the wizard. No e2e test removes a member. `accessibility.spec.ts` and `visual.spec.ts` include `/orgs` (`organizations`), so the offer on `/orgs` changes the visual baselines `organizations-*`.
- Unit: `UI/test/unit/mock/exhibition.test.ts` covers the mock's exhibition. `UI/test/unit/mock/step-up.test.ts:219–226` covers leaving, but only as an Owner. No unit test mounts `/orgs`.
- Testiny `2SP` has 240 cases. The onboarding joins are ACC-SGN-10 and ACC-SGN-11 (495, 496, folder "Access > Sign In and Account"). The members cases are ACC-MEM-01 to 04 (50, 52, 54, 55, folder "Access > Members"). `/orgs` has ORG-ORG-01 and 02 (8, 23, folder "Organizations and Projects > Organizations"). The counts appear in `CLAUDE.md`, `docs/Platform/UI.Admin.AutomationTests/test-cases.md` and `automation-tests.md`.
- Pool users on a deployed target are members of the target project and of `Target:RoleProjects`. A real leave there would drop their membership, and the deployed exhibition is not known to the tests. So the new join and leave cases run on the mock only, except "the only Owner cannot leave", which uses a disposable project.

## Proposed Changes

### 1. The mock lets any member leave, as the API does

`UI/app/mock/access-handlers.ts`, `DELETE …/access/points/:key/members/:accountId`. The checks run in the API's order:

1. `leaving = params.accountId === db.user.id`.
2. Not leaving: `requireRole(db, key, 'Administrator')` → `403 AccessDenied`.
3. No access point, or the account is not a member → `404 MemberNotFound`.
4. Not leaving, the member is an `Owner` and the caller is not → `403 ElevatedRole` ("Only Owners can remove an Owner.").
5. The member is the last `Owner` → `409 LastOwner`. This applies to leaving too.
6. Not leaving: the step-up check (unchanged).
7. The removal, `syncAccount` and the `MemberRemoved` audit record (unchanged).

Unit tests in `UI/test/unit/mock/members.test.ts` (new). A Reader, a Contributor and an Administrator each leave, and the account keeps its other memberships. Leaving the exhibition leaves `GET v1/access/exhibition` offered, and a second join adds the membership again. The last Owner gets `409 LastOwner`. An Administrator who removes an Owner gets `403 ElevatedRole`. A Contributor who removes another member gets `403`.

### 2. Join the exhibition on `/orgs`

#### 2.1 `UI/app/composables/useExhibition.ts` (new)

```ts
/** The deployment's exhibition as the API offers it, and whether the caller is a member of it. */
export function useExhibition(): {
  exhibition: ComputedRef<Exhibition | undefined>   // only after a successful answer
  projectKey: ComputedRef<string | undefined>       // "{Organization}.{Project}"
  member: ComputedRef<boolean>                      // roleRank(session.accessMap[projectKey]) > 0
  offered: ComputedRef<boolean>                     // exhibition && !member
  isExhibition: (key: string | undefined) => boolean
  refetch: () => Promise<unknown>
}
```

- `useGetExhibition({ query: { meta: { silent: true }, staleTime: 5 * 60_000 }, request: { timeoutMs: 5_000 } })`. `silent` keeps the global error report away. The query defaults do not retry a `4xx`, and sign-out clears the cache.
- `exhibition` is defined only while the query's status is `success`. A refetch that fails after a success keeps vue-query's previous `data`, so `data` alone would keep offering an exhibition that has gone.
- Any failure hides the offer, a `404` and others alike. The exhibition is optional, and `/orgs` must not show an error for it.

```ts
/** "Join the exhibition": the join, the account of its answer, and the project. */
export function useJoinExhibition(offer: ReturnType<typeof useExhibition>): {
  joining: Ref<boolean>
  join: () => Promise<void>
}
```

`join()`:

1. It returns at once when nothing is offered or a join is in flight.
2. It calls `joinExhibition()`. The answer is the caller's `Account` with the membership: `session.setAccount(answer)` is the reload of the account, without a second request.
3. Toast `onboardingTexts.end.joined` ("You joined the exhibition", success, `i-lucide-party-popper`).
4. `router.push('/{Organization}/{Project}/default/annotations')`.

When the join fails:

- **`404`:** the warning toast "The exhibition is no longer offered." (`exhibitionTexts.notOffered`). Then `offer.refetch()`, whose `404` hides the card.
- **Any other failure:** the error toast "Joining the exhibition failed" with `errorMessage(error)`. The card stays, so the user can try again.

`projectPath` moves from `useOnboardingEnd.ts` to `UI/app/utils/memberships.ts` (`projectPath(organization, project)`). The wizard, the composable and `/orgs` share it.

#### 2.2 `UI/app/components/orgs/ExhibitionOfferCard.vue` (new)

A `UCard` (`variant="subtle"`, `data-exhibition-offer`) with:

- the header: `i-lucide-sparkles` and the heading `<h2>` **The exhibition**;
- the body: `onboardingTexts.exhibition.text` ("Step into Fourth Wall Software, …") and `exhibitionChoiceDescription()` in `text-muted`;
- the footer: the button **Join the exhibition** (`i-lucide-log-in`, `:loading="joining"`). It is a button, not a link, so `OrganizationsPage.ListedAsync` of the automation tests lists no project for it.

Props: `joining: boolean`. Emits: `join`.

#### 2.3 `UI/app/pages/orgs/index.vue`

- `const offer = useExhibition()` and `const { joining, join } = useJoinExhibition(offer)`.
- The grid renders when there are organizations or an offer. The offer card is its last item, after the organization cards. Without organizations, the grid holds only the card, below the empty state.
- The empty state keeps its text and its **New organization**.

#### 2.4 Texts

`UI/app/utils/exhibition.ts` (new) holds what is not in the wizard:

```ts
export const exhibitionTexts = {
  heading: 'The exhibition',
  join: 'Join the exhibition',
  notOffered: 'The exhibition is no longer offered.',
  joinFailed: 'Joining the exhibition failed',
} as const
```

The card reuses `onboardingTexts.exhibition.text`, `exhibitionChoiceDescription()` and `onboardingTexts.end.joined`, so each sentence of the exhibition has one source.

### 3. Leave a project from its Settings

#### 3.1 Who may leave

Every member of the project, an Administrator and an Owner included, because the API allows it. The issue asks for members below Administrator. The rule is wider on purpose: an Administrator has no other way to leave (the members table refuses their own row), and the acceptance criterion "the last Owner still cannot" concerns Owners. The only exception is the last Owner.

- **The last Owner.** On Settings, an Owner reads the access point (`useGetAccessPoint`, enabled for `canAdmin`), so `lastOwner` is true when the caller is `Owner` and no other entry of its `AccessMap` is `Owner`. A Reader, Contributor or Administrator is never the last Owner. While the access point is not loaded, `lastOwner` is false, and the API's `409 LastOwner` decides.
- **The organization** keeps the caller's role in it. The action concerns the project only. Leaving an organization is out of scope.

#### 3.2 `UI/app/composables/useLeaveProject.ts` (new)

```ts
export function useLeaveProject(): {
  leaving: Ref<boolean>
  /** Removes the caller from the project; true when the caller is no member of it any more. */
  leave: (projectKey: string) => Promise<boolean>
}
```

`leave(key)`:

1. It returns `false` without an account id, or while a leave is in flight.
2. It calls `removeMember(key, session.account.Id)`. It needs no recent sign-in, so there is no `ensureRecentSignIn`.
   - A `404 MemberNotFound` means the caller is no member any more. That counts as left, and the steps below run.
   - Any other failure shows the toast "You did not leave the project" with the description below, and returns `false`.

   | Error code | Description |
   | --- | --- |
   | `LastOwner` | "The last Owner cannot leave. Make another member an Owner first." |
   | anything else | `errorMessage(error)` |

3. The account is read again with `getAccount()`. When that read fails, the session account drops the key locally (`withoutMembership(account, key)` in `UI/app/utils/memberships.ts`). The membership is gone either way, and the page must not keep the old map.
4. `session.setAccount(account)`, then `invalidateProject(queryClient, key)`.
5. The toast "You left {key}" (success, `i-lucide-log-out`).
6. `router.replace('/orgs')`. A failed navigation falls back to `window.location.replace`, as in `useOnboardingEnd`.

#### 3.3 `UI/app/components/access/ProjectLeave.vue` (new)

Props: `projectKey: string`, `role: AccountRoleName`, `lastOwner: boolean`, `exhibition: boolean`. It renders in the footer of the **Project** card on Settings:

- **A member who may leave:** the text "Leave the project to remove it from your organizations." and the button **Leave project** (`color="error"`, `variant="outline"`, `i-lucide-log-out`).
- **The last Owner:** the same button, disabled, and the text "You are the only Owner of this project. Make another member an Owner before you leave."

The button opens `ConfirmDialog` with:

- `title`: "Leave {key}?";
- `description`: "You lose your {role} role on {key}." followed by one of:
  - "To come back, you need a new invitation." (any project);
  - "You can join the exhibition again from Organizations." (`exhibition`);
- `confirm-label`: "Leave project", `color`: `error`, `:loading="leaving"`.

The role reads as `RoleBadge` writes it: its label moves from `UI/app/components/common/RoleBadge.vue` to `roleLabel(role)` in `UI/app/utils/roles.ts` (for example "Contributor + secrets"), which both use. The dialog closes after a leave that failed, and the toast says why.

#### 3.4 `UI/app/pages/[org]/[project]/settings.vue`

- `const exhibition = useExhibition()` (cached, one request per session).
- `const lastOwner = computed(() => role.projectRole.value === 'Owner' && ownerCount(accessPoint.data.value?.AccessMap) <= 1)`, where the count needs the loaded map (`ownerCount` in `UI/app/utils/roles.ts`, `undefined` while it is not loaded).
- The Project card gets `<template #footer><ProjectLeave … /></template>` while `context.projectKey` is set and the role is above `None`.

### 4. Unit and e2e tests of the admin UI

**Unit (Vitest, the app's mock handlers on an MSW node server):**

- `UI/test/unit/orgs/exhibition-offer.test.ts` (new) mounts `/orgs` with `mountScreen` and the account of the mock db. It checks:
  - the card shows for a user without the membership;
  - it hides for a member, for a deployment without an exhibition, and after any failure of the GET;
  - the join posts once, sets the account and opens the exhibition with the toast;
  - a join answered with `404` shows the warning and hides the card;
  - a join answered with `500` shows the error and keeps the button enabled.
- `UI/test/unit/access/project-leave.test.ts` (new) mounts Settings of `acme/shipping` (Contributor). It checks:
  - Cancel sends nothing;
  - Leave sends `DELETE …/acme.shipping/members/user_01MOCKADA`, reads the account, removes `acme.shipping` from the session, invalidates and navigates to `/orgs` with the toast;
  - `MemberNotFound` counts as left;
  - a failed read of the account removes the key locally;
  - `LastOwner` and `500` show their toast and keep the membership;
  - on `acme/billing` the only Owner sees the disabled button and its text;
  - an Owner who is not the last can leave;
  - the exhibition's sentence shows for the exhibition project.
- `UI/test/unit/mock/members.test.ts` (new), see section 1.
- `useOnboardingEnd` and the wizard keep their tests. `projectPath` gets a test in `UI/test/unit/utils/shell.test.ts`.

**e2e (Playwright, `UI/test/e2e/membership.spec.ts`, new; desktop and mobile):**

1. "a user with an account joins the exhibition from the organizations page and opens it as a Reader".
2. "the organizations page offers no exhibition to its member, or where the deployment has none".
3. "a Contributor leaves a project from its settings and loses access to it".
4. "a visitor leaves the exhibition, and the organizations page offers it again".
5. "the only Owner of a project cannot leave it".

`accessibility.spec.ts` gets the open leave dialog on `/acme/shipping/settings`. The `organizations` screen already shows the offer, because Ada is not a member of the mock's exhibition. The visual baselines `organizations-*` (4) are made again in the Linux container (`npm run test:visual:docker -- --update`), with any other baseline that the footer of the Project card changes.

### 5. Test cases and automation

New cases in Testiny `2SP` (STEPS template; type Functional, status Ready for Test, automation Automated, as the others), with their tests in `src/Platform/UI.Admin.AutomationTests`:

**Organizations and Projects > Organizations** (`OrganizationTests`, folder 12):

- **ORG-ORG-03** · High · Any · M · `Organization_JoinExhibition_OpensItAsReader` — "A user with an account joins the exhibition from the Organizations page and opens it as a Reader". Mock only: the `default` scenario, where Ada has an account and is no member of `exhibition/exhibition`.
  1. Open /orgs → a card with the heading 'The exhibition', the text of the exhibition, 'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.' and the button 'Join the exhibition'; no organization 'exhibition'.
  2. Click 'Join the exhibition' → URL /exhibition/exhibition/default/annotations; toast 'You joined the exhibition'; none of the buttons 'New', 'New annotation' and 'Bulk create'.
  3. Open /orgs → the organization 'exhibition' with the badge 'Project member' and the project link 'exhibition Reader'; no 'Join the exhibition'.
- **ORG-ORG-04** · Medium · Any · M · `Organization_ExhibitionOffer_HiddenForMemberOrWithoutExhibition` — "The Organizations page offers the exhibition only to a user who is not its member, on a deployment that has one".
  1. Open /orgs, then turn the mock's exhibition off (`MockApi.SetExhibitionAsync(null)`) and open /orgs again → no 'The exhibition' and no 'Join the exhibition'.
  2. Turn it on again (`exhibition/exhibition`) and open /orgs → the button 'Join the exhibition'.
  3. Join through the mock API (`POST v1/access/exhibition/join`) and open /orgs → the project link 'exhibition Reader'; no 'Join the exhibition'.

**Access > Members** (`MemberTests`, folder 25):

- **ACC-MEM-05** · High · Contributor · M · `Members_ContributorLeavesProject_LosesAccess` — "A Contributor leaves a project from its Settings and loses access to it". Mock only: leaving drops the pool user's membership on a live target. Mock: acme/shipping.
  1. Open the project's Settings → 'Your role' 'Contributor'; the text 'Leave the project to remove it from your organizations.' and the button 'Leave project'.
  2. Click 'Leave project' → dialog 'Leave acme.shipping?' with 'You lose your Contributor role on acme.shipping. To come back, you need a new invitation.'
  3. Click 'Cancel' → the dialog closes; 'Your role' is still 'Contributor'.
  4. Click 'Leave project', then 'Leave project' in the dialog → URL /orgs; toast 'You left acme.shipping'; the card 'acme' has no project link 'shipping'.
  5. Open /acme/shipping/default/annotations → 'You have no access here'.
- **ACC-MEM-06** · Medium · Any · M · `Members_VisitorLeavesExhibition_OffersItAgain` — "A visitor leaves the exhibition from its Settings, and the Organizations page offers it again". Mock only.
  1. Open /orgs and click 'Join the exhibition' → the exhibition's annotations.
  2. Open /exhibition/exhibition/settings → 'Your role' 'Reader'; click 'Leave project' → dialog 'Leave exhibition.exhibition?' with 'You lose your Reader role on exhibition.exhibition. You can join the exhibition again from Organizations.'
  3. Click 'Leave project' in the dialog → URL /orgs; toast 'You left exhibition.exhibition'; no organization 'exhibition'; the button 'Join the exhibition'.
- **ACC-MEM-07** · Medium · Owner · B,P · `Members_OnlyOwner_CannotLeave` — "The only Owner of a project cannot leave it". In a disposable project, whose creator is its only member.
  1. Create the disposable project and open its Settings → 'Your role' 'Owner'.
  2. Look at the footer of the Project card → the button 'Leave project' is disabled; the text 'You are the only Owner of this project. Make another member an Owner before you leave.'

**Folder descriptions.** "Organizations" (12) adds the offer and the join of the exhibition. "Members" (25) adds leaving a project from its Settings. "Access" (11) adds leaving, next to the Members pages.

**Code.**

- Page objects:
  - `Pages/OrganizationsPage.ACC.cs` (new partial): `ExhibitionOffer`, `ExhibitionOfferHeading`, `JoinExhibitionButton`, `JoinExhibitionAsync()`.
  - `Pages/ProjectSettingsPage.ACC.cs`: `LeaveProjectButton`, `LeaveText`, `LastOwnerText`, `LeaveDialog(key)` (a `ConfirmDialog`).
- `Support/MockApi.ACC.cs`: `JoinExhibitionAsync()` over `FetchAsync("POST", "v1/access/exhibition/join")`.
- The texts live as constants in the page objects, as in `OnboardingPage`.

**Counts.** 240 → 245 cases; Access 40 → 43; Organizations and Projects 10 → 12; mode M 14 → 18; B,P 41 → 42 (P 48 → 49); roles Any 36 → 39, Contributor 10 → 11, Owner 54 → 55. Every number that a new case shifts is in `CLAUDE.md`, `test-cases.md` and `automation-tests.md`, and is counted again from `test-cases.md`, not added by hand.

**Not automated on a deployed target, and why.** The join depends on the target's exhibition, which the tests do not know (`prod.bicepparam` configures none). A leave would drop a pool user's membership of the target project. Both run on the mock, where every test starts from the seed.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - "The exhibition is offered only here." (:17) becomes a sentence about the card on Organizations.
  - Organizations (:31) gains **Join the exhibition**.
  - The Settings row (:257) gains **Leave project**, for every member except the only Owner.
- `docs/Platform/Storyteller/access.md`:
  - :279 names the Organizations page next to onboarding.
  - **Leaving** (:365) says that a visitor leaves on the project's Settings, and that the Organizations page offers the exhibition again.
- `docs/Platform/development.md:228`: the Organizations page offers the exhibition too.
- `src/Platform/ui.admin/README.md`:
  - :56 says that the user is no member until they join, in onboarding or on Organizations;
  - :93 says that Organizations asks once per page load too;
  - the e2e list gets `membership.spec.ts`.
- `docs/Platform/UI.Admin.AutomationTests/test-cases.md`: the five rows, the counts and this spec in the header. `automation-tests.md`: the counts, the mock's exhibition (:167) and the new page objects. `CLAUDE.md`: 245 cases.

### Commits

| Commit | Checks |
| --- | --- |
| `docs(platform): plan joining and leaving the exhibition after onboarding (424-271)` | — |
| `fix(platform): let any member leave a project in the admin UI's mock, as the API does (424-271)` | `npm test` (mock files) |
| `feat(platform): join the exhibition from the organizations page of the admin UI (424-271)` | typecheck, lint, unit, e2e |
| `feat(platform): leave a project from its settings in the admin UI (424-271)` | typecheck, lint, unit, e2e |
| `test(platform): visual baselines of the exhibition offer and the leave action (424-271)` | `npm run test:visual:docker` |
| `test(platform): Testiny cases and automation tests of joining and leaving (424-271)` | `dotnet build`, the mock profile on the five tests |
| `docs(platform): joining and leaving the exhibition after onboarding (424-271)` | — |
| `docs(platform): review of joining and leaving the exhibition after onboarding (424-271)` | — |

### Out of scope

- Leaving an organization (a role on the organization access point).
- The members table's own row: it keeps its rules, so an Administrator still leaves through Settings, not through Members.
- Hiding the offer for good ("not interested"). The card shows while the deployment offers the exhibition and the user is not its member.
- Any change of the API or the SDKs.
