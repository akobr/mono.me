# Flat Output Formats for Configurations

Linear: [424-104 Flat output formats for configurations on the API, sform and a GitHub Action (C5)](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github)

## Problem

A configuration is one JSON document, but most consumers take flat keys: .NET `IConfiguration` (`db:hosts:0`), Spring Boot (`db.hosts[0]`), environment variables and `.env` files (`DB__HOSTS__0`), Helm values (YAML). Study gap **G6**: `sform story config` writes the whole `Configuration` envelope as decorated JSON, the API returns only that envelope, and every consumer flattens the document on its own. Only the .NET provider has a flattener, and it is internal to its package. Capability **C5** (study section 18, Phase A "Consumable") closes the gap in three places:

1. **The API.** `?format=flat|properties|env|yaml` on the effective and the resolved configuration read, with the delimiters of study section 3.3, as a plain body without the `Configuration` envelope.
2. **sform.** `sform story config <key> --resolved --format flat|properties|env|yaml --content-only`, with output that can be piped or redirected.
3. **A GitHub Action** (study section 14.3, `export-env: true`) that writes the flattened variables into the job environment and masks secrets with `::add-mask::`.

### Acceptance criteria

| Criterion of the issue | Where |
|---|---|
| All four formats on the API and in `sform`, identical output for the same document | Sections 1-4 (one library, one canonical reading of the document), 7 |
| Rules of 3.3: numbers and booleans as JSON text, no null, empty containers make no key, a delimiter in a name escaped or rejected, case collisions a validation error with a clear 4xx / CLI error and no partial output | Sections 2-4 (rules), 5 (422), 7 (exit 2). One deliberate exception: `yaml` writes `null` for a gap in an array, so that every other item keeps the index it has in `flat`, `properties` and `env` (section 3) |
| `flat` passes `flattening.json`; `properties` and `env` get vectors (C22) | Section 4 |
| Conditional reads keep working per format; the ETag varies by format | Section 6 |
| Action usable from a workflow, with a non-interactive sign-in (G9) and masked secrets | Sections 7-9. **Partly, pending the product owner:** the action works from this repository with a `sform` packed by the workflow (`cli-source`); installing it from NuGet needs a `sform` release, and a dedicated `@v1` repository needs an owner (section 14). Secrets reach a job only with a certificate machine or a project that does not require mTLS for secret delivery (section 8) |
| OpenAPI and SDKs regenerated; docs updated | Sections 5, 10, 11 |

The inputs to this spec are:

