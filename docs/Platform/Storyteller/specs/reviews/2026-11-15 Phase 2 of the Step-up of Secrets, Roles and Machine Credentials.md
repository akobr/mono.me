# Phase 2 of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

This review covers Phase 2 (the SDKs, sections 11 and 12) of the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md). The OpenAPI document was exported as `0.10.40` and both SDKs were regenerated from it (`f9fae98d5`); the step-up detection of both SDKs is `8dd289641`. The `Sdk.NSwag` tests pass (396, with the hardening of the review), and so do the TypeScript SDK's (364, one skipped as before), its type check, lint and build.

## What Was Done

### 11. OpenAPI snapshot and generated clients

- `OpenApiExportTests` exported `info.version` `0.10.40.50949`: `Sdk.NSwag/open.api.v0.10.40.json` next to the earlier ones, and `sdk.typescript/open.api.v0.10.40.json` in place of `0.10.34`. Both READMEs name the new file.
- `Sdk.NSwag/src/ApiSdk.g.cs`, regenerated with NSwag 14.6.3: the 401 of the 21 step-up operations is now an `ApiException<ErrorResponse>` with the new description, and the discovery's description names the step-up. A 401 of those routes without a body (missing or invalid credentials) still surfaces as an `ApiException` with status 401, now with NSwag's text for a missing body; `sform` and the MCP server decide on the status and the code, never on that text. `ApiSdk.Conditional.cs` and `ApiSdk.Annotations.cs` copy routes whose 401 did not change, so they stay as they are.
- The TypeScript SDK: `npm run generate` updated the generated client, models, Zod schemas and MSW mocks (mostly the version line of their headers), `StepUpConfiguration` and `AuthConfiguration.StepUp`, and the package version `0.10.40`.

### 12. Detecting the step-up

- `Sdk.NSwag/src/StepUp.cs`:
  - `IsStepUpRequired(this ApiException)` and `IsStepUpRequired(this ApiException, out TimeSpan? maxAge)`: a 401 whose `ErrorCode` is `StepUpRequired` (the typed result or the JSON text) or whose `WWW-Authenticate` names `insufficient_user_authentication`; the window from `max_age` (quoted or not), else from `Details.MaxAge` of the text, else of the typed result.
  - `DescribeWindow(TimeSpan)` ("10 minutes", "1 hour", "90 seconds"), as the API writes it, shared by `sform` and the MCP server.
  - `SecretsWithheldReasons`: `MtlsRequired`, `ActorRestricted`, `StepUpRequired`.
  - `test/StepUpTests.cs`: typed and plain exceptions, the challenge alone, quoted and bare `max_age`, other 401s and a 403 with the code, the typed details, the window text.
- TypeScript SDK:
  - `lib/errors.ts`: `stepUpRequiredCode`, `isStepUpRequired(error)` and `stepUpMaxAge(error)` (a whole number of seconds at least 0, else `undefined`).
  - `lib/fetcher.ts`: `StorytellerConfig.onStepUpRequired`; `failOnStepUp` reads a 401 with a JSON body on `response.clone()`, raced against the request's signal; a step-up cancels the original body, calls the hook and throws, with no refresh, no retry and no `onUnauthorized`; the retried response and the conditional reads (`runRequest`) are checked the same way; an unreadable clone counts as no step-up; an abort or a timeout while it is read fails the request.
  - `lib/conditional.ts`: `SecretsWithheldReason`.
  - Tests: 7 new in `fetcher.test.ts`, the reasons and the new root exports in `conditional.test.ts`.
  - **Deviation:** the existing test "cancels the body of the rejected 401 response before retrying" now uses a `text/plain` 401, which is not cloned; a new test checks the cancel of a JSON body on `ReadableStream.prototype.cancel`, because `Response.clone()` replaces the original body with a tee, so a spy on the original stream no longer sees it.
  - `README.md`: a section "Step-up (a recent sign-in)", the usage example with the hook, the fetcher list and the `secretsWithheld` row.
