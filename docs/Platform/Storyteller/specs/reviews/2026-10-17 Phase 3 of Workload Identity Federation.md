# Phase 3 of Workload Identity Federation

Linear: [424-46 Add workload identity federation](https://linear.app/424fun/issue/424-46/add-workload-identity-federation)

## Overview

Phase 3 covers sform (section 14) of [Workload Identity Federation for Machine Access](../2026-10-17%20Workload%20Identity%20Federation%20for%20Machine%20Access.md). `sform account machine` now creates `Federated` machines with their rules, replaces rules, and manages the project's trusted issuers, on top of the NSwag SDK regenerated in [Phase 2](2026-10-17%20Phase%202%20of%20Workload%20Identity%20Federation.md). A reviewer and a skeptic went over the result; three of four findings were confirmed and fixed.

| Suite | Result |
|---|---|
| `dotnet build src/Platform/Cli/src` | 0 errors; no warnings in touched or new files |
| `dotnet test src/Platform/Cli/test` | 270 passed (190 before) |

## What Was Done

### 14. sform

- **Kind.** `CredentialKinds.Names` lists `Federated`; the existing parser already accepts any case.
- **`machine create`.** New options `--issuer`, `--audience`, `--subject`, `--claim key=value` (repeatable; a repeated key adds an accepted value; the key may be a JSON pointer) and `--rules-file` (a JSON array in the API shape). Before any request it refuses rule options with another kind, `-k federated` without rules, a file together with rule options, an incomplete rule (naming the missing options) and a malformed claim or rules file. The output prints the machine id, `TokenEndpoint`, the rules and a `curl` exchange, never the "copy the access key" message.
- **`machine rules <id>`** replaces the rules through the rules `PUT` (`--rules-file` or the single-rule options) and prints the result; 404, 400 and 409 print the server's message.
- **`machine issuer`** (alias `issuers`): `list` shows name, issuer, keys, max lifetime and required claims; `set <name> --issuer …` sends the issuer `PUT` (Discovery by default, `--jwks-uri`, `--jwks-file` whose content is the `Jwks` string, `--max-lifetime`, repeatable `--require-claim`, `--description`); `delete <name>`.
- **`machine reset`** of a `Federated` machine prints the server's 400; **`machine list`** shows `–` instead of a secret for `Federated` machines and any machine with an empty `AccessKey` (certificate machines showed an empty cell).
- **Deviations and additions:**
  - Rule options without `-k` imply `-k federated`, as `-s` already implies `CertificateAndApiKey`.
  - With a `Federated` machine, `--lifetime-days` and `--output` are ignored with a note.
  - `machine create` prints `ApiException`s of every kind through `Console.WriteApiError` instead of crashing ("Total mayhem"), because the rule-numbered 400s are the main feedback for federated creation.
  - `machine rules <id>` without rule options shows the current rules and token endpoint, like `machine-auth` without options shows the policy.
  - `machine issuer` without a subcommand lists; aliases `set|create`, `delete|remove`. `set` requires `--issuer` even for an update, because the `PUT` replaces the whole issuer (settings not given are cleared; the command description says so).
  - **Shared error helper:** `KeyValueConsoleExtensions.ReadError` (behind `Console.WriteApiError`) reads `ApiException<ErrorResponse>.Result` first. The NSwag client keeps no response text for statuses an operation declares, so messages such as 400 `InvalidFederation` and 409 `FederatedIssuerInUse` were lost; the key/value commands benefit too.
  - The new examples in `authentication.md` use `sform account machine …`, the path the CLI accepts; the older examples there say `sform machine …`, which the CLI does not recognise (pre-existing, left unchanged so as not to widen the conflict with #104).
- New files: `ClaimOptions`, `FederationRuleOptions`, `FederationCommandBase`, `MachineRulesCommand`, `MachineIssuerCommand` with `List`/`Set`/`Delete`, `Output/FederationConsoleExtensions`. `MachineCreateCommand.cs` only gains the options, one validation block, a try/catch around the create call, an `else if` output branch and a helper, to keep the later merge with #104 small.

### 16. Documentation (Phase 3 part)

- `authentication.md`: an sform block in the federation section (create, rules, issuers, the Azure audience values, what the rules file accepts), and the "Not available yet" bullet (only the C# exchange helper and signing in as a federated machine remain).

## Review fixes

| Finding | Fix |
|---|---|
| `--rules-file` silently dropped misspelled properties (`Claim`, `RequiredClaims`) and turned non-string claim values (`[74]`, `[true]`) and date-like strings into something else | The file is read without date parsing, converted with a serializer that refuses unknown members, and checked for non-string `Issuer`/`Audience`/`Subject` and claim values, with rule-numbered messages |
| The `--audience` help gave the wrong value for Azure v2 app registrations | One shared sentence: `<appId>` with `requestedAccessTokenVersion` 2, `api://<appId>` with 1 |
| `RulesFile_WithAnotherKind` passed without the kind check | The test uses a valid rule and asserts the exact message |

Rejected by the skeptic: "`issuer set` clears unspecified settings" — intended replace semantics, stated in the command description.

Each fix was checked by undoing it: 18 tests failed, among them every new one.

## Verification

```
dotnet build src/Platform/Cli/src
dotnet test src/Platform/Cli/test
dotnet 42.Platform.Cli.dll account machine [issuer|issuer set|issuer delete|rules|create] --help
```

- Request bodies and error handling are covered with a mocked `IAccessApiClient` and hand-built `ApiException`s; nothing ran against a live API.
- Merge with #104 (`feat/424-45`): both branches touch `MachineCreateCommand.cs` (the same `using`, neighbouring output branches, and 424-45 adds `IOptions<GeneralOptions>` to the constructor, which `MachineCreateFederatedTests` will then need). 424-45's `WritePipelineVariables` has no `Federated` branch and `DescribeKind` falls back to "a token file"; whichever branch merges second adds them.

## Left for later

- Signing in to sform as a federated machine (after 424-45).
- `docs/42for.net/cli/sform.md` is a stub on main and is rewritten by #104; the federation commands belong there after that merge.
