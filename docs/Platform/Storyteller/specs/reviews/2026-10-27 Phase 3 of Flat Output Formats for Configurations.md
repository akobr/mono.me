# Phase 3 of Flat Output Formats for Configurations

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

Phase 3 covers section 10 of [Flat Output Formats for Configurations](../2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md) and the phase 3 docs of section 11. The OpenAPI snapshot was exported from the API of phase 2, both SDKs were regenerated, and both gained formatted conditional reads that return the text of a format with its tag. The TypeScript transformer keeps the generated envelope reads unchanged, so ui.admin needed no change. The final review of the branch raised the depth limit of the C# configurations client to match what the API writes.

| Commit | Content |
|---|---|
| `d85f072f` | Snapshot `open.api.v0.10.2.json`, both SDKs regenerated, the formatted twins and helpers, the readmes (section 10) |
| `3a7093eb` | Final review fix: the C# configurations client reads 256 levels |

| Project | Passed (end of the branch) |
|---|---|
| `Sdk.NSwag/test` | 328 |
| `sdk.typescript` (`npm test`) | 145, 1 skipped; `npm run lint` and `npm run typecheck` clean |
| `ui.admin` (`npm test`) | 1447 in 98 files |

## What Was Done

### 10. SDKs

- **Snapshot.** `open.api.v0.10.2.json` (`info.version` from the project-scoped height of `0.10-alpha`) went into `Sdk.NSwag/` next to the history and replaced `open.api.v0.10.0.json` in `sdk.typescript/`; `package.json` of the TypeScript SDK carries `0.10.2`. The version stays `0.10-alpha`.
- **C# generated code.** NSwag 14.6.3 changed only the two configuration reads: `(…, key, string format, string if_None_Match[, CancellationToken])`, and the effective read types its 422. The callers pass `format: null`: `ConfigGetCommand` (until phase 4 moved it to the twins), `ConfigSetCommand`, `ConfigurationsApiClientDateTests` and `ConditionalReadsTests`.
- **C# twins.** `SendConditionalAsync` became generic over the result and takes the format, the `Accept` value and a reader of the 200. `GetConfigurationFormattedConditionalAsync` and `GetResolvedConfigurationFormattedConditionalAsync` return `ConditionalResponse<string>`, require `format` (`ArgumentNullException`), build `?format=` as the generated reads do, send the media type of the format as `Accept` from a private case-insensitive map (`*/*` for any other value), read the 200 with `ReadAsStringAsync`, and map errors as the envelope twins do. The 422 of both reads is typed, each with the message of its regenerated description. `ConditionalReadsTests` mirrors the envelope tests for the formatted reads (the URL equal to the generated one, `Accept`, text and empty bodies, a 422 `format.case-collision` with its details, every failure) and its effective 422 row is now typed.
- **TypeScript.** `scripts/openapi-transformer.mjs` removes the `format` query parameter and the media types other than `application/json` of the 200 of `GetConfiguration` and `GetResolvedConfiguration` before Orval runs, so the generated functions, composables, Zod schemas and mocks keep their envelope signatures; across the 285 generated files only the version strings changed, and ui.admin was not touched. `lib/conditional.ts` adds `ConfigurationFormat` and `getConfigurationFormattedConditional` / `getResolvedConfigurationFormattedConditional`, which build `?format=` on the generated URL and read a 200 as text; `readConditional` checks 304 first and sends every non-ok response through `StorytellerError.fromResponse`, so a 422 of a format keeps its `errorCode`. `test/conditional.test.ts` covers the URL, text and empty bodies, and the 422.
- **Final review fix (`3a7093eb`): depth.** The configurations client read its responses with Newtonsoft's default limit of 64 levels, which failed on the envelope of content that the API serves with `?format=` and, after the fix of phase 2, also writes in the envelope. `ConfigurationsApiClient.UpdateJsonSerializerSettings` now sets `MaxDepth` to 256 next to `DateParseHandling.None`, which covers the 128 levels the API writes a few levels down in a response (the composition read holds content in a list of nodes). The generated reads, the twins, the provider and `sform` all read through these settings. `ConditionalReadsTests.DeepContent_OfTheLevelsTheApiWrites_IsRead` reads 128-level content on both reads.

### 11. Documentation (phase 3)

`Sdk.NSwag/README.md` (the snapshot, `format` then `if_None_Match` after `key`, null for both, the composition read unchanged, the 422 of both reads, the formatted twins and that the generated methods cannot read a formatted body), `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`, `sdk.typescript/README.md` (the snapshot, the new correction of the transformer, the formatted helpers, the 422 of a format), and the SDK helpers in `configuration-formats.md`. The "Depth" note of `configuration-formats.md` names the limit of the SDK since the final review fix.
