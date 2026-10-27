# Phase 2 of Flat Output Formats for Configurations

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

Phase 2 covers sections 5 and 6 of [Flat Output Formats for Configurations](../2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md) and the phase 2 docs of section 11. The effective and the resolved configuration reads take `?format=flat|properties|env|yaml` and answer the `Content` alone, formatted by `ConfigurationFormatter`, with the media type of the format and with entity tags and dependency manifests of their own. The envelope keeps its bytes and tags. The final review of the branch found that the envelope itself could not be written for content nested 32 levels or deeper, which `?format=` served; that is fixed here and in the SDK (phase 3).

| Commit | Content |
|---|---|
| `85e5691e` | `?format=` on both reads, the tags per format, the OpenAPI document and the docs (sections 5, 6) |
| `9eeed7ad` | Review fix: the docs say what a formatted 304 still costs and who asks for a format |
| `3a7093eb` | Final review fix: the envelope of deeply nested content is written |

| Project | Passed (end of the branch) |
|---|---|
| `Api.Functions/test` | 2305 |
| `Backend.Core/test` | 898 |

## What Was Done

### 5. `?format=` on the API

- `ConfigurationHttp.ParseFormat` reads `request.Query["format"]` after the key is parsed: absent, empty and `json` in any ASCII casing are the envelope; `flat`, `properties`, `env` and `yaml` in any casing a format; anything else, a repeat joined with a comma included, throws `InvalidInputException` with `ErrorCodes.InvalidQuery` and "The query parameter 'format' must be json, flat, properties, env or yaml." before anything is read or any header is written.
- `TryFormat` formats the effective or the resolved content. An `InvalidDocumentException` becomes a 422 `ObjectResult` with `ErrorCode` `format.<rule>` and `Details` `{ Format, Rule, Path }`; nothing else is set: no `ETag`, `Cache-Control`, withheld header or manifest. **Addition:** the handler logs the 422 as a warning with the key, the format, the rule and the path, never a value.
- The effective read formats before `MarkNoCache`. The resolved read computes its tags, looks for an unchanged manifest (a 304 from a manifest needs no formatting), resolves, then formats; the manifest is created after the resolve but recorded only after the format succeeded, and `SetResolvedHeaders` runs last. `If-None-Match: *` on a document a format cannot write is a 422.
- `ConditionalRequestExtensions.OkOrNotModified(HttpRequestData, FormattedConfiguration, EntityTagHeaderValue)` answers 304, or a `ContentResult` whose `ContentType` is the media type of the body.
- **OpenAPI.** `format` is declared after the path parameters and before `If-None-Match` (`Definitions.Descriptions.ConfigurationFormat`). The new `ConfigurationFormatResponseDocumentFilter`, registered after `DiffUnifiedResponseDocumentFilter`, adds `text/plain` and `application/yaml` (`Definitions.ContentTypes.Yaml`) to the 200 of both reads; the 200 descriptions say that `flat` is a JSON object of strings. The effective read declares a 422 (`ResponseConfigurationFormatInvalid`), and the 422 of the resolved read names `binding.*` and `format.<rule>` (`ResponseResolvedConfigurationInvalid`).
- **Tests.** The new `ConfigurationFormatReadTests` covers each format on both reads (the body equal to the client's formatting of the envelope, media types, tags, `Cache-Control`, the withheld header, `no-store`), the casing and the envelope values, a 304 per format, tags that do not cross formats, the 400 and the 422 without headers or manifest, the manifest of a format answering only that format, a binding that produces a colon in a name, and the document. The document theory of `ConfigurationConditionalReadTests` takes the expected parameter list.
- **Final review fix (`3a7093eb`): depth.** The MVC options of the API write JSON with a depth limit of 32, and `JObjectConverter` wrote `Content` straight into that writer, so the read of the envelope of content nested 32 levels or deeper answered 500, while `?format=` served the same document (`Canonicalize` reads without a limit) and the writes accept up to 64 levels. `sform`, which formats from the envelope, could not read such a document at all. `JObjectConverter.Write` now writes the object with a writer of its own, with the encoder and indentation settings of the enclosing writer, and splices it in with `WriteRawValue`, so the object counts as one level of the enclosing writer. Its own limit is `JObjectConverter.MaxDepth` = 128: storage reads 64 levels, and a binding result of up to 64 levels can sit inside a resolved document. The limits for request bodies did not change, and a compact writer writes the bytes it wrote before (`Backend.Core/test/JObjectConverterTests`). `ConfigurationFormatReadTests` checks that the envelope of a 40- and a 100-level document, effective and resolved, formats on the client to the body of `?format=`; the test fails without the fix. **Deviation from the finding:** the finding proposed raising `MaxDepth` of the API's JSON options to 128; that would also have accepted request bodies of 33 to 128 levels on every endpoint, where `JObjectConverter.Read` (Newtonsoft's 64 levels) would then have answered 500 instead of 400, so the limit stays on the converter.

### 6. Entity tags per format

- `ConfigurationEntityTags.ForEffective`, `ForResolved` and `ForResolvedBindings` take an optional `representation`, appended after `Content` only when set (never `Append(null)`), so every tag of the envelope stays byte-identical; `ConfigurationFormatReadTests` pins the old tags of the envelope.
- `RepresentationOf(format)` is `"<name>/<ConfigurationFormatter.Version>"`, from the canonical name, so `?format=ENV` and `?format=env` share a tag and a change of the formatted bytes changes every formatted tag.
- `d` tags hash the `r` tag and vary by format without a change to `ForDependencies`; `DependencyManifest` gets no `Format`.

### 11. Documentation (phase 2)

`configuration-formats.md` gained the API section; `inheritance.md` "Other reads", "The tags", "The dependency manifest" (per format, billing and audit), "Headers" and "Limits"; `binding.md` the 422 list of the resolved read; `docs/42for.net/platform/using-the-platform.md` `?format=` and the ETag per format. `9eeed7ad` corrected two statements: a read with `?format=` formats before it compares the tag, so its 304 without bindings or with volatile bindings still pays for the formatting (only a 304 from a manifest skips it), and the formats are for consumers that take flat keys or another file format. The final review fix rewrote the "Depth" note of `configuration-formats.md`, which had placed the limit at Newtonsoft's 64 levels of a client, while the API's own envelope failed from 32.
