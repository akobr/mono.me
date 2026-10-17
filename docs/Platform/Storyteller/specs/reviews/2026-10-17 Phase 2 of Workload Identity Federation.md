# Phase 2 of Workload Identity Federation

Linear: [424-46 Add workload identity federation](https://linear.app/424fun/issue/424-46/add-workload-identity-federation)

## Overview

Phase 2 covers the OpenAPI document and both SDKs (section 13 and the README parts of 16) of [Workload Identity Federation for Machine Access](../2026-10-17%20Workload%20Identity%20Federation%20for%20Machine%20Access.md). The document now describes the federation API of [Phase 1](2026-10-17%20Phase%201%20of%20Workload%20Identity%20Federation.md), the NSwag and TypeScript SDKs are regenerated from it, and the TypeScript SDK gains a helper that exchanges a platform token for a Storyteller token and plugs into `getAccessToken`. A reviewer and a skeptic went over the result; the one confirmed finding is fixed.

| Suite | Result |
|---|---|
| `sdk.typescript` typecheck, lint, build | pass |
| `sdk.typescript` Vitest | 119 passed, 1 skipped (17 federation tests) |
| CI gate (`npm run generate` + `git diff --exit-code -- generated`) | identical on a second generation |
| `ui.admin` build, lint | pass |
| `ui.admin` Vitest | 461 passed |
| `ui.admin` visual suite in the Playwright container (compare only) | 60 passed, baselines unchanged |
| `Sdk.NSwag`, `Cli` builds | 0 errors, no new warnings |
| `Cli/test` | 190 passed |

## What Was Done

### 13. OpenAPI and SDKs

- **Document.** `OpenApiExportTests` rendered version `0.9.17.17507`, saved as `open.api.v0.9.17.json` in `Sdk.NSwag/` and `sdk.typescript/`. Compared with 0.9.15 the only changes are the federation surface: `Federated` in the four kind enums, `FederationRules` on `MachineAccess` and `MachineAccessCreate`, the issuer and rule operations, `ExchangeMachineToken` with its form body, and the new models.
- **Superseded copies.** Only `sdk.typescript/open.api.v0.9.15.json` was removed, as the regeneration in `f7414f37` did; the older copies there (0.8.89–0.9.8) and the NSwag history stay (**deviation** from "remove superseded copies"; a cleanup of its own).
- **NSwag.** `ApiSdk.g.cs` regenerated with NSwag 14.6.3; the README command points at v0.9.17. `ExchangeMachineTokenAsync` sends `FormUrlEncodedContent` and throws `ApiException<OAuthErrorResponse>` for 400 and 503 (checked by hand with a throwaway program and a stub handler; `Sdk.NSwag` has no test project, so nothing of it is committed). Both generators handle the form body, so no generator workaround was needed.
- **Usage limit of the generated token operations.** The DI clients of `AddStorytellerSdk` attach `Authorization` from `AccessTokenFactory`, and Orval's `exchangeMachineToken` goes through `storytellerFetch`, which does the same; the token endpoint refuses that with `invalid_request`. Both READMEs say to call the endpoint without credentials (a plain `AccessApiClient` on a non-redirecting `HttpClient` in C#, the helper in TypeScript).
- **Claim maps.** The C# `IReadOnlyList<string>` values surface as a schema named `IReadOnlyList_String` (`Collection<string>` in C#, `string[]` in TypeScript). It is usable; a nicer name would need a document filter in Api.Functions.
- **TypeScript helper** (`src/lib/federation.ts`, exported from the root entry point; tsup entries and package exports unchanged):
  - `exchangeFederatedToken({ baseUrl, organization, project, machineId, subjectToken, subjectTokenType?, scope?, fetch? })` posts the form to `{baseUrl}/v1/{organization}/{project}/access/token` (segments encoded through the generated URL function) with plain `fetch`, no `Authorization` and `redirect: 'error'`, and returns `{ accessToken, expiresAt, scope }`. `subjectTokenType` defaults to `urn:ietf:params:oauth:token-type:jwt`.
  - `createFederatedTokenProvider(options & { getSubjectToken })` caches the token until 60 s before it expires, shares one exchange between concurrent callers, exchanges again on `forceRefresh`, and does not cache failures.
  - **Beyond the spec:** an exported `FederatedSubjectTokenType` constant and the option and result types; `expiresAt` in milliseconds since the epoch, counted from when the request was sent; a 2xx answer without `access_token` or `expires_in` and network failures (status 0) become `StorytellerError`s that never carry the subject token.
  - **OAuth errors** (**deviation** in form): `StorytellerError.fromResponse` maps an OAuth body onto the existing `errorCode` (`error`) and `message` (`error_description`) instead of a new field; `ErrorResponse` fields are read first, so existing behaviour is unchanged.

### 16. Documentation (Phase 2 part)

- `Sdk.NSwag/README.md`: version bump and a "Workload identity federation" section (the operations, building the claim maps, calling the token endpoint without credentials and redirects).
- `sdk.typescript/README.md`: the helper in the entry-point table, the OAuth `errorCode` note, and a "Workload identity federation" section (Vercel with `getVercelOidcToken`; GitHub Actions, a Kubernetes token file, Azure and Google; error handling; why not to use the generated `exchangeMachineToken`).
- `authentication.md`: the "Not available yet" bullet no longer says the SDKs lack federation.

### 15. Admin UI (only what the regenerated SDK forced)

- `app/utils/machines.ts` describes `Federated`, because `credentialKindDescriptions` is an exhaustive `Record<MachineCredentialKind, string>` and `vue-tsc` failed without it. Everything else is Phase 4. Until then the settings page lists `Federated`, and the mock API refuses it ("doesn't exist").

## Review fixes

| Finding | Fix |
|---|---|
| A failed refresh within the last 60 s threw away a token that was still valid, so an outage of the token endpoint failed API calls that could have gone through | `createFederatedTokenProvider` falls back to the cached token while it has not expired; a forced refresh (the fetcher's retry after a 401) never falls back; four tests, three of which fail without the fallback |

Rejected by the skeptic: "the admin UI offers Federated without a rule editor" (Phase 4 by plan) and "`IReadOnlyList_String` leaks into both SDKs" (usable, and renaming is an API change).

## Verification

```
$env:STORYTELLER_OPENAPI_EXPORT = "<path>/open.api.v0.9.17.json"
dotnet test src/Platform/Storyteller/Api.Functions/test --filter "FullyQualifiedName~OpenApiExportTests"
nswag openapi2csclient /input:open.api.v0.9.17.json …          # Sdk.NSwag README command
cd src/Platform/Storyteller/sdk.typescript/src; npm ci; npm run generate; npm run typecheck; npm run lint; npm test; npm run build
cd src/Platform/ui.admin/src; npm ci; npm run build; npm run lint; npm test; npm run test:visual:docker
dotnet build src/Platform/Storyteller/Sdk.NSwag/src; dotnet build src/Platform/Cli/src; dotnet test src/Platform/Cli/test
```

- Generated locally with Node 23.11.0 and npm 10.9.2 (`.nvmrc` pins Node 24, CI uses npm 11); two generations were byte-identical.
- Not tested: a sform or NSwag client generated before 0.9.17 reading a `Federated` machine; its `StringEnumConverter` would most likely throw.
- Document version 0.9.17 comes from the NBGV git height; whichever of this branch and the other open branches merges later exports the document and regenerates both SDKs again.
