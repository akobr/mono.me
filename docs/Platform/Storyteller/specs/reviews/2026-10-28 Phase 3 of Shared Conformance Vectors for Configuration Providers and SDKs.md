# Phase 3 of Shared Conformance Vectors for Configuration Providers and SDKs

Linear: [424-105](https://linear.app/424fun/issue/424-105/shared-conformance-vectors-for-configuration-providers-and-sdks-c22).

## Overview

Phase 3 covers section 6 of [Shared Conformance Vectors for Configuration Providers and SDKs](../2026-10-28%20Shared%20Conformance%20Vectors%20for%20Configuration%20Providers%20and%20SDKs.md).

- The TypeScript SDK gained `lib/configuration-formats.ts`, a port of the .NET formats library's flattening, paths, rules, number text, and `flat` and `env` text.
- It also gained `isTransientError`, and the .NET treatment of a blank `ErrorCode`.
- Vitest runners in `test/conformance` run the shared flattening, format and error vectors.
- The review compared the port with the .NET library on thousands of random documents and found two differences. The case fold worked on UTF-16 units instead of code points, and `properties` and `env` did not read the canonical document. Both are fixed, and the fold now matches .NET 10 on every code point.

| Commit | Content |
|---|---|
| `c32cd386` | `configuration-formats.ts`, `isTransientError`, the `ErrorCode` alignment, the runners, `prepublishOnly` |
| `8286bec5` | Review fix: the case fold of .NET, the canonical document for `properties` and `env`, and the vectors that pin both |

| Package | Result (end of the phase) | Result (end of the branch) |
|---|---|---|
| `sdk.typescript` | typecheck, lint, 308 passed and 1 skipped (smoke), build | typecheck, lint, 322 passed and 1 skipped |

## What Was Done

### 6. The TypeScript SDK

#### `lib/configuration-formats.ts`

It is exported from `index.ts` and has no `node:*` import.

- **Types.** `ConfigurationTarget`, `DocumentRule` and `InvalidDocumentError` (`rule`, `path`).
- **`flattenConfiguration(content, target = 'flat')`** returns a null-prototype record in document order.
- **`formatConfiguration(content, 'flat' | 'env')`** canonicalizes first and returns the text.
- **The port**, from the .NET library:
  - the base walk with names checked before values;
  - the later leaf reported on a collision;
  - the JSON paths of `JsonPaths`;
  - `bracket-in-name`, then `array-gap`, for `properties`;
  - the charset and the root's leading-letter rule for `env`;
  - the bracket form of `properties` keys;
  - Newtonsoft's escaping of `flat`, which is `JSON.stringify`'s with U+0085, U+2028 and U+2029 escaped too;
  - the quoting of `env`.
- **Numbers.** A safe integer is its digits. Any other number is written in .NET's double notation, from the shortest digits of `toExponential()`. It passes the number vectors, `1E+20` in `formats.json` included.
- **Deviations.**
  - The canonical reading replaces unpaired surrogates with a regular expression instead of `String.prototype.toWellFormed`, which the package's ES2022 `lib` does not type.
  - `ConfigurationTarget` is `Extract<ConfigurationFormat, 'flat' | 'properties' | 'env'>`, the same union.
  - A `bigint` is accepted as its digits.
  - A plain object is one whose prototype is `Object.prototype` or `null`. A nested `Date` or `Map` throws `TypeError`, and a root one is `not-an-object`.

#### Review fix: case (`8286bec5`)

`foldCase` upper-cased each UTF-16 unit. An exhaustive comparison with .NET 10's `OrdinalIgnoreCase` over every code point found 316 code points in a different class.

- The pairs beyond the Basic Multilingual Plane did not fold, nor did the Greek letters with ypogegrammeni.
- The dotless `ı` and the long `ſ` folded to `I` and `S`, which .NET keeps.

So `{"I": 1, "ı": 2}` was a collision in TypeScript and valid in .NET, and `{"𐐀": 1, "𐐨": 2}` the other way round.

The fold now works per code point:

- it keeps `toUpperCase` when the result is one code point;
- it keeps the case of U+0131, U+017F and the Unicode 16 pairs, which .NET 10's table does not map;
- it maps U+1F80-1F87, U+1F90-1F97 and U+1FA0-1FA7 to +8, and U+1FB3, U+1FC3 and U+1FF3 to +9.

The comparison runs a .NET dump of every class, through a `Dictionary` with `OrdinalIgnoreCase`, against the fold's source extracted from the module. It now finds 0 differences, and the old fold gives 316. The four case vectors of phase 1 pin the change for every runtime.

#### Review fix: surrogates (`8286bec5`)

`properties` and `env` now read the canonical document, as `ConfigurationFormatter.Format` and `ToEnvironmentVariables` do, so an unpaired surrogate becomes U+FFFD before the rules. `flat` keeps the document as it is, as `DocumentFlattener.Flatten` does. The vector `an unpaired surrogate becomes U+FFFD` pins it.

#### `lib/errors.ts`

- **`isTransientError`** is true for a network failure (status 0 without `aborted`), 408, 424, 429, a 5xx other than 501 and 505, and a 2xx. It is false for an abort, any other status, and any error that is not a `StorytellerError`.
- **`fromResponse`** treats an `ErrorCode` that is blank after trimming as no code.
- **Deviation.** A blank `ErrorCode` still falls back to the OAuth `error` field, as an absent one does, so a token endpoint's code survives.

#### Runners

- **`conformance.ts`** is a helper, not a test. It reads `suite.json` and the files with `node:fs` from `new URL('../../../../conformance/', import.meta.url)`, and turns a vector response into a `Response`.
- **`suite.test.ts`** has a `run` set and a `notApplicable` map, with `refresh` and the reason. An unknown kind fails, and the suite version is in the name of the describe block.
- **`flattening.test.ts`** runs one test per (file, vector) and fails on an unknown target.
  - **Deviation.** It compares `Object.entries` in order, not as a map, which is stricter than the spec. The review made the .NET flat runner compare in order too (phase 1), so both runtimes check the order the schema documents.
- **`formats.test.ts`** runs the `flat` and `env` vectors. A test named after the count asserts that the vectors left to .NET are exactly the `properties` and `yaml` ones.
- **`errors.test.ts`** uses `configureStoryteller` with a fake `fetch` that answers every request, then the conditional read of the mode. It compares `{ status, errorCode ?? null, transient: isTransientError(error) }` and checks the request paths.
- **`test/configuration-formats.test.ts`** covers what the vectors cannot hold: `null` and `undefined` content, the default target, a `__proto__` name, `undefined` values, values that are not JSON data, a message without the value, and `isTransientError` for a network failure, an abort, other errors and a 2xx.

#### The package

`prepublishOnly` is `npm run typecheck && npm test && npm run build`.

#### Notes

- **Raw characters.** The editor that wrote the module turned the escapes `\u2028`, `\u2029` and `\uFFFD` in the source into raw characters, which broke the regular expression of the `flat` escaping. A script restored them, and every changed file was then checked for raw U+0085, U+2028, U+2029 and U+FFFD. The same happened to `\u0023` in a comment (fixed in `8286bec5`), and to the spec's text (phase 4).
- **Not changed.** The key/value helpers (`flattenJson`, `toFlatJson`, `parseDotEnv`, `serializeDotEnv`) are unchanged.
