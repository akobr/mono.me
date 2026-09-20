# Configuration JSON Schema API Endpoints

## Problem Statement

The system needs API endpoints to store, update, retrieve, and delete JSON schemas per configuration type (e.g., responsibility, usage, execution). Before persisting a schema, the API must validate all existing configurations of that type against the new schema, returning detailed errors if any configurations are non-compliant. Schemas are stored in CosmosDB as entities similar to configuration entities.

## Current State

- Configurations are stored in CosmosDB as `ConfigurationEntity` objects within organization-scoped containers, keyed by `{viewName}.cnf.{annotationKey}` with partition keys derived from the annotation type.
- The `IConfigurationService` / `CosmosConfigurationService` handles CRUD for configurations using `IContainerRepositoryProvider` to obtain `Container` instances.
- The API layer (`ConfigurationHttp`) uses Azure Functions with OpenAPI attributes, security scopes, and `IActionResult` return types.
- Route definitions, tags, parameters, and route IDs live in `Definitions.cs`. Security scopes in `Security/Scopes.cs`.
- Entities inherit from `Entity` (with `PartitionKey`, `Id`, `ProjectName`, `ViewName`, `AnnotationKey`, `Name`) or `ExtendableEntity`.
- `EntityIdPrefixTypes` defines ID prefixes like `cnf` for configuration.
- Central NuGet package management via `Directory.Packages.props` — NJsonSchema is not yet present.

## Proposed Changes

### 1. Add NJsonSchema NuGet Package

- Add `NJsonSchema` to `Directory.Packages.props`.
- Reference it in `Backend.CosmosDb.csproj` (where the validation and service logic lives).

### 2. New Entity: `ConfigurationSchemaEntity`

File: `Backend.CosmosDb/src/Entities/Configurations/ConfigurationSchemaEntity.cs`

- Inherits from `Entity`.
- Properties: `Content` (`JObject`, the JSON Schema), `Author` (`string`), `Version` (`ulong`).
- Stored with ID format: `{viewName}.cfs.{annotationType}` (e.g., `default.cfs.rst`), using `cfs` as a new prefix.
- Partition key: `{projectName}.__schema__` — a dedicated schema partition per project, avoiding collision with annotation-type-based partitions.

### 3. New Entity ID Prefix

File: `Backend.CosmosDb/src/Entities/EntityIdPrefixTypes.cs`

- Add `ConfigurationSchema = "cfs"`.

### 4. New Model: `ConfigurationSchema`

File: `Abstractions.Annotations/src/Model/ConfigurationSchema.cs`

- Properties: `AnnotationType` (`string`, the type code, e.g. `rst`), `Version` (`ulong`), `Content` (`JObject`), `Author` (`string`).

### 5. New Model: `SchemaValidationErrorResponse`

File: `Api.Functions/src/Models/SchemaValidationErrorResponse.cs`

- Extends `ErrorResponse` with `Errors` (`IReadOnlyList<SchemaValidationErrorDetail>`).
- `SchemaValidationErrorDetail`: `AnnotationKey` (`string`), `ViewName` (`string`), `Errors` (`IReadOnlyList<string>`).

### 6. New Service Interface: `IConfigurationSchemaService`

File: `Backend.Core/src/Configuring/IConfigurationSchemaService.cs`

Methods:

- `Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string annotationType)`
- `Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string annotationType, JObject schemaContent, string author)` — validates all existing configs before saving, throws a domain exception with validation errors if non-compliant.
- `Task<bool> DeleteSchemaAsync(string organization, string project, string annotationType)`

### 7. Service Implementation: `CosmosConfigurationSchemaService`

File: `Backend.CosmosDb/src/Configuring/CosmosConfigurationSchemaService.cs`

- Uses `IContainerRepositoryProvider` to get the organization container.
- **SetSchema flow:**

1. 1. Parse the incoming JSON as an NJsonSchema `JsonSchema`.
    2. Query all `ConfigurationEntity` documents matching the annotation type across all views (using LINQ `Where` on `Id.StartsWith("{viewName}.cnf.{annotationType}")` — we must query without a specific view, iterating all matching configs within the project's partitions).
    3. For each config, validate `Content` against the schema using `NJsonSchema`.
    4. If any config fails validation, throw a `SchemaValidationException` containing all errors.
    5. If all pass, upsert the `ConfigurationSchemaEntity`.

- **GetSchema flow:** Read by ID from the schema partition.
- **DeleteSchema flow:** Delete by ID from the schema partition.

### 8. Domain Exception: `SchemaValidationException`

File: `Backend.Core/src/Configuring/SchemaValidationException.cs`

- Carries a list of `SchemaValidationError` (annotation key, view name, list of error messages) so the API can return structured errors.

### 9. DI Registration

File: `Backend.CosmosDb/src/EntryPoint.cs`

- Register `IConfigurationSchemaService` → `CosmosConfigurationSchemaService`.

### 10. API Endpoints: `ConfigurationSchemaHttp`

File: `Api.Functions/src/V1/ConfigurationSchemaHttp.cs`

Three endpoints, following the same pattern as `ConfigurationHttp`:

- **GET** `v1/{org}/{project}/configuration-schema/{annotationType}` → returns the schema or 404.
- **PUT** `v1/{org}/{project}/configuration-schema/{annotationType}` → sets schema (validates first); returns 200 or 409 with validation errors.
- **DELETE** `v1/{org}/{project}/configuration-schema/{annotationType}` → deletes schema, returns 200 or 404.

The schema applies project-wide (not view-scoped), since a type schema is the same across all views.

Security: uses `Configuration.ReadWrite` / `Default.ReadWrite` scopes for write, `Configuration.Read` / `Default.Read` for read.

### 11. Route Definitions

File: `Api.Functions/src/Definitions.cs`

- Add `Routes.ConfigurationSchema.V1` with route constants.
- Add `RouteIds.ConfigurationSchema` with operation ID constants.
- Add `Tags.ConfigurationSchema`.
- Add `Parameters.AnnotationType` constant.