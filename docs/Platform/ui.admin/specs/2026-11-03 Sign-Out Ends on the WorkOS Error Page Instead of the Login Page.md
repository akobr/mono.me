# Sign-Out Ends on the WorkOS Error Page Instead of the Login Page

Linear: [424-165](https://linear.app/424fun/issue/424-165/sign-out-ends-on-the-workos-error-page-app-homepage-url-not-found) — Sign out ends on the WorkOS error page "app-homepage-url-not-found" instead of /login.

## Problem

Signing out of the admin UI running locally (`http://localhost:5173`, AuthKit) does not return to `/login`. The browser stays on `https://error.workos.com/user_management/app-homepage-url-not-found` ("Something went wrong. Couldn't sign in."). Three automation tests fail there (Testiny run TR-4, run `20261102-230831-hviz`):

- case 12, `SignInTests.SignIn_SignOutAndContinue_ReturnsToLastProject`;
- case 13, `SignInTests.SignIn_SignedOutDeepLink_ReturnsToThePage`;
- case 107, `MyInvitationTests.MyInvitations_LandingSignedOut_ContinuesToSignIn`.

It was their first run against the real AuthKit: the review of phase 4 of the test cases lists them as "Not run: no deployed target was available".

## Current State

**The admin UI sends a correct sign-out.**

- `app/stores/session.ts:82-91`: `signOut()` runs the sign-out hooks, clears the account and calls ``getAuthService()?.signOut(`${location.origin}/login`)``. The target is absolute (`http://localhost:5173/login`); `test/unit/stores/session.test.ts:48-62` checks it.
- `app/boot/auth.ts:128-137`: `client.signOut({ returnTo: target })` of `@workos-inc/authkit-js` 0.20.4, with the default `navigate: true`. Without a session AuthKit throws `NoSessionError`, and the service goes to the target itself.
- authkit-js (`src/create-client.ts:178-224`, `src/http-client.ts:184-199`): it removes the local session (the devMode refresh token in `localStorage`), then navigates to `https://api.workos.com/user_management/sessions/logout?session_id=<sid>&return_to=<target>`.
- No unit test covers `app/boot/auth.ts`.

**WorkOS decides where the logout ends.** From the [Sessions guide](https://workos.com/docs/authkit/sessions):

- `return_to` is honoured only when it is one of the application's **Sign-out URIs** (Applications → the application → **Redirects**).
- Otherwise the browser goes to the **default** Sign-out URI.
- The **App homepage URL** is deprecated. It still works as a legacy fallback.
- With none of these, WorkOS shows `app-homepage-url-not-found`. WorkOS' own AuthKit setup guidance names this error: "inspect the application's default Sign-out URI … The error URL's legacy wording does not mean the app needs a new homepage route."

**The WorkOS Staging environment has no Sign-out URI.** Its settings were read on 2026-11-03 through the WorkOS MCP (read only). Staging is the environment of client `client_01M3MMV9SP2W3D7VDZ3242KJNR`, which `Api.Functions/src/local.settings.json` and the dev profile use.

| Setting | Value |
| --- | --- |
| Redirect URIs | `http://localhost:5173/callback`, and `http://localhost:7071/callback` (the default) |
| CORS origins | `http://localhost:5173` and `http://localhost:7071` |
| Sign-out URIs | none |
| App homepage URL | none |

Production has nothing configured, and no deployed admin host is registered anywhere. The cause is the WorkOS configuration, not the code.

**The docs know the setting but do not make it hard to miss.**

- `src/Platform/ui.admin/README.md:144-154` "WorkOS dashboard checklist" lists "Sign-out redirect: `https://<admin-host>/login` (and `http://localhost:5173/login`)". It does not say:
  - which environment the localhost entries belong to;
  - that one Sign-out URI must be the default;
  - what happens without one.
- `docs/Platform/development.md` §3 "WorkOS AuthKit (staging)" lists only the MCP settings. Its troubleshooting table (§8) has no sign-in or sign-out row.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md:115` "AuthKit checklist" says "(and the sign-out redirect `/login`) registered" without an origin. Its troubleshooting table has no row for this either.
- `docs/Platform/Storyteller/authentication.md` "AuthKit dashboard setup" covers only the API side.

**The automation tests report a bare URL mismatch.**

- `Pages/App.cs:293-298` `SignOutAsync` and the inline sign-out of case 12 (`SignInTests.cs:49-50`) wait for `/login(?:[?#].*)?$`.
- When WorkOS keeps the browser on its error page, the failure says only "Page URL expected to match regex … But was: 'https://error.workos.com/…'". Nothing points at the missing Sign-out URI.
- Cases 12 and 13 then sign in again on the hosted page (Email, Password) and need the WorkOS session really ended. Case 107 only waits for the hosted page.

## Proposed Changes

### 1. The WorkOS configuration (the fix; outside the repository)

- In the **Staging** environment, open Applications → the admin UI's application → **Redirects** → **Sign-out URIs**. Add `http://localhost:5173/login` and **Set as default**.
- In every environment that serves a deployed admin UI, add `https://<admin-host>/login` as its default Sign-out URI.
- Leave the App homepage URL empty: WorkOS deprecated it, and the default Sign-out URI replaces it.

This changes the identity provider's allowlist, so the owner makes the change and the pull request lists it. Nothing in the repository applies it.

### 2. Keep the navigating logout (decision, no code change in `auth.ts`)

The issue suggests catching the WorkOS error in the app. I considered three ways and rejected each:

- **`client.signOut({ navigate: false, returnTo })` followed by `location.assign(target)`.** This would always end on `/login`, but it only hides the configuration error:
  - the logout runs as a `no-cors` fetch, so its result is opaque;
  - whether WorkOS revokes the session when it cannot resolve a redirect is not documented;
  - the request is cross-site, so browsers may drop WorkOS' cookies.
  
  A session that stays alive lets the next "Continue with AuthKit" sign the user back in silently, which is a weaker sign-out and breaks cases 12 and 13. WorkOS recommends redirecting to the logout endpoint. `navigate: false` exists for WebViews (authkit-js PR #84).
- **Detecting the error page from the app.** Not possible: the browser has left the app for `error.workos.com`.
- **Setting the App homepage URL as the fallback.** It is deprecated. The default Sign-out URI is its replacement (change 1).

`auth.ts` stays as it is. A unit test pins the behaviour (change 3).

### 3. Unit test of the AuthKit service's sign-out

Add `test/unit/boot/auth.test.ts`, in the style of `test/unit/boot/telemetry.test.ts`.

Setup:

- `vi.hoisted` holds a fake client (`signOut`, `getUser`, `getAccessToken`, `signIn`) and the `createClient` mock.
- `vi.mock('@workos-inc/authkit-js', …)` returns `createClient`, plus `NoSessionError` and `LoginRequiredError` classes so that `instanceof` works.
- The auth configuration (`{ Provider: 'AuthKit', ClientId: 'client_test' }`) is seeded in `sessionStorage` under `2s-admin:auth-configuration:<apiBaseUrl>`, so `createAuth` makes no request.
- `location.assign` is spied.

Cases:

1. `createAuth` creates the client with ``redirectUri: `${location.origin}/callback` `` and `devMode` on localhost. These are the Redirect URI and the local mode of the checklist.
2. `signOut('<origin>/login')` calls `client.signOut({ returnTo: '<origin>/login' })`, the navigating logout through WorkOS (no `navigate: false`), and does not call `location.assign`.
3. When `client.signOut` throws `NoSessionError`, the service goes to the target itself (`location.assign(target)`).
4. Any other error of `client.signOut` propagates, and `location.assign` is not called.

### 4. A clearer failure in the automation tests

In `src/Platform/UI.Admin.AutomationTests/src/Pages/App.cs`:

- New `ClickSignOutAsync()`:
  - reads the app's origin from the current page;
  - clicks **Sign out** of the open user menu;
  - waits for `/login` as before.
- When that wait fails and the page is no longer on the app's origin, it throws a `PlaywrightException` with the original assertion as the inner exception. The message reads: "The sign-out ended on `<url>` instead of `<origin>/login`: WorkOS returns there only when `<origin>/login` is a Sign-out URI of the target's WorkOS environment, or its default one ("AuthKit checklist" in `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`)."
- A failure on the app's own origin keeps Playwright's message.
- `SignOutAsync()` opens the user menu and calls `ClickSignOutAsync()`.
- Case 12 (`SignInTests.cs:49-50`) uses `ClickSignOutAsync()` after it checks the menu items, instead of the click plus `ExpectAtAsync(Routes.Login())`.

The test logic and its expectations do not change.

### 5. Documentation

- `src/Platform/ui.admin/README.md`, "WorkOS dashboard checklist":
  - use WorkOS' names and places (the application's **Redirects**: Redirect URIs, Sign-out URIs);
  - say that a Sign-out URI must be the **default**;
  - say that the localhost entries belong to the **Staging** environment;
  - say what a missing Sign-out URI does (`app-homepage-url-not-found`);
  - say that the App homepage URL is not needed.
- `docs/Platform/development.md`:
  - §3 gets a step for the admin UI: the Redirect URI `http://localhost:5173/callback`, the default Sign-out URI `http://localhost:5173/login` and the CORS origin `http://localhost:5173`;
  - §8 gets a troubleshooting row for `app-homepage-url-not-found`.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`:
  - the checklist item names `<admin origin>/callback`, and `<admin origin>/login` as the default Sign-out URI (for the `dev` profile, the localhost URLs in Staging);
  - a troubleshooting row matches the new failure message.
- `docs/Platform/Storyteller/authentication.md`, "AuthKit dashboard setup": a step that points to the admin UI's checklist for its Redirect URI, Sign-out URI and CORS origin.

### 6. Verification

- `npm test`, `npm run lint` and `npm run typecheck` in `src/Platform/ui.admin/src`.
- `dotnet build` of the automation tests.
- Cases 12, 13 and 107 with the `mock` profile against `npm run dev:mock`, if it can be started, to show the helper did not change the passing path.
- After change 1, the owner runs the three cases with the `dev` profile against the local stack. That run needs the pool passwords and AuthKit Staging.
