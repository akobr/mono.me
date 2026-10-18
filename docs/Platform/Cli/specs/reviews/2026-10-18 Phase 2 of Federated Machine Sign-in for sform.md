# Phase 2 of Federated Machine Sign-in for sform

Linear: [424-62](https://linear.app/424fun/issue/424-62/sign-in-to-sform-as-a-federated-machine).

## Overview

Phase 2 covers the commands, the renewal after a `401` and the documentation: sections 6–9 and 11 of [Federated Machine Sign-in for sform](../2026-10-18%20Federated%20Machine%20Sign-in%20for%20sform.md), with the tests of section 10. It builds on [Phase 1](2026-10-18%20Phase%201%20of%20Federated%20Machine%20Sign-in%20for%20sform.md).

Commands bind their checked project, so a federated machine exchanges at that project's token endpoint. A request the API refuses with `401` is sent once more with a new token. `sform account machine create -k federated` prints the pipeline variables, and both the technical and the public docs describe the sign-in.

Commits:
- `abcac4e2` (feature) and `20ea696a` (tests). The feature commit passes the 540 tests of Phase 1; with its tests there are 571.
- `274a8921`, `0e9d34cf` and `1b72bffe`: fixes from the review below. The suite now has 572 tests, all passing.
- `21027f83`: docs.

Deviations from the plan:
- In `Startup`, `ConfigureMachineSignIn` and `ConfigureStorytellerSdk` moved above the private methods. StyleCop orders internal members before private ones, so the diff shows `ConfigureLogging` and `ConfigureOptions` as moved.
- The handler disposes the `401` on any exception from the renewal, not only an `AuthenticationException`.
- One extra test file, `SdkHttpClientConfigurationTests`.

## What Was Done

### 6. Target binding (`CommandContext`)

As planned. `TrySetExplicitTarget` calls `_machine.BindTarget(OrganizationName, ProjectName)` as its last step, after every check has passed. Only a configured machine reaches that step. So `-p` wins over `STORYTELLER_PROJECT`, a target that fails a check binds nothing, and `access.default.json` is never used for a machine.

### 7. `sform account`

As planned.
- The project check and `TrySetExplicitTarget(null, null)` are one helper, `CheckTarget()`.
- For a federated machine, `CheckTarget()` runs before `SignInAsync`: no project returns `43` and sends no request. It runs again after the sign-in, as for every kind. Invalid variables fall through to `SignInAsync` and its exit code.
- The other kinds keep their order. Their output and exit codes are unchanged.

### 8. One resend after a `401`

As planned.
- **SDK.** `AddStorytellerSdk(configurationFactory, configureHttpClient)` is a new overload. The old signature calls it with `null`, so existing callers keep binary compatibility. The hook runs for each of the seven typed clients after their primary handler is set. `Sdk.NSwag/README.md` documents it, including the redirect caveat for resending handlers. `ApiSdk.g.cs` is untouched.
- **`MachineTokenRenewalHandler`** (`Cli/src/Authentication/`). Before sending, it captures `RequestUri` and the `Bearer` value of the request. On a `401` it renews only when all of these hold:
  - a machine is configured;
  - after the response, `RequestUri` and `Authorization` are still the captured ones (`Uri` equality, exact token);
  - the content is `null` or a `ByteArrayContent`.

  It renews with `CancellationToken.None`, disposes the first response, and sends the same message once more. A `403`, a `StreamContent`, another kind (renewal returns `null`) or a redirect elsewhere gets the original response.
- **Startup.** Only the typed clients get the handler: `AddStorytellerSdk(null, builder => builder.AddHttpMessageHandler(provider => new MachineTokenRenewalHandler(provider.GetRequiredService<IMachineSignIn>())))`. The exchange client and the AuthKit client keep their own pipelines.
- `Program.cs` is unchanged, as planned.

### 9. `sform account machine create -k federated`

As planned. The skip is gone, and `WritePipelineVariables` prints `STORYTELLER_API_URL`, `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID` and `STORYTELLER_SUBJECT_TOKEN_FILE=<file with the platform token>`. Two more lines follow: one says a GitHub Actions job leaves out the file, the other lists the optional variables.
- `STORYTELLER_API_URL` is `machine.TokenEndpoint` without `/v1/{org}/{project}/access/token`. The suffix is matched ordinally, with the escaped organization and project of the context. So the endpoint `sform` builds, and the GitHub audience, equal the server's endpoint byte for byte. The test fixture shows why: `general.baseUrl` is `api.example.test`, the endpoint `api.example.com`.
- When the endpoint is missing or blank, ends differently, or is only the suffix, `general.baseUrl` is printed. A lowlight note follows: `STORYTELLER_API_URL could not be derived from the token endpoint; the rule's audience must then equal {base}/v1/{org}/{project}/access/token.`
- The curl exchange, `The machine has no secret` and the no-secret assertions of `MachineCreateFederatedTests` stay.

### 10. Tests (Phase 2 part)

- `MachineTokenRenewalHandlerTests` (new, 10 tests): the rules above, including a `302` to another host followed by a `401`, which gets no renewal and sends no Storyteller token to that host. Two of them go beyond the plan:
  - a `StringContent` request is sent again with the same body (checked after the review);
  - the renewal is never cancelled by the request's token.
- `FederatedSignInWiringTests` (new): the real `Startup` registrations with a scripted transport. After `TrySetExplicitTarget("acme.billing", null)`, one call of `IAnnotationsApiClient` sends three requests: the GitHub `GET` with the escaped audience, the exchange `POST` without `Authorization`, and the API call with the exchanged token. A variant answers `401` and checks one renewal and one resend. Since the review, a third test resolves the real handler of the exchange client and checks `AllowAutoRedirect == false`.
- `SdkHttpClientConfigurationTests` (new): the hook runs for the seven clients, and the one-parameter overload registers them as before.
- `CommandContextTests`: binding with `-p` over `STORYTELLER_PROJECT`, nothing bound without a project, and never the project or view of `access.default.json`.
- `AccountCommandTests`, `Machine_Federated_*`:
  - the GitHub source, with its variable names and the runner request before the exchange;
  - no project gives `43` without a request;
  - exit codes: `invalid_request` → `44`, `503` → `3`, an unreachable endpoint → `3`, a missing file, a missing `STORYTELLER_CLIENT_ID` or `STORYTELLER_API_URL` → `2` without a request;
  - the stored user session is never used, and no device code starts.
- `MachineCreateFederatedTests`: the variable lines, the derived URL, and the fallback with its note (a theory over a missing, a blank, another project's and a suffix-only endpoint).
- `ScriptedHttpHandler` records the raw body in `RecordedRequest.Body`, a new optional parameter.

### 11. Documentation

As planned.
- **`docs/Platform/Storyteller/authentication.md`, "Machine sign-in for pipelines".**
  - The variable table has the new rows, and the `CLIENT_ID`, `CLIENT_SECRET` and `TOKEN_SCOPE` rows are reworded for both kinds.
  - The text says that the GitHub variables are no credential variables, and that the admin UI has no federated snippet yet.
  - The shared-variable rule is stated, and the kind table has a `Federated` row.
  - A new **Federation** paragraph covers the endpoint and form, the token types, both sources, the GUID rule, the audience rule, renewal, and which commands never exchange.
  - `STORYTELLER_TOKEN_FILE` and `STORYTELLER_SUBJECT_TOKEN_FILE` are compared.
  - The target-project and `sform account` paragraphs cover federation, with sample output, and the `2`, `3` and `44` exit-code rows name the federated causes.
  - There is a GitHub Actions and a Kubernetes example.
  - The heading is unchanged, so `#machine-sign-in-for-pipelines` still works.
- **Elsewhere in `authentication.md`.** The `machine create` bullet now describes the pipeline variables and the derived URL. "The token endpoint", "Setting up a platform → GitHub Actions" and "→ Kubernetes" point to the `sform` sign-in. "Not available yet" drops the `sform` part of its federation bullet.
- **`docs/42for.net/cli/sform.md`.** The page has a `Federated` row, the project rule with `-p`, a paragraph on the exchange and the audience, and the difference between the two token files. It also has shortened GitHub Actions and Kubernetes examples, the exit codes, and a tip that mentions federation.
- **Wording that interprets the spec.** The spec calls the content of `STORYTELLER_TOKEN_FILE` "a Storyteller token". The docs say "a token the API accepts, for example a Storyteller token or a client-credentials token", as 424-45 defined it.

### Review

A review workflow checked the branch from four angles: spec conformance, correctness and concurrency, security, and test quality. An independent skeptic then tried to refute each finding. The four reviewers produced nine findings, several of them the same issue seen from different angles. Six were confirmed, covering four distinct issues, all of low severity, and all are fixed:
- `PipelineLines()` sat between public tests (StyleCop SA1202), and an unused constant raised CA1823 (`274a8921`).
- `StringContent_IsSentOnceMore` did not check that the body was sent again (`0e9d34cf`, which adds `RecordedRequest.Body`).
- The federated GitHub Actions example in `sform.md` lacked `actions/checkout`, in the docs commit.

Refuted:
- Missing review files: they are written last, here.
- An untested `AllowAutoRedirect = false` on the exchange client. The setting is older than this branch and correct, and the spec chose the scripted transport. A cheap test was added anyway (`1b72bffe`), because the federated GitHub request and exchange depend on it.

The new and changed files raise no analyzer warnings, and the CLI and `Sdk.NSwag` builds have no new ones.

### Not done

- **A run in a real GitHub Actions job** with `id-token: write`, `sform account` and one `sform story` command (spec section 10). It needs a deployed API with a trusted GitHub issuer and a federated machine. The wiring test covers the same sequence with a scripted transport. Do it once after deployment.
- The out-of-scope items of the spec are unchanged:
  - the admin UI snippet;
  - a custom GitHub audience;
  - other built-in sources;
  - `401` renewal for the other kinds;
  - a C# SDK exchange helper;
  - `-p` on `sform account`.
