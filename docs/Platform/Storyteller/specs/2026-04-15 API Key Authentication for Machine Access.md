# API Key Authentication for Machine Access
## Problem
Currently, machine access to the API requires authenticating via Azure AD (application registrations + client credentials) or Keycloak (client creation). Both approaches create external identity provider resources (Azure AD apps or Keycloak clients) and then authenticate via JWT Bearer tokens with `azp`/`appid` claims.
The goal is to add a simpler, self-contained API key authentication: when `CreateMachineAccess` is called, generate an API key, store its hash in CosmosDB, and return the raw key to the caller. Machines can then authenticate by passing `Authorization: ApiKey <key>` instead of acquiring a JWT.
## Current State
**Authentication flow:**
* `HttpRequestDataExtensions.GetClaims()` extracts claims from `HttpRequestData.Identities` (Azure Functions built-in auth) or manually parses a `Bearer` JWT from the `Authorization` header.
* `CheckScope()` verifies JWT scopes/roles.
* `CheckAccessToAsync()` detects machine identity via `TryGetApplicationIdentity()` (checks `azp`/`appid` claim ≠ own ClientId), then calls `IAccessService.VerifyAccessForMachineAsync()` to verify the app exists in CosmosDB.
* For user identity, it checks `AccountRole` via `GetAccountRoleAsync()`.
**Machine access creation:**
* `IMachineAccessService` interface (Backend.Core) with `CreateMachineAccessAsync`, `ResetMachineAccessAsync`, `DeleteMachineAccessAsync`.
* Two implementations: `AzureAdMachineAccessService` (creates Azure AD app registrations) and `KeycloakMachineAccessService` (creates Keycloak clients).
* `CosmosAccessService.CreateMachineAccessAsync()` calls the `IMachineAccessService`, stores the `MachineAccess` record in Cosmos (partition key `{project}.access`), masking the access key to `xxx***`.
* `MachineAccess` model has: `Id`, `ObjectId`, `AccessKey`, `Scope`, `AnnotationKey`.
* `MachineAccessEntity` (CosmosDB) mirrors this structure.
**DI registration:** `Program.cs` registers `AddAzureAdMachineAccess()` which wires `IMachineAccessService → AzureAdMachineAccessService`.
## Proposed Changes
### 1. New project: `Backend.ApiKeys`
Create `src/Platform/Storyteller/Backend.ApiKeys/src/Backend.ApiKeys.csproj` following the same pattern as `Backend.AzureAd` and `Backend.Keycloak`.
Files:
* `ApiKeyMachineAccessService.cs` — implements `IMachineAccessService`:
    * `CreateMachineAccessAsync`: generates a cryptographically random API key (e.g. 48-byte base64url), returns `MachineAccess` with `Id = Guid`, `ObjectId = Guid`, `AccessKey = rawKey`. The raw key is what the caller receives; `CosmosAccessService` already stores it masked.
    * `ResetMachineAccessAsync`: generates a new key, returns it.
    * `DeleteMachineAccessAsync`: no-op (returns `true`), since there's no external resource to clean up.
