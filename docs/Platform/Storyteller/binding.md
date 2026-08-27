# Configuration Data Binding

This document describes the concept and implementation of the data binding system for configurations within the Platform Storyteller.

## Overview

The data binding system allows configuration values (stored as JSON) to dynamically reference data from external sources, such as Azure Key Vault, or to compute values via string interpolation and math expressions. This is particularly useful for managing secrets or environment-specific values without embedding them directly in the configuration files.

The system is split into four main projects:
- **Binding.Abstractions**: Defines the core interfaces and data structures (`IBindingExecutor`, `IBindingRegistry`, `IBindingSource`, `IBindingFunction`).
- **Binding.Language**: Implements the binding language interpreter (tokenizer, parser, evaluator) and the `IBindingExecutor` entry point.
- **Binding.Core**: Provides dependency-injection registration and options for wiring up sources and functions.
- **Binding.Azure.KeyVault**: Implements a concrete `IBindingSource` backed by Azure Key Vault.

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
- `BindingEvaluator`: Walks the AST, resolving statements against registered sources/functions and evaluating interpolation/math.
- `BindingExecutor`: Implements both `IBindingExecutor` and `IBindingRegistry`; it guards on the leading `@`, then tokenizes, parses, evaluates, and assigns the result back onto the JSON token.
- `BindingSyntaxException` / `BindingEvaluationException`: Binding-specific exception types.

### Binding.Core

Contains dependency-injection registration:
- `EntryPoint.AddConfigurationBindings`: Registers `BindingExecutor` as `IBindingExecutor`/`IBindingRegistry` and applies `BindingsOptions`.
- `BindingsOptions` / `BindingsOptionsExtensions`: Fluent API for registering sources (keyed, defaulting to `"default"`) and functions (by name) during startup.

### Binding.Azure.KeyVault

Provides integration with Azure Key Vault:
- `KeyVaultBindingSource`: Implements `IBindingSource`. Declines (`null`) unless `IncludeSecrets` is set, and transforms configuration paths (using dots) to Key Vault secret names (using double dashes, e.g., `db.password` becomes `db--password`).
- `EntryPoint`: Provides the `AddAzureKeyVaultBindings` extension method to configure multiple Key Vaults and register a `KeyVaultBindingSource` per vault.

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

`IBindingFunction` is a general-purpose extension point; no built-in functions (such as JSONPath or JSON Pointer lookups) are registered by default. Register custom functions via `BindingsOptions.AddFunction` when such capabilities are needed.
