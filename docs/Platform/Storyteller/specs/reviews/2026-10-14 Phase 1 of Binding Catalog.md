# Phase 1 of Binding Catalog

## Overview

Phase 1 covers the API (sections 1.1 to 1.7) of [Binding Catalog with Examples and IntelliSense](../2026-10-14%20Binding%20Catalog%20with%20Examples%20and%20IntelliSense.md), Linear 424-10. Bindings can now describe themselves, `BindingExecutor` lists every registered function and source as a catalog, and `GET v1/{organization}/{project}/bindings` (`GetBindingCatalog`) returns it. `config`, `annotation` and the Azure Key Vault sources describe themselves with a summary, parameters and examples. The OpenAPI document was exported as `open.api.v0.9.2.json`, and both SDKs were regenerated from it. Phase 2 (the Administration UI) builds on `useGetBindingCatalog`.

## What Was Done

### 1.1 Self-description (`Binding.Abstractions`)

- New `IDescribedBinding` with `BindingDescription Describe(string name)`.
- New `BindingDescription`, `BindingParameterDescription` and `BindingExample`, with the members of the spec.
- **Deviation:** the spec wrote the data types as records. They are `sealed class`es with `required`/`init` members, like `BindingRequest` and `BindingFunctionRequest` in the same project. The OpenAPI extension documents them the same way.

### 1.2 Catalog

- New `IBindingCatalog`, `BindingCatalog`, `BindingFunctionDescriptor` and `BindingSourceDescriptor` in `Binding.Abstractions`.
- `BindingExecutor` implements `IBindingCatalog`. `GetCatalog()` sorts by name (`StringComparer.Ordinal`) and maps each registration through `DescribeFunction` / `DescribeSource`. A binding without `IDescribedBinding` is listed by its name, with an empty summary and no parameters or examples. `IsDefault` is set for the `default` key (`DefaultSourceKey`).
- `AddConfigurationBindings` registers `IBindingCatalog` over the same `BindingExecutor` singleton (`TryAddSingleton`).

### 1.3 Descriptions of the registered bindings

- `ConfigBindingFunction`: signature `config("<expr>")`, parameter `expr`, examples `@config("/maxPrice")` and `@config("$.endpoints.db")`.
- `AnnotationBindingFunction`: signature `annotation("<expr>"[, "<type>"])`. The spec did not fix the form of the optional argument; brackets mark it optional. Parameters `expr` and `type` (optional, `Values` = the six ancestor types, from `nameof(AnnotationType.…)`); examples `@annotation("/owner")` and `@annotation("/currency", "Subject")`.
- `KeyVaultBindingSource`: `IsSecret = true`, a summary with the registered name, the example `@(db.password, <name>)`, and `@db.password` too when the source is registered as `default`.
- Every name in the descriptions comes from the `name` argument, so a function registered under another name gets matching examples.

### 1.4 Endpoint `GetBindingCatalog`

- `Definitions.Routes.Bindings.V1.Bindings` = `v1/{organization}/{project}/bindings`, `Definitions.RouteIds.Bindings.GetBindingCatalog`.
- `V1/BindingsHttp.cs`: scopes `Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.ReadWrite`, then `CheckAccessToProjectAsync` (Reader). Machines are allowed.
- The constructor takes `IAccessService` and an optional `IBindingCatalog? catalog = null`. Without bindings, the endpoint returns an empty `BindingCatalog`.
- Tag `Configurations`. Responses `200` (`BindingCatalog`), `401`, `500`; the existing document filter adds `403`.
- Checked against a running host: `GET /api/v1/acme/billing/bindings` returned `annotation` and `config` with their descriptions, and no sources (no `AzureKeyVaults` section locally).

### 1.5 OpenAPI and SDKs

- **Export.** The Chocolatey `func` (4.0.7032) is x86 and looks for an x86 .NET 10 runtime, which is not installed, so its worker never started. Rider's core tools (`AzureFunctionsTools/Releases/4.109.0/cli_x64/func.exe`) served the API from a temporary build folder on port `7072`. `/api/openapi/v3.json` was saved, `servers` was set back to `http://localhost:7071/api`, and the host was stopped.
- **Version.** The build after the first commit was `0.9.2.44924`, so the document is `open.api.v0.9.2.json`, in `Sdk.NSwag` and `sdk.typescript`.
- **Changes against `v0.9.1`:** one new operation (`GetBindingCatalog`) and five new schemas (`BindingCatalog`, `BindingFunctionDescriptor`, `BindingSourceDescriptor`, `BindingParameterDescription`, `BindingExample`). Nothing else changed apart from `info.version`.
- The schemas have no `required` list, as with the other models, so every member is optional in TypeScript.
- **`Sdk.NSwag`:** `src/ApiSdk.g.cs` regenerated with the README command (272 lines added, none removed); the README names `v0.9.2`.
- **TypeScript SDK:** `npm run generate` set the package version to `0.9.2`. New: `getBindingCatalog`, `useGetBindingCatalog`, `getGetBindingCatalogQueryKey`, `getGetBindingCatalogMockHandler`, and the five model types. Every generated file also changed its `OpenAPI spec version` header, as in the earlier generations.
- **`ui.admin`:** the installed SDK copy was refreshed with `npm install`. It again only dropped `libc` fields from `package-lock.json`, and that was reverted.
- The superseded `open.api.v0.9.1.json` files are kept.

### 1.6 Tests

| Project | Result |
| --- | --- |
| `Binding.Language/test` | 101 pass. New `BindingCatalogTests` (6): empty catalog; sorting of functions and sources; an undescribed binding listed by name; a described source with the registered key; `ConfigBindingFunction` described under another name; the `config` examples parse and resolve against a document (`[10,"sql://db"]`). |
| `Api.Functions/test` | 183 pass. New `BindingsHttpTests` (3): the endpoint returns the injected catalog and never resolves a source; an empty catalog without bindings; `AddConfigurationBindings` registers the executor as `IBindingCatalog`. `RouteTableTests` has `GET v1/{}/{}/bindings`. `OpenApiDocumentTests.BindingCatalogSchema_PublishesFunctionsAndSources` checks the five schemas. |
| `Cli/test` (on the regenerated `Sdk.NSwag`) | 110 pass |
| `sdk.typescript` | typecheck, lint, 59 pass (1 skipped), build |

- **Deviation:** the spec listed a test that a request without a matching scope is rejected. Debug builds define `DEV_AUTH`, which skips `CheckScope` and `CheckAccessToProjectAsync`, so the test run cannot see that rejection. The access double answers `GetAccountRoleAsync` with Reader, so the tests also pass in a Release build.
- As planned, `Binding.Core` and `Binding.Azure.KeyVault` got no test projects. The `annotation` description is covered by the running-host check above.

### 1.7 Documentation

`docs/Platform/Storyteller/binding.md`: a new **Binding Catalog** section (the endpoint, a response sample, the rules of `GetCatalog`, and a custom function that describes itself). `IBindingCatalog` and `IDescribedBinding` are listed under Binding.Abstractions, and `BindingExecutor` and `AddConfigurationBindings` mention the catalog.

### Notes

- The binding.md project overview still says that `@annotation` is contributed by `Backend.Core`. `AnnotationBindingFunction` lives in `Binding.Core/src`. This is older than this change and was left as it is.
