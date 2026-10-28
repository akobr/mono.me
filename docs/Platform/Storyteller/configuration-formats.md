# Configuration Formats

A configuration is one JSON document, but most consumers take flat keys: .NET `IConfiguration` (`db:hosts:0`), Spring Boot (`db.hosts[0]`), environment variables and `.env` files (`DB__HOSTS__0`), Helm values (YAML). The library `42.Platform.Storyteller.ConfigurationFormats` renders the `Content` object of a configuration in four flat formats, `flat`, `properties`, `env` and `yaml`, with one set of rules. The [.NET configuration provider](configuration-provider.md) takes its `IConfiguration` keys from the same library, so the `flat` keys are exactly the keys the provider applies, and every format renders the same values. The API serves the four formats on the effective and the resolved read with `?format=` (see [The API](#the-api)).

The code is in `src/Platform/Storyteller/ConfigurationFormats/src/`: the flattener and its rules (`DocumentFlattener`, `DocumentRules`, `JsonPaths`, `InvalidDocumentException`), the formatter (`ConfigurationFormatter`, `ConfigurationFormatNames`) and one renderer per format (`Rendering/`). The design and its decisions are in [the spec](specs/2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md).

## The library

```csharp
using _42.Platform.Storyteller.ConfigurationFormats;

// The whole body, or an InvalidDocumentException that names the rule and the JSON path.
var body = ConfigurationFormatter.Format(content, ConfigurationFormat.Env);

// UTF-16 text with LF line ends; write it as UTF-8 without a byte order mark.
await File.WriteAllTextAsync(".env", body.Text, new UTF8Encoding(false));
```

| Member | Does |
| --- | --- |
| `ConfigurationFormatter.Format(content, format, environmentPrefix)` | The complete text, its format and its media type (`FormattedConfiguration`) |
| `ConfigurationFormatter.ToEnvironmentVariables(content, environmentPrefix)` | The `env` names and the raw values, kinds and JSON paths, without the quoting of a `.env` file |
| `ConfigurationFormatter.Canonicalize(content)` | The document as a client reads it, see [One reading of the document](#one-reading-of-the-document) |
| `ConfigurationFormatter.Version` | `1`, changed whenever the bytes of any format change |
| `ConfigurationFormatNames` | `Names` (`flat`, `properties`, `env`, `yaml`), `TryParse` (exact names in any ASCII casing, no trimming, no numbers), `GetName` and `GetMediaType` |
| `DocumentFlattener.Read(content, validate)` | The values of the document in document order: the steps to each value, its text, its JSON type and its JSON path (`DocumentLeaf`). With `validate: false` no rule applies |
| `DocumentFlattener.Flatten(content)` | The `IConfiguration` keys, case-insensitive; what the provider applies |
| `InvalidDocumentException` | `Rule` (one of `DocumentRules`) and `Path`; the message names the path, never a value |

- The namespace is not `…Storyteller.Configuration` or `…Storyteller.Formatting`: the first is the `Configuration` model of `Abstractions.Annotations`, and a namespace `Formatting` directly under `_42.Platform.Storyteller` would hide Newtonsoft's `Formatting` from all code in `_42.Platform.Storyteller.*`.
- The only dependency is `Newtonsoft.Json`. The YAML writer is part of the library; no YAML parser reaches the applications of the provider.
- `ToString()` of the results prints names, paths and lengths, never a value.

## The API

The effective and the resolved read of a configuration take the format as a query parameter:

```text
GET /v1/{organization}/{project}/{view}/configuration/{key}?format=env
GET /v1/{organization}/{project}/{view}/configuration/{key}/resolved?format=yaml
```

| `format` | Answer |
| --- | --- |
| Absent, empty (`?format=`) or `json`, in any casing | The `Configuration` model, exactly as without the parameter: the same body, tags, headers and manifests |
| `flat`, `properties`, `env` or `yaml`, in any casing | `Content` alone in that format, without the envelope (no `Version`, `Hash` or `Modified`), with the media type of the format |
| Anything else, also a repeated parameter (`?format=flat&format=yaml`) or a list (`?format=flat,yaml`) | `400` with `ErrorCode` `InvalidQuery`: "The query parameter 'format' must be json, flat, properties, env or yaml." |

- **The body** is the text of `ConfigurationFormatter.Format` over the effective or the resolved `Content`: the same bytes a client gets when it formats the `Content` of the envelope with the library (see [One reading of the document](#one-reading-of-the-document)). `flat` is `application/json`; read it as a JSON object of strings, not as a `Configuration`.
- **Only the query decides.** `Accept` is not considered, so a response needs no `Vary`. The composition read and the read of a version take no `format`.
- **Format errors** are `422 Unprocessable Content` with an `ErrorResponse`. `ErrorCode` is `format.<rule>` (`format.not-an-object`, `format.empty-name`, `format.colon-in-name`, `format.case-collision`, `format.bracket-in-name`, `format.array-gap` or `format.invalid-env-name`), `Message` names the JSON path, never a value, and `Details` hold `Format`, `Rule` and `Path`:

  ```json
  {
    "Message": "The name of the property at $.db['pass word'] cannot be part of an environment variable name: use ASCII letters, digits, '.' and '-' with single '_' between them, and start the names of the root with a letter.",
    "ErrorCode": "format.invalid-env-name",
    "Details": { "Format": "env", "Rule": "invalid-env-name", "Path": "$.db['pass word']" }
  }
  ```

  A 422 carries no `ETag`, `Cache-Control` or `X-2S-Secrets-Withheld`. The document is formatted before the tags are compared, so `If-None-Match: *` on a document the format cannot represent is a 422, not a 304. The resolved read also answers 422 for a binding that cannot be evaluated (`binding.evaluation`, `binding.limit.<kind>`, see [binding.md](binding.md#errors)).
- **Bindings.** An effective document with object bindings or JSON-e is refused by `env` for the `$` of `$binding` or `$if`, and its `@` expressions are written as strings in every format. Read the resolved document to export a configuration.
- **Tags.** Every format has its own `ETag`. The tag hashes the canonical name of the format and `ConfigurationFormatter.Version` (`env/1`) after the inputs of the envelope's tag, so `?format=ENV` and `?format=env` share a tag, the tag of one format never answers another with 304, and a release that changes the bytes of a format changes its tags at once. The tags of the envelope are the same as before the formats. See [the tags](inheritance.md#the-tags).
- **Manifests.** The `r` tag of a formatted resolved read is per format, and the `d` tag hashes it, so each format a caller polls holds its own [dependency manifest](inheritance.md#the-dependency-manifest), within `ConditionalReads:ManifestCacheSizeLimit`. A 304 from a manifest is answered without evaluating or formatting; a manifest is recorded only after its body was formatted, and the formatter is deterministic for a given `Version`.
- **Billing and audit.** A format error after a resolve is a resolved read like any other: it records `reads.binding` and `reveals` and counts in the hourly `BindingRead`. A 422 carries no tag and records no manifest, so a poller that repeats it is evaluated, metered and audited on every request. Fix the document, or read another format.
- **SDKs.** The generated reads of the SDKs return the envelope: the TypeScript ones take no `format`, and the C# ones take it but cannot read a formatted body. A format is read as text, with its `ETag`, by `GetConfigurationFormattedConditionalAsync` and `GetResolvedConfigurationFormattedConditionalAsync` of the C# SDK ([readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md#formatted-reads)) and by `getConfigurationFormattedConditional` and `getResolvedConfigurationFormattedConditional` of the TypeScript SDK ([readme](../../../src/Platform/Storyteller/sdk.typescript/README.md#formatted-reads)).
- **sform.** `sform story config <key> --format env` reads the envelope through the conditional reads and formats its `Content` with the library, so its text equals the body of `?format=`, also for `--version` and against an API without the parameter. `--github-env` exports a resolved configuration into a GitHub Actions job, after masking the values bindings produced; see [Read a configuration](https://42for.net/cli/sform#read-a-configuration).

## The formats

| Format | Media type | For |
| --- | --- | --- |
| `flat` | `application/json; charset=utf-8` | .NET `IConfiguration`, any JSON reader |
| `properties` | `text/plain; charset=utf-8` | Spring Boot `.properties` |
| `env` | `text/plain; charset=utf-8` | `.env` files, and the variables of a process |
| `yaml` | `application/yaml; charset=utf-8` | Helm values, Spring `application.yml`, any YAML 1.2 reader |

The document `{"db":{"hosts":["a","b"],"port":5432},"features":["vat"]}` as `flat`, `properties` and `env` (the `yaml` is in [its section](#yaml)):

```json
{
  "db:hosts:0": "a",
  "db:hosts:1": "b",
  "db:port": "5432",
  "features:0": "vat"
}
```

```properties
db.hosts[0]=a
db.hosts[1]=b
db.port=5432
features[0]=vat
```

```sh
DB__HOSTS__0=a
DB__HOSTS__1=b
DB__PORT=5432
FEATURES__0=vat
```

Every format writes LF line ends, no byte order mark and the keys in document order, without sorting, and is deterministic. Every non-empty text ends with a line end. Where the library writes `\uXXXX` itself (paths, `properties`, `yaml`), the hex digits are upper-case.

## Rules shared by every format

A document is checked in three steps, each over the whole document, and the first error is reported:

1. **The base rules**, those of the provider, whatever the format's own delimiter is: a document is valid or invalid for the provider, and a format adds rules but never removes one.
2. **The name rule of the format** (`properties`, `env`), over every property name in document order, also of a `null` or an empty container.
3. **The value rule of the format** (`properties`).

| Rule | Formats | Invalid when |
| --- | --- | --- |
| `not-an-object` | all | The content is not a JSON object. A `null` content, which a version-0 configuration may have, is an empty document |
| `empty-name` | all | A property name is empty |
| `colon-in-name` | all | A property name contains `:`, the separator of `IConfiguration` keys |
| `case-collision` | all | Two keys differ only in case (`IConfiguration` compares keys case-insensitively). Sections that differ only in case merge, and a value may share a name with a section |
| `bracket-in-name` | `properties` | A property name contains `[` or `]` |
| `array-gap` | `properties` | An array item without a value is followed by an item with a value |
| `invalid-env-name` | `env` | A property name is outside the variable charset, see [`env`](#env) |

- **Values.** A string is its value. A number or a boolean is its JSON text: `5432`, `1.5`, `true`. `yaml` writes them without quotes, the other formats as text.
- **No null.** A `null` property, an empty object and an empty array make no key, in every format. The one exception is a gap in a `yaml` array, written `null` so that every other item keeps its index.
- **Names** keep their casing; `env` upper-cases them.
- **No partial output.** The formatter builds the complete text before it returns. On an error it throws and returns nothing.
- **Errors** name the rule and the JSON path, never a value: `$` is the root, `.name` a name of ASCII letters, digits, `_` and `-` that starts with an ASCII letter or `_`, `['name']` any other name, `[0]` an array item. In a bracketed name, `\` and `'` are escaped with a backslash, the C0 control characters (U+0000 to U+001F) and DEL are written as `\n`, `\r`, `\t` or `\uXXXX` with upper-case hex digits, and `#` as `\u0023`; every other character, C1 controls and U+2028 and U+2029 included, is kept. A path therefore never holds a CR, an LF or a `#`, so a crafted name cannot start a GitHub workflow command in a message, a log line or an error output: neither on a new line (`::command::`) nor as the `##[command]` of the runner's older syntax, which the runner finds anywhere in a line, on standard output and standard error alike. `sform` writes every `#` of its messages on standard error as `\u0023` too, also in the messages of an API whose paths keep `#`.

## One reading of the document

The formatter formats `Canonicalize(content)`: the token written as the API writes `Content` (`ToString(Formatting.None)`) and read back with the settings of the SDK (`DateParseHandling.None`, `FloatParseHandling.Double`), with every unpaired UTF-16 surrogate in names and strings replaced by U+FFFD, as UTF-8 encoding makes it anyway and as YAML readers require.

That is the document a client sees in the envelope. The server holds date tokens and decimals where a client holds strings and doubles; after canonicalization a server token of `2026-10-20T12:00:00.123+02:00` and a decimal `1.50` format as the client's `2026-10-20T12:00:00.123+02:00` and `1.5`. A document therefore formats to the same bytes on the server and in a client. `DocumentFlattener.Flatten` does not canonicalize: the provider already reads with these settings.

## `flat`

The `IConfiguration` keys and values as a JSON object of strings, indented by two spaces, followed by one LF. An empty document is `{}`.

- Escaping is Newtonsoft's default: `\"`, `\\`, `\b`, `\t`, `\n`, `\f`, `\r`, every other C0 character as `\u00xx` (lower-case hex), and U+0085, U+2028 and U+2029; DEL, the other C1 characters and everything else are written raw.
- It passes `flattening.json`: the library's tests parse the body of every vector and compare its keys, or the error.

## `properties`

Spring Boot `.properties`, one `key=value` line per value; an empty document is an empty text.

- **Keys.** The first name, then `.name` per property and `[i]` per array item: `db.hosts[0]`, `list[3].name`, `list[4][0]`. A name with any other character than ASCII letters, digits, `-` and `_` is written in Spring's bracket form, `.[name]`, or `[name]` as the first step: `Logging.LogLevel.[Microsoft.AspNetCore]`, `[c d]`, `x.[@at]`. Spring Boot binds a bracketed name with every character kept, and drops the other characters from an unbracketed map key. The encoding is lossless, so distinct paths give distinct keys.
- **`bracket-in-name`.** A name with `[` or `]` cannot be written in the bracket form and is refused.
- **`array-gap`.** Spring Boot does not bind a list with a missing index, so an item without a value (`null`, or a container without any value) that a later item with a value follows is refused, at the path of the first such item. A trailing item without a value is left out.
- **Escaping.** The text is ASCII, as `java.util.Properties.load(InputStream)` reads ISO-8859-1. In keys `\`, space, `=`, `:`, `#` and `!` are escaped with a backslash; in values `\` and a leading space; in both `\t`, `\n`, `\r` and `\f` are escapes, and every other character below U+0020 or above U+007E is `\uXXXX` per UTF-16 code unit.
- **Other readers.** MicroProfile Config and Quarkus quote dotted segments instead and read `[…]` as an index, so they read only documents whose names are made of `[A-Za-z0-9_-]`.

## `env`

A `.env` file, one `NAME=value` line per value; an empty document is an empty text. Docker Compose, python-dotenv and pydantic-settings, and the key/value import of `sform` read the file; .NET, config-rs and others read the variables once they are in the environment of a process.

- **Names.** The steps upper-cased (`ToUpperInvariant`) and joined with `__`, an array item by its index: `DB__HOSTS__0`, as the key/value `.env` export does.
- **`invalid-env-name`.** A property name must match `[A-Za-z0-9.-]+(_[A-Za-z0-9.-]+)*`: ASCII letters, digits, `.` and `-`, with a single `_` only between them, so `__` stays the only delimiter and `a_:b` cannot meet `a:_b`. A name of the root object must also start with an ASCII letter; a nested `0b` is valid (`A__0B`). With this charset the base rule `case-collision` already makes the names unique.
  - It is Kubernetes' charset; `.` and `-` are kept because .NET configurations routinely contain names such as `Microsoft.AspNetCore`. A POSIX shell cannot reference such a variable (`$MICROSOFT.ASPNETCORE` is not one name); a process reads it from its environment.
  - An effective document with object bindings or JSON-e (`$binding`, `$definition`, `$if`, …) is refused for the `$`. The resolved document, where bindings are evaluated, is the one to export.
- **Values,** in the dotenv dialect of the key/value export, extended so that common readers take them literally:

  | Value | Written |
  | --- | --- |
  | Empty | `NAME=` |
  | Only `[A-Za-z0-9_.,:/@%+-]` | Unquoted: `DB__HOST=db.local:5432` |
  | Without `'`, `\`, CR or LF | Single-quoted, literal for bash, Docker Compose, python-dotenv and the key/value import: `URL='https://db.local/x?y=1'` |
  | Anything else | Double-quoted, with `\\`, `\"`, `\n`, `\r` and `\t` in this order: `NOTE="it's\nnew"` |

  - `$` is never escaped. The file is a dotenv file, not a shell script: do not `source` it. Readers that interpolate may expand `$` inside double quotes.
  - `docker run --env-file`, `kubectl create … --from-env-file` and kustomize take every value verbatim, quotes included, so only unquoted values survive them.
- **Prefix.** `environmentPrefix` (only with `env` and `ToEnvironmentVariables`; an `ArgumentException` otherwise) is prepended to every name, upper-cased and without a separator: `APP_` gives `APP_DB__HOST`, as .NET `AddEnvironmentVariables("APP_")` expects. It must match `[A-Za-z][A-Za-z0-9_]*`. The name rule applies to the names without the prefix, so a document is valid or not whatever the prefix is.

## `yaml`

YAML 1.2, written by the library from the values of the document, without a YAML library:

```yaml
db:
  hosts:
    - "a"
    - "b"
  port: 5432
features:
  - "vat"
```

- **Structure.** The nested document in document order, two-space indentation, block sequences indented under their key, a mapping in a sequence written compactly (`- name: "w"`, its other keys aligned under the first), a sequence in a sequence as `- - "x"`. An empty document is `{}`.
- **Keys** are plain when they match `[A-Za-z_][A-Za-z0-9_-]*` and are none of `y`, `yes`, `n`, `no`, `true`, `false`, `on`, `off` and `null` in any casing, which YAML 1.1 readers take for a boolean or null; otherwise they are double-quoted like strings. A key whose written form is longer than 1024 characters, YAML's limit for an implicit key, is written as an explicit key: `? key` on its own line, then `:` and the value at the same indentation.
- **Strings** are always double-quoted, with `\"` and `\\`, `\n`, `\r`, `\t`, and `\uXXXX` for every other character YAML does not print raw (C0 controls, DEL, U+0080 to U+009F, U+2028, U+2029, U+FFFE, U+FFFF) and for the byte order mark, U+FEFF. Other characters, also outside the Basic Multilingual Plane, are written raw. `"True"`, `"05432"`, `"2026-10-20"`, `"@db.host"` and `""` stay strings.
- **Numbers and booleans** are plain, as their JSON text. `1E+20` is a float in YAML 1.2 and in SnakeYAML (Spring), which binds it to a string as `1.0E20`; strict YAML 1.1 readers such as PyYAML read it as a string. Bind numbers to numeric types.
- **Nulls and gaps.** A `null` property and a container without any value are left out, as in the other formats. A gap in an array (an item without a value that a later item with a value follows) is written `null`, so that the other items keep the indexes they have in `flat` and `env`; Spring binds it as an empty string. A trailing item without a value is left out, and so is an array without any value. Flattening the parsed YAML gives exactly the `flat` keys; the library's tests parse every output with YamlDotNet to check it.
- Spring splits a YAML key at its dots, so a name with dots survives only into a map.

## Test vectors

Four files of the [conformance suite](conformance.md), in `src/Platform/Storyteller/conformance/`, pin the rules in a shape that an implementation in another language can reuse. Each has a `$schema` and a `description` and `vectors`, every vector a `name` and a `document`, the `Content` object; a flattening file also names its `target`. The library's tests (`ConfigurationFormats/test`) embed and run all of them, read as the SDK reads a body (date-like strings stay strings, numbers are doubles).

| File | Target | Vectors | Each vector has |
| --- | --- | --- | --- |
| `flattening/flat.json` | `flat` | The `IConfiguration` keys of the provider and of `flat` | `keys`, or `error` with `rule` and `path` |
| `flattening/properties.json` | `properties` | Every document of `flat.json` and more, as `properties` keys | `keys` as a reader returns them (`[c d]`, not `[c\ d]`) and the raw values, or `error` |
| `flattening/env.json` | `env` | Every document of `flat.json` and more, as `env` variables | `keys`, the names with their raw values, or `error` |
| `formats.json` | | The exact text of every format: escaping, quoting, gaps, long keys, `@` expressions | `format` and `text`, or `error` |

## Limits

- **MicroProfile Config and Quarkus** read `properties` only for documents whose names are made of `[A-Za-z0-9_-]`.
- **Shells** cannot reference `env` names with `.` or `-`, and expand `$` in double-quoted values; verbatim readers keep the quotes of quoted values.
- **YAML 1.1 readers** read `1E+20` as a string, and Spring binds a gap as an empty string.
- **A format change is a version change.** `ConfigurationFormatter.Version` changes whenever the bytes of any format change, so that anything derived from the bytes, such as a cache tag, changes with them.
- **A format error is not cached.** A 422 of `?format=` carries no tag, so after a resolve each repeat is evaluated, metered and audited again (see [The API](#the-api)).
- **The key length of YAML** is counted in UTF-16 code units, so a key with characters outside the Basic Multilingual Plane may be written as an explicit key a little before YAML's limit.
- **Depth.** `Canonicalize` reads without Newtonsoft's default limit of 64 levels, so a document formats wherever it is held. The envelope carries the same documents: the API writes `Content` with a limit of 128 levels (`JObjectConverter.MaxDepth`), counted on its own rather than within the 32 levels of the API's JSON options, and the C# SDK reads the responses of the configuration reads with a limit of 256 levels instead of Newtonsoft's 64. Storage reads documents of up to 64 levels, and a binding result of up to 64 levels can sit inside a resolved document, so no document the API holds or resolves reaches these limits. A client that reads the envelope with Newtonsoft's default limit still fails on content deeper than about 63 levels.
