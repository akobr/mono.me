# Sign-Out Ends on the WorkOS Error Page Instead of the Login Page

## Overview

Signing out of the local admin UI ended on WorkOS' `app-homepage-url-not-found` page instead of `/login`, and automation cases 12, 13 and 107 failed there. The app sends a correct absolute `returnTo` (`<origin>/login`). The cause is the WorkOS Staging environment: it has no Sign-out URI and no App homepage URL, so WorkOS has nowhere to send the browser.

This change keeps the navigating logout and pins it with a unit test. It also makes the automation tests name the missing Sign-out URI, and fixes the WorkOS setup docs. The WorkOS setting itself is left for the owner to change, outside the repository.

Spec: [2026-11-03 Sign-Out Ends on the WorkOS Error Page Instead of the Login Page](../2026-11-03%20Sign-Out%20Ends%20on%20the%20WorkOS%20Error%20Page%20Instead%20of%20the%20Login%20Page.md). Linear: [424-165](https://linear.app/424fun/issue/424-165/sign-out-ends-on-the-workos-error-page-app-homepage-url-not-found).

## What Was Done

### 1. The WorkOS configuration

**Not applied.** This change edits the identity provider's allowlist, so the owner makes it. The steps, also in the pull request:

- In **Staging**, open Applications → the application → **Redirects** → **Sign-out URIs**.
- Add `http://localhost:5173/login`, then **Set as default**.
- Each deployed environment needs its `https://<admin-host>/login` as the default Sign-out URI. No deployed admin host is registered yet.

The Staging settings were read once through the WorkOS MCP, read only, to confirm the cause.

### 2. Keep the navigating logout

`app/boot/auth.ts` is unchanged. The spec's reasons hold: `navigate: false` and detecting the error page would only hide the missing setting, and the App homepage URL is deprecated.

### 3. Unit test of the AuthKit service's sign-out

New `src/Platform/ui.admin/src/test/unit/boot/auth.test.ts` with the four cases of the spec:

- the client in devMode with `<origin>/callback`;
- `client.signOut({ returnTo })` without `navigate: false`;
- `NoSessionError` → `location.assign(target)`;
- any other error passed on.

The mocked `authkit-js` keeps `NoSessionError` and `LoginRequiredError` as classes. The configuration is answered from the `sessionStorage` cache.

As a check, `auth.ts` was switched to `navigate: false` for a moment, and the sign-out case failed.

### 4. A clearer failure in the automation tests

`Pages/App.cs` has `ClickSignOutAsync()`. `SignOutAsync()` (cases 13 and 107) and case 12 use it. The expectations are the same as before.

When the sign-out ends off the app's origin, it throws a `PlaywrightException` with Playwright's assertion as the inner exception. The message begins "The sign-out ended on `<url>` instead of `<origin>/login`" and names the Sign-out URI to register.

Deviations from the plan, from the review of the branch:

- **It waits for `/login` on the app's own origin** (`^<origin>/login…`) instead of any URL ending in `/login`. Otherwise, a WorkOS default Sign-out URI on another host's `/login` would have passed the wait and failed later without a hint.
- **The comment and the message describe the WorkOS fallback correctly.** WorkOS uses the default Sign-out URI, and the error page only without one. The first version read as if any default Sign-out URI brought the browser back to the app.
- **`OriginOf` returns `null` for a URL without a host** (`about:blank`, `data:`), as its comment says.

### 5. Documentation

- `src/Platform/ui.admin/README.md`, "WorkOS dashboard checklist":
  - the settings use WorkOS' names and places (**Redirects**: Redirect URIs, Sign-out URIs);
  - the localhost entries belong to **Staging**;
  - the Sign-out URI must be the **default**;
  - a missing one ends on `app-homepage-url-not-found`;
  - the App homepage URL is not needed.
- `docs/Platform/development.md`:
  - step 4 of §3 covers the admin UI: the Redirect URI, the default Sign-out URI and the CORS origin;
  - §8 has a troubleshooting row for `app-homepage-url-not-found`.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`:
  - the AuthKit checklist names the full URLs, including the `dev` profile's URLs in Staging;
  - the troubleshooting table has a row for the new failure message.
- `docs/Platform/Storyteller/authentication.md`, "AuthKit dashboard setup": step 7 points to the admin UI's checklist.

### 6. Verification

**In `src/Platform/ui.admin/src`:**

- `npm run lint`, `npm run typecheck` and `npm test` pass (102 files, 1482 tests).

**`src/Platform/UI.Admin.AutomationTests/src`:**

- `dotnet build` passes, with no new warnings.
- With the `mock` profile against `npm run dev:mock` in this worktree:
  - `SignInTests` passes, 6 of 6, including cases 12 and 13;
  - case 107 passes.
  - Both were run again after the review fixes, with the same results.

**Forced failures:** the mock's sign-out was pointed at `about:blank`, then at `http://127.0.0.1:5174/login`, which is another origin. Each failed case 13 with the new message, and the mock was restored afterwards.

**Not run:** the `dev` profile against the local stack with AuthKit Staging. It needs the Sign-out URI from change 1 and the pool passwords. After the setting is added, run:

```sh
UITESTS_PROFILE=dev dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/*/*/SignInTests/*"
```

Then run case 107 (`/*/*/MyInvitationTests/MyInvitations_LandingSignedOut_ContinuesToSignIn`) the same way.
