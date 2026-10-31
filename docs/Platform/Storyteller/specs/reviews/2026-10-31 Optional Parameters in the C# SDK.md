# Optional Parameters in the C# SDK

Linear: [424-118](https://linear.app/424fun/issue/424-118/c-sdk-generate-optional-parameters-with-nswag).

## Overview

This review covers the whole of [Optional Parameters in the C# SDK](../2026-10-31%20Optional%20Parameters%20in%20the%20C%23%20SDK.md), delivered in one go on `fix/424-118`, based on `origin/main` at `596b07c8`. The C# SDK is generated with `/GenerateOptionalParameters:true`: one method per operation, optional parameters last with defaults. The two operations whose required body moved are fixed in `sform`, the typed Moq callbacks take the token, and the placeholder `null`s are gone from `sform`, the MCP server, the SDK tests and the docs. Nothing outside the repository consumes the SDK, so the reorder of `ImportKeyValuesAsync` and `StartViewImportAsync` breaks no one.

| Commit | Content |
|---|---|
| `c5df6479` | The spec |
| `05e88535` | Sections 1-4: the regeneration, the README command, the twins' default, the reordered imports in `sform` and the typed callbacks of its tests |
| `30dfe703` | Sections 5-6 for `sform` and its tests |
| `1c11a8c6` | Sections 5-6 for the MCP tools and the SDK tests, with the new twin test |
| `baa01750` | Section 7: the docs |
| This commit | The review |

## What Was Done

### 1. Generation

- `Sdk.NSwag/README.md` ("Generate"): the command gains `/GenerateOptionalParameters:true`, and a new paragraph says what it does: one method per operation, optional query, header and body parameters last with `= null`, the token with `= default`, a required body ahead of them, and later arguments passed by name.
- `src/ApiSdk.g.cs` regenerated from `open.api.v0.10.11.json` with NSwag 14.6.3 (NJsonSchema 11.5.2, header unchanged). The same command without the option first reproduced the committed file byte for byte, so the diff (413 lines in, 3474 out) holds only the option's changes: 276 interface declarations become 138, the 138 forwarding overloads without a token are gone, 65 operations gain `= null` defaults (37 for optional query or header parameters only, 28 for an optional body as well), and 2 change order. Request building, query serialization, content types and response handling are unchanged; the configuration reads still throw on `304`.
- No `open.api.v*.json` and nothing in `sdk.typescript` changed.

### 2. Hand-written SDK code

- `ApiSdk.Conditional.cs`: `ifNoneMatch` of all five twins (the plan's current state counted five, the issue two) is `string? ifNoneMatch = null`, in the interface and in the class. `format` of the formatted twins stays required.
- `ApiSdk.partial.cs` and `ServicesCollectionExtensions.cs` compile unchanged.
- `ConditionalReadsTests.cs`: the generated reads are called without placeholders; the tests of a tag, of `null` sent explicitly and of `""` keep their argument; the cancellation test passes `cancellationToken: cancellation.Token`. New `ATwinCalledWithoutATagOrAToken_SendsNoIfNoneMatchAndReturnsThe200`, a theory over the five twins: each is called with neither `ifNoneMatch` nor a token, and the test checks the `200` result, the `ETag`, the route and query, and that no `If-None-Match` was sent. `StorytellerCredentialHandlerTests` and `StorytellerResilienceTests` drop the twin placeholder too (they are about the handler and the pipeline, not the header).

### 3. `sform`: the reordered operations

- `KeyValueImportCommand.cs`: the plan reads `ImportKeyValuesAsync(…, document, mode, dryRun: true, keyPrefix: Prefix)`, the apply `ImportKeyValuesAsync(…, document, mode, keyPrefix: Prefix)`, without the old `null` for `dryRun`.
- `StorytellerImportCommand.cs`: `archive` follows `view`, then `include`, `existing`, `IsForced ? true : null`, `IsDryRun ? true : null` and `fileName`.
- `KeyValueImportExportCommandTests.cs` and `StorytellerTransferCommandTests.cs`: the setups, the verification, the callbacks and the `ReturnsAsync` delegate take the new order. Each was checked by parameter name, since `mode`, `keyPrefix` and `fileName` are all `string`; the import tests assert the forwarded values, so `sform kv import` with and without `--dry-run` and `sform storyteller import` still send `mode`, `dryRun`, `keyPrefix`, `include`, `existing`, `force`, `fileName` and the body as before.

### 4. `sform` tests: typed callbacks

- 32 delegates in 12 test files (22 `Callback`s and 10 `ReturnsAsync`) gain a last `CancellationToken`: as a type argument of a generic `Callback<…>`, and as a lambda parameter (`_`, or `CancellationToken _` in a typed lambda). They were found by a script that compared each delegate's parameter count with the regenerated signature of its setup; 11 of the files gained `using System.Threading;` in StyleCop order.
- Before the fix, 183 of the 1062 tests failed with `Invalid callback`; after it, all pass.
- As planned, the setups and verifications that leave the token out were not touched. Under C# 14 they compile, and they match `CancellationToken.None`, which is what the old overload without a token passed. Neither the CodeRabbit plan's `It.IsAny<CancellationToken>()` in 19 files nor the issue's CS0854 applies.

### 5. Placeholder `null`s in production code

- `sform`, 19 calls in 11 files: the twins' `ifNoneMatch: null` (`ConfigGetCommand`), `if_None_Match`/`format` (`ConfigCompositionCommand`, `ConfigSetCommand`), the diffs' `format` (`ConfigDiffCommand` ×4, `TemplateDiffCommand` ×3, `KeyValueVersionsCommand` ×2), `GetKeyValueAsync`'s `reveal`/`includeDeleted` (`KeyValueReencryptCommand:53`, `KeyValueSetCommand:117`), and `includeDeleted: null` (`StorytellerGetCommand`, `StorytellerDeleteCommand`, `AnnotationsApiExtensions`). None of these passed a token. `StorytellerApiExtensions` had no placeholder.
- MCP, 25 calls in 10 files under `Tools/`: every `…, null[, null], cancellationToken)` became `…, cancellationToken: cancellationToken)`. This covers the 8 calls the issue named, plus the diffs, `GetViewsAsync`'s `discover`, `GetAnnotationAsync`'s `includeDeleted` and `GetDescendantsAsync`'s three trailing options.
- Kept: `null`s before a passed value (`KeyValueReencryptCommand:96`, `StorytellerDeleteCommand`'s positional `continuationToken`, `StorytellerDeletedCommand`, `StorytellerListCommand`, `AnnotationTools.cs:143-173` and `:417`), conditional values, body-building `null`s, non-SDK calls and `AddStorytellerSdk(null, …)`. No call was switched to named arguments except to pass the token.
- `Configuration` and `Hosting` are unchanged.

### 6. Placeholder `null`s in tests that call the clients

- `sform` tests: `ConfigurationsApiClientDateTests` (2), `CompositionLinesTests`, `FederatedSignInWiringTests`. SDK tests: see section 2. Moq expressions keep every argument they listed.
- `Configuration/test/Transport/TransportFactoryTests.cs` still passes `null` to the twins. It was not in the plan's list, and those tests are about the transport, so it was left alone.

### 7. Docs

- `Sdk.NSwag/README.md`: the "Generate" paragraph (section 1); `ifNoneMatch` is optional, `null` (the default) or `""` sends none; the generated reads take `format` and `if_None_Match` as optional parameters, so leave them out for the envelope without a tag; the encryption example drops `includeDeleted: null`.
- `export-import.md`: the new `StartViewImportAsync` order with its defaults. The note on the export body says the body is optional in the SDK, but a call without it still sends the JSON `null`, which the API answers with `400`.
- `inheritance.md` and `configuration-formats.md`: the generated C# reads take `format` and the tag as optional parameters.
- **Deviation: what a left-out body sends.** The spec said that a call leaving out an optional body "sends no body". The review of the docs found that wrong: the generated method serializes the `null` (`JsonConvert.SerializeObject(body)`) and sends the JSON `null`. The API rejects that with `400` because it is not an object. A request with no body at all would pass for some operations: `TryParseExport` treats an empty body as the default export. The README and `export-import.md` say what is actually sent.
- A search of the other `docs/` pages (the `specs/` folders aside) and the `src/Platform` readmes found no other C# example with placeholders or the old order.

### 8. Validation

- `dotnet build 42.mono.slnx`: 0 errors. The first, parallel build reported `CS0246 IChainedCrumbsExecutor` in `src/Crumble/Playground/Examples`, which this change does not touch. The project built with 0 errors on its own and in the next solution build.
- `dotnet test`: `Sdk.NSwag/test` 338 passed (333 + the 5 cases of the new theory), `Cli/test` 1062, `Mcp/test` 727, `Configuration/test` 639 (2 skipped, as before), `Hosting/test` 218, `Aspire.Hosting/test` 164, `Configuration.KeyVault/test` 87, `Api.Functions/test` 2536. No failures.
- `Mcp/testint` builds; it needs a running platform, so it was not run.
- No new warnings in the changed files; the three left in them (`KeyValueVersionsCommand` SA1402, `StorytellerGetCommand` CS8618 and SA1518) were there before.

### 9. Commits

As planned, except that the docs fix found in review went into the docs commit. Every commit builds and passes the tests above.

### 10. Follow-up

Not done here, as planned: mark the request bodies the API requires `required` in the OpenAPI document. That removes their `= null` and lets the compiler catch a forgotten body. It also changes the TypeScript types, so it needs its own issue.

### The CodeRabbit plan

It served as a second opinion. Where this change differs:

- MCP exists on `main` (PR #131 is merged), so its placeholders are in scope, without a conditional step.
- The generation input is `open.api.v0.10.11.json`, not `v0.10.0`.
- No `It.IsAny<CancellationToken>()` was added to setups, because C# 14 compiles them as they are (section 4). Only the typed delegates changed.
- All five twins got the default, not two.
- `KeyValueReencryptCommand:96` keeps its `null`s, which the plan left as a choice.
