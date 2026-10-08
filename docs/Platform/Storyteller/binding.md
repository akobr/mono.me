# Configuration Data Binding

This document describes the concept and implementation of the data binding system for configurations within the Platform Storyteller.

## Overview

The data binding system lets configuration values, stored as JSON, reference external data such as Azure Key Vault, compute values with string interpolation and math, and evaluate an object as a JSON Logic rule or a JSON-e template. Secrets and environment-specific values can live in a source and be filled in on the resolved read.

Resolution runs on the resolved read (`GetResolvedConfigurationAsync` and the with-secrets / without-secrets variants). Writes, `GetRawConfigurationAsync`, the hierarchy view, and version content keep the stored JSON, including any object-binding envelope.

The system is split into five binding projects, plus built-in functions contributed by `Backend.Core`:
- **Binding.Abstractions**: Core interfaces and data structures (`IBindingExecutor`, `IConfigurationBindingResolver`, `IBindingRegistry`, `IBindingSource`, `IBindingFunction`, `BindingScope`).
- **Binding.Language**: The `@` string interpreter (tokenizer, parser, evaluator), the `IBindingExecutor` entry point, the `JsonQuery` structured-query helper, and the built-in `@config` function.
- **Binding.Object**: The object-binding walker. An envelope whose `$binding` is `jlogic` or `jsone` is evaluated with JSON Logic or JSON-e.
- **Binding.Core**: Dependency-injection registration and options for wiring up sources, functions, and the object-binding resolver.
- **Binding.Azure.KeyVault**: A concrete `IBindingSource` backed by Azure Key Vault.
- **Backend.Core**: The built-in `@annotation` function on top of `IAnnotationService`.

## Core Concepts

### String syntax (`@`)

Binding is triggered whenever a string value starts with the `@` character. The whole string is parsed as a single binding expression. Five forms are supported:

1.  **Path (default source)**: `@path.to.value`
    - Resolves `path.to.value` against the source registered with the `"default"` key.
    - Example: `@database.connectionString`
2.  **Sourced path**: `@(path.to.value, source)`
    - Resolves the path against the source registered under the given name.
    - Example: `@(db.password, primaryVault)`
    - The path and the source name are identifiers. A hyphen is not an identifier character, so `@(db-password, primary-vault)` does not parse. `KeyVaultBindingSource` turns each dot in the path into `--` when it asks Key Vault for the secret (`db.password` becomes `db--password`).
3.  **Function call**: `@name(arg1, arg2, ...)`
    - Invokes the `IBindingFunction` registered under `name`. Each argument is itself a path, a string literal (`"..."`), or a nested `@...` statement.
    - Example: `@myFunction(some.path, "literal", @another.path)`
    - **Quoting matters**: a bare identifier/path argument (e.g. `some.path`) is resolved through the default source *before* the function ever runs, exactly like a top-level `@some.path` statement. If an argument is meant to be literal text for the function itself to interpret (as with `@config`/`@annotation` below), it **must** be a quoted string literal, or it will be treated as an unrelated configuration lookup instead.
4.  **Interpolation**: `@[ literal text @statement more text ]`
    - Concatenates literal text with the stringified results of one or more nested statements. Use `\@`, `\]`, and `\\` to escape those characters within the literal text.
    - Example: `@[https://@host.value:@port.value/api]`
5.  **Math expression**: `@{ expr }`
    - Evaluates a numeric expression using `+ - * / %` with standard precedence and parentheses. Operands are number literals or nested `@...` statements that resolve to numbers.
    - Example: `@{(@base.value + 10) * 2}`

Expressions never nest inside one another (an interpolation cannot contain a math expression or vice versa), but statements can nest arbitrarily as function arguments, interpolation parts, or math operands.

When a top-level path, sourced path, or function statement cannot be resolved (no matching source/function registered, or the source declines), the original string value is left untouched. Inside an interpolation or math expression, an unresolved statement instead throws a `BindingEvaluationException`. Malformed syntax throws a `BindingSyntaxException` that includes the character offset of the problem; both exceptions are wrapped with the JSON property path when raised through `IBindingExecutor`.

### Object binding

An object is an envelope when `$binding` is a JSON string and `$definition` is present. Names are exact and case-sensitive.

| `$binding` | `$definition` present | Result |
| --- | --- | --- |
| `jlogic` or `jsone` | yes | Evaluate. Any other property is a malformed envelope and throws. |
| `jlogic` or `jsone` | no | Malformed envelope. Throws. |
| any other string | yes | Malformed envelope. Throws. A typo such as `json-e` or `JLogic` is reported at the object's JSON path. |
| any other string | no | Ordinary object. The walk still visits its properties. `{"$binding": "manual"}` stays stored data. |
| not a string | either | Ordinary object. |

