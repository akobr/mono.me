# Conformance Suite

Every Storyteller integration should behave the same way. A Spring Boot service, a Node.js app, a Kubernetes operator and a .NET worker should read the same execution key, see the same keys, and fail in the same way. The conformance suite makes that testable. It is a folder of language-neutral JSON vectors that every provider and SDK runs in its own CI, the way OpenFeature SDKs share a Gherkin suite. It also holds the catalog of [project settings](access.md#project-settings), so every client allows the same names, colors, icons, time zones and date formats, and validates a settings document the same way.

The suite lives in `src/Platform/Storyteller/conformance/`. Its version is in `suite.json`.

## Layout

```text
src/Platform/Storyteller/conformance/
  suite.json                 the manifest: the version and every file with its kind
  schema/                    a JSON Schema (draft-07) per kind, and one of the manifest
  flattening/flat.json       the IConfiguration keys of a document (target flat)
  flattening/properties.json the Spring Boot keys (target properties)
  flattening/env.json        the environment variable names (target env)
  formats.json               the exact text of the formats flat, properties, env and yaml
  errors.json                how an answer of the platform maps to a failure
  refresh.json               how a provider refreshes
  project-settings.json      the catalog of project settings, and how a settings document is validated
  tools/time-zones.cs        prints the time zone ids of the catalog (a file-based app, not a vector file)
```

- **The manifest.** A runner reads `suite.json` and runs every listed file of a kind it implements. It fails on a kind it does not know, and it says which kinds it does not run and why. A new kind or file therefore fails every runner until each one decides; nothing is skipped silently.
- **Schemas.** Every file has a `$schema` with the relative path of `schema/<kind>.schema.json`, so an editor validates and completes it. The schemas are self-contained: a runner can validate a file from a string, without resolving references to other files.
- **Identity.** A vector is identified by its file and its `name`, and names are unique within a file. A flattening file is identified by its `target`. `properties.json` and `env.json` hold every document of `flat.json` under the same name, plus documents of their own, so the outcome of one document can be read side by side for every target.

### Versions

The suite has one SemVer, `version` in `suite.json`, for all its files. A provider states the version it passes.

| Change | Version |
| --- | --- |
| An expected result changes, a vector is removed or renamed, a field changes meaning, or a schema rejects a file it accepted | Major |
| A vector, a file, a kind or an optional field is added | Minor |
| A description or the README changes | Patch |

Version `1.1.0` added the kind `project-settings`.

## Kinds

### `flattening`

How the `Content` object of a configuration becomes the keys of a target. The rules are in [Configuration Formats](configuration-formats.md#rules-shared-by-every-format), with the target's own in its section.

```json
{
  "$schema": "../schema/flattening.schema.json",
  "target": "properties",
  "description": "…",
  "vectors": [
    {
      "name": "study example",
      "document": { "db": { "hosts": [ "db-1", "db-2" ], "port": 5432 } },
      "keys": { "db.hosts[0]": "db-1", "db.hosts[1]": "db-2", "db.port": "5432" }
    },
    {
      "name": "a colon in a name is invalid",
      "document": { "db": { "a:b": "x" } },
      "error": { "rule": "colon-in-name", "path": "$.db['a:b']" }
    }
  ]
}
```

- **`target`** is one of the names of the API's `?format=`:

| Target | Keys | Study 3.3 |
| --- | --- | --- |
| `flat` | `db:hosts:0`, the keys of .NET `IConfiguration` | .NET |
| `properties` | `db.hosts[0]`, and `[name]` for a name with other characters than `[A-Za-z0-9_-]` | Spring, MicroProfile, Quarkus |
| `env` | `DB__HOSTS__0` | Environment variables, `.env` |

- **The outcome** is either `keys` or `error`.
  - `keys` holds every key with its raw value, compared by exact text, in document order.
  - `error` holds the rule and the JSON path of the first error. The rules run group by group over the whole document, each group in document order: first the base rules, then the target's name rule, then its value rule.
- **Case.** Two keys collide when .NET's `OrdinalIgnoreCase` finds them equal: the simple upper-case mapping of each code point. So `ß`, the dotless `ı` and the long `ſ` keep their case, letters beyond the Basic Multilingual Plane fold, and the Greek letters with ypogegrammeni fold to their title-case forms. The vectors pin each of these.
- **The seven rules** are `not-an-object`, `empty-name`, `colon-in-name` and `case-collision` for every target, plus `bracket-in-name` and `array-gap` for `properties` and `invalid-env-name` for `env`.

Every target covers the five rules of the study, and a suite test checks that each target keeps them:

| Rule | `flat` | `properties` | `env` |
| --- | --- | --- | --- |
| Numbers and booleans are their JSON text | `numbers are their JSON text`, `booleans are lower case` | the same | the same |
| No null | `null makes no key` | the same; an inner null is `array-gap` | the same |
| Empty containers make no key | `empty objects and arrays make no key` | the same | the same |
| A delimiter in a name is escaped or rejected | `colon-in-name` | the bracket form; `bracket-in-name` | `invalid-env-name` |
| Keys that differ only in case are an error | `case-collision` | the same | the same |

### `formats`

The exact text of a format, written as UTF-8 without a byte order mark and with LF line ends, or the error that makes the document invalid in that format. Each vector has `name`, `document`, `format` and either `text` or `error`. This kind pins quoting and escaping. The keys and values are in the flattening files.

### `errors`

How an answer of the platform to a configuration read becomes the failure a provider reports.

```json
{
  "$schema": "./schema/errors.schema.json",
  "description": "…",
  "key": "@acme.billing.default.sbt.northwind",
  "vectors": [
    {
      "name": "403 MtlsRequired is definitive and keeps its code",
      "read": "resolved",
      "response": { "status": 403, "json": { "Message": "…", "ErrorCode": "MtlsRequired" } },
      "error": { "status": 403, "errorCode": "MtlsRequired", "transient": false }
    }
  ]
}
```

- **`key`** is the full key the runner reads, as `STORYTELLER_KEY`.
- **`read`** is the mode. `effective` reads `…/configuration/{key}`, and `resolved` reads `…/configuration/{key}/resolved`.
- **`error.status`** is the HTTP status the failure reports. It is `null` when no answer arrived or the failure is local.
- **`error.errorCode`** is the `ErrorCode` of the `ErrorResponse` body when it is not blank, otherwise `null`.
- **`error.transient`** says whether the failure is worth trying again later. A transient failure makes a provider fall back to its cache and keep polling. A definitive one fails even an optional startup, and at a refresh it keeps the previous document.

The vectors cover:

- the mappings of study section 3.12: `401`, `403 MtlsRequired`, `404 AnnotationInactive`, `422 binding.*` and `503`;
- the rest of the classification. `408`, `424`, `429` and every `5xx` except `501` and `505` are transient; every other status is definitive.

A `3xx` is left out, because HttpClient and `fetch` treat redirects differently before any SDK code runs.

### `refresh`

How a provider refreshes a configuration it has applied.

**A scenario** has a `name`, a `read`, and two parts:

- `start`: the answer to the startup read, and the keys the provider then has;
- `steps`: one refresh run each. A step's `response` answers every request of the run, and `timeout: true` is a read that gets no answer within the provider's read timeout.

**After each run**, `expect` holds:

| Field | Content |
| --- | --- |
| `refreshed` | whether the run succeeded |
| `keys` | the complete flat keys |
| `notifications` | the change notifications since the start, cumulative: one per applied document that changes a key or a value |
| `ifNoneMatch` | the tag the run's read sent, or `null` |
| `error` | the failure of the run, present exactly when `refreshed` is false |

The scenarios cover:

- a change, a `304`, and a new tag with the same content;
- a `503`, a timeout, a `401`, a `404` and a `422`, each keeping the document;
- withheld secrets on a `200` and on a `304`, which fail loudly, and an effective read, which ignores them;
- an invalid document, which keeps the previous one and its tag.

### `project-settings`

The catalog of [project settings](access.md#project-settings), and how a settings document of any client is validated and normalized. The file is `project-settings.json`, and it is also the catalog of the platform itself: `Abstractions.Annotations` embeds it (`ProjectSettingsCatalog`, with the rules in `ProjectSettingsRules` and `ProjectSettingsJson`), so the API checks every write against it, and the TypeScript SDK generates its catalog module from it.

```json
{
  "$schema": "./schema/project-settings.schema.json",
  "description": "…",
  "catalog": {
    "format": "2s.project-settings.v1",
    "typeCodes": [ "rst", "unt", "sbt", "usg", "cnt", "exe", "uxe" ],
    "builtIn": { "sbt": { "label": "Subject", "plural": "Subjects", "color": "blue", "icon": "i-lucide-sun" } },
    "colors": [ "red", "orange", "…", "gray" ],
    "icons": [ "i-lucide-target", "…", "i-lucide-zap" ],
    "dateFormats": [ "yyyy-mm-dd", "dd.mm.yyyy", "dd/mm/yyyy", "mm/dd/yyyy", "d mmm yyyy" ],
    "defaults": { "timeZone": "UTC", "dateFormat": "yyyy-mm-dd" },
    "limits": { "label": 40, "plural": 40 },
    "timeZones": [ "Africa/Abidjan", "…", "Zulu" ]
  },
  "vectors": [
    {
      "name": "time zones match in any case and are stored in their canonical spelling",
      "settings": { "DefaultTimeZone": "europe/prague" },
      "expected": { "Types": {}, "DefaultTimeZone": "Europe/Prague" }
    },
    {
      "name": "an unknown color is refused",
      "settings": { "Types": { "sbt": { "Color": "beige" } } },
      "problems": [ { "path": "$.Types.sbt.Color", "rule": "color" } ]
    }
  ]
}
```

**The catalog** holds every allowed value, and its lists keep their order: clients show them in it, and settings are written with the type codes in it.

| Member | Content |
| --- | --- |
| `format` | The marker of a settings file, `2s.project-settings.v1`: the `Format` of a data set's `project-settings.json` |
| `typeCodes` | The seven codes of the annotation types, the keys of `Types` |
| `builtIn` | The label, plural, color and icon of each type when the project sets none |
| `colors` | The 18 color names of the admin UI's palette |
| `icons` | The 65 icons, Iconify names of the Lucide set that the admin UI bundles (`i-lucide-building-2`) |
| `dateFormats` | `yyyy` the year, `mm` the month, `mmm` its English abbreviation, `dd` and `d` the day with and without a leading zero |
| `defaults` | The time zone and the date format of settings that have none |
| `limits` | The most UTF-16 code units of a trimmed label and plural |
| `timeZones` | The IANA time zone ids of the tz database, in ordinal order |

**The time zones** are every canonical zone of the tz database and every link (`Asia/Calcutta` next to `Asia/Kolkata`, so the spelling of any browser is in it), the `Etc/*` zones and `UTC`: 597 ids of release 2026f. The list is in the catalog rather than taken from each runtime, because the runtimes disagree: the API and the MCP server run with `InvariantGlobalization`, under which `TimeZoneInfo` cannot resolve IANA ids on Windows and resolves them from the image's tzdata on Linux, while `sform` and DbCreator use ICU and a browser its `Intl`. `tools/time-zones.cs`, a file-based app over NodaTime's copy of the tz database, prints the list, one JSON string per line, and the tz release on the standard error. After a NodaTime update that brings a new release, run it and paste its lines into `timeZones`:

```bash
dotnet run src/Platform/Storyteller/conformance/tools/time-zones.cs
```

**A vector** has a `name`, a `settings` document as a client sends it (any JSON value), and either:

- `expected`: the normalized settings, as the API writes them without `Version` and `Modified`. `Types` holds every type that has a value, and `DefaultTimeZone` and `DateFormat` are there only when set. A runner compares them as JSON values, so the order of the members does not count.
- `problems`: every problem that refuses the document, each a `path` and a `rule`, in any order.

**The rules** are `type-code`, `label`, `plural`, `plural-without-label`, `color`, `icon`, `time-zone`, `date-format`, `unknown-member`, `case-collision`, `type` and `format` (see [the rules of the API](access.md#project-settings)). A runner checks that the vectors refuse by each of them.

- Member names, type codes, colors, icons, date formats and time zones match in ASCII case only (`A-Z` equals `a-z`, no other folding) and are stored in the catalog's spelling. `Format` matches exactly.
- Labels and plurals are trimmed of Unicode `White_Space` and measured in UTF-16 code units, as `String.length` of JavaScript. A blank label, plural, time zone or date format means none, `null` means none, and a type without any value is removed.
- `Version` and `Modified` are read-only and ignored, of any JSON type.
- **A path** starts at `$`. A member whose name is ASCII letters, digits, `_` and `-` (starting with a letter or `_`) is `.name`, any other `['name']`. A known member or type code is written in its canonical spelling, an unknown one as given.

The 72 vectors cover:

- an empty document, every member, every type code, and the file shape with its marker;
- each rule once accepted and once refused, and a document that breaks several at once;
- matching in any case, trimming, blank values, `null`, and an appearance without any value;
- labels and plurals of 40 and 41 characters, also outside the Basic Multilingual Plane;
- the zones `Europe/Prague`, `America/Phoenix`, `UTC`, a link and an `Etc` zone accepted, and a made-up zone, the Windows zone `Central Europe Standard Time` and an offset refused;
- the camelCase shape of the `exhibit` data set, read as the PascalCase one.

## The response model

The `errors` and `refresh` files describe an answer of the platform as `{ status, headers, json | text }`:

- **`headers`** are sent as written: `ETag`, `Cache-Control`, `Retry-After`, `X-2S-Secrets-Withheld`.
- **`json`** is sent as compact JSON with `Content-Type: application/json; charset=utf-8`. An error body is an `ErrorResponse` in PascalCase (`Message`, `Hint`, `ErrorCode`), as the API writes it. A field is left out rather than written as `null`, because the .NET SDK drops a body with a `null` field.
- **`text`** is sent with `Content-Type: text/plain; charset=utf-8`.
- **No body** without either.

A response answers every request of its read, so retries and a credential's single re-send of a `401` see the same answer. A runner needs no retry setting.

## Portability

A vector is read with `JSON.parse` in JavaScript and with Newtonsoft (`FloatParseHandling.Double`) in .NET. The suite holds only what every runtime reads the same way, and a suite test rejects anything else:

1. **Numbers.**
   - An integer is within ±(2^53 − 1), because a JavaScript number cannot hold `9007199254740993`.
   - A number written with a fraction or an exponent is either not an integer, or of magnitude 2^53 or more. JavaScript reads `1.0` and `1E+2` as `1` and `100`, but Newtonsoft keeps them as doubles and writes `1.0` and `100.0`.
2. **Order.** No object has a name that is an array index (`"0"`, `"12"`) after a name that is not, and the array-index names are in ascending order, because `JSON.parse` lists array indexes first, in ascending order.
3. **Duplicates.** No object has the same name twice, because `JSON.parse` keeps the last one.

**The text of a double**, as the .NET reference writes it:

- the shortest round-trip digits;
- fixed notation while the decimal point falls between 3 places before the first digit and 17 places after it (`0.0001`, `123456.789`, `10000000000000000.0`);
- scientific notation otherwise, with an upper-case `E`, a sign and at least two exponent digits (`1E+20`, `1.5E-07`, `5E-324`).

A runtime whose own text differs, as JavaScript's `String(1e20)` does, emulates it. The vector `numbers that are not safe integers are their double text` pins the edges in every target.

## Runners

| Runtime | Where | Runs | Does not run |
| --- | --- | --- | --- |
| .NET (reference) | `ConfigurationFormats/test`: `FlatteningVectorTests`, `FormatVectorTests` | `flattening` (all targets), `formats` (all formats) | |
| .NET (reference) | `Configuration/test/Conformance`: `SuiteTests`, `ErrorVectorTests`, `RefreshScenarioTests` | the suite itself (manifest, schemas, coverage, portability); `errors` at startup and at refresh; `refresh` | |
| .NET (reference) | `src/Platform/Storyteller/Backend.Core/test/ProjectSettings`: `ProjectSettingsConformanceTests` | `project-settings` through `ProjectSettingsJson.Read`: every vector, the normalized settings read again in both JSON shapes, a vector for every rule, and the file's catalog against the one `Abstractions.Annotations` embeds | |
| TypeScript SDK | `sdk.typescript/src/test/conformance` | `flattening` through `flattenConfiguration`; `formats` for `flat` and `env` through `formatConfiguration`; `errors` through the conditional reads and `isTransientError`; `project-settings` through `validateProjectSettings`, and the catalog module (`generated/catalog/project-settings.ts`) against the file's catalog | `refresh`: the SDK has no configuration provider. `formats` for `properties` and `yaml`, whose count the runner reports |

**.NET.**

- The test projects of `ConfigurationFormats` and `Configuration` embed the folder as resources (`Conformance/<path>`), and they read it as the SDK reads a body: date-like strings stay strings, numbers are doubles.
- The error and refresh runners drive the real `AddStoryteller` source over a stub HTTP handler, with a fake clock and no retries. A timeout step shortens `ReadTimeout` to one second.
- `Backend.Core/test` embeds `project-settings.json` (`Conformance/project-settings.json`) and reads it with `System.Text.Json` (`JsonNode`), as the API reads a body. `SuiteTests` checks the file against its schema with the rest of the suite.

**TypeScript.**

- The runners read the folder from the repository with `node:fs`.
- The error runner configures the SDK with a fake `fetch`, then reads through `getResolvedConfigurationConditional` or `getConfigurationConditional`.
- The project settings runner compares the normalized settings as values (the order of the members does not count) and the problems by `path` and `rule` in any order, as the .NET reference does; `npm run generate` writes the catalog module from the same file.

Run them:

```bash
dotnet test --project src/Libraries/Platform/Storyteller/ConfigurationFormats/test
dotnet test --project src/Libraries/Platform/Storyteller/Configuration/test --filter "FullyQualifiedName~Conformance"
dotnet test --project src/Platform/Storyteller/Backend.Core/test --filter-class "*ProjectSettingsConformanceTests"

cd src/Platform/Storyteller/sdk.typescript/src
npx vitest run test/conformance
```

A change of the catalog also changes the SDK's generated module: run `npm run generate` in `sdk.typescript/src` and commit `generated/catalog/project-settings.ts`, or the check "generated client matches the OpenAPI document" of `Platform.UI.Admin.yml` fails.

## CI and the release gate

**`.github/workflows/Platform.Storyteller-conformance.yml`** runs the suite on every push and pull request to `main` that touches the suite, the projects that run it, or the build settings. It can also be run by hand, and called by another workflow.

- The .NET job runs both test projects in full on Linux and Windows, and `ProjectSettingsConformanceTests` of `Backend.Core/test` (only that class: the rest of Backend.Core's tests are not part of the suite). Changes of `Abstractions.Annotations` and of the project settings tests of Backend.Core trigger it too.
- The TypeScript job type-checks and tests the SDK.

**`.github/workflows/Platform.Storyteller.Sdk-release.yml`** releases the SDKs and is run by hand.

1. **Gate.** It calls the conformance workflow first.
2. **Pack.** Only then does it pack, once, from that commit:
   - the NuGet packages of the .NET configuration provider, Hosting, Aspire and the packages they depend on (`Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Sdk.NSwag`, `ConfigurationFormats`, `Configuration`, `Configuration.KeyVault`, `Hosting`, `Aspire.Hosting`);
   - the npm package `@42for.net/2splatform.sdk`.
3. **Publish.** The publish jobs push exactly those files.

Without the input `publish`, the run is a dry run that uploads the packages as an artifact. Publishing runs from `main` only, and it refuses an npm version that is already published. The registry secrets `NUGET_API_KEY` and `NPM_TOKEN` belong to the environment `storyteller-sdk`, which only the publish jobs use. The owner creates that environment, its secrets and its required reviewers.

`prepublishOnly` of the TypeScript SDK type-checks and tests before the build. That is a local safeguard; the workflow is the gate.

## Adding a provider

A provider in another language adopts the suite in four steps.

1. Read `suite.json` from the repository, a submodule or a copy pinned to a version.
2. Run every file of the kinds it implements:
   - `flattening`, for every target its framework uses;
   - `errors`, with a fake HTTP server or handler that serves each response;
   - `refresh`, when it has a provider that refreshes;
   - `project-settings`, when it validates or shows project settings, with its lists taken from the catalog.
3. Fail on an unknown kind or target, and report the kinds it does not run.
4. Run it in CI, and state the suite version it passes.

A vector that shows a difference between runtimes documents the behaviour of the .NET reference, and the other runtimes follow it.
