# Phase 2 of Aligning the OpenAPI Document and sform with the Provider Contract

Linear: [424-96](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security).

## Overview

Phase 2 covers sections 1 and 6 of [Align the OpenAPI Document and sform with the Provider Contract](../2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md). The platform is at `0.10-alpha`, the document of phase 1 is exported as `open.api.v0.10.0.json`, and both SDKs are generated from it. The generated C# configuration reads take the `If-None-Match` tag; the TypeScript client keeps its signatures. The hand-written conditional twins stay.

| Commit | Content |
|---|---|
| `3507a49c` | `chore(platform): bump version to 0.10-alpha` (`src/Platform/version.json` only, as `15666baa9` for 0.9) |
| `e26d964f` | The snapshot, both SDKs regenerated, the callers, the generation docs |

| Check | Result (end of the phase) |
|---|---|
| `Api.Functions/test` | 2265 passed |
| `Sdk.NSwag/test` | 199 passed |
| `Configuration/test` | 542 passed, 2 skipped |
| `Cli/test` | 793 passed |
| `sdk.typescript`: `build`, `lint`, `typecheck`, `test` | green; 140 passed, 1 skipped |
| `ui.admin` against the rebuilt SDK: `lint`, `test`, `build` | green; 1446 passed |

## What Was Done

### 1. Version 0.10

- The first export after the bump is `0.10.0.13575`, so the snapshot is `open.api.v0.10.0.json`. Later phases do not touch `Api.Functions/src`, so the API stays at `0.10.0`.
- No commit message contains the words that `mrepo` reads as a breaking change.

### 6. Snapshot and regeneration

- **Export.** No `Auth__*` variables were set, as for 0.9.50. Against `open.api.v0.9.50.json` the export differs only where phase 1 changed it: `security` of 85 operations, `parameters` and `responses` of the three reads. The `integrated` scheme and every schema are identical.
- **Snapshots.** `Sdk.NSwag/open.api.v0.10.0.json` joins its history. `sdk.typescript/` now keeps only `open.api.v0.10.0.json`: the nine superseded snapshots were removed, as its README says (all of them are in `Sdk.NSwag/`).
- **C#.** NSwag 14.6.3 with the README command. The diff of `ApiSdk.g.cs` is the three reads only: `string if_None_Match` after `key` in both overloads of the interface and the class, and the header sent when it is not null. The `304` branch still throws. The members that `ApiSdk.Conditional.cs` uses and the messages it copies did not change.
- **Callers.** `sform` passes `if_None_Match: null` in `ConfigGetCommand` (2), `ConfigSetCommand` and `ConfigCompositionCommand`. The tests pass `null`: `ConfigCompositionCommandTests` (four `Setup`s and one `Verify` of a strict mock), `CompositionLinesTests`, `ConfigurationsApiClientDateTests` (2) and `Sdk.NSwag/test/ConditionalReadsTests` (4). The provider uses only the twins and did not change.
- **TypeScript.** `npm run generate`: the version header of every generated file, `package.json` `0.10.0`, and three Zod schemas (`GetConfigurationHeader`, `GetResolvedConfigurationHeader`, `GetConfigurationCompositionHeader`). The client, vue-query, model and MSW outputs did not change otherwise.
- **ui.admin** was checked against the rebuilt SDK and did not change. Its `package-lock.json`, which `npm install` rewrote with another npm version, was restored.
- **Docs.**
  - `Sdk.NSwag/README.md`: the tool pinned to 14.6.3, the input `open.api.v0.10.0.json`, and why the twins are hand-written (a `304` is an exception in every generated read, and `/WrapResponses` would not change that).
  - `sdk.typescript/README.md`: the `SPEC=` example, and the conditional-reads section (the document declares the parameter, the generated functions take none).
  - `docs/Platform/Storyteller/export-import.md:587`: no longer names `open.api.v0.9.48.json` as the current document.
  - `docs/Platform/Storyteller/inheritance.md:376`: the generated C# reads take the tag, the TypeScript ones do not, both throw on a `304`.
