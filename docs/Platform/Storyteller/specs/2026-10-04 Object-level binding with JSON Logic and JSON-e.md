# Object-level binding with JSON Logic and JSON-e

Line numbers refer to the tree this plan was written against. No application code is changed by this document.

"JLogic" in the request is [JSON Logic](https://jsonlogic.com/). There is no NuGet package named JLogic. The envelope discriminator for that language is the string `jlogic`. "Json-e" is [JSON-e](https://json-e.js.org/). Its discriminator is `jsone`.

## Problem

Configuration binding rewrites a resolved configuration while it is being read. It only rewrites string values, and only when the whole string is one `@` expression. A property whose value is an object or an array is walked so that strings inside it can be rewritten, and is otherwise left as stored.

Authors need a value that is computed as a whole JSON value: a boolean decision, a filtered array, a merged object, a string built from several context fields, or a structure whose shape depends on the context. String interpolation and `@{ ... }` math cannot produce an object, omit a property, or branch on structured data.

The stored shape that should trigger this is an object envelope:

```json
{
  "$binding": "jsone",
  "$definition": {},
  "$context": {}
}
```

`$binding` selects the language (`jlogic` or `jsone`). `$definition` is the program in that language. `$context` is the data the program reads. The envelope is replaced by the value the program returns. That value may itself contain `@` strings and further envelopes.

## Current State

### String binding

`docs/Platform/Storyteller/binding.md` describes the `@` language. `IBindingExecutor` (`Binding.Abstractions/src/IBindingExecutor.cs`) exposes `TryBinding` for a `JProperty` and for a `JValue`. `BindingExecutor` (`Binding.Language/src/BindingExecutor.cs`, lines 39–48) returns false unless the token is a string whose first character is `@`. A resolved value can be any `JToken` (a math expression becomes a number; a source can return a structured secret). The executor assigns that token back onto the property or replaces the `JValue`.

`BindingScope` carries two pieces of ambient data into every function call:

- `Document`, an immutable deep clone of the configuration taken before the binding pass. `@config` reads this snapshot, so one binding never observes another binding's output.
- `Context`, a `ConfigurationBindingContext` with the `FullKey` of the configuration being resolved. `@annotation` requires it.

Sources and functions are registered on `BindingExecutor` through `BindingsOptions` in `AddConfigurationBindings` (`Binding.Core/src/EntryPoint.cs`). `Api.Functions/src/Program.cs` (line 83) and the Cosmos test startup register `@config` and `@annotation` there. Key Vault is a source that declines unless `includeSecrets` is true; a declined top-level path leaves the original `@...` string in place.

`BindingException` is not mapped to a client error. `ExceptionHandlingMiddleware` turns it into HTTP 500, the same as any other unhandled exception.

### Where resolution walks the document

`CosmosConfigurationService.GetResolvedConfigurationInternalAsync` (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`, lines 592–655) loads the calculated document, builds the scope, and walks `config.Content`:

- A string property or string array item is passed to `TryProcessDataBinding`, which guards on `@` and calls `IBindingExecutor`.
- An object is enqueued. The comment on line 628 is `TODO: [P3] logic operations and templates processing`.
- An array enqueues its object items and binds its string items.

The constructor takes `IBindingExecutor` as an optional parameter (line 33). When it is null the method returns the calculated document unchanged. The service is constructed by DI. Nothing in the repository calls `new CosmosConfigurationService`.

Resolution runs on every resolved read. It does not run on write, on schema validation, or on template merge. `CalculatedContent` is the document before this walk. `GetResolvedConfigurationAsync` (and the with-secrets / without-secrets variants) are the reads that walk. `templating.md` (the paragraph "What each read returns") already states that template strings are bound by the same rules, and points at the same TODO.

`Configuration.Content` is a `JObject` (`Abstractions.Annotations/src/Model/Configuration.cs`, line 12). The root of a configuration is always an object.

### Dollar-prefixed instructions that already exist

`$remove` and `$patch` are write-time instructions. `CreateOrUpdateConfigurationAsync` applies them to stored content. Calculation merges template JSON with `MergeInto` only, so a `$remove` or `$patch` property inside a template is copied into the calculated document as an ordinary property (`templating.md`, section "Merge"). Object binding adds a third dollar-prefixed family, and it is read-time.

### JSON libraries already in the solution

The binding projects use Newtonsoft.Json 13.0.4 (`JToken`, `JObject`, `JValue`). `Directory.Packages.props` also pins `JsonPatch.Net` 5.0.2, used by `Backend.CosmosDb` and the CLI to apply RFC 6902 patches. `JsonPatch.Net` is part of [json-everything](https://github.com/json-everything/json-everything) and uses `System.Text.Json`. Version 5.0.0 of that package (1 February 2026) is the release that added the maintainer's binary EULA. The repository already restores a post-fee build of that publisher's library.

Central package management is on (`Directory.Build.props`). Transitive pinning is off, so only direct `PackageReference`s need a version in `Directory.Packages.props`. Versions stay alphabetical.

`Binding.Language` references Newtonsoft.Json and `Binding.Abstractions` only. `Binding.Core` references `Binding.Language` and registers the executor. `Backend.CosmosDb` reaches the binding interfaces through `Backend.Core` → `Binding.Abstractions`. It does not reference `Binding.Language`.

## Options

### The two languages

Both languages store a program as JSON and apply it to a separate JSON value. Both are specified to be safe on untrusted documents: they do not call arbitrary host code, and JSON-e additionally refuses unbounded iteration. Neither language's native call looks like the envelope above. The native calls are `JsonLogic.Apply(rule, data)` and `JsonE.Evaluate(template, context)`. The envelope is a Storyteller wrapper that names the language and holds both arguments in one object, so a single walk of a configuration can find it.

| | JSON Logic (`jlogic`) | JSON-e (`jsone`) |
| --- | --- | --- |
| Program | One operator object, such as `{ "<": [ { "var": "temp" }, 110 ] }`. A literal is returned as itself. | A template of any JSON value. Objects with a `$` property are operators (`$eval`, `$if`, `$map`, `$merge`, `$mergeDeep`, `$let`, `$sort`, `$switch`, `$fromNow`, and the others in the JSON-e operator list). |
| Data access | `var` reads the data argument. Dot paths and a default are part of the operator. Inside `map`, `filter`, and `reduce`, `var` is relative to the element. | Expressions read names from the context object. `${name}` interpolates inside strings. |
| What it is good at | Predicates, comparisons, `missing` / `missing_some`, `filter`, `all` / `some` / `none`, strict `===`, `substr`, `cat`. | Building and reshaping documents: keeping surrounding properties, omitting a key when a branch is absent, mapping objects as well as arrays, deep-merging, sorting, stable structural transforms. |
| What it does not do | It cannot omit a key from a surrounding object. The caller always gets one result value. | It has no `missing`, `substr`, `filter`, or strict `===`. A `$` property it does not define is an error. `$$` emits a literal `$` property. |
| Time | Pure relative to its inputs. | `$fromNow` without `from`, and the `now` built-in, read the clock. Two resolved reads of an unchanged document can differ. |
| Host functions | Custom operators can be added in code. | The context may contain functions in the JavaScript implementation. A JSON document cannot carry a function. |

Truthiness is defined by each language and the two definitions differ. An author ports a condition from one language to the other by re-checking the language's own truth table.

JSON-e covers enough logic that a one-language design is workable: a condition becomes `$if` / `$switch` / `$eval`. JSON Logic still has operators JSON-e does not, and the rule shape is the one already published at jsonlogic.com. Supporting both costs one extra package and one extra branch behind the same envelope. This plan does that.

### Package options

Checked on 4 October 2026 against NuGet and the upstream docs.

**Option A — `JsonLogic` 6.1.0 and `JsonE.Net` 3.0.1 (chosen).**

Both packages are the json-everything implementations. The JSON-e project lists that port as its .NET implementation. `JsonLogic` 6.1.0 (16 May 2026) targets `net10.0`, `net9.0`, `net8.0`, and `netstandard2.0`, and depends on `JsonPointer.Net`. `JsonE.Net` 3.0.1 targets the same frameworks and depends on `Json.More.Net`, `IndexRange`, and `Microsoft.Bcl.Memory`. The public API is `JsonLogic.Apply(JsonNode rule, JsonNode data)` and `JsonE.Evaluate(JsonNode template, JsonObject context)`. Both speak `System.Text.Json.Nodes.JsonNode`, so Storyteller's `JToken` tree crosses a JSON-text boundary on the way in and out.

These are the builds that receive spec fixes (JSON Logic 6.0.2 array-to-string coercion, 6.1.0 null handling in `if`; JSON-e 2.5.x bounds and `$reduce` / `$find`). They are the same publisher as `JsonPatch.Net` 5.0.2, which this repository already restores.

**Option B — pin the last builds from before the binary EULA.**

`JsonLogic` 5.5.0 (9 December 2025) already targets `net10.0`. `JsonE.Net` 2.5.1 targets `net9.0` and `netstandard2.0`, which a `net10.0` project can reference. These builds predate the fee and miss the fixes shipped in the 6.x / 3.x line. Choosing them does not leave the publisher: `JsonPatch.Net` 5.0.2 is already a later build. This option is a fallback if distribution of the newer binaries is rejected.

**Option C — `JsonLogic.Net` 1.1.11 plus `JsonE.Net`.**

`JsonLogic.Net` (package owner MaxHayman, last release 19 August 2021) uses Newtonsoft.Json and would avoid a conversion for the logic half. It is five years behind the JSON Logic fixes above, and there is still no Newtonsoft port of JSON-e. The templating half would keep the `System.Text.Json` boundary. One conversion helper for both engines is a smaller surface than two JSON stacks maintained for one of them.

**Option D — run the official JavaScript implementations on a .NET JS host.**

`json-e` and `json-logic-js` are the reference implementations. Hosting them (Jint, or another embedded runtime) would track the reference suites closely and would still need a `JToken` conversion. It would also put a general script runtime in the API process. The C# ports exist specifically so that step is unnecessary, and JSON-e documents that the json-everything port is the .NET implementation. A script host is justified only if a third language with no C# port is added later.

**Option E — grow the `@` language until it can return objects.**

The interpreter already evaluates paths, calls, interpolation, and arithmetic. An object form would be a new language owned here: templates, `map` / `filter` / `reduce`, conditionals that omit keys, and a data-access story parallel to `var` and `$eval`. That duplicates JSON-e and loses the property that a definition written for Storyteller is a definition written in a published language. The `@` language stays the way individual strings and context slots reach Key Vault, `@config`, and `@annotation`.

**Option F — ship `jsone` only.**

Fewer names to document, one engine, and structural templates cover the object-shaped cases that string binding cannot. Predicates move into `$eval` and `$if`, and JSON Logic operators with no JSON-e equivalent (`missing`, `substr`, `filter`, `===`) are unavailable. The request asks for both discriminators. The incremental cost of `jlogic` on top of Option A is the `JsonLogic` package and a second branch in the engine adapter. Option F remains a cut if one language is preferred later; the envelope already isolates that cut to the `jlogic` branch.

### License

Source for json-everything is MIT, copyright .NET Foundation and contributors. Starting with the February 2026 releases, the published binaries carry an Open Source Maintenance Fee EULA. The fee is not a license fee. It applies to revenue-generating use at or above US$10,000 annual gross revenue; the EULA says the MIT license governs where the two conflict, and that the fee does not attach to source builds. `JsonPatch.Net` 5.0.2 is already one of those binaries in this repository. Adopting `JsonLogic` 6.1.0 and `JsonE.Net` 3.0.1 stays inside that publisher relationship. It does not add a second maintainer. Confirming whether the fee is already owed for `JsonPatch.Net` is an organizational question, outside this change.

### How the envelope uses each language

`$definition` is passed through as the rule or the template. `$context`, after Storyteller has resolved it, is passed through as the data argument. The engines do not see `@` expressions, Key Vault, or the configuration service. They see JSON.

A JSON-e template treats every `$` property as an operator. A nested Storyteller envelope written inside `$definition` would be rejected by JSON-e, because `$binding` is not one of its operators. The composition path is the other direction: the template emits an envelope with `$$binding`, `$$definition`, and `$$context`, JSON-e turns those into `$binding`, `$definition`, and `$context`, and the walk that runs on the result evaluates that envelope. JSON Logic does not reserve `$`. A rule that returns an envelope object is evaluated by the same post-pass.

## Proposed Changes

Adopt Option A. Add an object-binding pass to the existing resolved read. Keep the `@` interpreter, the write path, schema validation, and template merge as they are.

### 1. Envelope

An object is an envelope when it has a property `$binding` whose value is a JSON string, and a property `$definition`.

| `$binding` | `$definition` present | Result |
| --- | --- | --- |
| `jlogic` or `jsone` | yes | Evaluate. Any other property is a malformed envelope and throws. |
| `jlogic` or `jsone` | no | Malformed envelope. Throws. |
| any other string | yes | Malformed envelope. Throws. A typo such as `json-e` or `JLogic` is reported at the object's JSON path. |
| any other string | no | Ordinary object. The walk visits its properties. `{"$binding": "manual"}` stays stored data. |
| not a string | either | Ordinary object. |

`$definition` may be any JSON value, including a string, number, array, or object. `$context` is optional and defaults to `{}`. When present, its resolved value must be a JSON object.

Recognized names and property names are exact and case-sensitive. The three property names are `$binding`, `$definition`, and `$context`.

The envelope object is replaced by the engine result. A property can become an object, an array, a string, a number, a boolean, or null. The root `Configuration.Content` is a `JObject`; an envelope that is the entire content must evaluate to an object. Any other root result throws. The replacement is written into the existing `JObject` so the instance `Configuration.Content` already references keeps its identity.

### 2. Resolution order

One walker owns the walk that `GetResolvedConfigurationInternalAsync` does today. For each token:

1. A string is handed to the existing `IBindingExecutor`. Unresolved top-level `@` expressions stay as written, including a Key Vault path read with `includeSecrets: false`.
2. An array resolves each item in index order and writes the item back.
3. An object that is an envelope resolves `$context` only, with this same walker, then calls the engine with the untouched `$definition` and the resolved context. The result is written back and then resolved with this same walker. Depth increases by one at the engine call.
4. Any other object resolves each property value and writes it back.

`$definition` is the program. The walker does not bind `@` strings inside it and does not evaluate envelopes inside it. A secret, a `@config` value, or an `@annotation` value that the program needs is placed in `$context`, where step 3 resolves it before the engine runs. An `@` string that the program copies out as a whole JSON string is bound by the post-pass on the result. A literal `@` that must survive as text stays inside a larger string; the `@` language only rewrites a string whose first character is `@`, which is already the rule in `binding.md`.

The scope's `Document` stays the pre-pass clone. Object results are not written into that clone. A sibling envelope is invisible to `@config` in another envelope. Dependence is expressed by nesting the inner envelope inside the outer `$context`, which step 3 resolves first.

```json
{
  "retries": {
    "$binding": "jlogic",
    "$definition": { "if": [ { "<": [ { "var": "tier" }, 2 ] }, 1, 5 ] },
    "$context": { "tier": "@config(\"/plan/tier\")" }
  }
}
```

With a snapshot `plan.tier` of `1`, the property `retries` becomes the number `1`. The snapshot still has whatever `retries` was before the pass, and a later `@config("/retries")` sees that earlier value.

```json
{
  "connection": {
    "$binding": "jsone",
    "$definition": {
      "host": { "$eval": "host" },
      "password": { "$eval": "password" }
    },
    "$context": {
      "host": "@config(\"/endpoints/db\")",
      "password": "@(db.password, primaryVault)"
    }
  }
}
```

The context is resolved with the `@` language first. The template then sees plain JSON.

A depth counter starts at 0 for the root and increments for each envelope evaluation on the way down, including envelopes produced by an earlier result. The maximum is 32. The 33rd evaluation throws `BindingEvaluationException` with the JSON path. JSON-e already bounds its own loops; the counter bounds envelopes that emit envelopes. A result that is identical to the envelope that produced it hits the same limit.

`includeSecrets` is forwarded only into `@` resolution of strings. The engines never receive a source or a credential.

No host functions and no custom operators are registered. JSON-e built-ins (`now`, `fromNow`, and the expression functions defined by the language) remain available because they are part of the language. `$fromNow` without `from` makes the resolved document depend on the clock. Calculated content is unchanged and stays cacheable; the resolved read is the one that varies.

### 3. Projects and contracts

New project `src/Platform/Storyteller/Binding.Object/src/Binding.Object.csproj`:

- Assembly `42.Platform.Storyteller.Binding.Object`, root namespace `_42.Platform.Storyteller.Binding.Object`, `net10.0`.
- Project reference: `Binding.Abstractions`.
- Package references, versions only in `Directory.Packages.props` (alphabetical, beside `JsonPatch.Net`): `JsonE.Net` 3.0.1, `JsonLogic` 6.1.0, `Newtonsoft.Json`.
- Registered in `42.mono.slnx` under `/Platform/Storyteller/`, next to `Binding.Language`.

New test project `src/Platform/Storyteller/Binding.Object/test/Binding.Object.UnitTests.csproj`, same shape as `Binding.Language.UnitTests` (xUnit, FluentAssertions, `IsPackable` false, `IsTestProject` true).

`Binding.Abstractions` gains one interface:

```csharp
public interface IConfigurationBindingResolver
{
    ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope);
}
```

`IBindingExecutor` stays the string entry point. The resolver calls it. Callers outside the binding projects do not take a dependency on `JsonNode`.

`ConfigurationBindingResolver` in `Binding.Object` implements the interface and contains the walk from section 2. A private engine adapter converts tokens, calls one of the two libraries, and converts the result back. `Binding.Core` constructs the resolver with the `BindingExecutor` it already builds and registers it as `IConfigurationBindingResolver` from `AddConfigurationBindings`. `Program.cs` gains no new call.

`Binding.Object` does not reference `Binding.Core`, `Binding.Language`, or `Backend.CosmosDb`. The string executor is consumed through `IBindingExecutor`.

### 4. Token conversion

The adapter serializes a `JToken` to JSON text and parses a `JsonNode`, then serializes the `JsonNode` and parses a `JToken`. Both directions use explicit settings:

- Newtonsoft `DateParseHandling.None`, so an ISO-8601 string produced by `$fromNow` stays a string.
- Newtonsoft `FloatParseHandling.Decimal`.
- `System.Text.Json` default node parse, which already leaves date-like strings as strings.

The helper lives next to the adapter and is the only place the two JSON models meet. `JToken.ToObject` and `JsonSerializer.Deserialize<JsonNode>` without those settings are the wrong direction: Newtonsoft's default date parsing would rewrite strings the engine just produced.

Engine exceptions (`JsonEException` and the JSON Logic exception type the 6.1.0 package throws) are caught at the adapter and rethrown as `BindingEvaluationException`. The resolver wraps that with the JSON path, in the same style as `BindingExecutor` (the failing expression, the path, the inner message). A malformed envelope throws `BindingEvaluationException` before either engine is called.

The JSON Logic `log` operator, in current json-everything builds, is pluggable through `ILogicLogger`. During implementation, check the default sink. If it writes to the console, replace it with `ILogger` at Debug so a stored rule cannot write an unbounded console stream. The operator still returns its input unchanged.

### 5. Cosmos integration

`GetResolvedConfigurationInternalAsync` keeps the snapshot and the `ConfigurationBindingContext`. It stops walking. When a resolver is available it calls `ResolveAsync(config.Content, includeSecrets, scope)` and returns `config`. When the resolver is null it returns the calculated document, which is today's behavior when `IBindingExecutor` is null.

The optional `IBindingExecutor` constructor parameter is replaced by an optional `IConfigurationBindingResolver`. `Backend.CosmosDb` does not gain a reference to `JsonLogic` or `JsonE.Net`. The test host already calls `AddConfigurationBindings`, so the resolver is injected once `Binding.Core` registers it.

`TryProcessDataBinding` moves into the resolver and is deleted from the service, or becomes a private helper of the resolver. The array-item and property paths both go through the single walk, so an envelope that is an array item is handled by the same code as an envelope that is a property value.

### 6. Behavior that stays

- Writes (`CreateOrUpdateConfigurationAsync`, `PatchConfigurationAsync`) store the envelope as JSON. They do not evaluate it.
- Schema validation sees the stored document. An envelope is an object with `$binding`, `$definition`, and `$context`. A schema written for the resolved shape does not match the stored shape. The same split already exists for `@` strings. This change does not add a resolved-document validation pass.
- Template merge copies an envelope into `CalculatedContent` as data. The resolved read evaluates it. Template content stays unvalidated.
- `@` syntax, sources, `@config`, `@annotation`, and Key Vault are unchanged.
- Hierarchy view and version content stay on stored content and do not resolve.
- HTTP status for a binding failure stays 500.

### 7. Documentation

Update `docs/Platform/Storyteller/binding.md` with the envelope, the two languages, the context-versus-definition rule, the depth limit, the root-object rule, and the fact that resolution still happens on the resolved read. Keep the `@` language section as the string language.

Update the "What each read returns" paragraph of `docs/Platform/Storyteller/templating.md` so the TODO on logic operations points at object binding instead of an open item.

### 8. Tests

`Binding.Object` unit tests, with a fake `IBindingExecutor` that rewrites strings the test cares about:

- Each discriminator: a small JSON Logic rule and a small JSON-e template, asserting the replacement type (number, object, array, null).
- Missing `$context` is `{}`.
- Extra properties, a known discriminator without `$definition`, and an unknown discriminator with `$definition` throw `BindingEvaluationException` and leave the token unchanged.
- `{"$binding": "manual"}` without `$definition` is left in place and its other properties are still walked.
- `$context` is resolved before the engine: an `@` string becomes the fake executor's value; a nested envelope inside the context becomes that envelope's result.
- An `@` string and a nested envelope inside `$definition` are not resolved before the engine. A JSON-e template that emits `$$binding` produces an envelope the post-pass then runs.
- A result string that starts with `@` is bound by the post-pass.
- Root envelope returning an object replaces the properties of the same `JObject`. Root envelope returning a string throws.
- Depth 32 succeeds on a chain that terminates; depth 33 throws.
- `includeSecrets` is forwarded to the string executor.
- The scope's document is unchanged after a pass.
- An ISO-8601 string result stays a `JValue` of type String.
- An ordinary document with no envelope still binds `@` strings in properties and in array items.

`Backend.CosmosDb` tests, against the existing configuration service fixtures:

- A stored object property and a stored array item each resolve through one of the two languages.
- A template that contributes an envelope is present in the calculated document as an envelope and in the resolved document as the engine result.
- A write returns the stored envelope. `GetRawConfigurationAsync` returns the calculated document with the envelope still in place.
- `includeSecrets: false` leaves a Key Vault expression inside `$context` unresolved, and the engine receives that literal string.

### 9. Phases

1. Add `Binding.Object`, the abstractions interface, the package versions, and the solution entries. Implement the walker, the adapter, and the unit tests in section 8. No Cosmos change yet.
2. Register the resolver from `AddConfigurationBindings`. Switch `GetResolvedConfigurationInternalAsync` to it. Add the Cosmos tests.
3. Update `binding.md` and the one paragraph in `templating.md`.

Phase 1 is independently testable. Phases 2 and 3 are the integration and the docs that make the behavior reachable and described.
