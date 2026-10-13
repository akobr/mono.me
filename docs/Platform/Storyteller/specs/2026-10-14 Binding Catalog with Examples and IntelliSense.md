# Binding Catalog with Examples and IntelliSense

Linear: [424-10 Extend "fx" functionality with examples and intellisense](https://linear.app/424fun/issue/424-10/extend-fx-functionality-with-examples-and-intellisense)

The issue asks for examples and IntelliSense wherever the system shows "fx" (a binding expression), built over the registered binding functions, the built-in expressions (string interpolation, number calculations) and the bindings to key-value storage. Tooltips or floating views are suggested.

The work is split into two phases. Each phase gets its own review:

1. **API:** a catalog of the registered binding functions and sources, described by the bindings themselves, and an endpoint that returns it.
2. **Administration UI:** completion and hover tooltips in the code editors, a floating binding reference with examples, and an explanation of the expression in the schema form.

## Problem

1. **"fx" is only a marker.** The admin UI highlights a binding and draws "fx" next to it, but nothing says what can be written there. An author has to know the `@` language from [binding.md](../binding.md): the five string forms, the quoting rule for function arguments, the names of the functions and sources, and the envelope keys.
2. **Clients cannot know what is registered.** Functions are registered in `Api.Functions/src/Program.cs`. Sources depend on the deployment: one Azure Key Vault source per entry of the `AzureKeyVaults` configuration section. No endpoint lists them, so a client cannot offer `@(db.password, primaryVault)` with the right source name.
3. **Paths are typed blind.** `@config("…")` reads the configuration being resolved and `@annotation("…")` reads the `Values` of the annotation (or of an ancestor). Both are the platform's own key-value storage, and the UI already holds both documents, but the author has to type JSON Pointers or JSONPath by heart.
4. **Wrong examples.** The admin UI documentation (`docs/42for.net/platform/admin-ui.md`, line 117), the mock data (`app/mock/documents.ts`, line 85) and the mock schema description (`app/mock/schemas.ts`, line 15) show `@sbt.northwind/baseUrl`. That does not parse: `/` is not a path character, and `@sbt.northwind…` would be looked up in the `default` source, not in the subject.

## Current State

### Server

- **Registry.** `Binding.Abstractions/src/IBindingRegistry.cs` has `RegisterSource(key, source)` and `RegisterFunction(name, function)`. `Binding.Language/src/BindingExecutor.cs` implements it with two `ConcurrentDictionary` instances (ordinal names). Nothing reads the registrations back.
- **Wiring.** `Binding.Core/src/EntryPoint.cs` (`AddConfigurationBindings`) builds the `BindingExecutor` from `BindingsOptions` and registers it as `IBindingRegistry` and `IBindingExecutor`.
- **Registered bindings** (`Api.Functions/src/Program.cs`, lines 122–133):
  - function `config` (`Binding.Language/src/ConfigBindingFunction.cs`): one quoted argument, a JSONPath (`$…`) or JSON Pointer (`/…`) into the snapshot of the document being resolved;
  - function `annotation` (`Binding.Core/src/AnnotationBindingFunction.cs`): a quoted JSONPath or JSON Pointer into `Annotation.Values`, and optionally a quoted annotation type name (`Enum.TryParse`, case-insensitive) that selects an ancestor through `AnnotationKeyExtensions.TryGetAncestorKey`;
  - one `KeyVaultBindingSource` per `AzureKeyVaults:<key>` entry (`Binding.Azure.KeyVault/src/EntryPoint.cs`), registered under that key. It declines (returns `null`) without `IncludeSecrets`, and turns the path into a secret name joined by `--`.
- **Language** (`Binding.Language/src/Tokenizer.cs`): identifiers start with a letter or `_` and continue with letters, digits or `_`. Paths are identifiers joined by `.`. A hyphen is not an identifier character.
- **Endpoints.** Nothing describes bindings. The closest existing metadata endpoint is `GetAuthConfiguration` (`V1/AuthConfigurationHttp.cs`). Project-scoped reads such as `GetViews` (`V1/ViewsHttp.cs`) check scopes, then `CheckAccessToProjectAsync`.
- **SDKs.** `Sdk.NSwag` and `sdk.typescript` are generated from `open.api.v0.9.1.json`, exported from the running Functions host (`/api/openapi/v3.json`).

### Administration UI (`src/Platform/ui.admin/src`)

- **Detection** (`app/utils/bindings.ts`): `isBindingString` (a string starting with `@`), `isEnvelope` (an object with `$jsonlogic`, `$jsone` or `$definition`) and `isBinding`.
- **Code editors** (`app/components/editor/setup/bindings.ts`): `bindingDecorations()` returns a `ViewPlugin` that marks `"@…"` strings, YAML scalars after `: ` or `- `, and the envelope keys, plus an "fx" gutter whose title is always "Binding expression, evaluated by the server". It is used by:
  - `components/configuration/ConfigurationEditor.vue` (Edit tab, JSON and YAML; line 93, together with the schema linter);
  - `components/configuration/ConfigurationEffective.vue` (Effective tab, read-only; line 12);
  - `components/templates/TemplateDetail.vue` (line 218, as an inline `[bindingDecorations()]`, so a new extension array on every render).
- **No completion.** `CodeEditor.vue` adds `completionKeymap` but not the `autocompletion()` extension, and neither JSON nor YAML language support brings a completion source. Nothing shows a tooltip on hover.
- **Schema form.**
  - `renderers/ControlField.vue`, lines 87–97: the "fx" toggle turns a value into `@<value>`, with the tooltip "Use a binding expression (@…)".
  - `BindingExpressionInput.vue`: a monospace `UInput` with a static "fx" badge (line 32) for `@…` strings, a JSON `DocumentEditor` for envelopes, and the line "Evaluated by the server; schema checks run after evaluation." with **Convert to value**.
  - `renderers/context.ts` provides the form context (`provideSchemaForm` / `useSchemaForm`) to every control.
- **Mock API** (`app/mock/*.ts`): hand-written handlers come first (`all-handlers.ts`), and the Orval-generated Faker handlers answer everything else.
- **Tests:** `test/unit/configuration/schema-form.test.ts` covers the fx toggle and the rendering of a binding value. Nothing covers `setup/bindings.ts`. `mountInApp` installs no Vue Query and no Pinia, so a form control cannot depend on a query.

## Proposed Changes

### Phase 1 — Binding catalog in the API

#### 1.1 Self-description (`Binding.Abstractions`)

A binding may describe itself. The interface is optional, so custom functions and sources keep working unchanged.

```csharp
namespace _42.Platform.Storyteller.Binding;

/// A binding source or function that can describe itself for the binding catalog.
public interface IDescribedBinding
{
    /// <param name="name">The name the binding is registered under (a source key or a function name).</param>
    BindingDescription Describe(string name);
}

public sealed record BindingDescription
{
    public required string Summary { get; init; }           // one sentence, Markdown allowed
    public string? Signature { get; init; }                  // functions: config("<expr>")
    public IReadOnlyList<BindingParameterDescription> Parameters { get; init; } = [];
    public IReadOnlyList<BindingExample> Examples { get; init; } = [];
    public bool IsSecret { get; init; }                      // sources: values are secrets, resolved only with secrets
}

public sealed record BindingParameterDescription
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsOptional { get; init; }
    public IReadOnlyList<string> Values { get; init; } = []; // the allowed values, when the set is closed
}

public sealed record BindingExample
{
    public required string Expression { get; init; }         // the stored string, e.g. @config("/maxPrice")
    public required string Description { get; init; }
}
```

`Describe` takes the registered name because a source does not know its key (`KeyVaultBindingSource` is created per vault), and examples must use it.

#### 1.2 Catalog (`Binding.Abstractions`, `Binding.Language`, `Binding.Core`)

```csharp
public interface IBindingCatalog
{
    BindingCatalog GetCatalog();
}

public sealed record BindingCatalog
{
    public IReadOnlyList<BindingFunctionDescriptor> Functions { get; init; } = [];
    public IReadOnlyList<BindingSourceDescriptor> Sources { get; init; } = [];
}

public sealed record BindingFunctionDescriptor
{
    public required string Name { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Signature { get; init; }
    public IReadOnlyList<BindingParameterDescription> Parameters { get; init; } = [];
    public IReadOnlyList<BindingExample> Examples { get; init; } = [];
}

public sealed record BindingSourceDescriptor
{
    public required string Name { get; init; }
    public string Summary { get; init; } = string.Empty;
    public bool IsDefault { get; init; }                    // registered under "default": @path.to.value
    public bool IsSecret { get; init; }
    public IReadOnlyList<BindingExample> Examples { get; init; } = [];
}
```

- `BindingExecutor` implements `IBindingCatalog`. Functions and sources are sorted by name (ordinal). A binding that implements `IDescribedBinding` contributes its description. Any other binding is listed by name only, with an empty summary, so the catalog always lists everything that is registered.
- `AddConfigurationBindings` registers `IBindingCatalog` over the same `BindingExecutor` singleton (`TryAddSingleton`, like `IBindingRegistry`).
- The catalog holds names and texts only. It never resolves a value and never touches a source.

#### 1.3 Descriptions of the registered bindings

| Binding | Summary | Parameters | Examples |
| --- | --- | --- | --- |
| `config` (`ConfigBindingFunction`) | Reads a value of the configuration being resolved, from the snapshot taken before evaluation. | `expr`: a quoted JSON Pointer (`/a/b`) or JSONPath (`$.a.b`). | `@config("/maxPrice")`, `@config("$.endpoints.db")` |
| `annotation` (`AnnotationBindingFunction`) | Reads a field of the `Values` of the annotation being configured, or of one of its ancestors. | `expr` as above; `type` (optional): a quoted annotation type name, `Values` = `Responsibility`, `Unit`, `Subject`, `Usage`, `Context`, `Execution`. | `@annotation("/owner")`, `@annotation("/currency", "Subject")` |
| Key Vault source (`KeyVaultBindingSource`) | A secret of the Azure Key Vault registered as `<name>`. The path becomes the secret name joined by `--`. Resolved only for callers that may read secrets. `IsSecret = true`. | — | `@(db.password, <name>)`; when `<name>` is `default` also `@db.password` |

#### 1.4 Endpoint `GetBindingCatalog`

- Route `v1/{organization}/{project}/bindings` (`Definitions.Routes.Bindings.V1.Bindings`), `GET`, operation id `GetBindingCatalog`, tag `Configurations`, in a new `V1/BindingsHttp.cs`.
- The registrations are global today. The route is project-scoped anyway, so it is guarded like every other project read and can later list sources a project registers.
- Scopes `Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.Write`, then `CheckAccessToProjectAsync` (Reader). Machines are allowed: the catalog is read-only and contains no values.
- `200` with `BindingCatalog`; `401`, `403` (from the existing document filter) and `500` as on the other reads.
- The class takes `IBindingCatalog?`. When bindings are not registered, the endpoint returns an empty catalog.

#### 1.5 OpenAPI and SDKs

- Export `/api/openapi/v3.json` from the running Functions host, as in the Phase 2 review of soft-delete: build into a temporary folder, serve it on a second port, set `servers` back to `http://localhost:7071/api`. The file name follows the version of the build (`open.api.v0.9.<height>.json`), in `Sdk.NSwag` and `sdk.typescript`.
- Regenerate `Sdk.NSwag/src/ApiSdk.g.cs` with the README command and update the README to the new file.
- `npm run generate` in `sdk.typescript/src`. The new composable is `useGetBindingCatalog(org, project)`; the request function is `getBindingCatalog`.
- The superseded documents stay, as in the earlier exports.

#### 1.6 Tests (Phase 1)

- `Binding.Language/test`: the catalog lists registered functions and sources sorted by name, uses `IDescribedBinding` when it is implemented and the bare name otherwise, marks `default` as `IsDefault`; `ConfigBindingFunction.Describe` has the signature and examples.
- `Api.Functions/test`:
  - `BindingsHttpTests`: the endpoint returns the catalog of the injected `IBindingCatalog`, and an empty catalog without one; a request without a matching scope is rejected before the catalog is read.
  - `RouteTableTests`: `GET v1/{}/{}/bindings` is registered and unique.
  - `OpenApiDocumentTests`: the `BindingCatalog` schema publishes `Functions` and `Sources`.
- `Binding.Azure.KeyVault` and `Binding.Core` have no test projects. Their descriptions are covered through a catalog test in `Binding.Language/test` that registers described doubles, and through the API test; adding test projects only for two texts is not worth it.

#### 1.7 Documentation (Phase 1)

`docs/Platform/Storyteller/binding.md`: a new section **Binding catalog** (the interfaces, the endpoint, how a custom function describes itself), and `IBindingCatalog` / `IDescribedBinding` in the project structure.

### Phase 2 — Administration UI (`src/Platform/ui.admin`)

#### 2.1 Binding language model (`app/utils/binding-language.ts`)

A small, dependency-free model of the `@` language for the UI. It does not evaluate anything.

- **Built-in expressions**, a static list with `id`, `title`, `summary` (Markdown), `syntax`, a `snippet` (a CodeMirror snippet template) and `examples`:
  - `path` — `@path.to.value`, the `default` source (only offered when a `default` source is registered);
  - `sourced` — `@(path.to.value, source)`;
  - `function` — `@name(arg, …)`, with the quoting rule;
  - `interpolation` — `@[text @statement text]`, with the escapes `\@`, `\]`, `\\`;
  - `math` — `@{(@a + 10) * 2}`, operators `+ - * / %`;
  - `jlogic` and `jsone` envelopes — `{"$binding": "jlogic", "$definition": …, "$context": {…}}`, with one example each from binding.md.
- **`explainBinding(text, catalog)`** classifies a stored string (decoded, starting with `@`):
  - returns `{ kind, name?, entry?, problem? }`, where `kind` is `path | sourced | function | interpolation | math | invalid`;
  - `entry` is the matching function or source of the catalog;
  - `problem` is a short text when the shape is wrong (for example `/` in a path, a missing `)` or `]`, an unknown function or source, an unquoted function argument). It is a hint, not a parser: the server stays the judge, and the save is never blocked.
- **`bindingCompletionContext(prefix)`** decides what can follow the text before the cursor (the decoded string from its `@` up to the cursor):
  - `start` — right after `@`, or after `@` inside an interpolation or a math expression: functions, sources, and (top level only) the interpolation and math forms;
  - `source` — after `@(path, `: source names;
  - `argument` — inside the first quoted argument of `config` or `annotation`: JSON Pointers;
  - `annotationType` — inside the second quoted argument of `annotation`: the ancestor types that exist for the key;
  - `none` — anything else.
- **Quoting.** Snippets contain `"`. In a JSON string they are inserted as `\"`, in a single-quoted YAML scalar as is, and a double-quoted YAML scalar like JSON. `quoteFor(delimiter)` returns the right escape.
- **`pointersOf(value, limit = 200)`**: the JSON Pointers of a document, breadth first (objects and arrays, then leaves), skipping `$`-envelope internals.
- **`ancestorKeyOf(key, type)`**: the ancestor key of a type, mirroring `TryGetAncestorKey` (built from the parsed key parts with `buildAnnotationKey`; `undefined` when the type is not an ancestor; `UnitOfExecution` never is).

#### 2.2 Binding assist (`app/composables/useBindingAssist.ts`)

One object that feeds every fx surface:

```ts
interface BindingAssist {
  catalog: ComputedRef<BindingCatalogView>         // built-in expressions + registered functions and sources
  configPointers: () => string[]                   // JSON Pointers of the effective document
  annotationPointers: (type?: string) => Promise<string[]>  // of Values: own annotation, or the ancestor of `type`
  annotationTypes: () => string[]                  // ancestor types that exist for the key
}
```

- `useBindingAssist(options: { annotationKey?: MaybeRefOrGetter<string | undefined>; document?: () => unknown })` asks `useGetBindingCatalog(org, project)` (stale for 10 minutes, `meta: { silent: true }`). When the request fails, or while it runs, the catalog holds the built-in expressions and the two functions every deployment registers (`config`, `annotation`) as a fallback.
- Annotation values come from the Vue Query cache through `queryClient.fetchQuery` of `GetAnnotation`, so the own annotation (already loaded by the detail) costs nothing, and an ancestor is fetched once.
- `provideBindingAssist(assist)` / `injectBindingAssist()` (an `InjectionKey`, like `renderers/context.ts`). Without a provider, `injectBindingAssist()` returns a static assist with the fallback catalog and no pointers, so the schema form works in tests and anywhere it is mounted.

#### 2.3 CodeMirror: completion and hover (`app/components/editor/setup/bindings.ts`)

`bindingDecorations(assist?: BindingAssist)` keeps the decorations and the gutter and adds:

- **Completion.** `autocompletion()` with a source added through `EditorState.languageData` (so other sources can coexist). It runs only when the cursor is inside a binding string: a JSON string that starts with `"@`, or a YAML scalar (plain, `'…'` or `"…"`) after `: ` or `- ` that starts with `@`. It answers the contexts of 2.1:
  - functions as snippets (`config(\"${/path}\")`), with `detail` = signature and `info` = summary and first example;
  - sources as `(${path}, primaryVault)` snippets, with a lock icon text for secret sources;
  - interpolation and math snippets at the top level;
  - JSON Pointers and annotation types inside the quoted arguments;
  - typing `@` at the start of a JSON string value opens the list (`activateOnTyping`).
- **Hover.** `hoverTooltip` over a highlighted range shows a floating card: the kind and name, the summary, the signature or syntax, the first example, the `problem` of `explainBinding` when there is one, and "Evaluated by the server when the resolved configuration is read". Over the envelope keys it shows the JSON Logic or JSON-e card.
- **Gutter.** The "fx" marker keeps its title.
- The `"@…"` pattern of the decorations stays; the YAML pattern also accepts quoted scalars (`'@…'`, which the YAML serializer writes because `@` is reserved in plain YAML).

#### 2.4 Binding reference (`app/components/configuration/BindingReference.vue`)

A floating view (a `UPopover` with a trigger slot) that lists, in sections:

1. **Expressions** — the built-in forms with their syntax and examples;
2. **Functions** — the registered functions with signature, parameters (and their allowed values) and examples;
3. **Sources** — the registered sources, secret ones marked, with examples;
4. **Object bindings** — the JSON Logic and JSON-e envelopes.

- A filter input narrows the list by name or text.
- Each example has **Copy**. When the parent passes `insertable`, it also has **Use**, which emits `insert` with the example expression.
- A link "Binding reference" points to the user documentation (`/platform/configuration#bindings` on 42for.net).

#### 2.5 Schema form

- **`BindingExpressionInput.vue`**:
  - the "fx" badge in the leading slot becomes the trigger of `BindingReference` (`insertable`; **Use** replaces the value);
  - the line under the input explains the expression with `explainBinding`, for example "Function `config`: Reads a value of the configuration being resolved." or "Source `primaryVault` (secret)". A `problem` is shown in the warning color instead. The text "Evaluated by the server; schema checks run after evaluation." stays, with **Convert to value**;
  - envelopes keep the JSON editor and get `bindingDecorations(assist)` too.
- **`ControlField.vue`**: the tooltip of the fx toggle says "Use a binding expression (@…): a function, a source, text or math".
- The assist comes from `injectBindingAssist()`; the form needs no new props.

#### 2.6 Editors

- `ConfigurationEditor.vue`: `useBindingAssist({ annotationKey, document: () => effective content })`, `provideBindingAssist`, and `bindingDecorations(assist)`. A `BindingReference` button ("fx Bindings") is added to the editor actions in JSON and YAML; it is not `insertable` there (the examples are copied).
- `ConfigurationEffective.vue`: the same assist for hover (read-only; completion never triggers).
- `TemplateDetail.vue`: an assist without an annotation key (config pointers from the draft; no annotation pointers, since a template has no annotation), the reference button, and a constant extension array instead of the inline one.

#### 2.7 Mock API

`app/mock/handlers.ts`: `GET */api/v1/:org/:project/bindings` (Reader) returns `config`, `annotation` and a secret source `primaryVault`, with the descriptions of 1.3. The mock binding values become valid expressions: `endpoint: '@annotation("/baseUrl", "Subject")'` in `documents.ts`, and the description in `schemas.ts` says the same.

#### 2.8 Tests (Phase 2)

- **Unit**
  - `test/unit/utils/binding-language.test.ts`: `explainBinding` for each kind, unknown names and the common mistakes; `bindingCompletionContext` for each context; `quoteFor`; `pointersOf`; `ancestorKeyOf` for every type of an `uxe` key and for invalid types.
  - `test/unit/configuration/binding-editor.test.ts`: an `EditorState` with `bindingDecorations(assist)` and the JSON language; the completion source (called with a `CompletionContext`) offers the functions after `"@`, escaped snippets in JSON, sources after `@(a.b, `, config pointers in `@config("/`, and annotation types in the second argument; nothing outside a binding string. YAML single-quoted strings get unescaped quotes.
  - `test/unit/configuration/binding-reference.test.ts`: the sections, the filter, Copy, and **Use** emitting `insert`.
  - `schema-form.test.ts`: the explanation line for a valid expression and for a malformed one, and **Use** from the reference replacing the value.
- **End to end** (`test/e2e/configuration.spec.ts`): in the JSON editor of a configuration, typing `"@con` offers `config`, and accepting it inserts `@config(\"/…\")`; hovering a binding shows the tooltip; the form's fx reference lists `primaryVault` from the mock API.
- **Accessibility:** the existing axe check of the configuration detail covers the new button.

#### 2.9 Documentation (Phase 2)

- `docs/42for.net/platform/admin-ui.md`, Configuration section: replace the invalid `@sbt.northwind/baseUrl`, and describe completion, hover and the binding reference.
- `docs/42for.net/platform/configuration.md`, Bindings: one sentence that the admin UI lists the functions and sources a deployment registers.

## Out of scope

- A full client-side parser of the `@` language, and evaluation or preview of an expression in the UI. The resolved tab already shows the evaluated document.
- Completion of Key Vault secret names. Listing secrets would need a new permission and a call to the vault; the catalog names the vault only.
- Completion inside `$definition` of JSON Logic or JSON-e programs.
- The CLI (`sform`). It has no fx surface today.
