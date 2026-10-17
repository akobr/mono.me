# Phase 2 of Non-interactive Sign-in for sform

Linear: [424-45](https://linear.app/424fun/issue/424-45/add-non-interactive-cli-sign-in).

## Overview

Phase 2 covers the commands and the documentation: sections 12, 13 and 15 of [Non-interactive Sign-in for sform](../2026-10-17%20Non-interactive%20Sign-in%20for%20sform.md). It builds on [Phase 1](2026-10-17%20Phase%201%20of%20Non-interactive%20Sign-in%20for%20sform.md).

`sform account` signs a machine in without a prompt, and `sform account machine create` prints the variables a pipeline needs. Both the public and the technical docs describe the pipeline sign-in.

The work is in these commits:
- `ca18e108`: feature.
- `187b4fbe`: tests.
- `046fb5e2`: docs.
- `7f088157`, `88db062d`, `dca51b09`: fixes after an adversarial review of both phases.

There are two changes from the plan:
- `sform account` refuses a token file whose JWT has expired.
- The heading in `authentication.md` is `Machine sign-in for pipelines`, so the anchor works on GitHub and VitePress alike.

## What Was Done

### 12. `sform account` and `sform account logout`

`AccountCommand` gets `IMachineSignIn` and `ICommandContext`, and `AccountLogoutCommand` gets `IMachineSignIn`, as planned.

In machine mode `SignInMachineAsync` calls `SignInAsync` and prints the planned lines. The header renders as `# Machine sign-in`. It never calls `GetAccountAsync`, the device code or a prompt, and `--browser` is ignored.

Checks:
- No project: the command prints the planned hint and returns `43`.
- Then `TrySetExplicitTarget(null, null)` runs the checks every command runs. A target that differs from the credential's project returns `2`.

**Change from the plan:** a token file whose JWT `exp` has passed fails the sign-in with `AuthenticationFailureReason.Expired`. The message is `The token in STORYTELLER_TOKEN_FILE expired at {exp:O}.` and the exit code is `44`. Without the check, `sform account` printed a "valid until" time in the past, exited `0`, and the next step failed with `401`. The check runs in `MachineAuthenticationService.SignInAsync`. `GetAccessTokenAsync` still sends the token and leaves the decision to the API.

| Outcome | Exit code |
|---|---|
| Success | `0` |
| `Configuration` | `2` |
| `ServiceError` | `3` |
| `Denied`, `Expired` | `44` |

`sform account logout` in machine mode prints the planned message and returns `0`. It touches neither the API nor the stored session.

### 13. `sform account machine create`: pipeline variables

As planned. `MachineCreateCommand` gets `IOptions<GeneralOptions>`. After its current output it prints `To sign in sform in a pipeline, set:`, followed by one indented line per variable:

| Kind | Lines |
|---|---|
| `ApiKey` | `STORYTELLER_API_URL`, `STORYTELLER_API_KEY=<access key above>` |
| `Certificate` | `STORYTELLER_API_URL`, `STORYTELLER_CERTIFICATE_FILE`, `STORYTELLER_CERTIFICATE_PASSWORD=<certificate password above>` |
| `CertificateAndApiKey` | the key line, then the certificate lines. With a shared certificate, `<shared certificate .pfx>` and `<its password>`. |
| `ClientCredentials` | `STORYTELLER_API_URL`, `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET=<client secret above>`, `STORYTELLER_TOKEN_URL` (only when known), `STORYTELLER_TOKEN_SCOPE` (only when set) |

- `STORYTELLER_CERTIFICATE_FILE` is the full path of the written `.pfx` (`IFileSystem.Path.GetFullPath`).
- `WriteClientCredentialsAsync` now returns the token URL when the server or the AuthKit discovery named it, and null when the output falls back to the `https://<your AuthKit domain>` placeholder.
- The existing lines are unchanged, including the `curl` line with `$CLIENT_SECRET`, and the earlier tests pass as they were.

### 14. Tests (Phase 2 part)

| Class | Cases |
|---|---|
| `Commands/AccountCommandTests` (new) | 9 |
| `Commands/AccountLogoutCommandTests` (new) | 2 |
| `Commands/MachineCreateCommandTests` | 19, 6 new |

The account tests use the real `MachineAuthenticationService` and `CommandContext`, with strict mocks for `IAccessApiClient` and `IAuthenticationService`. A device code or account call would therefore fail the test.

`dotnet test src/Platform/Cli/test`: **317 passed**, 0 failed. The commands were not run against a live API or a real pipeline.

### 15. Documentation

**`docs/Platform/Storyteller/authentication.md`.** The new `### Machine sign-in for pipelines` under `## Signing in with sform` covers:
- the variables and the kinds;
- the certificate and the `X-ARR-ClientCert` header;
- the client credentials rules (redirects, caching, the AuthKit fallback);
- the token file and the target project;
- `sform account` and `logout`;
- the exit codes and a GitHub Actions example.

The machine kinds table and the client credentials section link to it.

**`docs/42for.net/cli/sform.md`.** The stub is replaced by `## Sign in` and `## Sign in from a pipeline`. The pipeline section has a variable table, a GitHub Actions example, a warning about job logs, and a link to the technical section.

**Other files.**
- `docs/42for.net/install.md`: a pointer to the pipeline sign-in.
- `src/Platform/Storyteller/Sdk.NSwag/README.md`: a `## Credentials` section on `AccessTokenFactory`, `ClientCertificate` and `ClientCertificateHeaderName`.

**Corrections after the review (`dca51b09`).**
- The examples used `sform story config my.service -r -e …`. `my.service` is not an annotation key, so the API answers `400`, which crashed the step. The read would also have printed resolved secrets into the job log. Both examples now write configuration with `sform story config set sbt.northwind -i configuration/northwind.json`.
- "Never prompts" and "stores nothing" are narrowed:
  - `sform` never asks anyone to sign in and stores no credential or token.
  - The AuthKit token URL fallback still caches `auth.discovery.json`.
  - Commands keep their own confirmations, for example before overwriting an `-e` file.
- The `44` row now says that the commands handle some `403`s themselves.

### Review

Two review workflows looked at the phases. Each examined the work from several angles, and an independent skeptic tried to refute every finding.

Confirmed and fixed (`7f088157`, `88db062d`, `dca51b09`):
- `-p` kept a view of another project.
- A JWT `exp` that is not a number, and a huge `expires_in`, escaped as unexpected exceptions.
- `sform account ca` was blocked for a machine without a project.
- The resolver's configuration errors mapped to exit `3`.
- An expired token file passed `sform account`.
- The documentation errors listed above.

Refuted:
- A redirect leaking the public certificate header.
- Untrusted discovery redirects.
- User-mode `sform account` ignoring `STORYTELLER_PROJECT`: that is intended, since it reports the stored default.
- A few message wordings.

Not fixed, and recorded as follow-ups:
- A configuration read (`config -e`) still prints the document and may prompt before overwriting a file. A quiet or non-interactive export mode is a separate change.
- The server ignores `X-ARR-ClientCert`, including the one App Service injects, unless `MachineAuth:IsCertificateHeaderFromClientAllowed=true` (spec, Current State). Certificate kinds in a deployed API depend on that setting.
