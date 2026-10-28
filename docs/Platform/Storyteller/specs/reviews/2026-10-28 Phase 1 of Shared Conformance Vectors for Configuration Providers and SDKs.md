# Phase 1 of Shared Conformance Vectors for Configuration Providers and SDKs

Linear: [424-105](https://linear.app/424fun/issue/424-105/shared-conformance-vectors-for-configuration-providers-and-sdks-c22).

## Overview

Phase 1 covers sections 1 to 3 of [Shared Conformance Vectors for Configuration Providers and SDKs](../2026-10-28%20Shared%20Conformance%20Vectors%20for%20Configuration%20Providers%20and%20SDKs.md).

- The vector files of 424-104 moved from `Configuration/test/Vectors/` to the new language-neutral folder `src/Platform/Storyteller/conformance/`, with a manifest (`suite.json`, version `1.0.0`), draft-07 schemas and a file-level `target`. Their vectors are unchanged.
- Both .NET test projects read the folder.
- The provider's tests check the suite itself: the manifest, the schemas, the study's rules per target, and the portability rules.
- New vectors pin the text of numbers that are not safe integers.

The review of the branch added the case-fold vectors and a kind check (see the end of this page and phase 3).

| Commit | Content |
|---|---|
| `5c78ec2f` | The folder, the manifest, the schemas, the moves, the re-pointed runners, the suite tests, the number vectors, the comments and docs that named the old folder |
| `8286bec5` | Review fix: case-fold vectors in every target, and an unpaired-surrogate vector in `properties` and `env` (with the TypeScript fix of phase 3) |
| `2f6f04db` | Review fix: the reference fails on a kind it does not run, the flat body is compared in order, the study's named vectors must be key vectors |
| `bd98cbd7` | Review fix: the docs and the flattening schema say how the rules order errors and how keys fold their case |

| Project | Passed (end of the phase) | Passed (end of the branch) |
|---|---|---|
| `ConfigurationFormats/test` | 390 | 416 |
| `Configuration/test` | 543, 2 skipped | 639, 2 skipped |

## What Was Done

### 1. One shared, versioned suite

- **Layout.** `src/Platform/Storyteller/conformance/` holds `suite.json` and `schema/`, with `suite.schema.json`, `flattening.schema.json` and `formats.schema.json`; the errors and refresh schemas came in phase 2. It also holds `flattening/flat.json`, `flattening/properties.json`, `flattening/env.json` and `formats.json`. The `README.md` came in phase 4.
- **The manifest** has `name` (`storyteller-configuration`), `version` (`1.0.0`), `description` and `files` with `path` and `kind`. In this phase the `kind` enum of `suite.schema.json` held only `flattening` and `formats`, so the phase was green on its own; phase 2 added `errors` and `refresh`.
- **Schemas.** They are draft-07 and self-contained, as specified.
  - `flattening` and `formats` require exactly one outcome through `oneOf` with `required`.
  - The seven rules of `DocumentRules` are an enum.
  - Paths must start with `$`.
  - Every file has a `$schema` with the relative path of its kind's schema, and a suite test checks that path.
- **Embedding.** Both test projects embed `..\..\conformance\**\*.json` as `Conformance/%(RecursiveDir)%(Filename)%(Extension)`, and the loaders replace `\` with `/`, so the names are the same on Windows and Linux.
  - `ConfigurationFormats/test/VectorFiles.cs` became `ConformanceFiles.cs`, with the manifest paths as constants (`Flat`, `Properties`, `Env`, `Formats`), `OfKind`, `Read`, `Load` and `Names`.
  - `Configuration/test/Conformance/ConformanceFiles.cs` also lists every embedded path and reads the raw text. It reads with `DuplicatePropertyNameHandling.Error`, so a duplicate name fails every test that reads the file.
- **`Configuration/test/Conformance/SuiteTests.cs`** (NJsonSchema, `PackageReference` without a version) checks:
  - the manifest against its schema;
  - that every embedded vector file is listed and every listed file exists;
  - that every kind of the schema has a schema file;
  - every file against the schema of its kind, with its `$schema` path;
  - that every item is named once;
  - one flattening file per target;
  - the study's rules per target (section 3);
  - portability (section 3).
- **Negative cases.** `TheFlatteningSchema_RejectsAnInvalidFile` proves that NJsonSchema enforces the schema, with seven invalid files: both outcomes, neither, an unknown rule, a relative path, a number value, an extra field, and an unknown target. `ThePortabilityRules_RejectWhatJavaScriptReadsDifferently` proves the portability rules with ten documents.
- **Review fix (`2f6f04db`).** The reference had no list of the kinds it runs, so a new kind with a schema and a listed file passed every .NET test unrun. A reviewer reproduced this with a scratch copy of the suite. `EveryKind_IsRunByTheReference` now keeps `RunKinds` (`flattening`, `formats`, `errors`, `refresh`), requires the schema's enum to equal it, and fails on any listed file of another kind.
- **Deviations.**
  - The portability test is named `TheFile_IsReadTheSameByEveryRuntime`, not `EveryDocument_IsPortable`. It walks the whole file, not only `document` and `json`. It also requires array-index names in ascending order, because `JSON.parse` sorts them.
  - `TheSchemasOfErrorsAndRefresh_DefineTheSame` (phase 2) compares `failure` as well as `response`.

### 2. The target dimension and the migration

- **Moves.** `git mv` moved `flattening.json` to `flattening/flat.json`, `flattening.properties.json` to `flattening/properties.json`, `flattening.env.json` to `flattening/env.json` and `formats.json` to `formats.json`. The old folder is gone.
- **File-level fields.** A script inserted `$schema` and `target` (or only `$schema` for `formats.json`) after the opening brace. The descriptions changed only where they named another file (`flattening.json` became `flattening/flat.json`, and so on).
- **The vectors are unchanged.** A one-off check compared the text of each `vectors` array before and after the move, with the one new vector removed. The text was identical for all four files: 20, 32, 33 and 59 vectors. The arrays then gained only the new vectors of this change:
  - `numbers that are not safe integers are their double text`, in phase 1;
  - four case-fold vectors in every target, and `an unpaired surrogate becomes U+FFFD` in `properties` and `env`, in `8286bec5`.

  The issue's "21" counted three `"name"` properties inside documents; `main` has 18 vectors and 424-104 added 2.
- **Targets.** They are `flat`, `properties` and `env`, the names of `?format=`.
- **Runners.**
  - `FlatteningVectorTests` gained `EveryFlatteningFile_HasATargetOfThisRunner`, which fails on a target the runner does not implement and maps each target to its file.
  - `FormatVectorTests` gained `TheSuite_HasOneFormatsFile`.
  - **Deviation.** The spec moved the `properties` and `env` key vectors into `FlatteningVectorTests`. They stay in `FormatVectorTests` (`PropertiesVector`, `EnvVector`), which already reads the formatted text back through the test readers. The behaviour is the same, and the move would only have shuffled code.
  - **Review fix (`2f6f04db`).** `Vector_AsAFlatBody` compares the parsed body with the vector's keys in order. Before, it ignored order, while the docs, the schema and the TypeScript runner treat the order as part of the expectation. `Vector` keeps comparing `DocumentFlattener.Flatten` as a dictionary.
- **The library** changed only two comments that named the old folder (`DocumentRules.cs`, `DocumentFlattener.cs`).

### 3. Coverage of the five rules, and portability

- **Coverage.** `EveryTarget_CoversTheRulesOfTheStudy` requires these in every flattening file:
  - the vectors `numbers are their JSON text`, `booleans are lower case`, `null makes no key` and `empty objects and arrays make no key`;
  - an error vector for `case-collision`;
  - an error vector for the target's delimiter rule (`colon-in-name`, `bracket-in-name` or `invalid-env-name`).

  **Review fix (`2f6f04db`).** The named vectors must now be key vectors. The spec's table also lists `array items keep their index, also after a null` for `flat`; the test does not require it, because that row is covered by `null makes no key` in every target.
- **Portability.** `FindUnportable` rejects:
  - an integer beyond ±(2^53 − 1) (an Int64 beyond it, or a BigInteger);
  - a float of magnitude 2^53 or less that is integral (`1.0`, `1E+2`, `-0.0`);
  - an object whose names JavaScript would reorder.

  A duplicate name fails while the file is read.
- **Numbers.** Scratch runs of Newtonsoft 13 (`JValue.ToString(Formatting.None)` after a read with `FloatParseHandling.Double`) settled the notation:
  - the shortest round-trip digits;
  - fixed notation while the decimal exponent position is between -3 and 17 (`1e16` gives `10000000000000000.0`, `1e17` gives `1E+17`, `0.0001` gives `0.0001`, `0.00001` gives `1E-05`);
  - `.0` on an integral double;
  - `E`, a sign and two or more exponent digits.

  The vector `numbers that are not safe integers are their double text` pins `1E+20`, `1.7976931348623157E+308`, `5E-324`, `1.5E-07`, `0.0001`, `1E-05`, `123456.789`, `1000000000000000.5`, `0.30000000000000004` and `±9007199254740991` in all three targets, and the .NET runners confirmed every expectation.
- **Review fix: case.** `8286bec5` adds four vectors to every target:
  - `a dotless i and an I are different names`;
  - `a long s and an s are different names`;
  - `letters beyond the Basic Multilingual Plane that differ only in case collide`;
  - `Greek letters with ypogegrammeni that differ only in case collide`.

  The .NET reference confirmed them. In `env`, the dotless i and the long s are `invalid-env-name`; in `properties` they take the bracket form.
- **Review fix: surrogates.** `properties` and `env` gain `an unpaired surrogate becomes U+FFFD`.
  - A reviewer claimed that Newtonsoft reads an escaped unpaired surrogate as U+FFFD. A probe showed it keeps it, as `JSON.parse` does, so no portability rule was needed.
  - The difference was that the formatter and `ToEnvironmentVariables` canonicalize first, and `DocumentFlattener.Flatten` does not. The vector pins that for the two targets the formatter serves.