* `EntryPoint.cs` — `AddApiKeyMachineAccess()` extension method registering `IMachineAccessService → ApiKeyMachineAccessService`.
### 2. Store hashed API key in CosmosDB
Modify `MachineAccessEntity` to add a `HashedApiKey` property (SHA-256 hash of the raw key, stored as base64).
Modify `CosmosAccessService.CreateMachineAccessAsync()` to compute and store the hash before saving. Same for `ResetMachineAccessAsync()`.
Add a new method to `IAccessService` (and `CosmosAccessService`): `Task<MachineAccess?> ValidateApiKeyAsync(string apiKey)` — hashes the incoming key, queries CosmosDB across organization containers to find a matching `MachineAccessEntity` by `HashedApiKey`.
Alternatively (more efficient): store a separate API key lookup document in the `core` container with `id = hash(apiKey)` and partition key `apikeys`, containing `Organization`, `Project`, `MachineAccessId`. This avoids cross-partition queries.
### 3. API Key lookup entity (core container)
Create `ApiKeyEntity` in `Backend.CosmosDb/src/Entities/Access/`:
* `PartitionKey = "apikeys"`
* `Id = SHA256(rawKey)` (base64url)
* `Organization`, `Project`, `MachineAccessId` (the `Id` of the `MachineAccessEntity`)
* `Scope` (copied from machine access)
Update `CosmosAccessService`:
* On `CreateMachineAccessAsync`: after creating the `MachineAccessEntity`, also create an `ApiKeyEntity` in the core container.
* On `ResetMachineAccessAsync`: delete old `ApiKeyEntity`, create new one.
* On `DeleteMachineAccessAsync`: also delete the `ApiKeyEntity`.
* New method `ValidateApiKeyAsync(string apiKey)`: hash the key, read `ApiKeyEntity` from core container by `id + partitionKey("apikeys")`. Return the associated `MachineAccess` info (org, project, scope) if found.
### 4. Extend `IAccessService` interface
Add to `IAccessService`:
```csharp
Task<ApiKeyValidationResult?> ValidateApiKeyAsync(string apiKey);
```
With a new model `ApiKeyValidationResult` in `Backend.Core/src/Accessing/Model/`:
```csharp
public record ApiKeyValidationResult(string Organization, string Project, string MachineAccessId, MachineAccessScope Scope);
```
### 5. Modify authentication in `HttpRequestDataExtensions`
Update `GetClaims()` to also check for `Authorization: ApiKey <key>` header. If present:
* Call `IAccessService.ValidateApiKeyAsync(key)`.
* If valid, synthesize claims (e.g. `azp = machineAccessId`, plus scope claims matching the `MachineAccessScope`) and cache them. This makes the rest of the auth pipeline (`CheckScope`, `CheckAccessToAsync`, `TryGetApplicationIdentity`) work without changes.
* Challenge: `GetClaims()` is a static extension method on `HttpRequestData` and currently has no access to DI services. The `IAccessService` needs to be resolved. Options:
    * a) Resolve from `FunctionContext.InstanceServices` (the DI container is accessible via `@this.FunctionContext.InstanceServices.GetRequiredService<IAccessService>()`).
    * b) Create an `IFunctionsWorkerMiddleware` that handles API key auth before the function executes, injecting synthetic claims into the context items.
  Option (b) is cleaner — add an `ApiKeyAuthenticationMiddleware` that runs before the function, checks for the `ApiKey` header, validates it, and stores the result in `FunctionContext.Items` (similar to `CachedClaims`). Then `GetClaims()` can read from context items.
### 6. Add `ApiKeyAuthenticationMiddleware`
Create `Api.Functions/src/Security/ApiKeyAuthenticationMiddleware.cs`:
* Implements `IFunctionsWorkerMiddleware`.
* In `Invoke`: check `Authorization` header for `ApiKey ` prefix.
* If found, resolve `IAccessService`, call `ValidateApiKeyAsync`.
* If valid, store synthetic `List<Claim>` in `FunctionContext.Items[FunctionContextItemKeys.CachedClaims]`.
    * Claims to synthesize: `azp = machineAccessId` (so `TryGetApplicationIdentity` works), plus role claims based on `MachineAccessScope` (so `CheckScope` works).
* If invalid, short-circuit with 401.
* Register in `Program.cs` before `ExceptionHandlingMiddleware`.
### 7. Wire up in `Program.cs`
Replace `services.AddAzureAdMachineAccess()` with `services.AddApiKeyMachineAccess()` (or make it configurable). Register the new middleware:
```csharp
worker.UseMiddleware<ApiKeyAuthenticationMiddleware>();
worker.UseMiddleware<ExceptionHandlingMiddleware>();
```
### 8. OpenAPI security schema
Add a new security scheme in `Definitions.SecuritySchemas` for API key auth (`ApiKey` type, in `Header`, parameter name `Authorization`). Annotate machine-accessible endpoints with this scheme.
## Summary of files to change/create
**New files:**
* `Backend.ApiKeys/src/Backend.ApiKeys.csproj`
* `Backend.ApiKeys/src/ApiKeyMachineAccessService.cs`
* `Backend.ApiKeys/src/EntryPoint.cs`
* `Backend.CosmosDb/src/Entities/Access/ApiKeyEntity.cs`
* `Backend.Core/src/Accessing/Model/ApiKeyValidationResult.cs`
* `Api.Functions/src/Security/ApiKeyAuthenticationMiddleware.cs`
**Modified files:**
* `Backend.Core/src/Accessing/IAccessService.cs` — add `ValidateApiKeyAsync`
* `Backend.CosmosDb/src/Accessing/CosmosAccessService.cs` — implement `ValidateApiKeyAsync`, update Create/Reset/Delete to manage `ApiKeyEntity`
* `Api.Functions/src/Program.cs` — register `ApiKeyMachineAccess`, add middleware
* `Api.Functions/src/Api.Functions.csproj` — add reference to `Backend.ApiKeys`
* `Api.Functions/src/Definitions.cs` — add API key security schema
* `Api.Functions/src/Security/HttpRequestDataExtensions.cs` — minor: ensure `GetClaims()` picks up pre-cached claims from middleware (already works via `CachedClaims` check)
