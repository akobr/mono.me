# Phase 1 of Object-level binding

## Overview

Phase 1 of [2026-10-04 Object-level binding with JSON Logic and JSON-e](../2026-10-04%20Object-level%20binding%20with%20JSON%20Logic%20and%20JSON-e.md) adds the object-binding walker and its unit tests. A configuration `JObject` can now be resolved in place: whole strings that start with `@` still go to `IBindingExecutor`, and an object envelope whose `$binding` is `jlogic` or `jsone` is evaluated by JSON Logic or JSON-e. `AddConfigurationBindings`, `CosmosConfigurationService`, `binding.md`, and `templating.md` are unchanged. Those are phases 2 and 3.

`Binding.Object.UnitTests` passed 22 tests. `Binding.Language.UnitTests` passed 95 tests after `BindingEvaluationException` moved to the abstractions assembly.

## What Was Done

### 1. Envelope

`ConfigurationBindingResolver.TryReadEnvelope` implements the section 1 table.

- `$binding` must be a JSON string. Any other type, or a missing `$binding`, is an ordinary object and is walked.
- `jlogic` and `jsone` are exact and case-sensitive. A known kind requires `$definition` and may also have `$context`. Any other property throws `BindingEvaluationException` before the token is changed.
- A known kind without `$definition` throws the same way.
- An unknown kind that has `$definition` (for example `json-e`) throws. The message includes the object's JSON path. An empty path is shown as `$`.
- An unknown kind without `$definition`, such as `{"$binding":"manual"}`, stays data. The walk still binds `@` strings in its other properties.
- `$context` is optional. When it is absent the engine receives `{}`. When it is present, the resolved value must be a `JObject`.
- A non-root result may be any JSON type, including null. A root envelope that does not evaluate to a `JObject` throws before `ReplaceContents`, so the original token is unchanged. A root object result replaces the properties of the same `JObject` instance.

### 2. Resolution order

`ConfigurationBindingResolver` is the single walker.

- A string whose first character is `@` is passed to `IBindingExecutor.TryBinding`. The walker re-reads the parent property or array slot afterwards, because `TryBinding(JValue)` replaces the token. A detached result string is held in a temporary `JObject` and cloned back out. `JProperty` does not allow `Remove` on its child.
- An array resolves items in index order and writes each item back.
- An envelope resolves `$context` only, then `ObjectBindingEngine.Evaluate` sees the original `$definition`. The result is walked again, so a result string that starts with `@`, and an envelope a template emits, both run.
- Any other object resolves each property value and writes it back. Ordinary nesting does not consume the depth counter.
- Depth starts at 0. The check is `depth >= 32` before the engine call, so 32 evaluations succeed and the 33rd throws `BindingEvaluationException` with the path. The context walk and the result walk both use `depth + 1`.
- `includeSecrets` is forwarded only into string binding. The engines receive JSON.
- `BindingScope.Document` is not updated. Sibling envelopes do not see each other's results through `@config`.

The depth-32 chain is nested under a property. The innermost value is a JSON Logic literal `"done"`, and each outer layer is JSON-e whose `$definition` is the previous object with one extra `$` on every key that already starts with `$`. A root string would fail the root-object rule, so the chain of 32 is not itself the document root. The chain of 33 throws. That test does not assert that the original token is intact.

### 3. Projects and contracts

`Directory.Packages.props` gained `JsonE.Net` 3.0.1 and `JsonLogic` 6.1.0, alphabetical, immediately before `JsonPatch.Net`. These are the fee-era json-everything binaries the spec chose. Source is MIT. The February 2026 binary EULA is the same publisher relationship the repository already has through `JsonPatch.Net` 5.0.2.

`src/Platform/Storyteller/Binding.Object` is assembly `42.Platform.Storyteller.Binding.Object`, root namespace `_42.Platform.Storyteller.Binding.Object`, `net10.0`. It references `Binding.Abstractions`, `JsonE.Net`, `JsonLogic`, `Newtonsoft.Json`, and `Microsoft.Extensions.Logging.Abstractions` 10.0.5 (already in the catalog). It does not reference `Binding.Core`, `Binding.Language`, or `Backend.CosmosDb`. Both the library and `Binding.Object.UnitTests` are in `42.mono.slnx` under `/Platform/Storyteller/`, after `Binding.Language`. The test project matches `Binding.Language.UnitTests`: xUnit, FluentAssertions, `IsPackable` false, `IsTestProject` true, and no implicit usings.

`Binding.Abstractions` gained `IConfigurationBindingResolver.ResolveAsync(JObject content, bool includeSecrets, BindingScope scope)`.

`BindingEvaluationException` moved from `_42.Platform.Storyteller.Binding.Language` to `_42.Platform.Storyteller.Binding`. Object binding has to throw it, and `Binding.Object` must not reference `Binding.Language`. A second class in the language namespace would be ambiguous in tests that import both namespaces, and `TypeForwardedTo` cannot change a namespace. The old source file was removed. Enclosing-namespace lookup still finds the abstractions type from `Binding.Language` and `Binding.Object`.

