# Phase 4b of Aligning the OpenAPI Document and sform with the Provider Contract

Linear: [424-96](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security).

## Overview

Phase 4b covers the `STORYTELLER_KEY` target of section 10, section 11 and the phase 4 docs of section 12 of [Align the OpenAPI Document and sform with the Provider Contract](../2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md). A machine can now take its target from `STORYTELLER_KEY`, the variable of the configuration provider, so a job configured with the canonical names alone works for both. `sform account machine create` and the admin UI print the canonical names, and the docs describe the sign-in as it is after phase 4a, with a list of the changes against the earlier `sform`. A code review and a docs review followed; the docs review's four corrections are the second commit.

| Commit | Content |
|---|---|
| `feec55d0` | The target, the outputs, the admin UI snippet, the phase 4 docs |
| `fa19787e` | Docs review fixes |
| `74fc03ac` | Fixes of the branch review (below) |

| Check | Result (end of the phase) |
|---|---|
| `Cli/test` | 817 passed (777 before: 35 new cases, and 5 after the branch review) |
| `Sdk.NSwag/test` | 284 passed |
| `Configuration/test` | 548 passed, 2 skipped |
| ui.admin `lint`, `test`, `build` | green; 1447 passed |

## What Was Done

### 10. The target

- `MachineSignInSettings` reads `STORYTELLER_KEY` with the SDK's reader and parses it with `FullKey.TryParse`, as the provider parses it; reading never throws. An invalid key makes `GetKind()` throw "STORYTELLER_KEY must be a full key, @{organization}.{project}.{view}.{annotation key}." (exit `2`). **Deviation:** the check is in `GetKind`, like that of a malformed `STORYTELLER_PROJECT`, so it stops every command that signs the machine in, also one with `-p`; the spec did not decide this.
- `CommandContext`, for a machine only: `-p`/`-v`, then `STORYTELLER_PROJECT`/`STORYTELLER_VIEW`, then the organization and project of the key, then the project of the credential. The key's view applies while the target is the key's project (compared without case), wherever that project came from; otherwise the view is `STORYTELLER_VIEW` or `default`. `STORYTELLER_PROJECT` wins over a key of another project. A key of another project than the credential's gives the existing "The machine credentials in … belong to …" error. A user never reads the key.
- A federated `sform account` takes the key's project when `STORYTELLER_PROJECT` is unset (it exited `43` before). The three "no project is set" messages name `STORYTELLER_KEY` as a way.
- `CommandContextTests` has 20 new cases, among them `StorytellerKey_NamesTheSameProject_ForSformTheSdkAndTheProvider`: one key, given to `sform`, to an unbound `StorytellerCredential.FromEnvironment` (through the exchange URL) and to `FullKey.Parse`, names the same organization, project and view. 15 more cases in `AccountCommandTests`, `MachineAuthenticationServiceTests`, `MachineSignInSettingsTests` and `MachineCreateCommandTests`.
- The SDK's unbound fallback (`UnboundTarget`) still splits a key more loosely (three segments). `sform` always binds its credential, so the two never meet in `sform`; for a valid key all three agree.

### 11. Outputs

- `machine create` prints `STORYTELLER_ENDPOINT` (for a federated machine still derived from its token endpoint, and named in the "could not be derived" note), `STORYTELLER_TOKEN_FILE=<file with the platform token>` (also in the GitHub hint), `STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`, and `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE`, `STORYTELLER_API_KEY` as before. So `sform account` no longer names as deprecated what `machine create` printed.
- `PrintedVariables_AreCanonicalNamesOfTheSdk` (six kinds): every `STORYTELLER_*` name on the console is an SDK constant and none is deprecated. **Deviation:** `STORYTELLER_PROJECT`, which is `sform`'s and not the SDK's, is the one name allowed besides.
- Admin UI: `OneTimeSecretDialog.vue` writes `STORYTELLER_ENDPOINT=` and `STORYTELLER_API_KEY=`; a new test in `access.test.ts` checks it and the absence of `STORYTELLER_API_URL`.
- The `account` output, the `401`/`403` hint of `Program.cs` and `account logout` already used the SDK names after phase 4a.

### 12. Documentation

- `docs/Platform/Storyteller/authentication.md`, "Machine sign-in for pipelines", rewritten: the variables with a deprecated-name column, `STORYTELLER_KEY`, `STORYTELLER_TOKEN_FILE` with and without a client id; deprecated names, the URL rule and mixed kinds; client credentials with renewal and direct AuthKit discovery; a "Token requests" block (attempt timeout, retries, `Retry-After`, the circuit breaker, the 100-second deadline, the `44`/`3` split); federation with the GitHub runner and the exact hint; the target with the key and its view; `sform account`; the exit-code table; and a numbered list of the changes against the earlier `sform`. The GitHub Actions and Kubernetes examples and the platform setups use the canonical names, and the user-provider section says a machine reads neither `authentication.provider` nor `auth.discovery.json`.
- `docs/Platform/Storyteller/configuration-provider.md`: "one set of variables drives `sform` and the provider" is now true and says how; the column says "Deprecated".
- `docs/42for.net/cli/sform.md`: the variables and examples, the deprecated names, a federated project from `STORYTELLER_KEY`, the deadline among the exit codes, and `sform story config <key> --resolved` instead of the non-existent `config get`.
- `docs/42for.net/platform/using-the-platform.md`: the provider shares the endpoint and credential variables with `sform`, and needs `STORYTELLER_KEY` besides.
- `src/Platform/Storyteller/Sdk.NSwag/README.md`: `sform`'s renewal handler renews client credentials too.
- **Docs review fixes:** the change list says that discovery failures ended with `2` and that a domain with a path was used; "never print a value" became "never print a secret" (file paths and cleaned URLs are printed), also in the SDK README; a stale "Not available yet" bullet about the C# SDK and federation went; the comment of `WritePipelineVariables` no longer says the provider reads `STORYTELLER_PROJECT`.

### After the branch review

Four independent reviewers then read the whole branch (security, SDK correctness, `sform` correctness, docs against code), and every finding was checked by a second agent that tried to refute it. No blocker or major finding survived. Fixed in `74fc03ac`:

- **The API key in the public variables.** `StorytellerCredentialVariables.ApiKey` returned the whole key, secret included; a structured logger or a serializer reads public properties, not `ToString()`, and would print it. It is now `ApiKeyMachine`, the machine segment of a structured key (`2s.{segment}.{secret}`), which is all `sform` needs: `MachineApiKey.TryParseMachine` reads the identity from it. The SDK test checks that no public property holds the secret. **Deviation from the spec**, whose property list had a public `ApiKey` against its own rule that no secret is readable through the type.
- **The deadline message** names retries and `Retry-After` waits and the sign-in discovery, because a rate-limiting endpoint, not only a silent one, can run the 100 seconds out.
- **`sform.md`** says that `-v` and `STORYTELLER_VIEW` win over the view of `STORYTELLER_KEY`, as `authentication.md` does.

Refuted: the `mtls` scheme description was said to hide the deployment switch `MachineAuth:IsCertificateHeaderFromClientAllowed`; the description is about what a client does, and `authentication.md` documents the switch.

Not done, as in the spec: a conditional helper for the composition read, machine credentials in the TypeScript fetcher, a public `FromGitHubActions`, and the flat formats of C5 (424-104). Two test projects outside this change (`Monorepo/Repo.Generator/test`, `Platform/Supervisor/Abstraction/test`) do not build on `net6.0` with the current test SDK; that is so on `main` too.
