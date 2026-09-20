When a configuration is updated there is a support for simple concept to remove properties on the object or anywhere in the hierarchy by JPath and property $remove, the extension method is in src/Platform/Storyteller/Backend.CosmosDb/src/JsonExtensions.cs with name RemoveRequested(...). The update method is CreateOrUpdateConfigurationAsync(...) in src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs. I want to add support for proper patch operations by JSON Patch (RFC 6902), I want two possibilities first is $patch property on input content object, where same like for $remove property the content will be looped and patch executed. The second option is totaly new API endpoint which will work as proper patch for a configuration. I think the best option is to use JsonPatch.Net package as it needs to work over generic JSON object.
# JSON Patch (RFC 6902) Support for Configuration Updates
## Problem
Configuration updates currently support only deep merge + `$remove` via JPath. There is no support for precise, atomic JSON Patch operations (RFC 6902: add, remove, replace, move, copy, test). Two new capabilities are needed:
1. **Inline `$patch`** – a `$patch` property on the input content object (processed alongside `$remove`)
2. **Dedicated PATCH endpoint** – a new HTTP PATCH endpoint accepting a JSON Patch document as the body
## Current State
* `CreateOrUpdateConfigurationAsync` (CosmosConfigurationService.cs:287) handles create/update: deep-clones existing content → merges input → calls `RemoveRequested()` → validates → persists.
* `RemoveRequested()` (JsonExtensions.cs:66) extracts a `$remove` JArray of JPath strings, removes matched tokens, then removes the `$remove` property itself.
* API is Azure Functions (isolated worker) with routes defined in `Definitions.cs`. The `SetConfiguration` endpoint accepts POST/PUT. There is no PATCH method defined.
* The `Configuration` model (Abstractions.Annotations) holds `JObject Content`.
* Conversion between `Newtonsoft.Json.Linq.JObject` ↔ `System.Text.Json.Nodes.JsonObject` already exists in `JsonExtensions.cs` (`ToJObjectAsync` / `ToJsonObjectAsync`).
## Package Choice: JsonPatch.Net
**JsonPatch.Net** (json-everything, v5.0.2) is the best fit:
* Operates on generic `System.Text.Json.Nodes.JsonNode` — ideal for schema-less configuration objects (unlike `Microsoft.AspNetCore.JsonPatch`/`SystemTextJson` which require typed `JsonPatchDocument<T>`).
* Full RFC 6902 support (add, remove, replace, move, copy, test).
* Clean API: `JsonSerializer.Deserialize<JsonPatch>(json)` → `patch.Apply(node)` → `PatchResult` with `IsSuccess` / `Error`.
* Widely adopted (Aspire, K8s .NET client, Umbraco).
* The JObject↔JsonNode conversion cost is minimal and already abstracted.
Alternatives considered:
* `Microsoft.AspNetCore.JsonPatch` (Newtonsoft) – legacy, works on typed/dynamic objects, not ideal for raw JSON docs.
* `Microsoft.AspNetCore.JsonPatch.SystemTextJson` – .NET 10+, typed `<T>` only, not for generic JSON.
## Proposed Changes
### 1. Add JsonPatch.Net dependency
* `Directory.Packages.props`: add `<PackageVersion Include="JsonPatch.Net" Version="5.0.2" />`
* `Backend.CosmosDb.csproj`: add `<PackageReference Include="JsonPatch.Net" />`
### 2. Add `ApplyPatchRequested` extension method in `JsonExtensions.cs`
New async method following the `RemoveRequested` pattern:
* Checks for `$patch` property on the JObject.
* If absent, returns the object unchanged.
* Parses the `$patch` value (a JArray of RFC 6902 operations) into a `Json.Patch.JsonPatch`.
* Converts the JObject to `JsonNode` (using existing `ToJsonObjectAsync`).
* Applies the patch via `patch.Apply(node)`.
* If the patch fails, throws an `InvalidOperationException` with the error details.
* Converts the result back to `JObject` (using existing `ToJObjectAsync`).
* Removes the `$patch` property from the result.
### 3. Integrate `$patch` into `CreateOrUpdateConfigurationAsync`
In the existing update flow, call `ApplyPatchRequested()` **after** `RemoveRequested()` at each call site:
* New config path (line ~304): `value.RemoveRequested()` → `await value.ApplyPatchRequested()`
* Existing config with content (line ~343): `newContent.RemoveRequested()` → `await newContent.ApplyPatchRequested()`
* Existing config without content (line ~373): `newContent.RemoveRequested()` → `await newContent.ApplyPatchRequested()`
Processing order: merge → `$remove` → `$patch` → validate → persist.
### 4. New `PatchConfigurationAsync` service method
Add to `IConfigurationService`:
```csharp
Task<Configuration> PatchConfigurationAsync(FullKey key, JsonPatch patch, string author);
```
Implementation in `CosmosConfigurationService`:
* Load existing configuration (return 404-equivalent if missing or empty content).
* Convert existing `Content` (JObject) to `JsonNode`.
* Apply the `JsonPatch`; throw on failure.
* Convert result back to `JObject`.
* Validate against schema if available.
* Save history of previous version (same pattern as `CreateOrUpdateConfigurationAsync`).
* Invalidate ancestor configurations.
* Persist the new version.
### 5. New PATCH HTTP endpoint in `ConfigurationHttp.cs`
Add `PatchConfiguration` function:
* HTTP PATCH method on the existing configuration route.
* Content-Type: `application/json-patch+json` (standard RFC 6902 media type).
* Deserialize body as `Json.Patch.JsonPatch` (array of operations).
* Calls `PatchConfigurationAsync`.
* Returns updated `Configuration` or appropriate error.
Supporting changes in `Definitions.cs`:
* Add `public const string Patch = "patch";` to `Definitions.Methods`.
* Add `public const string PatchConfiguration = nameof(PatchConfiguration);` to `Definitions.RouteIds.Configuration`.
### 6. Tests
Add tests in `CosmosConfigurationServiceTests.cs`:
* `$patch` inline: replace, add, remove, move operations via `CreateOrUpdateConfigurationAsync`.
* `$patch` combined with regular merge and `$remove`.
* `$patch` on non-existing config (create path).
* `PatchConfigurationAsync`: apply patch to existing config, verify result + versioning.
* Error handling: invalid patch path, failed test operation.
Add unit tests for `ApplyPatchRequested` in isolation.