`ConfigurationBindingResolver` takes `IBindingExecutor` and an optional `ILogger<ConfigurationBindingResolver>`. Phase 1 does not register it. `AddConfigurationBindings` and `Program.cs` are unchanged, which is the phase 2 step in section 9. Section 3 describes that registration as part of the finished design.

### 4. Token conversion

`JsonTokenConverter` is the only place the two JSON models meet.

- `JToken` becomes a `JsonNode` through `ToString(Formatting.None)` and `JsonNode.Parse`.
- `JsonNode` becomes a `JToken` through `ToJsonString` and `JsonTextReader` with `DateParseHandling.None` and `FloatParseHandling.Decimal`.
- `JsonNode.Parse("null")` returns a null reference, so a JSON-null `$definition` returns `JValue` null and neither engine is called.
- Numbers come back as `decimal` (`JTokenType.Float`). An ISO-8601 string stays a `JValue` string. `{"$fromNow":"1 hour","from":"2017-01-19T16:27:20.974Z"}` evaluates to the string `2017-01-19T17:27:20.974Z`.

`JsonE.Evaluate` in 3.0.1 takes `JsonNode` as its second argument. The spec's package note named `JsonObject`. The adapter passes the converted context node. `JsonLogic.Apply(JsonNode, JsonNode)` matches the note. The kind constants are `JsonLogicKind` and `JsonEKind`. Names `JsonLogic` and `JsonE` would hide the library types.

Engine failures are caught in `ObjectBindingEngine`. `JsonEException` and `JsonLogicException` become `BindingEvaluationException` with the engine message and the original exception. The resolver then wraps any `BindingException` from the engine as `BindingException`, with the path, in the same shape as `BindingExecutor` (`Failed to process the object binding for '{path}': ...`). A caller that catches only `BindingEvaluationException` therefore sees malformed envelopes, depth failures, a bad `$context`, and a non-object root, and sees an engine failure as `BindingException` whose inner exception is `BindingEvaluationException`. A malformed envelope is thrown directly and is not wrapped a second time.

`LogRule.Logger` is a static settable `ILogicLogger`. Its documentation says that, when unset, the property returns a console logger. `LogicLoggers.NullLogger` is documented as the default logger. The implementation follows `LogRule.Logger`: the static constructor assigns `JsonLogicDebugLogger.Instance` before any rule runs. `WriteLine` logs `JSON Logic log: {Value}` at Debug when a host logger has been supplied, and drops the message otherwise. Tests construct the resolver without a logger, so messages are dropped. The sink is process-wide. The last constructed resolver replaces it. No custom operators are registered.

### 8. Tests

`ConfigurationBindingResolverTests` covers the `Binding.Object` rows in section 8, with `FakeStringBinding` rewriting a whole string that starts with `@` to `resolved:` plus the raw text. The fake records the raw string, the path, `includeSecrets`, and the scope instance.

- JSON Logic `{ "+": [1, 2] }` becomes decimal 3. `{ "var": "absent" }` becomes null. `{ "merge": [[1, 2], [3]] }` becomes a decimal array 1, 2, 3.
- JSON-e `{ "host": { "$eval": "host" } }` with context `host: db.internal` becomes that object.
- A missing `$context` uses `{}`, and `{ "var": ["absent", 4] }` becomes 4.
- The three malformed shapes throw `BindingEvaluationException` and leave the JSON text unchanged.
- `{"$binding":"manual"}` walks `name: "@name"`.
- `$context` resolves `@tier` and a nested JSON Logic envelope before the outer rule runs. The outer sum of literal tier 1 and the nested sum 11 is decimal 12. The fake is not given `@tier` as a numeric input, because it would rewrite that string.
- `@only-in-definition` is not seen by the fake. Comparing it to 1 is false. The fake sees only `@secret` from `$context`.
- A nested `$binding` object inside a JSON-e template throws `BindingException`. The fake does not see `@hidden`.
- JSON-e `$$binding` / `$$definition` emits an envelope that the post-pass evaluates: `{ "+": [2, 2] }` becomes 4.
- A result string `"@name"` becomes `resolved:@name`.
- A root JSON-e object replaces properties of the same instance (`ok: true`). A root JSON Logic string `"hello"` throws and does not mutate.
- Depth 32 returns `"done"`. Depth 33 throws.
- `includeSecrets: false` and the same scope instance are forwarded.
- `scope.Document` still deep-equals the snapshot after a pass.
- `$fromNow` with `from` stays the string above.
- An ordinary document binds `@` in a property and in an array item, and leaves `"plain"` and the number 1.

The Cosmos rows in section 8 are not in this phase.

### 9. Phases

Phase 2 still has to register `ConfigurationBindingResolver` from `AddConfigurationBindings` and switch `GetResolvedConfigurationInternalAsync` to `ResolveAsync`, then add the Cosmos tests. Phase 3 still has to update `binding.md` and the "What each read returns" paragraph in `templating.md`.
