# Shared Conformance Vectors for Configuration Providers and SDKs

Linear: [424-105 Shared conformance vectors for configuration providers and SDKs (C22)](https://linear.app/424fun/issue/424-105/shared-conformance-vectors-for-configuration-providers-and-sdks-c22)

## Problem

Every Storyteller integration should behave the same way: a Spring Boot service, a Next.js app, a Kubernetes operator and a .NET worker read the same execution key, see the same document and fail in the same way (study `study-usage.md`, section 3). Study section 3.12 and capability **C22** (section 18, Phase B "Native in .NET", 1.0) make that testable. They ask for a folder of language-neutral JSON vectors that every provider runs in its own CI, the way OpenFeature SDKs share a Gherkin suite:

- documents and their expected flattened keys per target (.NET `:`, Spring `.` and `[i]`, env `__` upper-case), with the five rules of section 3.3: numbers and booleans as their JSON text, no null, empty containers make no key, a delimiter in a name is escaped or rejected, and case collisions are an error;
- refresh scenarios: change, error, timeout and withheld secrets;
- error mappings: `401`, `403 MtlsRequired`, `404 AnnotationInactive`, `422 binding.*` and `503`;
- a JSON Schema for the vector files, a .NET runner (the reference) and a TypeScript runner;
- the SDK release-pipeline part of C22: the suite as a gate before SDK packages are published.

Today the vectors are test fixtures of one .NET project, cover flattening and formatting only, have no schema and no version, and run nowhere in CI. No workflow runs a .NET test, and nothing publishes the SDK packages.

### Acceptance criteria

| Criterion of the issue | Where |
| --- | --- |
| Vectors live in one shared, versioned location with a schema; the existing .NET vectors are migrated unchanged and still green | Sections 1, 2 |
| Vectors for every target of section 3.3, covering all five rules | Section 3 |
| Refresh and error-mapping vectors run against the .NET provider (424-85) with a fake HTTP handler | Sections 4, 5 |
| A TypeScript runner for the flattening and error vectors (`.env` and flat JSON helpers) | Section 6 |
| The suite runs in CI, with the .NET test job the repository does not have yet | Section 7 |
| The suite is a gate before SDK packages are published | Section 8 |

The inputs to this spec are:

- the issue, its relations (424-85 merged; 424-104, C5, in review as PR #136, which this branch stacks on) and its comments;
- the studies `study-usage.md` (sections 3, 3.3, 3.5, 3.6, 3.7, 3.12, 5, 18, 19) and `study-evaluation.md` attached to the issue;
- a read-only map of the code at `feat/424-104` `e5c7e328`, made by three parallel readers: the provider's test harness, the TypeScript SDK, and CI and release conventions;
- scratch runs of Newtonsoft.Json 13 against edge-case numbers (section 3);
- the CodeRabbit coding plan attached to the issue (`2026-10-26 Shared conformance vectors for configuration providers and SDKs.md`). It is used as a second opinion and a source of ideas, not as the decision. Section 11 lists where this spec differs from it.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/`, `src/` or `.github/`.

### The base branch

424-104 (C5, `feat/424-104`) is not merged. It already did a large part of what the issue's "Reuse" section expects:

- It moved the flattener out of the provider into the packable library `ConfigurationFormats` (`42.Platform.Storyteller.ConfigurationFormats`). `DocumentFlattener.Read` returns the leaves with their segments, value, kind and path. `Flatten` returns the `:` keys. `ConfigurationFormatter` renders `flat`, `properties`, `env` and `yaml`. `DocumentRules` names seven rules: `not-an-object`, `empty-name`, `colon-in-name` and `case-collision` (the base rules), plus `bracket-in-name` and `array-gap` (properties) and `invalid-env-name` (env).
- It added three vector files next to `flattening.json` in `Configuration/test/Vectors/`. `flattening.properties.json` holds every document of `flattening.json` and more, as properties keys. `flattening.env.json` does the same as env names. `formats.json` holds the exact text of every format.
- It moved `FlatteningVectorTests` and added `FormatVectorTests`, both in `ConfigurationFormats/test`. That project embeds `..\..\Configuration\test\Vectors\*.json` as `Vectors.<file>`, and `VectorFiles.cs` loads them. The provider's own test project no longer embeds any vector file.
- Its spec says: "424-105 (C22) may move the vectors or change their shape; the new files follow the existing shape to make that a move."

This spec therefore builds on `feat/424-104`, and the PR targets that branch, as 424-104 targets 424-96.

### The vectors

| File | Vectors | `keys` | `error` | Other |
| --- | --- | --- | --- | --- |
| `flattening.json` | 20 | 11 | 9 | |
| `flattening.properties.json` | 32 | 14 | 18 | |
| `flattening.env.json` | 33 | 13 | 20 | |
| `formats.json` | 59 | | 6 | 53 with `format` and `text` |

- **18 versus 21.** On `main`, `flattening.json` has 18 vectors. The issue's "21" counts three `"name"` properties inside documents (the 424-104 spec found the same). 424-104 added two vectors on path escaping, which makes 20.
- **Shape.** Each file has `{ description, vectors: [{ name, document, … }] }`. There is no `$schema`, no version and no target field: the target is in the file name only. Names are unique within a file, and the properties and env files reuse the names of `flattening.json` for the same documents. `FormatVectorTests.TheFile_HasEveryDocumentOfTheFlatteningVectors` pins that rule.
- **Reading.** The runners read with the SDK's settings, `DateParseHandling.None` and `FloatParseHandling.Double`.
- **Numbers.** The documents use only 5432, -7, 0, 0.5, -0.25, 1.50 (read as 1.5), 1, 2, 3, 5 and 10. `formats.json` also uses `1E+20`.
- **References.** These places name `Configuration/test/Vectors`: `ConfigurationFormats/src/DocumentRules.cs:3`, `DocumentFlattener.cs:9`, `ConfigurationFormats/test/VectorFiles.cs:6`, `FlatteningVectorTests.cs:5`, `FormatVectorTests.cs:6`, `ConfigurationFormats.UnitTests.csproj:19`, `docs/Platform/Storyteller/configuration-provider.md:204` and `docs/Platform/Storyteller/configuration-formats.md:196-203`. The descriptions of the properties, env and formats files name the other files. Earlier specs and reviews name the folder too; they stay as written.

### The provider and its test harness

- **Classification.** `Configuration/src/Reading/DocumentReader.cs` turns every read into a `ReadOutcome`:
  - `Changed` (200), `NotModified` (304), `Withheld`, `Definitive` or `Transient`.
  - `IsTransientStatus` (l.105-115) marks 408, 424, 429 and 5xx other than 501 and 505 as transient, and so is a 2xx that did not parse. Every other status is definitive.
  - The `ErrorCode` is the non-blank `ErrorCode` of the `ErrorResponse`. For 400, 403, 404, 422 and 500 the generated client parses the body; for 401, 503 and the other statuses, `TryReadError` parses a body that starts with `{`.
  - `X-2S-Secrets-Withheld` on a resolved read is checked before the 304 check, so a 200 and a 304 with the header both fail: status 200 or 304, no error code, definitive.
  - `MtlsRequired` and `AnnotationInactive` get no special handling. They come through as the `ErrorCode` of a definitive failure.
- **Failures.** `StorytellerConfigurationException` carries `Key`, `StatusCode`, `ErrorCode` and `IsTransient`. An invalid document (`Engine/ContentApplication.cs:43-52`) has no status and no code, and is definitive.
- **Startup.** A required source throws on every failure, transient or definitive (`StartupTests.TheStartupTable`). An optional source starts from the cache or empty on a transient failure only.
- **Refresh.** `IStorytellerConfigurationRefresher.TryRefreshAsync` returns whether the run succeeded. It never throws for a failed read, and it completes once `IConfiguration` shows the result. A failure keeps the previous data. A 200 with the same content under a new tag makes no reload, but the new tag is stored.
- **Harness.** `Configuration/test/Engine/EngineFixture.cs` runs the real `AddStoryteller` source over `StubHttpHandler`, with a fake clock, a mock file system, no retries (`MaxRetryAttempts = 0`) and a circuit breaker that cannot open.
  - `Environment` is a dictionary, so a test can set `STORYTELLER_KEY`.
  - `StubHttpHandler` answers from a queue, then from `Otherwise`, and records every request with its raw headers. `StubHttpHandler.Response(status, json, headers)` builds a response.
  - Both `ReadTimeout` (which bounds a run, `Engine/RunScope.cs`) and the Polly attempt timeout run on the system clock. A hang is simulated with `Task.Delay(Timeout.Infinite, ct)` and a short `ReadTimeout` (`StartupTests` l.224-268).
  - A `null` field in an `ErrorResponse` body makes the generated client drop the whole body (`Required.DisallowNull`), so vectors leave fields out instead of writing `null`.

### The TypeScript SDK

- **No configuration flattener.** `sdk.typescript/src/lib/key-values.ts` has `flattenJson`, `toFlatJson`, `parseDotEnv` and `serializeDotEnv`, but they implement the key/value contract. Keys are dotted, arrays become JSON text and null becomes `''`, and `toEnvKey` turns `.` into `__`. 424-96 and 424-104 kept the two contracts apart, and `test/key-values.test.ts` pins this behaviour.
- **Errors.** `lib/errors.ts` has `StorytellerError` (`status`, `errorCode`, `hint`, `body`, `aborted`), built by `fromResponse` from PascalCase `Message`, `Hint` and `ErrorCode`.
  - An `ErrorCode` of spaces stays a code, where .NET drops it.
  - There is no transient classification.
  - A network failure is `status: 0`, and an abort is `aborted: true` (`lib/fetcher.ts:102-110`).
- **Conditional reads.** `lib/conditional.ts` has `getConfigurationConditional` and `getResolvedConfigurationConditional`. A 304 returns before error mapping, a withheld header is exposed as `secretsWithheld`, and other failures throw `StorytellerError.fromResponse`. `readBody` parses JSON only for a JSON content type.
- **Tooling.** Vitest includes `test/**/*.test.ts` (node environment), `node:fs` with `import.meta.url` is already used (`test/msw.test.ts`), `@types/node` is present, and `noUncheckedIndexedAccess` is on. tsup bundles with `platform: 'neutral'`, so library code cannot import `node:*`. `files: ["dist"]`, and `prepublishOnly` is `npm run build`.

### CI and releases

- **Workflows.** `.github/workflows` holds `ui-admin.yml` (lint, typecheck and test of the SDK and the admin UI, on its own paths only), `storyteller-action-release.yml` (from 424-104, `workflow_dispatch`, main only), and the docs site workflow.
  - No workflow runs `dotnet test` or uses `actions/setup-dotnet`.
  - Actions are pinned by major tag, `checkout` uses `persist-credentials: false`, and Node comes from an `.nvmrc` (24), with `npm@11`.
- **Toolchain.** `global.json` pins SDK 10.0.201 with `rollForward: latestMajor`. NBGV (`42.Monorepo.GitVersioning`) needs the full history: a shallow clone fails the build.
- **Packages.** Nothing publishes the SDKs:
  - No workflow has `nuget push` or `npm publish`, and no secret exists for either.
  - nuget.org has none of the Storyteller packages.
  - npm has `@42for.net/2splatform.sdk` 0.8.2; the repository is at 0.10.2, the version of the OpenAPI document.
  - The provider's package closure is `Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Sdk.NSwag`, `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault`.
- **Linux.** The provider tests have never run on Linux. The `[UnixFact]` tests and the non-Windows branches of `LastKnownGoodCache` will run for the first time in CI.

## Proposed Changes

### 1. One shared, versioned suite

New folder `src/Platform/Storyteller/conformance/`, next to the providers and SDKs that run it. It is lower-case like `sdk.typescript` and `scripts`, because it is not a .NET project.

```text
src/Platform/Storyteller/conformance/
  README.md                  what the suite is, the layout, how to run it; points to docs/Platform/Storyteller/conformance.md
  suite.json                 the manifest: the suite's version and every file with its kind
  schema/
    suite.schema.json
    flattening.schema.json
    formats.schema.json
    errors.schema.json
    refresh.schema.json
  flattening/
    flat.json                was Configuration/test/Vectors/flattening.json
    properties.json          was Configuration/test/Vectors/flattening.properties.json
    env.json                 was Configuration/test/Vectors/flattening.env.json
  formats.json               was Configuration/test/Vectors/formats.json
  errors.json                new, section 4
  refresh.json               new, section 5
```

**The manifest.** It is the single list a runner reads:

```json
{
  "$schema": "./schema/suite.schema.json",
  "name": "storyteller-configuration",
  "version": "1.0.0",
  "description": "…",
  "files": [
    { "path": "flattening/flat.json", "kind": "flattening" },
    { "path": "flattening/properties.json", "kind": "flattening" },
    { "path": "flattening/env.json", "kind": "flattening" },
    { "path": "formats.json", "kind": "formats" },
    { "path": "errors.json", "kind": "errors" },
    { "path": "refresh.json", "kind": "refresh" }
  ]
}
```

- `kind` is one of `flattening`, `formats`, `errors` and `refresh`. Its schema is `schema/<kind>.schema.json`.
- Every vector file has a `$schema` with the relative path of its schema, so editors validate and complete it.
- A runner fails on a kind it does not know, and it states which kinds it does not run. A new kind therefore forces a decision in every runner instead of being skipped silently.

**The version** is the suite's SemVer, in `suite.json` only, one for all files:

- **Major:** an expected result changes, a vector is removed or renamed, a field changes meaning, or a schema rejects a file it accepted.
- **Minor:** a vector, a file, a kind or an optional field is added.
- **Patch:** a description or the README changes.

A provider states the version it passes. The suite starts at `1.0.0` with this change.

**Identity.** A vector is identified by its file and its `name`. Names are unique within a file. Flattening files are identified by their `target`, and targets are unique.

**Schemas.**

- They use JSON Schema draft-07. NJsonSchema 11.5.2 (already in `Directory.Packages.props`) validates draft-07, and so do the common validators of every language (ajv, jsonschema, everit). Draft 2020-12 support is still uneven.
- Each schema is self-contained, with no cross-file `$ref`, so a runner can validate from a string. The errors and refresh schemas each repeat the small `response` definition, and a .NET test checks that the two copies are equal.
- `additionalProperties: false` holds on every object except `document` (any JSON object, or any value for the `not-an-object` vectors), `keys` (strings), `headers` (strings) and `json` (any value).
- Rules and other closed sets are enums:

| Schema | Closed sets |
| --- | --- |
| `flattening` | `target`: `flat`, `properties`, `env`. `error.rule`: the seven rules of `DocumentRules` |
| `formats` | `format`: the four format names. `error.rule`: the same seven rules |
| `errors`, `refresh` | `read`: `effective`, `resolved` |

- A vector has exactly one outcome (`keys` or `error`; `text` or `error`), through `oneOf` with `required`.

**The .NET runners read the folder as embedded resources.**

- Both test projects embed `..\..\conformance\**\*.json` with `LogicalName="Conformance/%(RecursiveDir)%(Filename)%(Extension)"`.
- `RecursiveDir` uses the separator of the OS, so the loaders replace `\` with `/` and look files up by their manifest path.
- `ConfigurationFormats/test/VectorFiles.cs` becomes `ConformanceFiles.cs`, loads by manifest path (`flattening/flat.json`), and keeps the SDK's reader settings.
- The provider's tests get their own small loader, `Configuration/test/Conformance/ConformanceFiles.cs`. The two projects have different root namespaces, so sharing a source file would cost more than the 40 lines it saves.

**Suite tests.** They go in `Configuration/test/Conformance/SuiteTests.cs`, in the reference provider's tests, which embed every file:

- the manifest validates against `suite.schema.json`;
- every embedded file other than `suite.json` and `schema/*` is listed in the manifest, and every listed file exists;
- every listed file validates against the schema of its kind, through NJsonSchema (`PackageReference` without a version);
- names are unique per file, and the targets of the flattening files are unique and complete (`flat`, `properties` and `env`);
- the two `response` definitions are equal;
- the portability rules of section 3.

### 2. The target dimension and the migration

- **Moves.** `git mv` keeps the history:

| From `Configuration/test/Vectors/` | To `conformance/` |
| --- | --- |
| `flattening.json` | `flattening/flat.json` |
| `flattening.properties.json` | `flattening/properties.json` |
| `flattening.env.json` | `flattening/env.json` |
| `formats.json` | `formats.json` |

  The old folder is removed.
- **File-level fields only.** Each flattening file gains `"$schema"` and `"target"` (`flat`, `properties` or `env`), and `formats.json` gains `"$schema"`. The `vectors` arrays stay unchanged, byte for byte. A one-off check compares the text of each `vectors` array before and after the move, and the review records it. The descriptions change only where they name another file.
- **Target names.** The targets use the names of the API's `?format=`, `sform --format` and `ConfigurationFormat`, so one vocabulary runs from the vectors to the API:

| Target | Keys | Study 3.3 row |
| --- | --- | --- |
| `flat` | the `:` keys of `IConfiguration` | .NET `IConfiguration` |
| `properties` | `.` keys with `[i]`; a name with other characters in the bracket form `[name]` | Spring `Environment`, MicroProfile, Quarkus |
| `env` | upper-case names joined with `__` | environment variables, `.env` |

  The flattening of the study's last two rows (Go koanf and Viper, and nested objects) is the document itself and needs no vectors.
- **Runners.** `FlatteningVectorTests` and `FormatVectorTests` load by manifest path. `FlatteningVectorTests` runs every flattening file, keyed by (target, name), and fails on an unknown target:
  - `flat`: `DocumentFlattener.Flatten`, and `Format(…, Flat)` parsed back, as today;
  - `properties` and `env`: the existing key read-back of `FormatVectorTests` (the rendered text parsed by the test readers).

  The rest of `FormatVectorTests` stays, and `TheFile_HasEveryDocumentOfTheFlatteningVectors` keeps the documents of the three targets aligned. The theory data stays strings, because xUnit needs public parameter types.
- **The library stays unchanged.** `ConfigurationFormats` already is the target-aware reference (424-104). Only comments that name the old folder change.

### 3. Coverage of the five rules, and portability

**Coverage.** All three targets already cover all five rules of study section 3.3. The suite tests pin that coverage, so a later edit cannot drop a rule from a target.

| Rule of 3.3 | `flat` | `properties` | `env` |
| --- | --- | --- | --- |
| Numbers and booleans as JSON text | `numbers are their JSON text`, `booleans are lower case` | the same names | the same names |
| No null | `null makes no key`, `array items keep their index, also after a null` | `null makes no key`; an inner null is `array-gap`, a trailing one is left out | the same as `flat` |
| Empty containers make no key | `empty objects and arrays make no key` | the same | the same |
| A delimiter in a name is escaped or rejected | `colon-in-name` (rejected) | `.`, a space and `@` escaped in the bracket form; `[` and `]` rejected (`bracket-in-name`); `:` allowed | `__` and other characters rejected (`invalid-env-name`); `.` and `-` kept |
| Case collisions are an error | `case-collision`, also for an item and a property | the same | the same; env upper-cases ASCII names only, so the base rule finds every collision |

- `SuiteTests.EveryTarget_CoversTheRulesOfTheStudy` checks:
  - each flattening file has the named key vectors of the first three rows;
  - it has an `error` vector for `case-collision`;
  - it has an `error` vector for its delimiter rule (`colon-in-name`, `bracket-in-name` or `invalid-env-name`).

**Portability.** A JavaScript runner reads a vector with `JSON.parse`, which loses three things that Newtonsoft keeps. The suite only uses documents on which every runtime agrees, and `SuiteTests.EveryDocument_IsPortable` enforces that for every `document` and every `json` body:

1. **Numbers.**
   - An integer literal must be within ±(2^53 − 1).
   - A number written with a fraction or an exponent must be non-integral, or of magnitude 2^53 or more.

   The reasons:
   - JavaScript cannot tell `1` from `1.0`, which Newtonsoft writes as `"1"` and `"1.0"`.
   - It cannot hold `9007199254740993`.
   - It cannot tell the integer literal `100000000000000000000` (Newtonsoft keeps a BigInteger) from `1E+20` (a double, written `"1E+20"`).
2. **Order.** No object has a name that JavaScript orders first (a canonical array index such as `"0"` or `"12"`) after a name it does not. `JSON.parse` lists such names first, which would change "the first error in document order", the path of a collision, and the order of lines in `formats.json`.
3. **No duplicate names.** `JSON.parse` keeps the last one; Newtonsoft is told to fail.

**Numbers in other runtimes.** These scratch runs of Newtonsoft 13 (`JValue.ToString(Formatting.None)` after a read with `FloatParseHandling.Double`) set the text that every runtime must produce for a double:

- **Digits.** The shortest round-trip digits (`R`).
- **Notation.** Fixed notation while the decimal exponent position is between -3 and 17, and scientific notation (`1E+20`, `1.5E-07`, `5E-324`) otherwise.
  - `0.0001` is `0.0001`, but `0.00001` is `1E-05`.
  - `1e16` is `10000000000000000.0`, but `1e17` is `1E+17`.
- **Exponents** have an upper-case `E`, a sign and at least two digits.
- **An integral double in fixed notation** gets `.0`. Rule 1 keeps those out of the suite.

New vectors pin the notation at the edges, under the name `numbers that are not safe integers are their double text`, in all three flattening files:

```json
{
  "large": 1E+20, "huge": 1.7976931348623157E+308, "tiny": 5E-324,
  "small": 1.5E-07, "tenThousandth": 0.0001, "hundredThousandth": 0.00001,
  "fraction": 123456.789, "longFraction": 1000000000000000.5, "sum": 0.30000000000000004,
  "maxSafe": 9007199254740991, "minSafe": -9007199254740991
}
```

The expected keys come from the .NET reference: `"1E+20"`, `"1.7976931348623157E+308"`, `"5E-324"`, `"1.5E-07"`, `"0.0001"`, `"1E-05"`, `"123456.789"`, `"1000000000000000.5"`, `"0.30000000000000004"`, `"9007199254740991"` and `"-9007199254740991"`. The `.NET` runner proves the expectations before the TypeScript runner exists.

### 4. Error-mapping vectors

New file `conformance/errors.json`, schema `schema/errors.schema.json`:

```json
{
  "$schema": "./schema/errors.schema.json",
  "description": "…",
  "key": "@acme.billing.default.sbt.northwind",
  "vectors": [
    {
      "name": "403 MtlsRequired is definitive and keeps its code",
      "read": "resolved",
      "response": {
        "status": 403,
        "json": { "Message": "Secrets are delivered to machines only over mTLS.", "ErrorCode": "MtlsRequired" }
      },
      "error": { "status": 403, "errorCode": "MtlsRequired", "transient": false }
    }
  ]
}
```

**The response model**, shared with the refresh vectors:

- `status`.
- `headers`: names and values, sent as written.
- At most one body:
  - `json`, sent as compact JSON with `Content-Type: application/json; charset=utf-8`;
  - `text`, sent with `Content-Type: text/plain; charset=utf-8`.

  Without either, there is no body.

**The runner contract.**

- `key` is the full key a runner reads (`STORYTELLER_KEY`).
- `read` is the mode: `effective` reads `…/configuration/{key}`, and `resolved` reads `…/configuration/{key}/resolved`.
- The response answers every request of the read. Retries and a credential's single 401 re-send then see the same answer, and a runner needs no retry setting.
- `error.status` is the HTTP status the failure reports, or `null` when no answer arrived or the failure is local. `errorCode` is the non-blank `ErrorCode` of the body, or `null`. `transient` says whether a provider should try again later: it falls back to the cache and keeps polling.

**Vectors.** The mappings of the issue first, then the rest of the classification table:

| Name (short) | Read | Response | `error` |
| --- | --- | --- | --- |
| 401 without a body | resolved | 401 | 401, null, definitive |
| 401 with an ErrorResponse | resolved | 401, `Message` only | 401, null, definitive |
| 403 AccessDenied | resolved | 403 `AccessDenied` | 403, `AccessDenied`, definitive |
| 403 MtlsRequired | resolved | 403 `MtlsRequired` | 403, `MtlsRequired`, definitive |
| 404 without a body | resolved | 404 | 404, null, definitive |
| 404 AnnotationDeleted | resolved | 404 `AnnotationDeleted` | 404, `AnnotationDeleted`, definitive |
| 404 AnnotationInactive | resolved | 404 `AnnotationInactive` | 404, `AnnotationInactive`, definitive |
| 400 InvalidKey | resolved | 400 `InvalidKey` | 400, `InvalidKey`, definitive |
| 422 binding.evaluation | resolved | 422 `binding.evaluation` | 422, `binding.evaluation`, definitive |
| 422 binding.limit.steps | resolved | 422 `binding.limit.steps` | 422, `binding.limit.steps`, definitive |
| 422 binding.limit.envelopeDepth | resolved | 422 `binding.limit.envelopeDepth` | 422, `binding.limit.envelopeDepth`, definitive |
| 503 without a body | resolved | 503 | 503, null, transient |
| 503 KeyEncryptionUnavailable with Retry-After | resolved | 503 `KeyEncryptionUnavailable`, `Retry-After: 1` | 503, `KeyEncryptionUnavailable`, transient |
| 503 with a body that is not JSON | resolved | 503, `text` | 503, null, transient |
| 408, 424, 429 (with `Retry-After`), 500, 502, 504 | resolved | the status | the status, null, transient |
| 501, 505 | resolved | the status | the status, null, definitive |
| 409 and 412 are definitive | resolved | the status | the status, null, definitive |
| a blank ErrorCode is no code | resolved | 403, `"ErrorCode": "  "` | 403, null, definitive |
| the effective read maps the same way | effective | 403 `AccessDenied`; 503 | as above |

Redirects (3xx) stay out: HttpClient and `fetch` differ there before any SDK code runs.

**.NET runner.** `Configuration/test/Conformance/ErrorVectorTests.cs` has two theories over the vector names:

- **`AtStartup`.** A required source with the vector's mode and `Http.Otherwise` set to the response. `builder.Build()` throws a `StorytellerConfigurationException`, and its `StatusCode`, `ErrorCode`, `IsTransient` and `Key` are compared.
- **`AtRefresh`.** A start from a valid document, then the response as `Otherwise`. `TryRefreshAsync` returns `false`, `Status.LastError` is compared, and the previous keys stay in `IConfiguration`.

Both check that the request went to the path of the mode.

### 5. Refresh scenarios

New file `conformance/refresh.json`, schema `schema/refresh.schema.json`.

**The file.**

- `key` is the full key (`STORYTELLER_KEY`).
- `scenarios` is a list of `{ name, read, start, steps }`:
  - `start` is `{ response, keys }`: the answer to the startup read, and the flat keys the provider then has.
  - `steps` is a list of `{ response | timeout: true, expect }`.
- A 200 body is the API's `Configuration` envelope (`AnnotationKey`, `Version`, `Content`, `Modified`, `Hash`), written in full, so another runner serves it as it is.

**One step** is one refresh run (`TryRefreshAsync`, or the provider's own "refresh now"):

- `response` answers every request of the run.
- `timeout: true` means the platform does not answer within the provider's read timeout. The runner chooses a short timeout.
- `expect` holds:
  - `refreshed`: whether the run succeeded;
  - `keys`: the complete flat keys after the run, compared by exact text;
  - `notifications`: the cumulative count of change notifications since the start (`IConfiguration` reload tokens, `IOptionsMonitor` and their equivalents);
  - `ifNoneMatch`: the tag the run's read sent, or `null`;
  - `error`: the failure of the run as `{ status, errorCode, transient }`, required exactly when `refreshed` is false.

**Scenarios.** Tags have the platform's shape, `W/"r.<22 characters>"`:

| Scenario | Read | Steps | Expected |
| --- | --- | --- | --- |
| a change is applied with one notification | resolved | 200, tag 2, new content | refreshed, new keys, 1 notification, sent tag 1 |
| a 304 keeps the document without a notification | resolved | 304; then 200, tag 2 | no notification, then 1; both sent tag 1 |
| the same content under a new tag makes no notification, and the new tag is sent next | resolved | 200, tag 2, same content; 304 | 0 notifications; the second step sends tag 2 |
| a 503 keeps the document, is transient, and the next run recovers | resolved | 503; 200, tag 2 | failed (503, null, transient), old keys, 0; then refreshed, 1; both sent tag 1 |
| a timeout keeps the document and is transient | resolved | timeout; 304 | failed (null, null, transient), old keys; then refreshed |
| a 401 keeps the document and is definitive | resolved | 401 | failed (401, null, definitive), old keys |
| a 404 AnnotationDeleted keeps the document | resolved | 404 `AnnotationDeleted` | failed (404, `AnnotationDeleted`, definitive), old keys |
| a 422 binding.evaluation keeps the document | resolved | 422 `binding.evaluation` | failed (422, `binding.evaluation`, definitive), old keys |
| withheld secrets on a 200 fail loudly and keep the document | resolved | 200, tag 2, new content, `X-2S-Secrets-Withheld: mtls-required` | failed (200, null, definitive), old keys, 0 |
| withheld secrets on a 304 fail loudly | resolved | 304 with the header | failed (304, null, definitive), old keys |
| an effective read does not care about withheld secrets | effective | 200, tag 2, new content, with the header | refreshed, new keys, 1 |
| an invalid document keeps the previous one | resolved | 200, tag 2, content `{ "a:b": 1 }` | failed (null, null, definitive), old keys, 0; the next run still sends tag 1 |

**.NET runner.** `Configuration/test/Conformance/RefreshScenarioTests.cs` has one theory over the scenario names, on a fresh `EngineFixture` each:

- **Setup.**
  - `Environment["STORYTELLER_KEY"]` is the file's key, and `Mode` is the scenario's.
  - `RefreshInterval = TimeSpan.Zero`, so no timer run takes a response.
  - Only a scenario with a timeout step gets `ReadTimeout` of 1 s and an infinite `Resilience.AttemptTimeout`. The read timeout bounds the startup too, so the other scenarios keep the defaults.
- **Responses.** Each step sets `Http.Otherwise` to its response. A timeout step's response waits on `Task.Delay(Timeout.Infinite, ct)`.
- **Notifications** are counted with `ChangeToken.OnChange(root.GetReloadToken, …)`.
- **Keys** are `root.AsEnumerable()` without the section entries (null values), compared as an ordinal dictionary.
- **Checks.** `ifNoneMatch` is the raw `If-None-Match` header of the step's last request, and `error` is `Status.LastError` (`StatusCode`, `ErrorCode`, `IsTransient`).

The provider's own tests (`RefreshTests`, `StartupTests`) stay. The vectors are the portable subset, not a replacement.

### 6. The TypeScript SDK: configuration formats, transient errors and runners

**New module `lib/configuration-formats.ts`**, exported from `index.ts`. It mirrors `ConfigurationFormats` and has no `node:*` import:

```ts
/** The flattening targets: the flat formats whose keys the conformance suite pins. */
export type ConfigurationTarget = 'flat' | 'properties' | 'env'

/** The rules of InvalidDocumentError, as the conformance vectors spell them. */
export type DocumentRule =
  | 'not-an-object' | 'empty-name' | 'colon-in-name' | 'case-collision'
  | 'bracket-in-name' | 'array-gap' | 'invalid-env-name'

/** A document that cannot be flattened or formatted. The message names the JSON path, never a value. */
export class InvalidDocumentError extends Error {
  readonly rule: DocumentRule
  readonly path: string
}

/** The keys and raw values of a configuration's Content for a target, in document order. */
export function flattenConfiguration(content: unknown, target?: ConfigurationTarget): Record<string, string>

/** The exact text of the flat or env format, as the API's ?format= writes it. */
export function formatConfiguration(content: unknown, format: 'flat' | 'env'): string
```

The rules port from the .NET library:

- **Base walk** (`DocumentFlattener.Read`).
  - `null` and `undefined` content have no keys, and a non-object is `not-an-object` at `$`.
  - Every name is checked before its value, a null's name too: `empty-name`, then `colon-in-name`.
  - Leaves are collected in document order, and a collision is reported at the path of the later leaf.
  - A string is its value, a boolean `true` or `false`, and a number its .NET text (section 3):
    - a safe integer as its digits;
    - any other number with the .NET double notation, derived from the shortest digits that `Number.prototype.toExponential()` gives.
- **Collisions.** They compare like .NET's `OrdinalIgnoreCase`, one UTF-16 unit at a time: each unit is upper-cased when that gives one unit, and kept otherwise (`ß` stays `ß`).
- **Then the target's rules, in the .NET order.**
  - `properties`: `bracket-in-name` over every name, then `array-gap`. Keys are `.` joined, `[i]` per index, and a name with characters other than `[A-Za-z0-9_-]` is in the bracket form.
  - `env`: `invalid-env-name` over every name, with the .NET charset and the root's leading-letter rule. Names are upper-cased, which only touches ASCII because the rule allows only ASCII, and joined with `__`.
- **Paths** follow `JsonPaths`: `.name` for plain names, otherwise `['name']`, with `\` and `'` escaped, C0 controls and DEL as `\n`, `\r`, `\t` or `\uXXXX` (upper-case hex), and `#` as `#`.
- **Output.** The output record has a null prototype, so a `__proto__` name is a plain key.
- **`formatConfiguration`** first canonicalizes the content as `ConfigurationFormatter.Canonicalize` does: an unpaired surrogate in a name or a string becomes U+FFFD (`String.prototype.toWellFormed`). Then it renders:
  - `flat`: `{}` and LF for an empty document. Otherwise `{`, one `  "key": "value"` line per leaf joined with `,` and LF, then `}` and LF, with Newtonsoft's default escaping. That is `JSON.stringify`'s escaping plus `\u0085`, `\u2028` and `\u2029`, which Newtonsoft also escapes.
  - `env`: `NAME=value` and LF per leaf. A value of bare characters is left as it is. A value is single-quoted when it has no `'`, `\`, CR or LF; otherwise it is double-quoted with `\\`, `\"`, `\n`, `\r` and `\t`.

  `properties` and `yaml` text stay .NET-only, and `formatConfiguration` does not accept them. The issue asks for `.env` and flat JSON helpers, and the API renders all four formats anyway (`getConfigurationFormattedConditional`).
- **Kept apart.** `flattenJson`, `toFlatJson`, `parseDotEnv` and `serializeDotEnv` keep the key/value contract and do not change. The README says which helper serves which contract.

**`lib/errors.ts`.**

- `isTransientError(error: unknown): boolean` mirrors `DocumentReader.IsTransientStatus` for a `StorytellerError`:
  - true for 408, 424, 429, 5xx other than 501 and 505, a 2xx, and a network failure (status 0 without `aborted`);
  - false for an abort, the other statuses, and any error that is not a `StorytellerError`, which the SDK cannot classify.
- `fromResponse` treats an `ErrorCode` that is blank after trimming as no code, as .NET does. A non-empty code is kept as written, and `Message` and `Hint` keep today's behaviour.

**Runners**, in `sdk.typescript/src/test/conformance/`:

- **Loader.** `conformance.ts` is a helper, not a test file. It reads `suite.json` and the files through `node:fs` from `new URL('../../../../conformance/', import.meta.url)`.
- **`suite.test.ts`.**
  - Every kind in the manifest is either run here (`flattening`, `formats`, `errors`) or listed as not applicable (`refresh`: the TypeScript SDK has no provider). An unknown kind fails.
  - It prints the suite version it ran.
- **`flattening.test.ts`.** One test per (target, name): `flattenConfiguration(document, target)` against `keys`, compared as a map, or an `InvalidDocumentError` with the exact `rule` and `path`. An unknown target fails.
- **`formats.test.ts`.** One test per vector of the formats `flat` and `env`: the exact `text`, or the error.
  - A test pins the implemented formats to `flat` and `env`.
  - The number of `properties` and `yaml` vectors left to .NET is logged, so the gap is explicit.
- **`errors.test.ts`.** One test per vector, through the real read path:
  - `configureStoryteller({ baseUrl, fetch })` with a fake `fetch` that answers every request with the vector's response;
  - `getResolvedConfigurationConditional` or `getConfigurationConditional`, by `read`;
  - the thrown `StorytellerError`'s `status`, `errorCode` (`null` as `undefined`) and `isTransientError(error)`;
  - the request path of the mode.

**Package.** `prepublishOnly` becomes `npm run typecheck && npm test && npm run build`. It is a local safeguard; the gate is section 8.

### 7. The conformance workflow

New `.github/workflows/storyteller-conformance.yml`, named "Storyteller conformance".

**Triggers.**

- `push` and `pull_request` to `main`, filtered to paths:
  - `src/Platform/Storyteller/conformance/**`;
  - the projects the runners build: `Configuration`, `ConfigurationFormats`, `Sdk.NSwag`, `KeyValues.ClientEncryption` and `Abstractions.Annotations` under `src/Platform/Storyteller/`, plus `src/Libraries/Testing/System.IO.Abstractions.Trace`;
  - `src/Platform/Storyteller/sdk.typescript/**`;
  - `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `stylecop.json`, `src/.stylecop/**`, `version.json`, `src/Platform/version.json`;
  - the workflow file.
- `workflow_dispatch`.
- `workflow_call`, with no inputs. Path filters do not apply to a call.

**Settings.**

- `permissions: contents: read`.
- `concurrency: storyteller-conformance-${{ github.workflow }}-${{ github.ref }}`, with `cancel-in-progress` only for `pull_request`. Under `workflow_call` the context is the caller's, so a release never cancels its own gate.

**Jobs.**

- **`dotnet`**, ".NET reference", matrix `ubuntu-latest` and `windows-latest`, `fail-fast: false`:
  - `actions/checkout@v4` with `fetch-depth: 0` (NBGV) and `persist-credentials: false`;
  - `actions/setup-dotnet@v4` with `global-json-file: global.json`;
  - `dotnet test` for `src/Platform/Storyteller/ConfigurationFormats/test/ConfigurationFormats.UnitTests.csproj` and `src/Platform/Storyteller/Configuration/test/Configuration.UnitTests.csproj`.

  These are the whole test projects, not just the conformance classes, because they are the .NET test job the repository lacks.
  - Windows is where the projects have always run.
  - Linux runs the `[UnixFact]` tests for the first time. Their first result is reported in the review, and a Linux-only failure is fixed or recorded there.
- **`typescript`**, "TypeScript SDK", on `ubuntu-latest`:
  - `actions/setup-node@v4` with `node-version-file: src/Platform/Storyteller/sdk.typescript/src/.nvmrc` and the npm cache on the SDK's lockfile;
  - `npm install --global npm@11`;
  - `npm ci`, `npm run typecheck` and `npm test` in `src/Platform/Storyteller/sdk.typescript/src`.

`ui-admin.yml` does not change. It keeps its SDK steps, which now include the conformance runners.

### 8. The release workflow and its gate

New `.github/workflows/storyteller-sdk-release.yml`, "Release the Storyteller SDKs", modelled on `storyteller-action-release.yml`. It is not run as part of this change.

**Trigger and settings.**

- `workflow_dispatch` with two inputs:
  - `packages`: a choice of `all`, `nuget` or `npm`, default `all`;
  - `publish`: a boolean, default `false`. Without `publish` the workflow is a dry run: it packs and uploads the packages as artifacts and stops.
- `run-name` names the packages and the mode.
- `concurrency: storyteller-sdk-release`, without cancel.
- `permissions: contents: read`.

**Jobs.**

1. **`conformance`** is `uses: ./.github/workflows/storyteller-conformance.yml`: the gate.
2. **`pack`** (`needs: conformance`, `ubuntu-latest`):
   - It refuses to publish from any ref other than `refs/heads/main`, so a dry run may use any branch.
   - It checks out with `fetch-depth: 0`, so NBGV computes the public version on `main`.
   - NuGet: `dotnet pack -c Release -p:ContinuousIntegrationBuild=true -o out/nuget` for the provider's closure, which is `Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Sdk.NSwag`, `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault`. The `Hosting` packages of 424-91 join the list when they reach `main`.
   - npm: `npm ci`, `npm run build` and `npm pack --pack-destination out/npm`. When publishing, it fails first if `npm view @42for.net/2splatform.sdk@<version>` already exists.
   - It uploads `out/nuget` and `out/npm` as artifacts.

   The packages are built once, from the commit that passed the gate, and published as built.
3. **`publish-nuget`** (`needs: pack`): runs only when `publish` is set and `packages` is `all` or `nuget`.
   - `environment: storyteller-sdk`, where a reviewer can be required and `NUGET_API_KEY` lives.
   - It downloads the artifact and runs `dotnet nuget push out/nuget/*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate`.
4. **`publish-npm`** (`needs: pack`): runs only when `publish` is set and `packages` is `all` or `npm`.
   - `environment: storyteller-sdk`, which holds `NPM_TOKEN`.
   - It runs `actions/setup-node@v4` with `registry-url: https://registry.npmjs.org`, then `npm publish out/npm/*.tgz --access public` with `NODE_AUTH_TOKEN`.

Secrets reach the publish jobs only, through the environment and `env:`, never in a `run:` line. The environment and its secrets are the owner's to create.

### 9. Documentation

- **New `docs/Platform/Storyteller/conformance.md`.** It covers:
  - the purpose;
  - the layout and the manifest;
  - the versioning and identity rules;
  - each kind, with its shape and outcome;
  - the response model and the runner contract;
  - the targets and their rules (in short, linking `configuration-formats.md`);
  - portability and the numbers;
  - the .NET and TypeScript runners, and what each does not run;
  - CI and the release gate;
  - how a provider in another language adopts the suite.
- **`conformance/README.md`** is short: the layout, the commands, and a link to the page.
- **`configuration-formats.md`**: "Test vectors" points at `conformance/` and the new page, with the new file names and the target field.
- **`configuration-provider.md`**:
  - the flattening paragraph names `conformance/flattening/flat.json`;
  - "Refresh" and "Errors" link the refresh and error vectors as the portable contract.
- **`sdk.typescript/README.md`**: a section on `flattenConfiguration`, `formatConfiguration`, `InvalidDocumentError` and `isTransientError`, next to the key/value helpers, saying which contract each serves; and the conformance runners.
- **`CLAUDE.md`**: a Platform row for `Storyteller/conformance/`, and the two workflows under CI/CD.
- **Comments in the code** name the new paths.

### 10. Out of scope

- Providers for Java, Python, Go and Rust (Phase D). They consume the suite later.
- A TypeScript configuration provider: the refresh vectors stay .NET-only until one exists.
- `properties` and `yaml` text in TypeScript.
- Provenance per property (C16).
- Running the release workflow, creating the `storyteller-sdk` environment and its secrets, or adding npm provenance (it needs a `repository` field and `id-token: write`).
- Changing the provider's or the library's behaviour. If a vector shows a difference between the runtimes, the vector documents the .NET behaviour, and the other runtime follows it.

### 11. Differences from the CodeRabbit plan

| CodeRabbit plan | This spec | Why |
| --- | --- | --- |
| Built on `main`: makes `DocumentFlattener` target-aware and adds a `delimiter-in-name` rule | Built on `feat/424-104`, whose `ConfigurationFormats` already is the target-aware reference, with `bracket-in-name`, `array-gap` and `invalid-env-name` | The plan predates 424-104. A second flattener and rule set would contradict the API's formats |
| Targets `dotnet`, `properties`, `env` | `flat`, `properties`, `env` | One vocabulary with `?format=`, `sform --format` and `ConfigurationFormat` |
| `suiteVersion` in every file | One `version` in `suite.json`, the manifest every runner reads | One version for the suite; a manifest makes a missing or unknown file an error, not a silent skip |
| Draft 2020-12 schemas | Draft-07 | NJsonSchema and the common validators of other languages support draft-07 fully |
| `errors/http.json` with `expect` | `errors.json` with `error`, and a `json` or `text` body with an implied content type | The outcome field matches the flattening vectors; the content type decides whether an SDK parses the body |
| Error vectors at startup only | At startup and at refresh | The refresh path keeps the previous document, and that is part of the contract |
| Refresh `steps` with `reloads` and `failure` | `refreshed`, `notifications`, `ifNoneMatch` and `error` | Neutral names; the sent tag is part of the conditional-read contract |
| `flattenConfiguration` and the error predicate in TS | Also `formatConfiguration` for `flat` and `env`, and the formats vectors in TS | The issue asks for `.env` and flat JSON helpers; the vectors pin their exact text |
| Assumes numbers can be matched in TS by checking | A portability rule enforced by a suite test, and edge-case number vectors from the .NET reference | `JSON.parse` loses `1.0`, big integers and key order; the suite must only hold documents every runtime reads the same way |
| One release job per registry | A `pack` job that builds once after the gate, publish jobs that push those artifacts, and a dry run by default | The published bytes are the ones the gate verified, and a first run cannot publish by accident |
| CI on `ubuntu-latest` | A matrix of `ubuntu-latest` and `windows-latest` for .NET | The provider tests have only ever run on Windows |

## Phases

1. **The shared suite.** Sections 1-3: the folder, the manifest, the schemas, the moves with file-level fields, the re-pointed runners, the suite tests (schemas, coverage, portability), the number vectors, and the comments and docs that name the old folder.
2. **Error and refresh vectors.** Sections 4-5: both files and the .NET runners against the real provider.
3. **TypeScript.** Section 6: `configuration-formats.ts`, `isTransientError`, the `ErrorCode` alignment, the runners and `prepublishOnly`.
4. **CI, release gate and documentation.** Sections 7-9.

Each phase is one commit or more; a review per phase follows in `specs/reviews/`.
