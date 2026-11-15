# Phase 3 of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

This review covers Phase 3 (the clients, sections 13-15) of the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md): the admin UI, `sform`, the MCP server and the .NET configuration provider, commit `acf3a5b15`, with the fixes of the review in `552ae2b6a` and `1520d10ad` (see [Review Findings](2026-11-15%20Review%20Findings%20of%20the%20Step-up%20of%20Secrets,%20Roles%20and%20Machine%20Credentials.md)). The tests pass: the admin UI's Vitest suite (2,014 tests after the review; with `NODE_OPTIONS=--no-experimental-webstorage` on the installed Node 26, as 16 unrelated tests need without the change too), its lint, type check and build, the Playwright flows of desktop and mobile (399 passed; two tests that failed once under load pass alone and do not touch the change), `sform` (2,047 after the review) with the coverage checks of `Cli.AutomationTests` (9), the MCP server (774), the configuration provider (645, 2 skipped as before) and the hosting library (348).

## What Was Done

### 13. The admin UI

- `boot/auth.ts`: `SignInOptions.maxAge`; the AuthKit state `{ returnTo, stepUp, stepUpAuthTime }` (`signInState`, `stepUpReturnOf`), read back in `onRedirectCallback`; with `maxAge`, `signIn` takes `getSignInUrl` with the same state and login hint (the PKCE verifier is stored as by `signIn`), appends `max_age` (`withMaxAge`) and navigates; `AuthService.getAuthTime()` (from `getClaims`) and `getStepUpReturn()`; the auth configuration in the ready state. `boot/state.ts`: `getStepUpSettings()`. `boot/api.ts` and `main.ts`: the SDK hook goes to `useStepUpStore().ask()`; mock mode loads the auth configuration too.
- `composables/useStepUp.ts` (a Pinia store: `ask`, `ensureRecentSignIn`, `confirm`, `cancel`, `rememberReveal`, `takePendingReveal`, `takeReturnNotice`, and `isSignInStale`, `signedInSince`, `hasStepUpItems`) and `components/shell/StepUpDialog.vue` (the confirmation, the loop guard's error with Sign out, the toast after the return), mounted in `App.vue`. `useUnsavedChanges.leaveWithoutPrompt()` keeps the browser from asking again after the person accepted losing unsaved changes.
- The screens: Reveal in `KeyValueDetail` and `KeyValueVersions` is remembered and repeated once right after the return; Set new value, a secret level in `KeyValueCreateDialog`, New machine, Invite and a role change in `MembersTable` check the hint first; restore items in `KeyValueDetail`, `KeyValueBulkBar` and Undo deletes in `KeyValueImportDialog` (which now reports refused items) open the dialog; `ConfigurationResolved` says that secret values are hidden, with "Confirm it's you"; `describeError`, `stepUpText` and `formatWindow` in `utils/errors.ts`, and the code map of `MembersTable`.
- The mock: `mock/step-up.ts` (the flag, the 401 with its body and header, the refused item), `window.storytellerMock.staleSignIn(stale?)`, the guards in the key/value, access and configuration handlers, `StepUp` in the mock's auth configuration, a token `auth_time` an hour old while stale; a sign-in with `maxAge` clears the flag. Off by default.
- Tests: `test/unit/components/step-up.test.ts` (18), `test/unit/mock/step-up.test.ts` (9), 9 new in `boot/auth.test.ts`, 2 in `utils/shell.test.ts`, the fakes of `telemetry`, `guards` and `session`; the Playwright flow "reveals a secret after confirming a stale sign-in" in `key-values.spec.ts`.
- `README.md`: a section "Step-up", the mock bullet, the errors note, and MFA in the WorkOS dashboard checklist.
- **Deviations:**
  - The loop guard compares the new `auth_time` with the one before the step-up (`stepUpAuthTime`), and falls back to the start time; comparing with the start alone misjudges a browser clock ahead of WorkOS's.
  - After a step-up whose sign-in moved, the hint counts from the browser's time of the return, for the same reason, and its margin is the smaller of a minute and half the window, so a one-minute window does not ask again at once.
  - The mock never resolved secret bindings; under the flag it only adds the withheld header.
  - Not done: an up-front check in the import dialog (pasted secrets that meet the step-up are lost on the redirect), and a repeated version Reveal shows only when the return lands on the Versions tab.

### 14. `sform`

- `Output/StepUpRefusal.cs`: the code, the message `This needs a recent sign-in: a sign-in counts for 10 minutes.` (the server's message when no window is known), and the hint: sign out in the browser first (the admin UI's Sign out ends the browser's session) or approve the code in a private window, then `sform account logout` and `sform account`, and repeat the command.
- `ApiRefusal.Of` (the record gains `Hint`), `Program.cs` (prints the hint after the error code, exit 44), `KeyValueConsoleExtensions.WriteApiError` and `PlainOutputExtensions.WriteApiErrorLine` (machine create, reset and federation: three lines, exit 44), and `WriteBulkResult` (each item's message, and the hint after the summary when an item is `StepUpRequired`).
- `ConfigurationReads.WithheldHint(reason)` in `config get` (appended to the warning) and `config export` (after "Nothing was exported.", exit 2): mTLS, the actor, a new sign-in; the `mtls-required` texts are as before.
- Tests in `ApiRefusalTests`, `ApiRefusalExitCodeTests`, `MachineListAndResetCommandTests`, `KeyValueBulkAndDeleteCommandTests`, `ConfigGetCommandTests`, `ConfigExportCommandTests` and the fixture of `AnnotationCommandFixture`. No command or option changed, so the coverage checks and the Testiny catalogue stay as they are.

### 15. The MCP server and the .NET provider

- `Tools/ApiErrors.cs`: `StepUpRequiredCode`, `StepUpRemedy`, `StepUpItemsNotice`, `StepUpRequiredMessage(maxAge)`; `ToResult` checks the step-up first, with `MaxAge` in the structured content and nothing of the API's body copied; `DescribeWindow` delegates to the SDK.
- `Tools/SecretsWithheldNotice.cs`; `get_resolved_configuration` and `check_configuration_bindings` read through `GetResolvedConfigurationConditionalAsync` and return `SecretsWithheld` and `SecretsWithheldNotice`; restore and purge pass the items on and add `StepUpNotice` when one is `StepUpRequired`.
- **Deviation:** `ApiErrors.Read` does not read the code of a plain `ApiException`'s JSON: `TokenPathTests.UnexpectedApiAnswer_NeverWritesTheBodyToALog` pins that nothing of an undeclared body reaches a log, so only `IsStepUpRequired` looks at it. `check_configuration_bindings` was not in the plan; it made the same resolved read and would have reported a bare `false`.
- Tests: `StepUpToolTests` (create and reset machine, set and reveal a key/value through the host, a bare 401, the resolved read's notice), `ApiErrorsTests`, `SecretToolsTests`, `BindingCheckToolsTests`, `KeyValueToolsTests`.
- The provider: `DocumentReader.WithheldMessage(key, reason)` (mTLS as before, the actor, a new sign-in, a generic text), still a definitive failure; `DocumentReaderTests` over the four reasons. The conformance vectors do not check the text.
