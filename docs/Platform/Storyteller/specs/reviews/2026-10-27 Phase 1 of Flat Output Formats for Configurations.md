# Phase 1 of Flat Output Formats for Configurations

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

Phase 1 covers sections 1 to 4 of [Flat Output Formats for Configurations](../2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md) and the phase 1 docs of section 11. The flattener of the .NET provider became the public core of a new packable library, `42.Platform.Storyteller.ConfigurationFormats`, which renders the `Content` of a configuration as `flat`, `properties`, `env` and `yaml` from one canonical reading of the document. The API (phase 2) and `sform` (phase 4) format with it, so the same document gives the same bytes in both. The final review of the branch changed the escaping of paths (section 1, `#`).

| Commit | Content |
|---|---|
| `71d020e5` | The flattener moves into the library (section 1) |
| `48fb331f` | The formatter, the four renderers, the vector files and the docs (sections 1 to 4) |
| `bd1b79d6` | Review fix: the format vectors pin the lower-case hex of `flat` |
| `12ac6bff` | Review fix: the docs and the vectors say that paths escape only C0 controls and DEL |
| `8fa6124c` | Final review fix: paths escape `#` (section 1) |

| Project | Passed (end of the branch) |
|---|---|
| `ConfigurationFormats/test` | 381 |
| `Configuration/test` | 524, 2 skipped |
| `Configuration.KeyVault/test` | 87 |

## What Was Done

### 1. A shared formats library

- `src/Platform/Storyteller/ConfigurationFormats/src/ConfigurationFormats.csproj`: assembly and package `42.Platform.Storyteller.ConfigurationFormats`, root namespace `_42.Platform.Storyteller.ConfigurationFormats`, `net10.0`, packable, with `Newtonsoft.Json` as its only dependency and the package readme `docs/Platform/Storyteller/ConfigurationFormats/package-readme.md`. The provider (`Configuration/src`), `Api.Functions/src` and `src/Platform/Cli/src` reference it; `Sdk.NSwag` does not. The source and the test project are in `42.mono.slnx` next to `Configuration`.
- The public types are those of the spec: `ConfigurationFormat`, `ConfigurationFormatNames` (`Names`, `TryParse`, `GetName`, `GetMediaType`), `DocumentRules`, `DocumentValueKind`, `DocumentSegment`, `DocumentLeaf`, `DocumentFlattener` (`Read`, `Flatten`), `JsonPaths`, `InvalidDocumentException` (`Rule`, `Path`), `FormattedConfiguration`, `EnvironmentVariable` and `ConfigurationFormatter` (`Version = "1"`, `Format`, `ToEnvironmentVariables`, `Canonicalize`). **Additions:** `DocumentSegment.IsIndex` with the factories `Property` and `Item`, and `ToString` overrides on `DocumentLeaf`, `EnvironmentVariable` and `FormattedConfiguration` that leave the value out, so a log line or an assertion message never shows one.
- `DocumentFlattener.Read` is the former walk turned into an ordered list of leaves with the same four base rules, messages and first-error semantics; `Read(validate: false)` applies no rule. `Flatten` is the `:` projection of `Read` and returns what it returned before. The renderers are internal: `FlatRenderer`, `PropertiesRenderer`, `EnvRenderer`, `YamlRenderer` and `PropertyNames`, the second walk over every property name of the canonical token for `bracket-in-name` and `invalid-env-name`.
- `Canonicalize` writes the token with `ToString(Formatting.None)` and reads it back with `DateParseHandling.None` and `FloatParseHandling.Double`, without a depth limit, after replacing unpaired surrogates with U+FFFD through a UTF-8 round trip.
- **Paths.** `JsonPaths` escapes `\` and `'` with a backslash and the C0 control characters and DEL as `\n`, `\r`, `\t` or `\uXXXX` with upper-case hex; C1 controls, U+2028 and U+2029 stay (`12ac6bff` made the docs and the vectors say exactly that, as a port that escaped every Unicode control would have written other paths). **Final review fix (`8fa6124c`):** the runner of GitHub Actions still reads the older `##[command]` syntax anywhere in a line of a step's output, standard error included, so a property named `##[set-output name=deploy_target;]prod` became a workflow command in the error output of `sform`. `JsonPaths` now also writes `#` as `\u0023`; a path never holds a `#`. `sform` does the same for every message on its error output (phase 4).
- **The provider** changed only its usings and keeps wrapping `InvalidDocumentException` in `StorytellerConfigurationException`; its public API did not change.

### 2. Rules shared by every format

As in the spec: the base rules over the whole document, then the name rule of the format over every property name, then the value rule (`array-gap`); the first error throws `InvalidDocumentException` and nothing is returned. Every format writes LF, no byte order mark, keys in document order, and upper-case hex where the library writes `\uXXXX` itself.

### 3. The four formats

Implemented as specified: `flat` with Newtonsoft's escaping and a trailing LF (`{}\n` when empty); `properties` with Spring's bracket form for names outside `[A-Za-z0-9_-]`, `bracket-in-name` and `array-gap`, and ASCII output; `env` with the variable charset, `__` as the only delimiter, the dotenv quoting of the key/value export with literal single quotes, and the optional prefix; `yaml` written without a YAML library, every string double-quoted, `null` for an inner gap of an array, and explicit keys over 1024 characters. `ToEnvironmentVariables` returns the `env` names with the raw values.

### 4. Vectors and tests of the library

- `ConfigurationFormats/test` (xUnit, Shouldly, YamlDotNet for the round trip only) links the vector files from `Configuration/test/Vectors/` as `Vectors.<file>`. `FlatteningVectorTests` and `DocumentFlattenerTests` moved there; `ADocumentReadByTheSdk_KeepsDateLikeStrings` reads its document with a `JsonTextReader`.
- Vector files at the end of the phase: `flattening.json` 19 (18 and the line break in a name), `flattening.properties.json` 31, `flattening.env.json` 32, `formats.json` 59. `bd1b79d6` put ESC (U+001B) into the control-character vectors of every format, as every escape the vectors expected had been made of digits only, so a change of the `flat` writer's lower-case hex would have changed the body, and the tags derived from it, without failing a test. `12ac6bff` added a `formats.json` vector with a raw U+0085 and U+2028 in an error path. **Final review fix (`8fa6124c`):** `flattening.json`, `flattening.properties.json` and `flattening.env.json` gain "a hash is escaped in paths" (now 20, 32 and 33), and the description of `flattening.json` names the escape.
- Readers in the tests read each format back: YamlDotNet for `yaml`, the `java.util.Properties` rules for `properties`, the dotenv dialect for `env`, JSON for `flat`; `RoundTripTests` flattens the parsed YAML and compares it with `flat`.

### 11. Documentation (phase 1)

New `docs/Platform/Storyteller/configuration-formats.md` (formats, rules, readers and their limits, the parity guarantee, the vector files); `configuration-provider.md` points to the library and the new page and states the path escaping; the package readme of the library. The final review fix states the `#` escape on both pages.