`$definition` is the program and may be any JSON value. The walker does not bind `@` strings inside it and does not evaluate envelopes inside it. `$context` is optional, defaults to `{}`, and must resolve to a JSON object. Secrets, `@config`, and `@annotation` that the program needs belong in `$context`. The walker resolves `$context` first, then the engine sees the untouched `$definition` and that resolved object.

`jlogic` is [JSON Logic](https://jsonlogic.com/) through the `JsonLogic` 6.1.0 package (`JsonLogic.Apply`). `jsone` is [JSON-e](https://json-e.js.org/) through `JsonE.Net` 3.0.1 (`JsonE.Evaluate`). No custom operators are registered. JSON-e treats every `$` property as one of its own operators, so a template that must emit a Storyteller envelope writes `$$binding`, `$$definition`, and `$$context`. One JSON-e pass peels a single `$`. The walk of the result then evaluates the emitted envelope. JSON Logic does not reserve `$`. A rule that returns an envelope is evaluated by that same post-pass.

```json
{
  "retries": {
    "$binding": "jlogic",
    "$definition": { "if": [ { "<": [ { "var": "tier" }, 2 ] }, 1, 5 ] },
    "$context": { "tier": "@config(\"/plan/tier\")" }
  }
}
```

With a snapshot `plan.tier` of `1`, `retries` becomes the number `1`.

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

The context is resolved with the `@` language first. The template then sees plain JSON. `includeSecrets` is forwarded only into that string resolution. With secrets excluded, a Key Vault source declines and the engine receives the original `@(db.password, primaryVault)` string.

The envelope is replaced by the engine result. The result may be an object, an array, a string, a number, a boolean, or null, and it is walked again, so a result string that starts with `@` is bound and an emitted envelope runs. The root `Configuration.Content` is a `JObject`. An envelope that is the entire content must evaluate to an object. The properties of that object replace the properties of the same instance. Any other root result throws `BindingEvaluationException` and leaves the token unchanged.

Depth starts at 0 and increases by one for each envelope evaluation on the way down, including an envelope produced by an earlier result. Ordinary object nesting does not count. Nesting deeper than 32 throws `BindingEvaluationException` with the JSON path. An empty path is shown as `$`. A second counter bounds the whole read: one resolved read evaluates at most 256 envelopes, whether they are nested or siblings produced by an earlier result. The 257th throws `BindingEvaluationException` with the JSON path.

One evaluation has its own limits. JSON-e `range` returns at most 1000 numbers. A JSON-e template nests at most 16 operators, counted on objects whose keys start with a single `$` (`$let`, `$map`, `$reduce`, `$eval`, and the other `$` operators). Keys that start with `$$` are escaped data and do not count. JSON-e string `+` stops at 100000 characters in every expression, including `$eval`, an `$if` condition, a `$switch` or `$match` key, `$sort` `by(...)`, and `$find` `each(...)`. `${...}` interpolation stops at the same limit in string values and in object keys. JSON Logic `cat` stops at the same character limit, `merge` returns at most 1000 items, and `reduce` accepts at most 1000 items. Passing a limit throws `BindingEvaluationException`. A failure inside an engine is then wrapped as `BindingException` with the JSON path. The depth and evaluation counters bound envelopes that emit envelopes. The same 100000-character limit is a size budget for one evaluation. The envelope result is charged against that limit too. JSON Logic `reduce` measures its accumulator after each step and stops when the serialized value passes the budget. JSON Logic `map` adds the serialized length of each mapped value and stops when that total passes the budget. JSON-e adds the serialized length of every operator result, including `$let`, `$eval`, `$if`, `$merge`, and `$flatten`, and of every `$map` or `$reduce` `each` result and `$json` value. `join` charges the same budget while it builds its string. An `$if` without `else` still drops the value, including a value inside `$map` or `$reduce`. JSON Logic `all`, `some`, `none`, `filter`, `map`, and `reduce`, together with every wrapped JSON-e evaluation, share one limit of 100000 steps in one read. A dropped value still counts. JSON-e `in`, `==`, `!=`, string indexing and slicing, and the builtins `len`, `split`, `lowercase`, `uppercase`, `strip`, `lstrip`, and `rstrip` charge one step for each array item, object property, or string character they scan. An array index or slice charges the value it copies, one step for each nested array item, object property, or string character, and a slice also charges the number of items it returns. `len` of an array stays uncharged. JSON Logic `in`, and `==` or `!=` when either side is an array, charge the same way. `===` and `!==` charge an array or object operand, and they charge string length when both sides are strings. `<`, `<=`, `>`, `>=`, `+`, `-`, `*`, `/`, `%`, `min`, and `max` charge a string operand's length, and they charge an array or object operand the same way, before the value is copied. Those scans share the 100000-step limit. The context names `range`, `join`, `split`, `len`, `lowercase`, `uppercase`, `strip`, `lstrip`, `rstrip`, `storytellerAdd`, `storytellerConcat`, `storytellerBound`, `storytellerStep`, `storytellerIn`, `storytellerEquals`, `storytellerIndex`, and `storytellerSlice` are reserved for these checks, so a value of the same name in `$context` does not replace them.

`@config` reads the snapshot taken before the pass. Object results are not written into that snapshot, so a sibling envelope is invisible to `@config` in another envelope. Nest the inner envelope inside the outer `$context` when one result must feed the other.

`$fromNow` without `from` reads the clock, so that resolved document changes between reads. Calculated content is unchanged and stays cacheable. `$fromNow` with `from` stays a string.

A malformed envelope throws `BindingEvaluationException` before either engine runs, and the token is left unchanged. `JsonEException` and `JsonLogicException` are rethrown as `BindingEvaluationException`. The resolver then wraps that failure as `BindingException` with the JSON path, in the same shape as `BindingExecutor`. The JSON Logic `log` operator writes to `ILogger` at Debug when the host supplied a logger, and drops the message otherwise.

## Project Structure

### Binding.Abstractions

Defines the fundamental building blocks:
- `IBindingExecutor`: Executes the `@` string language on a `JProperty`/`JValue`.
- `IConfigurationBindingResolver`: Walks a configuration `JObject` and resolves `@` strings and object-binding envelopes in place.
- `IBindingRegistry`: Registers named `IBindingSource`s and `IBindingFunction`s.
- `IBindingSource`: Resolves a `BindingRequest` (a path plus `IncludeSecrets`) to a `BindingValue`.
- `IBindingFunction`: Resolves a `BindingFunctionRequest` (a function name plus already-evaluated `BindingValue` arguments) to a `BindingValue`.
- `BindingValue`: A thin wrapper around a `Newtonsoft.Json.Linq.JToken`.
- `BindingException`: Base exception type for binding failures.
- `BindingEvaluationException`: A binding failed or an object envelope is malformed. Object binding raises this type from the abstractions assembly.

### Binding.Language

Contains the interpreter pipeline:
- `Tokenizer` / `Token` / `TokenType`: Lexes a binding string into tokens, tracking offsets for diagnostics.
- `Parser` and the AST node types (`PathStatement`, `SourcedStatement`, `FunctionStatement`, `InterpolationExpression`, `MathExpression`, etc.): Build a `BindingNode` tree via recursive descent.
- `BindingEvaluator`: Walks the AST, resolving statements against registered sources/functions and evaluating interpolation/math. Accepts an optional `BindingScope` (a `Document` and an opaque `Context`) which is forwarded onto every `BindingFunctionRequest` so functions can see ambient data beyond their own arguments.
- `BindingExecutor`: Implements both `IBindingExecutor` and `IBindingRegistry`; it guards on the leading `@`, then tokenizes, parses, evaluates, and assigns the result back onto the JSON token. `TryBinding` accepts an optional `BindingScope`.
- `JsonQuery`: Resolves a JSONPath or JSON Pointer (RFC 6901) expression against a `JToken`, auto-detecting the dialect from the expression's leading character (`$` for JSONPath, `/` or empty for JSON Pointer).
- `BindingFunctionArguments`: Shared helper that validates a function argument is a quoted string literal.
- `ConfigBindingFunction`: The built-in `@config` function (see "Built-in Functions" below).
- `BindingSyntaxException`: Malformed `@` syntax, including the character offset.

### Binding.Object

Walks one configuration document. `ConfigurationBindingResolver` implements `IConfigurationBindingResolver`. Strings that start with `@` go to `IBindingExecutor`. Envelopes go to `ObjectBindingEngine`, which converts between `JToken` and `JsonNode` and calls JSON Logic or JSON-e. Numbers come back as `decimal`. ISO-8601 strings stay strings. The project references `JsonLogic` 6.1.0 and `JsonE.Net` 3.0.1. It does not reference `Binding.Language`, `Binding.Core`, or `Backend.CosmosDb`.

### Binding.Core

Contains dependency-injection registration:
- `EntryPoint.AddConfigurationBindings`: Registers `BindingExecutor` as `IBindingExecutor`/`IBindingRegistry`, registers `ConfigurationBindingResolver` as `IConfigurationBindingResolver` over that same executor, and applies `BindingsOptions`.
- `BindingsOptions` / `BindingsOptionsExtensions`: Fluent API for registering sources (keyed, defaulting to `"default"`) and functions (by name) during startup.

### Binding.Azure.KeyVault

Provides integration with Azure Key Vault:
- `KeyVaultBindingSource`: Implements `IBindingSource`. Declines (`null`) unless `IncludeSecrets` is set, and transforms configuration paths (using dots) to Key Vault secret names (using double dashes, e.g., `db.password` becomes `db--password`).
- `EntryPoint`: Provides the `AddAzureKeyVaultBindings` extension method to configure multiple Key Vaults and register a `KeyVaultBindingSource` per vault.

## Built-in Functions

### `@config("<expr>")`

Reads a value out of the *same* configuration document currently being resolved. `<expr>` is a JSONPath expression (leading `$`, e.g. `"$.a.b"`) or a JSON Pointer (leading `/` or empty, e.g. `"/a/b"`); the dialect is auto-detected from the leading character. Implemented by `ConfigBindingFunction` in `Binding.Language`.

`@config` always resolves against an immutable snapshot of the configuration taken *before* the binding pass started (see `CosmosConfigurationService.GetResolvedConfigurationInternalAsync`), so the result never depends on the order in which properties are processed and never reflects other bindings' resolved output.

```json
{
  "maxPrice": 10,
  "limit": "@config(\"/maxPrice\")"
}
```

### `@annotation("<expr>")` / `@annotation("<expr>", "<annotationType>")`

Reads a value out of the freeform `Values` of an `Annotation`, using the same JSONPath/JSON Pointer auto-detection as `@config`. Implemented by `AnnotationBindingFunction` in `Backend.Core`, backed by `IAnnotationService`.

- With one argument, the target is the annotation whose key equals the configuration currently being resolved (a direct 1:1 link).
- With a second argument, the target is instead the *ancestor* annotation of the given type (e.g. `"Subject"`, `"Responsibility"`) — see `AnnotationKeyExtensions.TryGetAncestorKey`, which throws if the requested type is not a valid ancestor of the current configuration's annotation type.

```json
{
  "owner": "@annotation(\"/owner\")",
  "team": "@annotation(\"/team\", \"Subject\")"
}
```

Both arguments must be quoted string literals (see the quoting note under "Function call" above); this applies even to the annotation type name, which might otherwise look like it should be a bare keyword.

`@annotation` requires a `ConfigurationBindingContext` (carrying the `FullKey` of the configuration being resolved) supplied via `BindingScope.Context`; used outside of `CosmosConfigurationService`'s resolution pipeline, it throws a `BindingEvaluationException`.

Both functions are registered explicitly in `Api.Functions/Program.cs`:

```csharp
services.AddSingleton<ConfigBindingFunction>();
services.AddSingleton<AnnotationBindingFunction>();
services.AddConfigurationBindings(options => options
    .AddFunction<ConfigBindingFunction>("config")
    .AddFunction<AnnotationBindingFunction>("annotation"));
```

## Usage

### Registration

To enable data binding in your application, register the core services and any specific sources/functions:

```csharp
services.AddConfigurationBindings(options =>
{
    // Register custom sources/functions here if needed, e.g.:
    // options.AddSource<MySource>("my-source");
    // options.AddFunction<MyFunction>("myFunction");
});

// Register Azure Key Vault bindings (one source per configured vault)
services.AddAzureKeyVaultBindings(configuration);
```

### Configuration Example

```json
{
  "ConnectionStrings": {
    "Default": "@sql-connection-string"
  },
  "ThirdPartyApi": {
    "ApiKey": "@(prod.apiKey, securityVault)",
    "BaseUrl": "@[https://@host.value:@port.value/api]",
    "TimeoutMs": "@{@baseTimeout.value * 2}"
  }
}
```

### Dependency Injection

`AddConfigurationBindings` registers `IConfigurationBindingResolver`. `CosmosConfigurationService` takes that resolver. `GetResolvedConfigurationInternalAsync` builds a `BindingScope` (a deep clone of the calculated document, plus a `ConfigurationBindingContext` for the configuration key) and calls `ResolveAsync`. When no resolver is registered, the calculated document is returned unchanged. `IBindingExecutor` remains the string entry point used by the resolver.

## Extending the System

`IBindingFunction` remains a general-purpose extension point beyond the two built-in functions documented above; register additional custom functions via `BindingsOptions.AddFunction` when further capabilities are needed. A function that needs ambient data beyond its own arguments (such as the document being resolved, or a caller-supplied context object) can read it from `BindingFunctionRequest.Document`/`BindingFunctionRequest.Context`, populated from the `BindingScope` passed into `IBindingExecutor.TryBinding`.
