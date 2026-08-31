# Configuration Data Binding

This document describes the concept and implementation of the data binding system for configurations within the Platform Storyteller.

## Overview

The data binding system allows configuration values (stored as JSON) to dynamically reference data from external sources, such as Azure Key Vault, or to compute values via string interpolation and math expressions. This is particularly useful for managing secrets or environment-specific values without embedding them directly in the configuration files.

The system is split into four main projects, plus built-in functions contributed by `Backend.Core`:
- **Binding.Abstractions**: Defines the core interfaces and data structures (`IBindingExecutor`, `IBindingRegistry`, `IBindingSource`, `IBindingFunction`, `BindingScope`).
- **Binding.Language**: Implements the binding language interpreter (tokenizer, parser, evaluator), the `IBindingExecutor` entry point, the `JsonQuery` structured-query helper, and the built-in `@config` function.
- **Binding.Core**: Provides dependency-injection registration and options for wiring up sources and functions.
- **Binding.Azure.KeyVault**: Implements a concrete `IBindingSource` backed by Azure Key Vault.
- **Backend.Core**: Implements the built-in `@annotation` function on top of `IAnnotationService`.

## Core Concepts

### Syntax

Binding is triggered whenever a string value starts with the `@` character. The whole string is parsed as a single binding expression. Four forms are supported:

1.  **Path (default source)**: `@path.to.value`
    - Resolves `path.to.value` against the source registered with the `"default"` key.
    - Example: `@database.connectionString`
2.  **Sourced path**: `@(path.to.value, source)`
    - Resolves the path against the source registered under the given name.
    - Example: `@(db-password, primary-vault)`
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

## Project Structure

### Binding.Abstractions

Defines the fundamental building blocks:
- `IBindingExecutor`: Executes the binding process on a `JProperty`/`JValue`.
- `IBindingRegistry`: Registers named `IBindingSource`s and `IBindingFunction`s.
- `IBindingSource`: Resolves a `BindingRequest` (a path plus `IncludeSecrets`) to a `BindingValue`.
- `IBindingFunction`: Resolves a `BindingFunctionRequest` (a function name plus already-evaluated `BindingValue` arguments) to a `BindingValue`.
- `BindingValue`: A thin wrapper around a `Newtonsoft.Json.Linq.JToken`.
- `BindingException`: Base exception type for binding failures.

### Binding.Language

Contains the interpreter pipeline:
- `Tokenizer` / `Token` / `TokenType`: Lexes a binding string into tokens, tracking offsets for diagnostics.
- `Parser` and the AST node types (`PathStatement`, `SourcedStatement`, `FunctionStatement`, `InterpolationExpression`, `MathExpression`, etc.): Build a `BindingNode` tree via recursive descent.
- `BindingEvaluator`: Walks the AST, resolving statements against registered sources/functions and evaluating interpolation/math. Accepts an optional `BindingScope` (a `Document` and an opaque `Context`) which is forwarded onto every `BindingFunctionRequest` so functions can see ambient data beyond their own arguments.
- `BindingExecutor`: Implements both `IBindingExecutor` and `IBindingRegistry`; it guards on the leading `@`, then tokenizes, parses, evaluates, and assigns the result back onto the JSON token. `TryBinding` accepts an optional `BindingScope`.
- `JsonQuery`: Resolves a JSONPath or JSON Pointer (RFC 6901) expression against a `JToken`, auto-detecting the dialect from the expression's leading character (`$` for JSONPath, `/` or empty for JSON Pointer).
- `BindingFunctionArguments`: Shared helper that validates a function argument is a quoted string literal.
- `ConfigBindingFunction`: The built-in `@config` function (see "Built-in Functions" below).
- `BindingSyntaxException` / `BindingEvaluationException`: Binding-specific exception types.

### Binding.Core

Contains dependency-injection registration:
- `EntryPoint.AddConfigurationBindings`: Registers `BindingExecutor` as `IBindingExecutor`/`IBindingRegistry` and applies `BindingsOptions`.
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
    "ApiKey": "@(prod-api-key, security-vault)",
    "BaseUrl": "@[https://@host.value:@port.value/api]",
    "TimeoutMs": "@{@baseTimeout.value * 2}"
  }
}
```

### Dependency Injection

`IBindingExecutor` is typically injected into services that process configurations (like `CosmosConfigurationService`), which call `TryBinding` on JSON properties/values before returning them to the client.

## Extending the System

`IBindingFunction` remains a general-purpose extension point beyond the two built-in functions documented above; register additional custom functions via `BindingsOptions.AddFunction` when further capabilities are needed. A function that needs ambient data beyond its own arguments (such as the document being resolved, or a caller-supplied context object) can read it from `BindingFunctionRequest.Document`/`BindingFunctionRequest.Context`, populated from the `BindingScope` passed into `IBindingExecutor.TryBinding`.
