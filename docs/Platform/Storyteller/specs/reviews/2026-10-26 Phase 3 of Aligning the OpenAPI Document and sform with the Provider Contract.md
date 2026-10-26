# Phase 3 of Aligning the OpenAPI Document and sform with the Provider Contract

Linear: [424-96](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security).

## Overview

Phase 3 covers sections 7-9 of [Align the OpenAPI Document and sform with the Provider Contract](../2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md). The C# SDK now owns the `STORYTELLER_*` credential contract in public types: the names (canonical, deprecated, GitHub Actions), the reading rules, the URL rule, the kinds, and `StorytellerCredentialVariables`, the one reading of the credential variables that `FromEnvironment` and, in phase 4, `sform` share. A federated machine can take its platform token from the GitHub Actions runner. The provider uses the public types instead of its copies. `sform` did not change in this phase.

| Commit | Content |
|---|---|
| `018130c8` | Sections 7-9 and the phase 3 docs |

| Project | Passed (end of the phase) |
|---|---|
| `Sdk.NSwag/test` | 276 (199 before: variables, the shared reading with every rule row, the GitHub source) |
| `Configuration/test` | 548, 2 skipped (542 before: the GitHub cases of `SettingsReaderTests`, both federated fingerprints) |
| `Configuration.KeyVault/test` | 87 |
| `Cli/test` | 793 (unchanged; `Cli/src` builds without a change) |

## What Was Done

### 7. One canonical set

- `StorytellerVariables` is public: the canonical names, the deprecated `API_URL`, `CERTIFICATE_FILE`, `CERTIFICATE_PASSWORD` and `SUBJECT_TOKEN_FILE`, the GitHub names, `Credentials` (the 12 credential names in read order, without `ENDPOINT`, `API_URL`, `KEY` and the GitHub names) and `Deprecated` (deprecated → canonical).
- `STORYTELLER_SUBJECT_TOKEN_FILE` keeps its meaning: it always makes the credential federated and needs `STORYTELLER_CLIENT_ID`; its conflict with `STORYTELLER_TOKEN_FILE` is checked in the federation branch only; with a token file and no client id it is still a mixed-kinds error.
- The GitHub variables never count as credential variables: alone, `Read` and `FromEnvironment` return null.

### 8. The shared contract

- `EnvironmentVariables` → public `StorytellerVariableReader` (its constructor takes a nullable `getVariable`, the process environment by default). `LocalUrlRule` → public `StorytellerUrlRule`. **Beyond the spec:** the `Uri` overloads of `Require` and `RequireEndpoint`, and `Describe`, are public too, because the provider's `DocumentIdentity` needed `Describe` to drop its copy without a change of behaviour.
- New public enums `StorytellerCredentialKind` and `StorytellerSubjectTokenSource`.
- `StorytellerCredentialVariables`:
  - `Read(getVariable)` applies the rules table of the spec and keeps the `getVariable` it was given for the unbound fallbacks of the credential;
  - public `Kind`, `SubjectTokenSource`, `Names`, `DeprecatedNames`, `ApiKey` (without the scheme), `ClientId`, `TokenFile`; the secrets, the certificate file and password, the token URL and scope, the subject token file and type, and the GitHub URL and token are internal. **Changed after the branch review** (`74fc03ac`, see [Phase 4b](2026-10-26%20Phase%204b%20of%20Aligning%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md#after-the-branch-review)): `ApiKey` held the whole key, secret included, as the spec's own property list had designed it against its "no secret is readable" rule; it is now `ApiKeyMachine`, the machine segment of a structured key;
  - `ToString()` prints the kind and the names;
  - `CreateCredential` hands over to `EnvironmentCredential.Create`, which now only builds credentials. An internal `ApiKeyAsSet` keeps the key as it was set, so the credential normalises it exactly as before.
- `FromEnvironment` keeps its signature: `Read(getVariable)?.CreateCredential(fileSystem, timeProvider)`.
- **Beyond the spec:** `Read` also checks the client-credentials rule "a token URL, or a `client_` id that discovers one" (`ClientCredentialsCredential.RequireTokenUrl`), so `sform` sees that error when it reads the variables. `FromEnvironment` fails exactly as before.
- Two messages that no test pinned now name the GitHub way too: `STORYTELLER_CLIENT_ID` alone, and a federated machine without a platform token.
- Fingerprints: the type names and `ToString()` of the existing kinds did not change; `CredentialFingerprintTests` pins the file-sourced federated fingerprint, checked against the code before the change, and the GitHub-sourced one.
- **Provider.** `VariableReader.cs` and `LocalUrl.cs` are gone; `SettingsReader`, `DocumentIdentity`, `TransportFactory` and `StorytellerDocuments` use the public types and constants; `ConfigurationVariables` keeps only the provider's own names. Its errors keep their messages and types. Through `FromEnvironment` it gains the GitHub source.

### 9. GitHub Actions

- `TokenEndpointClient.RequestGitHubIdTokenAsync`: `GET {ACTIONS_ID_TOKEN_REQUEST_URL}&audience=…` (`?` without a query), `Authorization: Bearer` on that request only, the same client without redirects and the same resilience, `value` from the answer.
- `FederatedTokenCredential` takes its platform token from a file source or a GitHub source and asks again before every exchange. A new internal factory builds the GitHub one; the public options and factories did not change. **Deviation:** the credential keeps a private exchange record instead of `FederatedTokenOptions`, whose `SubjectTokenPath` stays `required`.
- **Audience:** `endpoint.OriginalString.TrimEnd('/')` + `/v1/{organization}/{project}/access/token`, escaped. **Deviation:** every trailing `/` is removed, not one, because that is what `sform` builds today; a test pins `/api//`. The exchange still posts to the normalised URI.
- **Failures:** network errors, timeouts, 408, 429 and 5xx are `Unavailable`; 3xx, other 4xx, an answer without a token or not JSON are `Rejected`. Messages name the runner URL without its query.
- **The hint** ends every `Rejected` exchange of a GitHub-sourced credential (a 4xx, a redirect, a 2xx without a bearer token), with the original exception inside. `sform` added it on `400` and `401` only.
- `ToString()`: `StorytellerCredential { Kind = Federated, ClientId = {id}, SubjectTokenSource = GitHubActions }`.

### Docs

- `Sdk.NSwag/README.md`: a variables section (canonical and deprecated names, kinds, rules), the GitHub Actions source (request, audience, failures, hint), the shared contract types, and the token requests: only the GitHub one carries an `Authorization` header.
- `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`: the GitHub source and the contract types.
- `docs/Platform/Storyteller/configuration-provider.md`: the GitHub row, the token file rows, `STORYTELLER_CLIENT_ID` alone, and which runner failures are transient for the provider. The "one set of variables" sentence and the column header are phase 4.
- Code comments that called the `sform` names fallbacks say "deprecated".