- the issue, its relations (424-96 open as PR #135, which this branch stacks on; 424-85 merged; 424-105, the C22 conformance vectors) and its comments;
- a read-only map of the code, tests and docs at `feat/424-96` `b49c6eb3` (five parallel readers: the flattener and the provider, the configuration reads and conditional reads of `Api.Functions`, both SDKs and their consumers, `sform`, and docs, CI and conventions);
- five adversarial reviews of the first draft against the code (API, formats, SDKs, `sform` and the action, scope and consistency). They ran scratch experiments outside the repository: NSwag 14.6.3 and Orval 8.41.0 against a modified snapshot, Spring Boot 3.3.5 and Java 21 reading `.properties` and YAML, YamlDotNet 16.3.0, `kubectl --from-env-file`, McMaster 5.0.1 parsing, `HttpRequestData.Query`, the Windows console encoding, `dotnet tool install` against nuget.org, MSBuild reading environment variables, and the canonical reading of server tokens;
- the studies `study-usage.md` and `study-evaluation.md` attached to the issue (sections 2.4, 3.1, 3.3, 3.7, 3.12, 12.2, 14.3, 18, 19; evaluation 9.3);
- the CodeRabbit coding plan attached to the issue (`2026-10-26 Flat output formats for configurations on the API.md`), used as a second opinion and a source of ideas, not as the decision. Section 15 lists where this spec differs from it.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The flattener

- **Where.** `Configuration/src/Flattening/DocumentFlattener.cs`, `JsonPaths.cs` and `InvalidDocumentException.cs`, all `internal`, namespace `_42.Platform.Storyteller.Sdk.Configuring.Flattening`, inside the packable provider `42.Platform.Storyteller.Configuration` (which references `Sdk.NSwag` and `Abstractions.Annotations`). `InternalsVisibleTo` grants only the provider's unit tests. Neither `Api.Functions` nor `Cli` references the provider.
- **Behaviour.** `Flatten(JToken?)` walks the document depth-first in document order (`Visit`, `DocumentFlattener.cs:41-93`) and returns a case-insensitive `IReadOnlyDictionary<string, string>`: properties join with `:`, array items with their invariant index, a string is its value, a number or a boolean its Newtonsoft `ToString(Formatting.None)` text, null and empty containers make no key, a null array item leaves a gap (`list:0`, `list:2`). A null or undefined root has no keys; any other non-object root is `not-an-object` at `$`. Every property name is checked before recursing (`empty-name`, `colon-in-name`, also when the value is null). `case-collision` compares whole leaf keys case-insensitively, so sections that differ only in case merge (`Db:Host` with `db:port`) and a value may share a name with a section (`a` with `A:b`); a null leaf takes part in no collision. It throws on the first error; the message names the JSON path, never a value.
- **Paths.** `JsonPaths.Property` writes `.name` for plain names and `['name']` otherwise, escaping only `\` and `'`; control characters reach `Path` and the message unescaped.
- **Scalars depend on how the token was parsed.** The SDK and the provider read with `DateParseHandling.None` and `FloatParseHandling.Double` (`Sdk.NSwag/src/ApiSdk.partial.cs:53-55`). The server does not: Cosmos DB reads `Content` with Newtonsoft defaults (`DateParseHandling.DateTime`), `ConfigurationHttp.cs:259-261` parses writes the same way, and binding results come from `Binding.Object/src/JsonTokenConverter.cs:26-29` with `FloatParseHandling.Decimal`. A date token flattens with `ToString("O")` and a decimal `1.50` stays `1.50`, while a client that reads the same envelope sees the ISO string Newtonsoft wrote and `1.5`.
- **Consumers.** `Engine/ContentApplication.cs` (wraps `InvalidDocumentException` in a non-transient `StorytellerConfigurationException`), `Reading/UnboundBindings.cs` and `References/ReferenceSubstitution.cs` (`JsonPaths`), `test/Caching/LastKnownGoodCacheTests.cs`, `test/Flattening/DocumentFlattenerTests.cs` (one test builds the SDK `Configuration` through `test/TestDocuments.cs`) and `FlatteningVectorTests.cs`. The provider has its own internal `Documents/DocumentEntry`.
- **Vectors.** `Configuration/test/Vectors/flattening.json`, embedded as `Vectors.flattening.json`, has **18 vectors** (11 with `keys`, 7 with `error`). The issue's "21" counts three `"name"` properties inside documents. The runner reads them with the SDK's settings and asserts `(keys == null) != (error == null)` per vector; key order is not asserted. Its `description` documents path escaping as "with `\` and `'` escaped".
- **Namespaces.** `_42.Platform.Storyteller.Configuration` is a type (`Abstractions.Annotations/src/Model/Configuration.cs`) and `_42.Platform.Storyteller.Sdk.Configuration` is the generated DTO, which is why the provider chose `Sdk.Configuring` (424-85 spec section 5). A namespace directly under `_42.Platform.Storyteller` also shadows imported types of the same name for all code in `_42.Platform.Storyteller.*`: a namespace `…Storyteller.Formatting` would break every `Formatting.None` in the API (`ConditionalReads/ConfigurationEntityTags.cs:111`).
- **The key/value formats are a different contract.** `src/Platform/Cli/src/Commands/KeyValues/KeyValueFormats.cs` (and `sdk.typescript/src/lib/key-values.ts`) serialize key/value pairs: `flat` is a JSON object over dotted key/value keys, `env` maps `db.password` to `DB__PASSWORD`, values are double-quoted with `\\ \" \n \r \t` when they match `[\s"'#=\\]`, lines are joined without a trailing newline. Arrays become JSON text and null becomes `""`. 424-96 (its spec section 14) rejected reusing it for configurations. Its `ParseEnv` reads double-quoted escapes and literal single-quoted values.
- **YAML.** No Storyteller runtime project writes YAML. `YamlDotNet` 16.3.0 is in `Directory.Packages.props` and used only by `c0ded0c` and the `Binding.JsonE` tests.

### The configuration reads

- **Handlers.** `Api.Functions/src/V1/ConfigurationHttp.cs`: `GetConfiguration` (`:89-130`, effective) and `GetConfigurationResolved` (`:132-213`, operationId `GetResolvedConfiguration`). Neither reads a query parameter. Both check access, parse the key (400), read the stored model (404), then:
  - effective: `MarkNoCache()` (`:128`), `OkOrNotModified(model, ForEffective(fullKey, model))`;
  - resolved: `ForResolved(…, rights)`; with bindings, `FindUnchangedManifestAsync` may answer 304 without evaluating (`:674-710`), otherwise `ResolveConfigurationAsync` evaluates **in place** (metering `reads.binding`, reveals and the `BindingRead` audit), the tag becomes `b` (volatile) or `d` (dependencies) and the manifest is recorded right away (`:207`); finally `SetResolvedHeaders` (`:211`) and `OkOrNotModified`.
- **Conditional reads.** `ConditionalReads/ConditionalRequestExtensions.cs:22-29`: `OkOrNotModified(object, EntityTagHeaderValue)` sets `ETag` and returns 304 or `OkObjectResult` (JSON only). `ConfigurationEntityTags.cs`: `W/"<prefix>.<22 base64url>"`, SHA-256 over length-prefixed inputs (`Create`, `:104-113`: prefix, key, rights code, version, hash, `Content.ToString(Formatting.None)`; `Append(null)` writes a `-1` length), HMAC for `b`; `d` hashes the `r` tag, the runtime version and the dependencies; `IsDependencies` keys on the `"d.` prefix. A manifest is reused only when its `Effective` equals the current `r` tag; `If-None-Match: *` is never a manifest candidate.
- **Headers on errors.** `MarkNoCache()` and `SetResolvedHeaders` write to `HttpContext.Response` before the tag compare; a later exception keeps them on the error. `inheritance.md:358` promises that `ETag`, `Cache-Control` and the withheld header are never sent on a 400, 404 or 422.
- **Errors.** `Models/ErrorResponse.cs` (`Message`, `Hint`, `ErrorCode`, `Details`). An unknown query value is `InvalidInputException(…, ErrorCodes.InvalidQuery)`, a 400 through the middleware (`V1/KeyValueRequests.cs`, `V1/ViewTransferRequests.cs`); both parsers treat an empty value as absent. 422 exists only for `BindingEvaluationException` (`binding.evaluation`, `binding.limit.<kind>`), mapped in `ErrorHandling/ExceptionHandlingMiddleware.cs:106-131`; any unmapped exception is a 500 with a stack trace. `HttpRequestData.Query` returns null for an absent or bare (`?format`) parameter, `""` for `?format=`, comma-joins repeats, and matches the key case-insensitively.
- **The `?format=` precedent.** `V1/DiffResultHttpExtensions.cs:27-42` returns a `ContentResult` (`text/plain`, no charset) for `format=unified` and silently falls back to JSON for any other value; its documented default is `json` (`Definitions.cs:527`). `OpenApi/Filters/DiffUnifiedResponseDocumentFilter.cs` adds the `text/plain` media type to the 200 of a fixed set of operation ids.
- **OpenAPI.** Both reads declare path parameters, then `If-None-Match` (`:97`, `:140`), the 200 (`Configuration`, with header classes), a bodyless 304, 400, 401, 403, 404, 500, and a 422 on the resolved read only (`:144`). The OpenAPI extension keeps the attribute order of parameters. `Api.Functions/test/ConfigurationConditionalReadTests.cs:746-794` is one theory over the effective, resolved and composition reads that pins the parameter list `[organization, project, view, key, If-None-Match]` and asserts that the 304 has no content. The snapshot is `Sdk.NSwag/open.api.v0.10.0.json` (`info.version` `0.10.0.13575`); `src/Platform/version.json` is `0.10-alpha` with a project-scoped height, so the first export after an `Api.Functions` commit is `0.10.<n>` with n ≥ 1.
- **Other reads.** The composition read (`ConfigurationHttp.Composition.cs`) and the version read (`:478-518`, the stored own content, no ETag) are not part of the issue. `Api.Web` serves no configuration reads.

### The SDKs

- **C#.** `Sdk.NSwag/src/ApiSdk.g.cs` (NSwag 14.6.3, no `/GenerateOptionalParameters`): `GetConfigurationAsync(organization, project, view, key, if_None_Match[, cancellationToken])` and `GetResolvedConfigurationAsync(…)`. A query parameter becomes a required positional `string` in document order; NSwag ignores a second media type of a 200 that also lists `application/json` (it keeps `Task<Configuration>` and `Accept: application/json`), and the DTO `Configuration` has `[JsonExtensionData]`, so a flat JSON map would deserialize silently into `AdditionalProperties`. The hand-written twins `GetConfigurationConditionalAsync` / `GetResolvedConfigurationConditionalAsync` (`src/ApiSdk.Conditional.cs`, members of the partial `IConfigurationsApiClient`) return `ConditionalResponse<Configuration>` with `ETag`, `IsNoStore` and `SecretsWithheld`, send `Accept: application/json`, build no query string, and type a 422 only on the resolved read (`:149-153`, one message). `test/ConditionalReadsTests.cs` compares every failure with the generated method and asserts an untyped effective 422 (`:180`). Callers of the generated reads: `src/Platform/Cli/src/Commands/Configuration/ConfigGetCommand.cs:70-81`, `ConfigSetCommand.cs:106-111`, `src/Platform/Cli/test/Sdk/ConfigurationsApiClientDateTests.cs:79` and `ConditionalReadsTests.cs:146-147,164-165`; no Moq setup of them exists; the provider uses only the twins (`Configuration/src/Reading/DocumentReader.cs:40-44`).
- **TypeScript.** `sdk.typescript/src` (Orval 8, `scripts/generate.mjs` picks the newest `../open.api.v*.json`; `scripts/openapi-transformer.mjs` corrects the document for all three outputs, and the README lists its corrections). A query parameter adds `params?` **before** `options?`, and a second media type turns the return type into a union (`getConfigurationVersionDiff`: `Promise<DiffResult | string>`). ui.admin calls `useGetConfiguration(…, key, { query })`, `useGetResolvedConfiguration(…)` and `getConfiguration(…, key, { signal })` (`app/composables/useConfiguration.ts:22`, `app/components/configuration/ConfigurationResolved.vue:19`, `app/pages/[org]/[project]/[view]/modeling/compare.vue:102`) and reads `.Content` / `.Version` in its tests. `lib/conditional.ts` `readConditional` reads the body with `readBody` before checking `response.ok` and rejects any 200 body that is not an object; `lib/fetcher.ts` `readBody` JSON-parses `application/json` and returns `undefined` for an empty text.

### sform

- **The command.** `sform story config <key>` is `ConfigGetCommand` (`[Command("config", "configuration")]`, also the parent of `set`, `edit`, `patch`, `delete`, `diff`, `versions`, `composition`). Options: `-e|--export`, `-r|--resolved`, `--version`; `-p`/`-v` come from `BaseContextCommand`. It reads with the generated methods, prints the whole envelope through `WriteJson` (a blank line, a line-number gutter, forced ANSI, straight to `System.Console` and Spectre's static `AnsiConsole`), then exports the envelope through `WriteJsonToFile` (prompts on overwrite, blank lines on `System.Console`). It catches only 404; a 422 of the resolved read ends as "Total mayhem", exit 1 (`WriteApiError` also maps 422 to exit 1). The annotation key is not validated. A test forbids `export`/`import` subcommands under `config` (`test/Commands/StorytellerTransferCommandTests.cs:41-53`).
- **Output.** `IExtendedConsole.WriteLine`, `WriteImportant`, `ValidateAnnotationKey` and `WriteApiError` render through Alba to `System.Console` with a two-column margin and wrapping, so they are neither raw nor capturable through the inner `IConsole`. The pipe-safe precedent is `StorytellerHistoryCommand.cs:63` (`Console.Console?.Out ?? System.Console.Out`). The only stderr write is the telemetry notice (`Telemetry/CommandTelemetry.cs:100-116`, static). `Program.cs` writes sign-in failures, 401/403 refusals, parsing errors and crashes to stdout; it maps only `UnrecognizedCommandParsingException` to `ERROR_INPUT_PARSING` (-1), so a missing option value or `--version abc` ends as a crash (exit 1, reported to Sentry). On Windows, `Console.Out` uses the console code page (437 on this machine) and `WriteLine` and Newtonsoft's indented output use CRLF.
- **Sign-in in CI (G9).** Done by 424-96 on this branch: `STORYTELLER_ENDPOINT`, `STORYTELLER_KEY` (target), `STORYTELLER_PROJECT` / `STORYTELLER_VIEW` (sform only), API key, certificate, client credentials, token file, and GitHub Actions federation when `STORYTELLER_CLIENT_ID` is a GUID and the job has `permissions: id-token: write` (`Sdk.NSwag/src/Credentials/StorytellerCredentialVariables.cs:116-122`). `STORYTELLER_API_URL` is deprecated. An empty `STORYTELLER_*` value counts as unset. Environment access is injected as `Func<string, string?>` (`Cli/src/Authentication/MachineSignInSettings.cs:68-117`).
- **Secret delivery.** `KeyValuePolicy.RequireMtlsForSecretDelivery` defaults to true: a token caller (API key, client credentials, federated machine) with `Secrets.Read` gets `X-2S-Secrets-Withheld: mtls-required` and the binding expressions instead of secret values (`key-values.md:104`, `authentication.md:1027`); a machine without `Secrets.Read` gets the expressions and no header (`key-values.md:108`). mTLS needs a certificate machine behind a front end that forwards the certificate (`MachineAuth:IsCertificateHeaderFromClientAllowed`, `authentication.md:257`).
- **Publishing.** `42.Platform.Cli` on nuget.org has only `0.8.32-alpha-gd24e7b5dbf` and `0.8.33-alpha-g108dbfb368` from 2024-03-27, without any of the `STORYTELLER_*` sign-in. `dotnet tool install` without `--prerelease` finds nothing, and `--prerelease` cannot be combined with `--version`. sform is versioned on its own (`src/Platform/Cli/version.json`, `0.8-alpha`). No workflow builds, tests or publishes .NET code; `.github/workflows` holds the docs site and `ui-admin.yml`. The owner `42for-net` of the study's `42for-net/storyteller-action` does not exist; the repository is `akobr/mono.me`.

### GitHub Actions

- A variable written to `GITHUB_ENV` (`NAME=value`, or `NAME<<DELIMITER` … `DELIMITER` for any value) reaches every later step of the job, third-party actions included, and is shown in the `env:` block of every later step's log; only values registered with `::add-mask::` (data escaped `%`→`%25`, CR→`%0D`, LF→`%0A`) are hidden, and a multi-line secret is masked line by line. The runner refuses `NODE_OPTIONS`. Composite action steps receive inputs only through expressions or `env:`, and need an explicit `shell`. `uses: owner/repo/path@ref` runs an action from a subfolder. `actions/setup-dotnet` is at v6 (2026-07); it exports `DOTNET_ROOT` and prepends `PATH` for the rest of the job.
- MSBuild turns every environment variable into a property (`RESTORESOURCES` redirects `dotnet restore`), and many toolchains read their configuration from variables, so no list of dangerous names is complete.

### Docs

- The format rules live only in `docs/Platform/Storyteller/configuration-provider.md` ("Flattening", `:187-204`). The reads are documented in `inheritance.md` ("Other reads", "Conditional reads": tags `:292-318`, manifest `:330-354` with "Billing and audit", headers `:356-366`, limits `:368-377`) and in `docs/42for.net/platform/using-the-platform.md` ("What a service reads"). `docs/42for.net/cli/sform.md` has no section on reading a configuration; its warning `:125-127` says a resolved read prints secrets into the job log. `authentication.md:1046-1071` is the shell example of the federated token exchange (it masks and exports `STORYTELLER_TOKEN`, not a configuration); its sform GitHub Actions examples are at `:342-383`. The VitePress sidebar is `docs/42for.net/.vitepress/config.mjs` (dead links fail the build).

## Proposed Changes

### 1. A shared formats library

New packable project `src/Platform/Storyteller/ConfigurationFormats/src/ConfigurationFormats.csproj`:

- assembly and package `42.Platform.Storyteller.ConfigurationFormats`, root namespace **`_42.Platform.Storyteller.ConfigurationFormats`** (no type of that name exists; `Configuration`, `Formatting` and `Sdk.Configuring` are avoided for the reasons in Current State), `net10.0`, nullable, implicit usings, `IsPackable`, a description and `PackageTags`;
- one dependency: `Newtonsoft.Json` (the document is a `JToken` everywhere it is read). No YAML library: the YAML writer is small and deterministic (section 3), and the provider package, which gains this dependency, should not pull a YAML parser into applications;
- package readme `docs/Platform/Storyteller/ConfigurationFormats/package-readme.md`;
- referenced by the provider (`Configuration/src`), `Api.Functions/src` and `src/Platform/Cli/src`, not by `Sdk.NSwag`; the source and test projects are listed in `42.mono.slnx` under `/Platform/Storyteller/` next to `Configuration`.

The moved and new public types (names may be refined during implementation, the shape is the contract; no type may be named `ConfigurationFormats`, which the namespace would shadow, or `DocumentEntry`, which the provider already has):

```csharp
namespace _42.Platform.Storyteller.ConfigurationFormats;

public enum ConfigurationFormat { Flat = 1, Properties, Env, Yaml }

public static class ConfigurationFormatNames
{
    public static IReadOnlyList<string> Names { get; }                 // flat, properties, env, yaml
    public static bool TryParse(string? value, out ConfigurationFormat format);   // exact names, case-insensitive; no trimming, no numbers
    public static string GetName(this ConfigurationFormat format);     // the canonical lower-case name
    public static string GetMediaType(this ConfigurationFormat format);   // the one source of the body's Content-Type
}

public static class DocumentRules                   // the vectors' rule names
{
    public const string NotAnObject = "not-an-object", EmptyName = "empty-name", ColonInName = "colon-in-name",
        CaseCollision = "case-collision", BracketInName = "bracket-in-name", ArrayGap = "array-gap",
        InvalidEnvName = "invalid-env-name";
}

public enum DocumentValueKind { String, Number, Boolean }
public readonly record struct DocumentSegment(string? Name, int Index);        // a property name, or an array index (Name null)
public sealed record DocumentLeaf(IReadOnlyList<DocumentSegment> Segments, string Value, DocumentValueKind Kind, string Path);

public static class DocumentFlattener               // moved; Flatten unchanged
{
    public static IReadOnlyList<DocumentLeaf> Read(JToken? content, bool validate = true);   // base rules when validate, document order
    public static IReadOnlyDictionary<string, string> Flatten(JToken? content);             // ':' keys, OrdinalIgnoreCase, as today
}

public static class JsonPaths { … }                 // moved, public
public sealed class InvalidDocumentException : Exception { string Rule; string Path; }   // moved, public

public sealed record FormattedConfiguration(ConfigurationFormat Format, string Text, string MediaType);
public sealed record EnvironmentVariable(string Name, string Value, DocumentValueKind Kind, string Path);

public static class ConfigurationFormatter
{
    public const string Version = "1";              // bumped whenever the bytes of any format change (section 6)
    public static FormattedConfiguration Format(JToken? content, ConfigurationFormat format, string? environmentPrefix = null);
    public static IReadOnlyList<EnvironmentVariable> ToEnvironmentVariables(JToken? content, string? environmentPrefix = null);
    public static JToken? Canonicalize(JToken? content);
}
```

- **One walk.** `DocumentFlattener.Read` is the existing `Visit` turned into an ordered list of leaves (segments, scalar text, kind, JSON path) with the same four base rules inline, the same messages and first-error semantics. A `JValue` that is not an integer, a float or a boolean has kind `String` (as its text today). `Flatten` is the `:` projection of `Read` and returns exactly what it returns today. With `validate: false` the walk applies no rule (used for the effective side of masking, section 7). Every format renders from the same leaves, so a value present in one format is present in all of them.
- **Name rules of the formats.** `bracket-in-name` (properties) and `invalid-env-name` (env) are checked by a second walk over the property names of the canonical token, every name in document order, including names whose value is null or an empty container (as the base name rules do), after `Read` has passed. They are not checked on leaves only.
- **One reading of the document.** `ConfigurationFormatter` formats `Canonicalize(content)`: the token written with `ToString(Formatting.None)` and read back with `DateParseHandling.None` and `FloatParseHandling.Double`, the settings of the SDK, with every unpaired UTF-16 surrogate in names and strings replaced by U+FFFD (what UTF-8 encoding does to them anyway, and what YAML readers require). That is the document a client sees in the envelope (the API writes `Content` with the same `ToString(Formatting.None)`, `Backend.Core/src/Json/JObjectConverter.cs:22-27`), so the server's date and decimal tokens format as the client's strings and doubles (`2026-10-20T12:00:00.123+02:00`, `1.5`), and `?format=` on the API and local formatting in `sform` cannot drift. A review experiment confirmed the equality for content parsed as Cosmos DB, the writes and the binding converter parse it. `DocumentFlattener.Flatten` itself does not canonicalize (the provider already reads with these settings).
- **Paths without control characters.** `JsonPaths` additionally escapes control characters in bracketed names (`\n`, `\r`, `\t`, other C0 characters and DEL as `\uXXXX`, upper-case hex), so a crafted name cannot put a line break, and with it a GitHub workflow command, into a message, a log line or stderr. No existing vector path changes; one vector is added (section 4).
- **Prefix.** `environmentPrefix` is valid only with `Env` (`ArgumentException` otherwise) and with `ToEnvironmentVariables` (section 3).
- **The provider.** `Configuration/src/Configuration.csproj` references the library; `ContentApplication`, `UnboundBindings`, `ReferenceSubstitution` and `LastKnownGoodCacheTests` change their `using`; the wrapping in `StorytellerConfigurationException` stays. The provider's public API does not change; its package gains the dependency.

### 2. Rules shared by every format

1. `Read` applies the base rules (`not-an-object`, `empty-name`, `colon-in-name`, `case-collision`) to the whole document, whatever the format's own delimiter is: a document is valid or invalid for the reference provider, and a format adds rules but never removes one.
2. Then the format's name rule (`bracket-in-name`, `invalid-env-name`) runs over every property name, in document order.
3. Then the format's value rule (`array-gap`, properties only).

The first error is reported, so the order is part of the contract (vector 17, `it's` with `a:b`, stays `colon-in-name` in every format).

| Rule of study 3.3 | Decision |
|---|---|
| Numbers and booleans become their JSON text | The flattener's scalar text after canonicalization: `5432`, `1.5`, `true`. YAML writes them plain, every other format as text |
| Null never appears | A null property makes no key in any format. In `yaml` a gap in an array is written `null` (section 3); `properties` rejects gaps |
| An empty array or object produces no key | In every format, also in `yaml` |
| A name that contains the target's delimiter is escaped or rejected | `flat`: `:` rejected (base rule). `properties`: names with other characters than `[A-Za-z0-9_-]` written in Spring's bracket form, `[` and `]` rejected. `env`: names outside the variable charset rejected (section 3) |
| Key casing is kept; names that differ only in case are a validation error | Base rule `case-collision` (whole leaf keys, case-insensitive). `env` upper-cases names; with its charset, the base rule already makes them unique |
| No partial output | The formatter builds the complete text before returning and throws `InvalidDocumentException` on the first error; callers write nothing on a throw |

Every format writes LF line endings, no byte order mark, keys in document order (no sorting), and is deterministic. Where the library writes `\uXXXX` itself (paths, `properties`, `yaml`), the hex digits are upper-case.

### 3. The four formats

Study document `{"db":{"hosts":["a","b"],"port":5432},"features":["vat"]}`:

| Format | Media type | Body |
|---|---|---|
| `flat` | `application/json; charset=utf-8` | `{\n  "db:hosts:0": "a",\n  "db:hosts:1": "b",\n  "db:port": "5432",\n  "features:0": "vat"\n}\n` |
| `properties` | `text/plain; charset=utf-8` | `db.hosts[0]=a\ndb.hosts[1]=b\ndb.port=5432\nfeatures[0]=vat\n` |
| `env` | `text/plain; charset=utf-8` | `DB__HOSTS__0=a\nDB__HOSTS__1=b\nDB__PORT=5432\nFEATURES__0=vat\n` |
| `yaml` | `application/yaml; charset=utf-8` | `db:\n  hosts:\n    - "a"\n    - "b"\n  port: 5432\nfeatures:\n  - "vat"\n` |

**`flat`** (.NET `IConfiguration`, any JSON reader). The `Flatten` keys and values as a JSON object of strings, written by a Newtonsoft `JsonTextWriter` (`Formatting.Indented`, indentation 2) over a `StringWriter` whose `NewLine` is `"\n"`, followed by one LF. Escaping is Newtonsoft's default: `\"`, `\\`, `\b \t \n \f \r`, any other C0 character as `\u00xx` (lower-case hex), U+0085, U+2028 and U+2029 escaped; DEL, C1 and all other characters raw. An empty document is `{}\n`. It passes `flattening.json` unchanged.

**`properties`** (Spring Boot `.properties`).

- Keys: the first name, then `.name` per property and `[i]` per array item (`db.hosts[0]`, `list[3].name`, `list[4][0]`). A name that contains any character other than ASCII letters, digits, `-` and `_` is written in Spring's bracket form, `.[name]` (`[name]` as the first segment): `Logging.LogLevel.[Microsoft.AspNetCore]`, `[c d]`, `.[@at]`. Spring Boot binds a bracketed name with every character kept, and drops the other characters from an unbracketed map key. A name that contains `[` or `]` cannot be written that way and is rejected: rule **`bracket-in-name`**, at the path of the property. The encoding is lossless, so distinct paths give distinct keys.
- Rule **`array-gap`**: Spring Boot refuses to bind a list with a missing index, so an array item without a value (null, or a container without any value) followed by an item with a value is rejected, at the path of the first such item. A trailing item without a value is simply left out.
- Escaping (ASCII output, as `java.util.Properties.load(InputStream)` reads ISO-8859-1): in keys `\`, space, `=`, `:`, `#`, `!`; in values `\` and a leading space; in both `\t`, `\n`, `\r`, `\f` as escapes, and every other character below U+0020 or above U+007E as `\uXXXX` per UTF-16 code unit.
- One `key=value` line per leaf, each ending with LF. An empty document is an empty body.
- MicroProfile Config and Quarkus quote dotted segments instead and read `[…]` as an index, so they read only documents whose names are made of `[A-Za-z0-9_-]`; the docs say so.

**`env`** (`.env` files read by Docker Compose, python-dotenv / pydantic-settings and the key/value import; .NET, config-rs and others read the variables once they are in the environment).

- Names: the segments upper-cased with `ToUpperInvariant` and joined with `__` (`DB__HOSTS__0`), as the key/value `.env` export does.
- Rule **`invalid-env-name`**, at the path of the property: a name must match `[A-Za-z0-9.-]+(_[A-Za-z0-9.-]+)*` (ASCII letters, digits, `.` and `-`, single `_` only between them, so `__` stays the only delimiter and `a_:b` cannot collide with `a:_b`), and the first property name of the document must start with an ASCII letter (a nested `0b` is valid: `A__0B`). The charset is Kubernetes' variable charset; `.` and `-` are kept because .NET configurations routinely contain names such as `Microsoft.AspNetCore`. A POSIX shell cannot reference such a variable; the docs say so. Effective documents that contain object bindings or JSON-e (`$binding`, `$if`, …) are rejected by this rule; the resolved read is the one to export.
- Values, the house dotenv dialect of the key/value export, extended so that common readers take them literally: an empty value is `NAME=`; a value made only of `[A-Za-z0-9_.,:/@%+-]` is unquoted; otherwise, a value without `'`, `\`, CR or LF is single-quoted (literal for bash, Docker Compose, python-dotenv and the key/value import); otherwise it is double-quoted with `\\`, `\"`, `\n`, `\r`, `\t` in this order, the escape order of `KeyValueFormats`. `KeyValueFormats.ParseEnv` reads every value back (a review experiment checked 31 values). `$` is not escaped; the docs state that interpolating readers may expand it inside double quotes, that the file is a dotenv file and not a shell script, and that `docker run --env-file`, `kubectl --from-env-file` and kustomize take values verbatim, so only unquoted values survive them.
- One line per variable, each ending with LF; an empty document is an empty body.
- `environmentPrefix` (`sform --env-prefix`, section 7) is prepended to every name as given, upper-cased, with no separator (`--env-prefix APP_` gives `APP_DB__HOST`, as .NET `AddEnvironmentVariables("APP_")` expects), and must match `[A-Za-z][A-Za-z0-9_]*`. The name rule applies to the unprefixed names, so validity never depends on the prefix.

**`yaml`** (Helm values, Spring `application.yml`, any YAML 1.2 reader). Written by the library from the leaves, without a YAML library:

- The nested document rebuilt from the leaves: mappings in document order, two-space indentation, block sequences indented under their key (`  - item`), a mapping item written compactly (`- name: "w"` with its other keys aligned), a sequence item as `- - "x"`.
- Keys plain when they match `[A-Za-z_][A-Za-z0-9_-]*` and are not one of `y`, `yes`, `n`, `no`, `true`, `false`, `on`, `off`, `null` in any casing; otherwise double-quoted with the string escaping below. A key whose written form is longer than 1024 characters (YAML's limit for implicit keys) is written as an explicit key: `? "key"` on its own line, then `:` and the value at the same indentation.
- Strings always double-quoted, with `"` and `\` escaped, `\n`, `\r`, `\t`, and `\uXXXX` for every other character YAML does not print raw (C0 controls, DEL, U+0080-U+009F, U+2028, U+2029, U+FEFF, U+FFFE, U+FFFF). The JSON escapes are valid YAML escapes, so `"True"`, `"05432"`, `"2026-10-20"`, `"@db.host"` and `""` stay strings.
- Numbers and booleans plain, as their JSON text. `1E+20` is a float in YAML 1.2 and in SnakeYAML (Spring), which binds it to a string as `1.0E20`; strict YAML 1.1 readers such as PyYAML read it as a string. The docs recommend binding numbers to numeric types. Spring also splits a YAML key at its dots, so names with dots survive only into maps.
- Null properties and containers without any value are left out, as in the other formats. A gap in an array (a null item, or an item without any value, followed by an item with a value) is written `null`, so the other items keep the indexes they have in `flat` and `env` (Spring binds it as an empty string); a trailing item without a value is left out, and an array without any value is left out. Flattening the parsed YAML therefore gives exactly the `flat` keys (a test asserts this round trip).
- An empty document is `{}\n`.

`ToEnvironmentVariables` returns the `env` names (with the prefix) and the raw values, kinds and paths, without quoting: the input of `--github-env` (section 7).

### 4. Vectors and tests of the library

New test project `ConfigurationFormats/test/ConfigurationFormats.UnitTests.csproj` (xUnit, Shouldly, `YamlDotNet` for the round trip only, not packable). It references only the library. It links the vector files from their current folder, `Configuration/test/Vectors/`, which the issue and the docs name and where the new files are created too, as embedded resources with the same logical names (`Vectors.<file>`), and reads them with the SDK's settings.

- **Moved.** `FlatteningVectorTests` moves from `Configuration/test/Flattening/` to the new project; so does `DocumentFlattenerTests`, except that `ADocumentReadByTheSdk_KeepsDateLikeStrings`, which needs the SDK's `Configuration`, reads its document with a `JsonTextReader` (`DateParseHandling.None`) instead. `LastKnownGoodCacheTests` stays in the provider tests. `flattening.json` keeps its 18 vectors and gains one: a name with a line break and a colon (`colon-in-name`, the line break escaped in the path); its `description` documents the control-character escaping, and so does `configuration-provider.md`.
- **Flat bodies.** A test runs every `flattening.json` vector through `Format(…, Flat)`, parses the body and compares the keys, or the error rule and path, so the criterion "`flat` passes `flattening.json`" holds for the output itself.
- **New vector files**, same shape as `flattening.json` (`description`, `vectors: [{ name, document, keys | error }]`), so 424-105 (C22) can adopt them. `keys` are the logical keys a reader returns (`Logging.LogLevel.[Microsoft.AspNetCore]`, `[c d]`, not `\ `-escaped) and raw values; quoting and escaping are pinned only in `formats.json`.
  - `flattening.properties.json`: every document of `flattening.json` with its `properties` keys and values, or its error; plus names with dots, spaces and `@` in bracket form, `[` and `]` rejected (also `{"x": {"a[0]": {}}}`, whose value has no key), a property named `0` next to an index, an array gap rejected and a trailing null left out;
  - `flattening.env.json`: every document of `flattening.json` with its `env` names and values, or its error (vector 9 becomes `invalid-env-name` for `c d`); plus `.` and `-` kept, `invalid-env-name` for a space (also `{"a b": null}`), a quote, a leading digit in the first name (`{"0a": 1}`) next to a valid nested `{"a": {"0b": 1}}`, `__` inside a name, a leading or trailing `_`, and non-ASCII;
  - `formats.json`: exact bodies, `{ name, document, format, text | error }`, for the study example in all four formats, an empty document, escaping in every format (quotes, backslashes, control characters, non-ASCII, a non-BMP character, an unpaired surrogate, a leading space, `=` and `#` in names), YAML strings that look like other types, a YAML gap and a trailing null, nested arrays, a long YAML key, and a document with `@` expressions.
- **Unit tests.** Every renderer: escaping, LF and the trailing newline, document order, empty and null content, error messages that name the path but never a value, `Canonicalize` (a date token, a decimal and a surrogate format as a client reads them), the YAML round trip (parsed with YamlDotNet, flattened, equal to `flat`), `ToEnvironmentVariables` with and without a prefix, the prefix refused for other formats, `Read(validate: false)` on a document with a colon in a name.

### 5. `?format=` on the API

`GetConfiguration` and `GetConfigurationResolved` read `request.Query[Definitions.Parameters.Format]` after the access check and the key parse:

| Query value | Result |
|---|---|
| null (absent, or a bare `?format`), `""` (`?format=`) or `json`, any casing | The current code path, byte for byte: envelope, tags, headers, manifests. Empty means absent, as in `KeyValueQuery` and `ViewTransferRequests`; `json` is the envelope, as the diff's documented default |
| `flat`, `properties`, `env`, `yaml`, any casing | The formatted body (below) |
| anything else, including repeats (`flat,yaml`, `flat,flat`) | `400`, `ErrorCode` `InvalidQuery`: "The query parameter 'format' must be json, flat, properties, env or yaml." (`InvalidInputException`, the existing convention), before any header is written |

With a format, the flow is:

1. Read the model (404 as today). On the resolved read, compute the tag inputs, look for an unchanged manifest (a 304 here needs no formatting, see section 6), otherwise resolve.
2. **Format** the effective or the resolved content with `ConfigurationFormatter.Format`. An `InvalidDocumentException` returns `new ObjectResult(error) { StatusCode = 422 }` with `ErrorResponse { Message = <the exception message>, ErrorCode = "format.<rule>", Details = new { Format = format.GetName(), Rule = e.Rule, Path = e.Path } }` (serialized by the MVC options: PascalCase, nulls omitted), and nothing else: no `ETag`, no `Cache-Control`, no withheld header, no manifest. The codes are a dotted family like `binding.*`, the other 422 of these reads: `format.not-an-object`, `format.empty-name`, `format.colon-in-name`, `format.case-collision`, `format.bracket-in-name`, `format.array-gap`, `format.invalid-env-name`. A format error after a resolve is still a billed and audited resolved read (`reads.binding`, reveals, `BindingRead`), repeated on every request, because a 422 carries no tag and records no manifest; the docs say so.
3. Only then write the headers (`MarkNoCache` on the effective read, `SetResolvedHeaders` on the resolved one), record the manifest (resolved, non-volatile), and answer through a new overload `OkOrNotModified(this HttpRequestData, FormattedConfiguration body, EntityTagHeaderValue tag)` that sets `ETag` and returns 304 or a `ContentResult` whose `ContentType` is `body.MediaType`, the only source of the media type.

The formatting runs before the tag compare, so `If-None-Match: *` on a document that is invalid in that format is a 422, not a 304. `Accept` is not considered: the query decides, so no `Vary` is needed. The composition and version reads take no `format`.

**OpenAPI.**

- A `format` query parameter (`Type = typeof(string)`, `Required = false`) on both reads, declared after the path parameters and **before** `If-None-Match`, which stays last as 424-96 intended. The description (`Definitions.Descriptions.ConfigurationFormat`) lists the values, says the formatted body has no envelope and that the ETag differs per format.
- The 200 keeps `application/json` → `Configuration` first. A new `ConfigurationFormatResponseDocumentFilter`, modelled on `DiffUnifiedResponseDocumentFilter` and registered after it, adds `text/plain` and `application/yaml` string media types with examples to the 200 of `GetConfiguration` and `GetResolvedConfiguration`, and says in the 200 description that `format=flat` returns a JSON object of strings. The 304 stays without content. `Definitions.ContentTypes` gains only `Yaml = "application/yaml"` for the filter's keys.
- A 422 on the effective read (`format.<rule>`), and a 422 description on the resolved read that names `binding.evaluation`, `binding.limit.<kind>` and `format.<rule>`. The 400 description stays `Descriptions.ResponseBadRequest` (the C# twins copy it).

**Tests** (`Api.Functions/test/ConfigurationConditionalReadTests.cs`, a new `ConfigurationFormatReadTests.cs` for the formats):

- each format on both reads: body equal to `ConfigurationFormatter.Format` of the same content as a client reads it (the envelope written by the API serializer and parsed with the SDK's settings), media type, tag shape, `Cache-Control`, withheld header and `no-store` on 200 and 304;
- `?format=ENV` and `?format=env` give the same tag; `?format=`, `?format=json` and no parameter give the envelope and the default tag; a 304 per format with its own tag; a JSON tag sent to `?format=env` and an `env` tag sent to the envelope read or to `?format=yaml` answer 200;
- the existing tests stay unchanged, except the document theory `Document_ConfigurationReads_DeclareTheConditionalReadContract`, which takes the expected parameter list as an argument: `[organization, project, view, key, format, If-None-Match]` for the two reads and `[organization, project, view, key, If-None-Match]` for the composition read;
- an unknown or repeated `format` is 400 `InvalidQuery` (handler tests expect the `InvalidInputException`, as `KeyValuesHttpTests` does; `request.Query["format"]` is set directly); an invalid document is 422 `format.<rule>` with `Details`, without `ETag`, `Cache-Control` or withheld header, and without a manifest;
- the resolved read with bindings: a manifest recorded for `env` answers a later `env` poll with 304 without evaluating; it does not answer `yaml` or the envelope; a binding that produces a colon in a name is a 422 after the resolve;
- the document: the 200 lists the three media types, the 304 has no content, both reads declare 422.

### 6. Entity tags per format

- `ConfigurationEntityTags.ForEffective`, `ForResolved` and `ForResolvedBindings` take an optional `string? representation`. When it is set, `Create` appends it as one more length-prefixed input after the content; when it is null, nothing is appended (not even `Append(null)`, which would write a `-1` length), so every existing tag stays byte-identical.
- The representation is `$"{format.GetName()}/{ConfigurationFormatter.Version}"`, the canonical name, never the raw query value, so a change of the rules (a new escape, a new quote) changes every formatted tag at deploy and never answers a stale 304.
- `d` tags hash the `r` tag, so they vary by format without a change to `ForDependencies`; the prefixes stay, so `IsDependencies` still works. A manifest is reused only when its `Effective` equals the current, format-specific `r` tag, so manifests of different formats never meet; `DependencyManifest` gets no `Format` field.
- The manifest 304 answers without formatting. It is safe because a manifest is recorded only after a successful format of the same inputs, and the formatter is deterministic for a given `Version`.
- Each format a caller polls holds its own manifest, inside `ConditionalReads:ManifestCacheSizeLimit`; the docs say so.

### 7. sform

`sform story config <key>` gains options (all on `ConfigGetCommand`, no new subcommand):

```text
sform story config <key> [-r|--resolved | --version <n>] [-f|--format json|flat|properties|env|yaml]
                         [--content-only] [-e|--export <file>] [--env-prefix <PREFIX>]
                         [--github-env] [--mask bound|all] [--allow-unprefixed] [--allow-unresolved]
```

**Reads.** The effective and the resolved read go through the conditional twins (`GetConfigurationConditionalAsync` / `GetResolvedConfigurationConditionalAsync` with no tag), which expose `SecretsWithheld`, `IsNoStore`, `Version` and `Hash`; `--version` keeps `GetConfigurationVersionAsync` (the stored own content of that version). `sform` formats locally with `ConfigurationFormatter`, so formats also work for `--version` and against an API without `?format=`; section 1 makes the bytes identical to the API's, and a test proves it on the same content.

**Output.**

| Options | stdout | `--export` file |
|---|---|---|
| default (`json`), no `--content-only` | the envelope display, as today | the envelope, with today's prompt |
| a text format, no `--content-only` | the formatted text with the line-number gutter of `WriteJson`, without colours; nothing when `--export` is given | the formatted text |
| `--content-only` | the payload, raw: the formatted text, or for `json` the `Content` (two-space indentation, `{}` for null content); nothing when `--export` is given | the same payload |
| `--github-env` or `--mask` | only the `::add-mask::` commands | the payload of `--format` (the `Content` for `json`) |

- Raw output (the payload and the mask commands) is written to `IExtendedConsole.Console.Out` as UTF-8 without BOM with explicit `\n` line ends, never through `WriteLine`, Alba or `System.Console` directly: in these modes `sform` sets `System.Console.OutputEncoding` to UTF-8 without BOM before writing, and every JSON writer uses a `StringWriter` with `NewLine = "\n"`. Files are written through `IFileSystem` as UTF-8 without BOM, without a prompt, and on Unix with mode 0600 when they hold resolved content. The docs say that Windows PowerShell 5.1 re-encodes `>` redirections and recommend `--export` there.
- **Diagnostics.** With `--content-only`, `--github-env` or `--mask`, the command writes every message as one plain line to `IExtendedConsole.Console.Error` through new helpers in `Cli/src/Output` (for example `WriteErrorLine(string)` and an API-error variant that reuses `ReadError` and the exit-code map), and calls none of `WriteLine`, `WriteImportant`, `ValidateAnnotationKey`, `WriteApiError`, `WriteJson` or `WriteJsonToFile`. Errors raised by `Program` (sign-in, 401/403, crashes) still go to stdout; the exit code is non-zero, and with `--export` the payload file is not touched.
- **Parsing errors.** `Program` maps every `CommandParsingException` (a missing option value, a repeated `--format`, `--version abc`) to `ERROR_INPUT_PARSING` instead of a crash, as it already does for unrecognized options.

**Validation, exit 2, nothing written.**

- an invalid annotation key; an unknown `--format` ("Unknown format 'x'; use json, flat, properties, env or yaml."); `--version` with `--resolved`;
- `--env-prefix` without `--format env` or `--github-env`, or not matching `[A-Za-z][A-Za-z0-9_]*`;
- `--github-env` or `--mask` without `--resolved`, or with `--version`; `--content-only` with `--github-env` or `--mask` but without `--export`; `--github-env` with neither `--env-prefix` nor `--allow-unprefixed`; `--allow-unprefixed` or `--allow-unresolved` without `--github-env` or `--mask`;
- an `InvalidDocumentException` (rule and path on the error output, sanitized by section 1); a 422 of the API (`binding.*`), now handled before `WriteApiError`, which maps 422 to exit 1. Other API errors keep their exit codes (`WriteApiError` as in `ConfigCompositionCommand`, or its plain variant in the raw modes).

**Withheld secrets and unresolved bindings.**

- When the resolved read carries `X-2S-Secrets-Withheld`, `sform` writes a warning to the error output (the values hold binding expressions); with `--github-env` or `--mask` it is an error, exit 2, nothing written, as study 3.7 asks a consumer to fail loudly.
- A machine without `Secrets.Read` gets no header and the expressions as values. With `--github-env` or `--mask`, a resolved string that starts with `@` and equals the effective value at the same path is treated as an unresolved binding: exit 2 naming its path, unless `--allow-unresolved`; in the other modes it is a warning with the paths. Values are never printed.

**`--github-env`** exports the resolved configuration into a GitHub Actions job, in one run with one effective and one resolved read:

1. `GITHUB_ENV` is read through a settings object built from `Func<string, string?>` (the `MachineSignInSettings` pattern); unset is exit 2.
2. Build the variables with `ToEnvironmentVariables(resolved, prefix)`.
3. Refuse (exit 2, the names or paths in the message, nothing written):
   - a value that contains U+0000 (on Windows it would add variables to later steps), a variable longer than 32,767 characters (`NAME=value`, the Windows limit, below Linux's 128 KiB), or more than 512 KiB in total;
   - names the runner reserves or that change how later steps or their tools run, compared case-insensitively after the prefix: `NODE_OPTIONS`, `PATH`, `HOME`, `SHELL`, `ENV`, `BASH_ENV`, `CDPATH`, `IFS`, `PS4`, `PROMPT_COMMAND`, `SHELLOPTS`, `BASHOPTS`, `GLOBIGNORE`, `CLASSPATH`, `CC`, `CXX`, `MAKEFLAGS`, `JAVA_TOOL_OPTIONS`, `_JAVA_OPTIONS`, `JDK_JAVA_OPTIONS`, `JAVA_OPTS`, `MAVEN_OPTS`, `GRADLE_OPTS`, `SBT_OPTS`, `PYTHONPATH`, `PYTHONSTARTUP`, `PYTHONHOME`, `PERL5OPT`, `PERL5LIB`, `PERLLIB`, `RUBYOPT`, `RUBYLIB`, `NODE_PATH`, `NODE_EXTRA_CA_CERTS`, `NODE_TLS_REJECT_UNAUTHORIZED`, `LUA_INIT`, `RUSTC_WRAPPER`, `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE`, `GOFLAGS`, `GOPROXY`, `GONOSUMDB`, `GOSUMDB`, `GOINSECURE`, `RESTORESOURCES`, `KUBECONFIG`, `PATHEXT`, `COMSPEC`, `PSMODULEPATH`, and every name starting with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `INPUT_`, `STATE_`, `LD_`, `DYLD_`, `BASH_FUNC_`, `DOTNET_`, `COREHOST_`, `CORECLR_`, `COMPLUS_`, `MSBUILD`, `NUGET_`, `NPM_CONFIG_`, `YARN_`, `PIP_`, `CARGO_`, `DOCKER_`, `GIT_` or `STORYTELLER_`.

   A denylist is never complete (MSBuild alone reads every variable), so `--github-env` requires a prefix: `--env-prefix` (for example `APP_`), or an explicit `--allow-unprefixed` for jobs where the people who edit the configuration may also steer the job. The denylist applies in both cases.
4. **Masking** (`--mask bound`, the default of `--github-env`). Without secret paths in the read (C6), a value is treated as possibly secret when resolution produced it: a resolved string or number whose path has no leaf in the effective document, or whose value differs from it. The effective document is canonicalized and read with `Read(validate: false)` (object bindings may break the base rules there); when the two envelopes differ in `Version` or `Hash`, or the effective read or walk fails (other than a 401/403, left to `Program`, or a 404, exit 2), every string and number is masked and the reason, never a value, goes to the error output. Booleans and empty values are never masked. Values shorter than 4 characters are not masked under `bound` (masking `1` or `on` would hide those characters in the whole log); their names are listed as "not masked: shorter than 4 characters". `--mask all` masks every non-empty string and number, whatever its length. For each distinct masked value, `::add-mask::` is written to stdout for the whole value and for every non-empty line of it, the data escaped as the runner expects, before anything else is written.
5. Build the complete `GITHUB_ENV` text: one `NAME<<DELIMITER`, value, `DELIMITER` block per variable, LF line ends, with one random delimiter (`ghadelimiter_` and 32 hex digits, from an injectable source) regenerated until it occurs in no value. Append it in one write through `IFileSystem` (UTF-8, no BOM).
6. With `--export`, write the file (section above) after the masks.
7. Write a summary to the error output: the count, the names and the number of masked values, never a value.

**`--mask bound|all`** without `--github-env` writes only the mask commands (step 4) and the `--export` file: for a job that keeps the resolved configuration in a file for one step instead of the job environment.

**Tests** (`src/Platform/Cli/test/Commands/ConfigGetCommandTests.cs`, with `StringWriter`s as `Out` and `Error` of the fixture's inner console, a strict `Mock<IConfigurationsApiClient>`, `MockFileSystem`, and parse tests through `CommandLineApplication<ConfigGetCommand>`):

- each format, effective, resolved and `--version`: stdout equals `ConfigurationFormatter.Format` of the content exactly; `--export` writes the same bytes and nothing reaches stdout; the default envelope path writes nothing to the inner `Out` and `Error` (it renders through `System.Console`, as today);
- in the raw modes `RecordingConsole` stays empty (`Lines`, `Tables`) and the `IExtendedConsole` mock has no other calls;
- every validation row above: exit 2, no stdout payload, no file;
- an invalid document: exit 2, rule and path on stderr, nothing on stdout, no file; a 422 of the API: exit 2;
- withheld secrets and unresolved bindings: a warning on stderr; exit 2 with `--github-env` or `--mask`, and `--allow-unresolved` lets the unresolved case through;
- `--github-env`: masks precede the file write and any other output; heredoc syntax; a value that contains the first delimiter gets another; denied names with and without the prefix; the prefix required; a NUL and an oversized value refused; `Version` or `Hash` mismatch and a failed effective read mask everything; booleans and short values are not masked under `bound` and short values are under `all`; multi-line values get one mask per line; stderr never contains a value; `GITHUB_ENV` unset is exit 2; `--export` in the same run;
- `Program` maps a parsing error to `ERROR_INPUT_PARSING`.

### 8. The GitHub Action

A composite action in the monorepo, `src/Platform/GitHubAction/action.yml` with its `README.md`, usable as `uses: akobr/mono.me/src/Platform/GitHubAction@<ref>` and from this repository as `uses: ./src/Platform/GitHubAction`:

| Input | Default | Meaning |
|---|---|---|
| `key` | required | The annotation key of the configuration |
| `project` | | `org.project`, passed as `STORYTELLER_PROJECT` |
| `view` | | Passed as `STORYTELLER_VIEW` |
| `endpoint` | | Passed as `STORYTELLER_ENDPOINT` |
| `client-id` | | Passed as `STORYTELLER_CLIENT_ID` (a GUID: GitHub OIDC federation) |
| `export-env` | `false` | Export the resolved configuration with `--github-env` |
| `env-prefix` | | `--env-prefix` |
| `allow-unprefixed` | `false` | `--allow-unprefixed` |
| `mask` | `bound` | `--mask` |
| `output-file` | | Also write the resolved configuration to this file, `--export <file> --format <format> --content-only`; best under `$RUNNER_TEMP` |
| `format` | `env` | Format of `output-file` |
| `cli-version` | required | The exact `42.Platform.Cli` version to install (for example `0.8.40-alpha-g1234567890`) |
| `cli-source` | | An additional NuGet source for the tool, for example a folder where the workflow packed `sform` |

- **Steps.** (1) Skip `actions/setup-dotnet@v6` (`10.0.x`) when `dotnet --list-runtimes` already lists `Microsoft.NETCore.App 10.`; otherwise run it, and the README states that it changes `DOTNET_ROOT` and `PATH` for the rest of the job. (2) Install with `dotnet tool install 42.Platform.Cli --version "$CLI_VERSION" --tool-path "$RUNNER_TEMP/storyteller-action/sform-$CLI_VERSION"` (plus `--add-source` for `cli-source`, never `--prerelease`), in `working-directory: ${{ runner.temp }}` so that the caller's `nuget.config` is not consulted, and reuse that folder when it exists. (3) Fail with a clear message when neither `export-env` nor `output-file` is set. (4) Run `sform story config "$KEY" --resolved` once, with `--github-env` when `export-env` is true, `--mask "$MASK"` otherwise, plus `--export "$OUTPUT_FILE" --format "$FORMAT" --content-only` when `output-file` is set, and the prefix options. The `env-prefix` reaches the file only when `format` is `env`.
- **Inputs never reach a script through `${{ inputs.* }}`.** Each input is passed through step `env:` under a private name (`SA_KEY`, `SA_PROJECT`, …), and the script exports `STORYTELLER_PROJECT`, `STORYTELLER_VIEW`, `STORYTELLER_ENDPOINT` and `STORYTELLER_CLIENT_ID` only when the input is non-empty, so a job-level value is never overridden by an empty input. Variables are quoted and the arguments built as a bash array. `shell: bash` on every step (Linux, macOS and Windows runners).
- **Credentials are not inputs.** They come from the job: `STORYTELLER_API_KEY`, client credentials, a certificate (`STORYTELLER_CLIENT_CERTIFICATE` / `_PASSWORD`, written to a file under `$RUNNER_TEMP` from a secret), or `permissions: id-token: write` with a GUID `client-id`.
- **Secrets need more than a credential.** Under the default project policy (`RequireMtlsForSecretDelivery`), only a certificate machine over mTLS (a front end that forwards the client certificate, `IsCertificateHeaderFromClientAllowed`) receives secret values; an API-key, client-credentials or federated machine needs the project policy set to false; every machine needs `Secrets.Read`. Otherwise the run fails on the withheld header or the unresolved bindings (section 7). The README, `github-action.md` and the `sform.md` section say so, and the federated example reads a configuration without secrets or names the policy.
- **What the job sees.** Every exported variable reaches every later step and action and is printed in their log headers; only masked values are hidden. The docs recommend `env-prefix`, `mask: all` for configurations whose secrets are stored literally, or `output-file` under `$RUNNER_TEMP` read by the one step that needs it.
- **Until `sform` is published** with these options, a workflow packs it first (`dotnet pack src/Platform/Cli/src -o "$RUNNER_TEMP/sform"`, then `cli-source` and the packed version); the README shows that recipe.

**Not done here, product owner decisions** (section 14): a dedicated repository (`42for-net/storyteller-action`, whose owner does not exist yet), Marketplace listing and `@v1` tags; publishing a `sform` with these options to NuGet.

### 9. CI for the action

No new workflow. Nothing in GitHub builds or tests .NET today, no Storyteller test instance or credential exists for a smoke test, and the security-relevant logic (masking, names, heredocs, refusals) is in `sform` with unit tests. The review records the checks that were possible locally: the action's scripts run in bash against a locally packed tool with a stubbed `GITHUB_ENV`, the YAML parses, and `actionlint` if it is available.

### 10. SDKs

- **Snapshot.** After the API commit, export `open.api.v0.10.<n>.json` (`STORYTELLER_OPENAPI_EXPORT`, `OpenApiExportTests`) into `Sdk.NSwag/` (history kept) and `sdk.typescript/` (replacing `0.10.0`); `generate.mjs` updates `package.json`, which is committed. The version stays `0.10-alpha`: 0.10 is not released yet (424-96 is open), and its breaking changes were accepted.
- **C# generated code.** Regenerate `ApiSdk.g.cs` with the README command (input renamed). The generated reads become `(…, key, string format, string if_None_Match[, CancellationToken])`; as in 424-96, the callers pass `format: null` (no compatibility overloads): `ConfigGetCommand` (until phase 4 moves it to the twins), `ConfigSetCommand`, `ConfigurationsApiClientDateTests`, `ConditionalReadsTests` (`:146-147`, `:164-165`). The generated methods cannot read a formatted body (NSwag keeps `Configuration` and `Accept: application/json`); the README says so.
- **C# twins.** `SendConditionalAsync` becomes generic over the result and takes an optional `format`, the `Accept` value and a reader for the 200. The query is appended exactly as the generated code appends it (`Append('?')`, `format=` and `Uri.EscapeDataString`, `Length--`), so with no format the URL is unchanged. New members of the partial interface and class:

  ```csharp
  Task<ConditionalResponse<string>> GetConfigurationFormattedConditionalAsync(string organization, string project, string view, string key, string format, string? ifNoneMatch, CancellationToken cancellationToken = default);
  Task<ConditionalResponse<string>> GetResolvedConfigurationFormattedConditionalAsync(… same …);
  ```

  `format` is required (`ArgumentNullException.ThrowIfNull`, like the route arguments). `Sdk.NSwag` keeps its own private, case-insensitive map from format to `Accept` (`flat` → `application/json`, `properties` and `env` → `text/plain`, `yaml` → `application/yaml`, anything else `*/*`) and does not reference the library; the server ignores `Accept`. The 200 is read with `ReadAsStringAsync` (an empty body is `""`), 304 has no result, errors map as for the envelope. The effective read now declares 422, so the `&& resolved` guard and its comment go, and the two 422 branches copy their regenerated descriptions as separate per-read messages; `ConditionalReadsTests.cs:180` becomes `[InlineData(false, 422, true)]`. Tests mirror the envelope ones (the URL with the query equal to the generated one, headers, every failure, an empty `env` body, a text body), with a media-type overload in `StubHttpHandler`.
- **TypeScript.** `scripts/openapi-transformer.mjs` removes the `format` parameter and the non-JSON media types of the 200 from `GetConfiguration` and `GetResolvedConfiguration`, so the generated functions, hooks, Zod schemas and mocks keep their envelope signatures and types (a review experiment with Orval 8.41.0 produced byte-identical output) and ui.admin needs no change. `lib/conditional.ts` gains `ConfigurationFormat = 'flat' | 'properties' | 'env' | 'yaml'` and `getConfigurationFormattedConditional` / `getResolvedConfigurationFormattedConditional(organization, project, view, key, format, options?)` returning `ConditionalResult<string>`. They build the URL as `` `${getGetConfigurationUrl(o, p, v, k)}?format=${encodeURIComponent(format)}` `` (resolved likewise). In `readConditional` the 304 check comes first, a non-ok response always goes through `readBody` and `StorytellerError.fromResponse` as today (so a 422 keeps its `errorCode`), and only a 200 goes through the body reader: the envelope reader keeps the object check, the text reader returns `await response.text()` (an empty body is `''`, a `flat` body the JSON text). Tests in `test/conditional.test.ts` (the URL, text and empty bodies, a 422 `format.case-collision` with its `errorCode`); `npm run generate`, the committed `generated/` and `package.json`, lint, typecheck, test and build, and the ui.admin lint, test and build stay green.

### 11. Documentation

Per phase, so that every commit leaves the docs and the site consistent:

- **Phase 1.** New `docs/Platform/Storyteller/configuration-formats.md`: the formats, the rules (sections 2-3) with the readers each format serves and their limits (Spring, MicroProfile and Quarkus, dotenv readers and verbatim readers, YAML 1.2), the env charset and dialect, the YAML gap, the parity guarantee and the vector files. `configuration-provider.md`: "Flattening" and the code map point to the library and the new page, with the control-character escaping. The library's `package-readme.md`.
- **Phase 2.** `configuration-formats.md` gains the API section (query values, media types, `format.<rule>` errors, tags and manifests per format, billing of a 422 after a resolve). `inheritance.md`: "Other reads" (`?format=`), "The tags" (the representation input), "The dependency manifest" (per format, and its billing and audit note), "Headers" (format errors carry none), "Limits". `binding.md` (the 422 list of the resolved read). `docs/42for.net/platform/using-the-platform.md`: `?format=` in "What a service reads", the ETag per format.
- **Phase 3.** `Sdk.NSwag/README.md` (snapshot name; the generated reads take `format` then `if_None_Match` after `key`, pass null for both; the composition read keeps `if_None_Match` after `key`; the 422 is typed on both reads; the formatted twins and the members they rely on), `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`, `sdk.typescript/README.md` (snapshot name, the new transformer correction in its list, the formatted helpers, a 422 is a binding or `format.<rule>`).
- **Phase 4.** `docs/42for.net/cli/sform.md`: a "Read a configuration" section (options, the output table, stdout and stderr, UTF-8 and PowerShell 5.1, exit codes including parsing errors, `--github-env`, `--mask`, the prefix and the denylist, masking and its limits, what later steps see, the secret-delivery prerequisites), the warning at `:125-127` rewritten. `templating.md:315` (the `sform story config` syntax). `key-values.md` (the key/value `flat`/`env` file formats are not the configuration formats). `authentication.md`: next to the sform GitHub Actions examples (`:342-383`), reading a configuration in the job with `--github-env`; the federated token-exchange example at `:1046-1071` stays as it is. No page links to `/cli/github-action` yet.
- **Phase 5.** New public page `docs/42for.net/cli/github-action.md` with its sidebar entry after `sform` (one commit), links to it from `sform.md` and `authentication.md`, and the action's `README.md`. `CLAUDE.md`: the new library and the action in the Platform table.

### 12. Security notes

- No configuration value appears in an exception message, a 422 body, stderr, a summary or telemetry; paths are sanitized (section 1).
- Masks are written before any value reaches `GITHUB_ENV` or a file. Every exported variable is shown in the `env:` block of every later step's log and is visible to every later action; only masked values are hidden. Without C6, `bound` masking is a heuristic: it over-masks bound public values, cannot mask a secret stored literally in a document, and masks a secret interpolated into a longer string only as that whole string. `mask: all`, a prefix and `output-file` are the documented mitigations; secret key/values and vault references are the documented way to store secrets.
- `env` and `--github-env` names come from configuration authors. The required prefix (or an explicit `--allow-unprefixed`), the denylist, and the refusal of NUL and oversized values keep them from steering later steps.
- The action passes inputs through the environment only, takes no secret inputs, installs an exact `sform` version into its own folder, and does not read the caller's `nuget.config`.

### 13. Phases and commits

| Phase | Content | Commits |
|---|---|---|
| — | This spec | `docs(platform): plan the flat output formats of configurations` |
| 1 | Sections 1-4, phase 1 docs | `refactor(platform): move the configuration flattener into a shared formats library`, `feat(platform): render configurations as flat, properties, env and yaml` |
| 2 | Sections 5-6, phase 2 docs | `feat(platform): serve configurations in flat, properties, env and yaml formats` |
| 3 | Section 10, phase 3 docs | `feat(platform): regenerate the SDKs and read formatted configurations conditionally` |
| 4 | Section 7, phase 4 docs | `feat(platform): print configurations in flat formats and export them to GitHub Actions from sform` |
| 5 | Sections 8-9, phase 5 docs | `feat(platform): add a GitHub Action that exports a configuration to the job environment` |
| — | One review per phase | `docs(platform): review the flat output formats of configurations` |

Each phase leaves the solution building and its tests green: `ConfigurationFormats/test`, `Configuration/test`, `Configuration.KeyVault/test`, `Api.Functions/test`, `Sdk.NSwag/test`, `src/Platform/Cli/test`; in phase 3 the TypeScript SDK and ui.admin checks. No commit message uses `!` or the words that `mrepo` reads as a breaking change.

### 14. Out of scope

- Publishing `sform` to NuGet, a dedicated action repository, Marketplace and `@v1` tags (product owner; see section 8).
- `list-executions` and `outputs.executions` of the study's example (catalog listing, C4/C13), GitLab and Azure DevOps equivalents.
- `?format=` on the composition and version reads, content negotiation by `Accept`, a prefix on the API's `env`.
- The External Secrets Operator recipe of study 12.2: the envelope's `$.Content` works today, `?format=env` returns a body for a single `Secret` key, and `flat` keys contain `:`, which `Secret` keys do not allow. A verified recipe is left to the integration work.
- Secret paths in the resolved read (C6), which would make masking exact.
- The conformance suite itself (424-105, C22): this spec adds vector files in the existing shape.
- Spring Cloud Config and other protocol adapters (C12), the local agent (P2), cloud projections (P4).
- Moving `Program`-level errors of `sform` to stderr; a .NET CI workflow.
- The pre-existing date handling of the envelope (the server parses date-like strings into dates and writes them in Newtonsoft's ISO form); formats follow the envelope.

### 15. Differences from the CodeRabbit plan

- **Namespace.** The plan's `_42.Platform.Storyteller.Configuration.Formatting` collides with the `Configuration` type; this spec uses `_42.Platform.Storyteller.ConfigurationFormats`.
- **YAML without YamlDotNet** at run time, so the provider package does not pull it into applications; YamlDotNet only checks the output in tests.
- **Parity by canonicalization.** The plan relies on the shared library alone; the server's date and decimal tokens would still format differently from the client's, so the formatter canonicalizes.
- **`properties` brackets names** with Spring's form instead of rejecting `.`, because .NET documents routinely contain names like `Microsoft.AspNetCore`; only `[` and `]` are rejected, and array gaps, which Spring cannot bind. **`env` allows `.` and `-`** for the same reason (Kubernetes' charset), keeping `__` unambiguous.
- **Query values and errors.** An empty value and `json` are the envelope, as elsewhere in the API; an unknown format is the existing `400 InvalidQuery`, not a new `format.unknown`; 422s are `format.<rule>` with `Details`.
- **Tags and manifests.** No `DependencyManifest.Format`: the format is in the `r` tag that manifests already must match. No media types on the 304, which must stay bodyless. The 400 already exists; the missing piece is the effective 422.
- **SDK compatibility.** No overloads with the old signatures: 424-96 updated its callers instead, the provider never calls the generated reads, and no Moq setup of them exists on this branch. The TypeScript transformer keeps the generated signatures, so ui.admin is untouched.
- **sform reads through the twins** to see `X-2S-Secrets-Withheld`, fails `--github-env` and `--mask` on withheld secrets and unresolved bindings, writes UTF-8 with LF, and keeps stdout free of diagnostics.
- **Masking** skips booleans and values shorter than 4 characters under `bound`, adds `--mask all`, checks `Hash` as well as `Version`, and masks a file written in the same run. The denylist is much wider, a prefix is required unless explicitly waived, and NUL and oversized values are refused.
- **The action** runs `sform` once (one effective and one resolved read for both the variables and the file, which is written with `--export`, not a shell redirection), uses `STORYTELLER_ENDPOINT` (not the deprecated `STORYTELLER_API_URL`), requires an exact `cli-version` (no prerelease of today's NuGet feed can run it), and has no release or smoke-test workflow: there is no target repository, deploy token, test instance or published `sform` to run it with.
- **Taken from the plan:** one library over structured leaves, rejection modelled on `colon-in-name`, formatting before every response, the formatter version in the tags, a raw `OkOrNotModified`, a document filter modelled on the diff's, formatted twins returning `ConditionalResponse<string>` and a body reader in `readConditional`, local formatting in `sform`, a tested `--github-env` mode behind a thin composite action, masking by comparing the resolved read with the effective read of the same version, heredocs with a checked random delimiter, inputs only through `env:`.

### 16. Risks

- **Stacked on 424-96** (PR #135, open). This branch and its PR are based on `feat/424-96`; a change there means a rebase, and the snapshot must be exported again from the combined API.
- **Merge order with other open PRs.** #133 changes `ConfigurationHttp.cs` and `inheritance.md`; #131/#132 add the MCP server, which calls the generated configuration reads (it must pass `format: null` as it must pass `if_None_Match: null`) and adds projects to `42.mono.slnx` at the same place. Whichever merges later re-exports the snapshot and regenerates both SDKs.
- **424-105 (C22)** may move the vectors or change their shape; the new files follow the existing shape to make that a move.
- **The action cannot run from NuGet until a `sform` with these options is published**; until then a workflow packs `sform` itself (`cli-source`).
- **`actions/setup-dotnet`** changes the caller's .NET for later steps when the runner has no .NET 10; the action skips it when .NET 10 is present and documents the effect.
