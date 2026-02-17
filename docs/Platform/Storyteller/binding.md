# Configuration Data Binding

This document describes the concept and implementation of the data binding system for configurations within the Platform Storyteller.

## Overview

The data binding system allows configuration values (stored as JSON) to dynamically reference data from external sources, such as Azure Key Vault. This is particularly useful for managing secrets or environment-specific values without embedding them directly in the configuration files.

The system is split into three main projects:
- **Binding.Abstractions**: Defines the core interfaces and data structures.
- **Binding.Core**: Provides the central orchestration logic and string parsing.
- **Binding.Azure.KeyVault**: Implements a concrete binding strategy for Azure Key Vault.

## Core Concepts

### Syntax

Binding is triggered by a string value starting with the `@` character. The system currently supports two main formats:

1.  **Default Source**: `@path`
    - Uses the strategy registered with the `"default"` key.
    - Example: `@database.connectionString`
2.  **Named Source**: `@(path, source)`
    - Uses a specific strategy identified by the `source` name.
    - Example: `@(db-password, primary-vault)`

### Binding Context

A `BindingContext` is created for each binding operation, containing:
- `Property`: The `JProperty` being processed (allows the strategy to update the value).
- `Path`: The path to the data in the external source.
- `BindingKey`: The identifier of the strategy to use.
- `IncludeSecrets`: A flag indicating whether secret data should be fetched.

## Project Structure

### Binding.Abstractions

Defines the fundamental building blocks:
- `IBindingStrategy`: Interface for implementing custom data retrieval logic.
- `IBindingExecutor`: Interface for executing the binding process on a JSON property.
- `IBindingRegistry`: Interface for registering new strategies.
- `BindingContext`: A record representing the state of a single binding operation.

### Binding.Core

Contains the implementation of the binding engine:
- `BindingService`: Implements both `IBindingExecutor` and `IBindingRegistry`. It uses a Regex to parse the `@` syntax and delegates the work to the appropriate `IBindingStrategy`.
- `BindingsOptions`: Facilitates the registration of strategies during dependency injection.

### Binding.Azure.KeyVault

Provides integration with Azure Key Vault:
- `KeyVaultBindingStrategy`: Fetches secrets from a specific Azure Key Vault instance. It automatically transforms configuration paths (using dots) to Key Vault secret names (using double dashes, e.g., `db.password` becomes `db--password`).
- `EntryPoint`: Provides the `AddAzureKeyVaultBindings` extension method to configure multiple Key Vaults and register them as binding strategies.

## Usage

### Registration

To enable data binding in your application, you need to register the core services and any specific strategies:

```csharp
services.AddConfigurationBindings(options =>
{
    // Register custom strategies here if needed
});

// Register Azure Key Vault bindings
services.AddAzureKeyVaultBindings(configuration);
```

### Configuration Example

When using the `KeyVaultBindingStrategy`, your JSON configuration might look like this:

```json
{
  "ConnectionStrings": {
    "Default": "@sql-connection-string"
  },
  "ThirdPartyApi": {
    "ApiKey": "@(prod-api-key, security-vault)"
  }
}
```

## Implementation Details

### Parsing Logic
The `BindingService` uses the following regular expression to identify and parse binding strings:
`^\@((?<path>[\w.]+)|\((?<pathSourced>[\w.]+)\,\s*(?<source>\w+)\)|(?<word>\w+)\((?<param>.+)\))$`

It handles:
- Simple paths (e.g., `@my.path`)
- Sourced paths (e.g., `@(my.path, my-source)`)
- Reserved for future use: word-based functions (e.g., `@pointer(...)`)

### Dependency Injection
The system is designed to be highly extensible via DI. `IBindingExecutor` is typically injected into services that process configurations (like a `ConfigurationService`), which then calls `TryBinding` on JSON properties before they are returned to the client.
