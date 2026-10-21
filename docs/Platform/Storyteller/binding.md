# Configuration Data Binding

This document describes the concept and implementation of the data binding system for configurations within the Platform Storyteller.

## Overview

The data binding system lets configuration values, stored as JSON, reference external data such as the native [key/value pairs](key-values.md) or Azure Key Vault, compute values with string interpolation and math, and evaluate an object as a JSON Logic rule or a JSON-e template. Secrets and environment-specific values can live in a source and be filled in on the resolved read.

Resolution runs on the resolved read (`GetResolvedConfigurationAsync` and the with-secrets / without-secrets variants). Writes, `GetRawConfigurationAsync`, the hierarchy view, and version content keep the stored JSON, including any object-binding envelope.

The resolved read of the API asks `IConfigurationBindingResolver.ContainsBindings` first. A document without bindings is answered without the resolver. A document with bindings is resolved for every read, even when `If-None-Match` ends in a 304 ([conditional reads](inheritance.md#conditional-reads)).

The system is split into five binding projects, plus built-in functions contributed by `Backend.Core`:
- **Binding.Abstractions**: Core interfaces and data structures (`IBindingExecutor`, `IConfigurationBindingResolver`, `IBindingRegistry`, `IBindingCatalog`, `IBindingSource`, `IBindingFunction`, `IDescribedBinding`, `BindingScope`).
- **Binding.Language**: The `@` string interpreter (tokenizer, parser, evaluator), the `IBindingExecutor` entry point, the `JsonQuery` structured-query helper, and the built-in `@config` function.
- **Binding.Object**: The object-binding walker. An envelope whose `$binding` is `jlogic` or `jsone` is evaluated with JSON Logic or JSON-e.
- **Binding.Core**: Dependency-injection registration and options for wiring up sources, functions, and the object-binding resolver, plus `KeyValueBindingSource`, the native key/value store as the `default`, `public` and `secret` sources.
- **Binding.Azure.KeyVault**: A concrete `IBindingSource` backed by Azure Key Vault.
- **Backend.Core**: The built-in `@annotation` function on top of `IAnnotationService`.

## Core Concepts

### String syntax (`@`)

Binding is triggered whenever a string value starts with the `@` character. The whole string is parsed as a single binding expression. Five forms are supported:

1.  **Path (default source)**: `@path.to.value`
    - Resolves `path.to.value` against the source registered with the `"default"` key. That is the native [key/value store](key-values.md) of the configuration's own organization, project and view: `@db.host` is the pair `db.host`. A public pair always binds; a secret binds only when the caller may receive secrets, and stays as stored otherwise.
    - Example: `@database.connectionString`
2.  **Sourced path**: `@(path.to.value, source)`
    - Resolves the path against the source registered under the given name. `public` and `secret` are the native store limited to one kind: `@(db.host, public)` declines a secret, `@(db.password, secret)` declines a public pair.
    - Example: `@(db.password, primaryVault)`
    - The path and the source name are identifiers. A hyphen is not an identifier character, so `@(db-password, primary-vault)` does not parse. `KeyVaultBindingSource` turns each dot in the path into `--` when it asks Key Vault for the secret (`db.password` becomes `db--password`).
3.  **Function call**: `@name(arg1, arg2, ...)`
    - Invokes the `IBindingFunction` registered under `name`. Each argument is itself a path, a string literal (`"..."`), or a nested `@...` statement.
    - Example: `@myFunction(some.path, "literal", @another.path)`
    - **Quoting matters**: a bare identifier/path argument (e.g. `some.path`) is resolved through the default source *before* the function ever runs, exactly like a top-level `@some.path` statement. If an argument is meant to be literal text for the function itself to interpret (as with `@config`/`@annotation` below), it **must** be a quoted string literal, or it will be treated as an unrelated configuration lookup instead.
    - **Source call form**: when `name` is a registered source and not a function, and the call has exactly one bare path argument, `@name(path)` means `@(path, name)`. The path is not resolved first. `@public(db.host)`, `@secret(db.password)` and `@primaryVault(db.password)` are source calls. A name registered as both a source and a function fails the startup, so the form is never ambiguous.
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

`jlogic` is [JSON Logic](https://jsonlogic.com/) through the `JsonLogic` 6.1.0 package (`JsonLogic.Apply`). `jsone` is [JSON-e](https://json-e.js.org/) through a vendored build of `JsonE.Net` 3.0.1 (`Binding.JsonE`, see below) that adds a metering hook and nothing else. No custom operators are registered. JSON-e treats every `$` property as one of its own operators, so a template that must emit a Storyteller envelope writes `$$binding`, `$$definition`, and `$$context`. One JSON-e pass peels a single `$`. The walk of the result then evaluates the emitted envelope. JSON Logic does not reserve `$`. A rule that returns an envelope is evaluated by that same post-pass.

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

`@config` reads the snapshot taken before the pass. Object results are not written into that snapshot, so a sibling envelope is invisible to `@config` in another envelope. Nest the inner envelope inside the outer `$context` when one result must feed the other.

`$fromNow` without `from` reads the clock, so that resolved document changes between reads. Calculated content is unchanged and stays cacheable. `$fromNow` with `from` stays a string.

#### Evaluation limits

JSON Logic and JSON-e are full languages, and a configuration author can write a small envelope that would otherwise run for minutes, allocate gigabytes, or overflow the stack. Instead of capping operators one by one, one meter bounds the real resources of a whole resolved read. Every JSON Logic rule invocation and every JSON-e template node, expression node, expression parse, `${...}` hole, and `range` item reports one step to it. The recursion of the interpreters, the expression parser, and expression evaluation is counted as depth. Every operator is bounded, including ones nobody has listed.

| Limit (`ObjectBindingLimits`) | Default | Scope | Kind |
| --- | --- | --- | --- |
| `MaxSteps` | 1,000,000 | one read, shared by all its envelopes | `Steps` |
| `MaxAllocatedBytes` | 64 MiB | one read, bytes allocated on the evaluating thread inside the engines | `Memory` |
| `MaxEvaluationTime` | 500 ms | one read, time inside the engines (asynchronous `@` resolution between envelopes does not count) | `Time` |
| `MaxDepth` | 512 | one engine call: template, rule, parser, and expression recursion; also the nesting of a `$definition` or `$context` | `Depth` |
| `MaxResultLength` | 100,000 serialized characters | one envelope result | `Result` |
| `MaxResultDepth` | 64 nested arrays and objects | one envelope result | `Result` |
| `MaxEnvelopeDepth` | 32 | nested envelope evaluations in one read | `EnvelopeDepth` |
| `MaxEnvelopeEvaluations` | 256 | envelope evaluations in one read | `EnvelopeCount` |

Steps are the same on every machine, so a template that passes on steps always passes. Allocation is nearly deterministic for one runtime version, and time is not; both are backstops for work inside one primitive, such as `in` over a large context array or `missing` over many keys, which counts as one step. Which limit stops a runaway template depends on its shape: loops that build values usually stop on memory, `range`-driven loops on steps, very long expression chains (more than about 500 terms of `||`, `+`, and so on) on depth, and repeated scans of large context data on time. The meter checks steps and allocation on every step, the clock every 16 steps, and allocation and time once more when an engine returns, so the work of the last primitive counts too. Copies of a string share one instance, so `[s,s,…]` is cheap to build but large once flattened; `join`, `$json`, and `${...}` interpolation therefore reserve the size of their result and stop on `Memory` before building it. String `in` in both engines uses a substring search with a linear worst case, because the .NET search costs |haystack| × |needle| on periodic strings without allocating. With that, a limit is overshot by at most one primitive whose cost is bounded by memory already paid for (16 primitives for time). The first exceeded limit is latched: every later step throws again.

The stack is never exhausted. Past `MaxDepth`, or when the thread is close to the end of its stack, evaluation stops with a `Depth` limit. Before this meter, a 15 KB expression such as `!!!…true` overflowed the stack in the JSON-e parser and killed the worker process.

The result of each envelope is walked before it is converted back. The walk stops as soon as the serialized length passes `MaxResultLength` or the nesting passes `MaxResultDepth`, so a result like `[s,s,…]` that shares one large string is rejected without being serialized. `MaxResultDepth` matches the reader that converts the result back to a token.

Depth of envelopes starts at 0 and increases by one for each envelope evaluation on the way down, including an envelope produced by an earlier result. Ordinary object nesting does not count. Nesting deeper than `MaxEnvelopeDepth` throws. A second counter bounds the whole read: one resolved read evaluates at most `MaxEnvelopeEvaluations` envelopes, whether they are nested or siblings produced by an earlier result. The next one throws.

Passing any limit throws `EvaluationLimitExceededException` (a `BindingEvaluationException`) with `Kind`, `Limit`, and the JSON `Path` of the envelope. An empty path is shown as `$`. The resolver logs a Warning with the kind, path, configuration, and the meter totals (steps, milliseconds, allocated bytes), and a Debug summary of the totals for every read that evaluated envelopes. The defaults can be changed in code through `BindingsOptions.ObjectBindingLimits`.

Converting `$definition` and `$context` to the engines' JSON model is linear in the stored document and is not metered.

#### Errors

A malformed envelope throws `BindingEvaluationException` before either engine runs, and the token is left unchanged. `JsonEException` and `JsonLogicException` are rethrown as `BindingEvaluationException`, and so is any other exception an engine throws for the author's input (for example an `OverflowException` from `2 ** 100000`). The resolver adds the JSON path to the message and keeps the type: a `BindingEvaluationException` stays one, and an `EvaluationLimitExceededException` keeps its `Kind` and `Limit`. The resolved-configuration endpoint returns these as `422 Unprocessable Content` with an `ErrorResponse` whose `ErrorCode` is `binding.evaluation` or `binding.limit.<kind>` (`binding.limit.steps`, `binding.limit.time`, `binding.limit.memory`, `binding.limit.depth`, `binding.limit.result`, `binding.limit.envelopeDepth`, `binding.limit.envelopeCount`). The response carries no exception details. `@config` and `@annotation` evaluation failures are `BindingEvaluationException` too and also return 422.

The JSON Logic `log` operator writes its operand to `ILogger` only when Debug is enabled.

## Project Structure

### Binding.Abstractions

Defines the fundamental building blocks:
- `IBindingExecutor`: Executes the `@` string language on a `JProperty`/`JValue`.
- `IConfigurationBindingResolver`: Walks a configuration `JObject` and resolves `@` strings and object-binding envelopes in place.
- `IBindingRegistry`: Registers named `IBindingSource`s and `IBindingFunction`s.
- `IBindingSource`: Resolves a `BindingRequest` (a path, `IncludeSecrets`, and the `Context` of the resolution pass, e.g. the configuration being resolved) to a `BindingValue`.
- `IBindingFunction`: Resolves a `BindingFunctionRequest` (a function name plus already-evaluated `BindingValue` arguments) to a `BindingValue`.
- `IDescribedBinding`: Optional. A source or function describes itself (`BindingDescription`: summary, signature, parameters, examples, whether it holds secrets) for the catalog.
- `IBindingCatalog`: Lists the registered functions and sources (`BindingCatalog` of `BindingFunctionDescriptor` and `BindingSourceDescriptor`) with their descriptions.
- `BindingValue`: A thin wrapper around a `Newtonsoft.Json.Linq.JToken`.
- `BindingException`: Base exception type for binding failures.
- `BindingEvaluationException`: A binding's own content cannot be evaluated: a failed statement, a malformed envelope, or an engine error. Carries the JSON `Path` when known. The API returns it as 422.
- `EvaluationLimitExceededException` / `EvaluationLimitKind`: An object-binding read ran into an evaluation limit (`Steps`, `Time`, `Memory`, `Depth`, `Result`, `EnvelopeDepth`, `EnvelopeCount`).

### Binding.Language

Contains the interpreter pipeline:
- `Tokenizer` / `Token` / `TokenType`: Lexes a binding string into tokens, tracking offsets for diagnostics.
- `Parser` and the AST node types (`PathStatement`, `SourcedStatement`, `FunctionStatement`, `InterpolationExpression`, `MathExpression`, etc.): Build a `BindingNode` tree via recursive descent.
- `BindingEvaluator`: Walks the AST, resolving statements against registered sources/functions and evaluating interpolation/math. Accepts an optional `BindingScope` (a `Document` and an opaque `Context`) which is forwarded onto every `BindingFunctionRequest` so functions can see ambient data beyond their own arguments.
- `BindingExecutor`: Implements `IBindingExecutor`, `IBindingRegistry` and `IBindingCatalog`; it guards on the leading `@`, then tokenizes, parses, evaluates, and assigns the result back onto the JSON token. `TryBinding` accepts an optional `BindingScope`.
- `JsonQuery`: Resolves a JSONPath or JSON Pointer (RFC 6901) expression against a `JToken`, auto-detecting the dialect from the expression's leading character (`$` for JSONPath, `/` or empty for JSON Pointer).
- `BindingFunctionArguments`: Shared helper that validates a function argument is a quoted string literal.
- `ConfigBindingFunction`: The built-in `@config` function (see "Built-in Functions" below).
- `BindingSyntaxException`: Malformed `@` syntax, including the character offset.

### Binding.Object

Walks one configuration document. `ConfigurationBindingResolver` implements `IConfigurationBindingResolver`. Strings that start with `@` go to `IBindingExecutor`. Envelopes go to `ObjectBindingEngine`, which converts between `JToken` and `JsonNode` and calls JSON Logic or JSON-e. Numbers come back as `decimal`. ISO-8601 strings stay strings. One `EvaluationMeter` per read enforces `ObjectBindingLimits`; `JsonLogicMetering` wraps every built-in JSON Logic rule so it reports to that meter, and `ResultCheck` walks each result. The project references `JsonLogic` 6.1.0 and `Binding.JsonE`. It does not reference `Binding.Language`, `Binding.Core`, or `Backend.CosmosDb`.

### Binding.JsonE

A source copy of `JsonE.Net` 3.0.1 (json-everything commit `8b8ab34`, MIT) with one change: a public `IEvaluationMeter` hook and a `JsonE.Evaluate(template, context, meter)` overload. The interpreter reports steps and recursion frames to the meter, and the meter may throw to stop it. Namespaces stay `Json.JsonE`. `VENDORED.md` in the project lists every patched line, the audits, and how to re-sync with upstream. Its tests run the JSON-e specification suite and compare every case with the unpatched `JsonE.Net` binary.

### Binding.Core

Contains dependency-injection registration:
- `EntryPoint.AddConfigurationBindings`: Registers `BindingExecutor` as `IBindingExecutor`/`IBindingRegistry`/`IBindingCatalog`, registers `ConfigurationBindingResolver` as `IConfigurationBindingResolver` over that same executor, and applies `BindingsOptions`.
- `BindingsOptions` / `BindingsOptionsExtensions`: Fluent API for registering sources (keyed, defaulting to `"default"`) and functions (by name) during startup. `BindingsOptions.ObjectBindingLimits` sets the object-binding evaluation limits (defaults in "Evaluation limits" above).

`EntryPoint.AddKeyValueBindings` registers `KeyValueBindingSource` three times: as `default` (public pairs and secrets), `public` and `secret`. Call it after the other source registrations, so the native store owns those names. The source reads the organization, project and view from the `ConfigurationBindingContext` in `BindingRequest.Context` and declines outside a configuration read. Each key is looked up at most once per resolved read; the memo lives with the read's context, so a changed pair takes effect on the next read. A key-encryption key that is unavailable fails the read (`503`) instead of declining, because a silently unbound secret would look like a configuration error.

### Binding.Azure.KeyVault

Provides integration with Azure Key Vault:
- `KeyVaultBindingSource`: Implements `IBindingSource`. Declines (`null`) unless `IncludeSecrets` is set, and transforms configuration paths (using dots) to Key Vault secret names (using double dashes, e.g., `db.password` becomes `db--password`).
- `EntryPoint`: Provides the `AddAzureKeyVaultBindings` extension method to configure multiple Key Vaults and register a `KeyVaultBindingSource` per vault. A vault entry named `default`, `public` or `secret` is a source of that name until `AddKeyValueBindings` is registered after it (as the API does), when the native store takes the name over; give such a vault another entry name to read it as `@(db.password, shared)` next to the store.

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

## Binding Catalog

Clients such as the Administration UI offer completion and examples for the functions and sources a deployment registered. `GET v1/{organization}/{project}/bindings` (`GetBindingCatalog`, tag `Configurations`) returns them:

```json
{
  "Functions": [
    {
      "Name": "config",
      "Summary": "Reads a value of the configuration being resolved, from the snapshot taken before evaluation.",
      "Signature": "config(\"<expr>\")",
      "Parameters": [ { "Name": "expr", "Description": "A quoted JSON Pointer (`/a/b`) or JSONPath (`$.a.b`) into the configuration.", "IsOptional": false, "Values": [] } ],
      "Examples": [ { "Expression": "@config(\"/maxPrice\")", "Description": "The value of `maxPrice` in the same configuration." } ]
    }
  ],
  "Sources": [
    {
      "Name": "primaryVault",
      "Summary": "A secret of the Azure Key Vault registered as `primaryVault`. …",
      "IsDefault": false,
      "IsSecret": true,
      "Examples": [ { "Expression": "@(db.password, primaryVault)", "Description": "The secret `db--password`." } ]
    }
  ]
}
```

- The endpoint needs the Reader role on the project. A machine needs the scope `Configuration.Read` instead, and may call it also when it is restricted to an annotation. The registrations are global today; the route is project-scoped so it can list project sources later.
- `BindingExecutor.GetCatalog()` sorts functions and sources by name (ordinal). It never resolves a value and never calls a source.
- A binding that implements `IDescribedBinding` contributes its `BindingDescription`. `Describe` receives the registered name, so the examples use it (a Key Vault source does not know its key otherwise). Any other binding is listed by its name only, with an empty summary.
- `IsDefault` is set for the source registered as `default`, which `@path.to.value` reads. `IsSecret` comes from the description.
- `config`, `annotation` and `KeyVaultBindingSource` describe themselves. The `annotation` parameter `type` lists its allowed values (`Responsibility`, `Unit`, `Subject`, `Usage`, `Context`, `Execution`).

A custom function describes itself next to `InvokeAsync`:

```csharp
public sealed class UpperBindingFunction : IBindingFunction, IDescribedBinding
{
    public BindingDescription Describe(string name) => new()
    {
        Summary = "The text in upper case.",
        Signature = $"{name}(<value>)",
        Examples = [new BindingExample { Expression = $"@{name}(@config(\"/name\"))", Description = "The name in upper case." }],
    };

    // InvokeAsync …
}
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

// Register the native key/value store as the default, public and secret sources (last, so it owns the names)
services.AddKeyValueBindings();
```

### Configuration Example

```json
{
  "ConnectionStrings": {
    "Default": "@sql.connectionString"
  },
  "ThirdPartyApi": {
    "ApiKey": "@secret(thirdParty.apiKey)",
    "LegacyKey": "@(prod.apiKey, securityVault)",
    "BaseUrl": "@[https://@host.value:@port.value/api]",
    "TimeoutMs": "@{@baseTimeout.value * 2}"
  }
}
```

`sql.connectionString`, `thirdParty.apiKey`, `host.value`, `port.value` and `baseTimeout.value` are key/value pairs of the configuration's view; `prod.apiKey` is the secret `prod--apiKey` of the vault registered as `securityVault`.

### Dependency Injection

`AddConfigurationBindings` registers `IConfigurationBindingResolver`. `CosmosConfigurationService` takes that resolver. `GetResolvedConfigurationInternalAsync` builds a `BindingScope` (a deep clone of the calculated document, plus a `ConfigurationBindingContext` for the configuration key) and calls `ResolveAsync`. When no resolver is registered, the calculated document is returned unchanged. `IBindingExecutor` remains the string entry point used by the resolver.

## Extending the System

`IBindingFunction` remains a general-purpose extension point beyond the two built-in functions documented above; register additional custom functions via `BindingsOptions.AddFunction` when further capabilities are needed. A function that needs ambient data beyond its own arguments (such as the document being resolved, or a caller-supplied context object) can read it from `BindingFunctionRequest.Document`/`BindingFunctionRequest.Context`, populated from the `BindingScope` passed into `IBindingExecutor.TryBinding`.
