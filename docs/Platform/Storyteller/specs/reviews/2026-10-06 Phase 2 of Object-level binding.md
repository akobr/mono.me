# Phase 2 of Object-level binding

## Overview

Phase 2 of [2026-10-04 Object-level binding with JSON Logic and JSON-e](../2026-10-04%20Object-level%20binding%20with%20JSON%20Logic%20and%20JSON-e.md) registers `ConfigurationBindingResolver` and makes the resolved configuration read use it. Writes, raw reads, and calculated documents still keep the envelope. `binding.md` and `templating.md` are unchanged. Those are phase 3.

`CosmosConfigurationServiceTests` passed 25 tests, including the four new object-binding cases and the existing `@config` and `@annotation` reads.

## What Was Done

### 3. Registration

`AddConfigurationBindings` registers `IConfigurationBindingResolver`. The factory builds `ConfigurationBindingResolver` with the same `BindingExecutor` singleton and with `ILogger<ConfigurationBindingResolver>` when the host has logging. A missing logger stays null, and JSON Logic `log` messages stay dropped.

`Binding.Core` now references `Binding.Object` and `Microsoft.Extensions.Logging.Abstractions` (already 10.0.5 in `Directory.Packages.props`). `Api.Functions/Program.cs` is unchanged. Hosts that already call `AddConfigurationBindings` receive the resolver.

`Backend.CosmosDb.csproj` has no package reference to `JsonLogic` or `JsonE.Net`. Those assemblies follow `Binding.Core` at runtime.

### 5. Cosmos integration

`CosmosConfigurationService` takes an optional `IConfigurationBindingResolver` in place of `IBindingExecutor`. `GetResolvedConfigurationInternalAsync` still loads the calculated document, clones it into `BindingScope.Document`, and sets `ConfigurationBindingContext` from the configuration key. It then calls `ResolveAsync` and returns the same configuration instance. A null resolver returns the calculated document, which is the previous behavior when no string executor was injected.

The service no longer walks properties itself. `TryProcessDataBinding` is gone. String bindings and envelopes both go through the resolver from phase 1.

### 8. Tests

The Cosmos rows from section 8 are in `CosmosConfigurationServiceTests`. The test host registers `DecliningSecretSource` under `primaryVault`. That source returns `s3cret` for `db.password` only when `includeSecrets` is true, and declines otherwise. It stands in for `KeyVaultBindingSource`, which needs an Azure secret client the test host does not have. The decline rule is the same: no secrets means the original `@` string stays.

- A stored `jlogic` property `{ "+": [1, 2] }` resolves to decimal 3. A stored `jsone` array item resolves to `{ "name": "db" }`.
- A responsibility template in its own project contributes a `jlogic` envelope. `GetRawConfigurationAsync` still shows `$binding`. `GetResolvedConfigurationAsync` shows decimal 4. The template project is `config-tests-template-envelope`, so the shared `config-tests` project used by the other tests does not pick up that template.
- An update that adds an envelope returns the stored object, with `$binding` still `jsone`. `GetRawConfigurationAsync` returns that same envelope. The first `CreateOrUpdateConfigurationAsync` still returns `ToConfiguration()`, whose content is the unset calculated document, so the stored-envelope assertion uses the update return (`ToConfigurationFromContent`) together with the raw read.
- `@(db.password, primaryVault)` inside `$context` stays that literal string when `includeSecrets` is false, and the JSON Logic `var` result is that string. The same read with `includeSecrets` true returns `s3cret`. The path uses dots and the source name is an identifier because the binding tokenizer rejects `-`. The documentation example `@(db-password, primary-vault)` does not parse.

### 9. Phases

Phase 3 still has to update `binding.md` and the "What each read returns" paragraph in `templating.md`.
