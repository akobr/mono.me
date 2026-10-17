# Enforce the Annotation Restriction of Machines

Linear: [424-48 Enforce key-scoped machines](https://linear.app/424fun/issue/424-48/enforce-key-scoped-machines-fix-it)

## Problem

A machine can be created with an `AnnotationKey`. The admin UI says the key "limits the machine to this annotation and its descendants", and the mTLS spec ([2026-09-16](2026-09-16%20mTLS%20Per-Machine%20Authentication%20for%20Storyteller%20API.md), decision 26) says certificate authentication "must not bypass single-annotation access restrictions". Nothing enforces the key:

- A machine with a key reads and writes every annotation, configuration, schema, template and key/value of every view of its project.
- A machine certificate copies the key onto an `annotationKey` claim. Nothing reads that claim. API key, shared-certificate and client-credentials machines do not even carry it.
- The review [Phase D of WorkOS AuthKit Authentication](reviews/2026-10-04%20Phase%20D%20of%20WorkOS%20AuthKit%20Authentication.md) states it: "the `annotationKey` claim is not enforced anywhere".

The goal of the restriction is least privilege for each execution: an agent or a service that runs one execution gets a machine restricted to that execution (`exe.S.R.C`) and can't touch anything else in the project.

## Current State

All paths are relative to `src/Platform/Storyteller/`.

- **Machine model.** `MachineAccess.AnnotationKey` (`Abstractions.Access/src/Model/MachineAccess.cs`) is a nullable string. Null means the whole project. It is stored on the `MachineAccessEntity` (organization container, partition `{project}.access`, id = machine id) and never changes after creation. `AccessHttp.PostMachines` (`Api.Functions/src/V1/AccessHttp.cs`) stores it as sent: no parsing, no normalization. A whitespace value is stored as it is.
- **Authentication.** `MachineAuthenticationMiddleware` (`Api.Functions/src/Security/`) authenticates API keys and certificates, and `BearerAuthenticationMiddleware` client-credentials tokens. Both set `FunctionContext.Items[MachineIdentity]`. Only a machine certificate carries the key (`ClientCertificateValidationResult.AnnotationKey`, a copy taken when the certificate was issued). The middleware turns it into the `annotationKey` claim.
- **Authorization.** Every project endpoint calls `HttpRequestDataExtensions.CheckAccessToProjectAsync`, which calls `CheckAccessToAsync`. For a machine it calls `IAccessService.VerifyAccessForMachineAsync` and `MachineCredentialPolicy.EnsureAllowedAsync`. `VerifyAccessForMachineAsync` (`Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`) reads the organization container and point-reads the machine document, then drops the document and returns `bool`. The role argument is ignored for machines.
- **Debug builds.** `DEV_AUTH` (Debug|AnyCPU, `Api.Functions/src/Api.Functions.csproj`) makes `CheckAccessToAsync` return at once, so the machine branch never runs in Debug and no unit test reaches it.
- **Key semantics.** An annotation key is `type.name[.name…]` (`Abstractions.Annotations/src/AnnotationKey.cs`). Every ancestor of a key is derived from the key (`AnnotationKeyExtensions.GetAllAncestorKeys`), and `IsDescendantOf` is a pure, strict check. `TryParse` drops empty segments and accepts the type code in any case, while `ToString` keeps the original text; names are case sensitive.
- **Resolution.** The effective and resolved configuration of K merges K's ancestors, the view's templates, and the key/values and vault secrets that the bindings name, on the server, without a check per ancestor. It never reads a descendant or a sibling of K.
- **Endpoints.** 126 HTTP functions. 86 `CheckAccessTo*Async` calls in `V1/`. None checks a key.

## Proposed Changes

### 1. The rule

A machine with an `AnnotationKey` K is **restricted**:

1. It may address K and the descendants of K, in every view of its project, with what its scope allows.
2. It may not address an ancestor, a sibling or another branch of K.
3. It may not call an operation that spans the whole project or view (lists, key/values, templates, type-level schemas, exports and imports), because such an operation reaches annotations outside K.
4. It may read project metadata that holds no annotation data: the views of the project and the binding catalog.

A machine without a key (null, empty or whitespace) keeps today's behaviour. A stored key that does not parse restricts the machine to nothing (fail closed).

Ancestor data still reaches a restricted machine through the effective and resolved configuration of K: that is the purpose of inheritance and is not a direct read. A refused request answers **403** with `ErrorCode` `AnnotationRestricted`, before any data is read, so the answer does not depend on whether the annotation exists.

### 2. `MachineAnnotationRestriction`

New `Api.Functions/src/Security/MachineAnnotationRestriction.cs`:

```csharp
public sealed class MachineAnnotationRestriction
{
    public static MachineAnnotationRestriction None { get; }

    // null or whitespace: None; a key that does not parse: restricted to nothing.
    public static MachineAnnotationRestriction From(string? annotationKey);

    // The canonical form of a stored or requested key: the type code in lower case, the names as they are,
    // empty segments dropped. False when the key does not parse.
    public static bool TryNormalize(string? annotationKey, out string? normalized);

    public bool IsRestricted { get; }

    // K itself or a descendant of K. Always true for None; always false for an invalid key.
    public bool Allows(AnnotationKey key);
    public bool Allows(string? annotationKey);

    // 403 AnnotationRestricted unless every key is allowed.
    public void EnsureAllows(IEnumerable<string?> annotationKeys);

    // 403 AnnotationRestricted for a restricted machine: the operation spans the whole project.
    public void EnsureUnrestricted();
}
```

- Comparison: both keys are normalized. Equal canonical text, or `key.IsDescendantOf(root)` with the canonical root. `IsDescendantOf` builds ancestors with lower-case type codes, so the case of the requested type code does not matter.
- `ErrorCodes.AnnotationRestricted = "AnnotationRestricted"` is added to `Backend.Core/src/ErrorCodes.cs`. The exception is `AccessDeniedException`, already mapped to 403.

### 3. Access checks

`HttpRequestDataExtensions` gets one private core and four public entry points:

| Method | Restricted machine |
| --- | --- |
| `CheckAccessToProjectAsync(access, org, project, role)` (unchanged signature) | Refused: the operation spans the project |
| `CheckAccessToAnnotationAsync(access, org, project, string annotationKey, role)` (new) | Allowed for K and its descendants |
| `CheckAccessToAnnotationsAsync(access, org, project, IEnumerable<string?> annotationKeys, role)` (new) | Allowed when every key is K or a descendant |
| `CheckAccessToProjectMetadataAsync(access, org, project, role)` (new) | Allowed |

`CheckAccessToAsync(access, accessPointKey, role)` keeps its signature and behaves as the project check.

The key check parses the raw text the same way the handler does afterwards (`AnnotationKey.TryParse`), so handlers keep their order: scope, access, then parsing (400 for an invalid key). A restricted machine that sends a key that does not parse gets 403, because the key is not in its scope.

The machine branch of the core:

1. Reads the machine with `IAccessService.GetMachineAccessAsync(org, project, appId)`. This is the same point read `VerifyAccessForMachineAsync` makes, without the container read in front of it: the stream read answers 404 for a missing container too. A missing machine is still `SecurityTokenException` (401). `VerifyAccessForMachineAsync` stays on `IAccessService` for its other callers.
2. Calls `MachineCredentialPolicy.EnsureAllowedAsync`, as today.
3. Builds `MachineAnnotationRestriction.From(machine.AnnotationKey)` and applies the reach of the operation: `EnsureUnrestricted`, `EnsureAllows(keys)`, or nothing for metadata.

The restriction comes from the machine document, never from a claim, so it holds for every credential kind (API key, machine certificate, shared certificate with API key, client credentials of AuthKit, Entra ID and Keycloak) and needs no extra read.

**The machine branch runs in every build.** `DEV_AUTH` keeps skipping the user role check and the scope checks, but machine credentials are already checked for real in Debug, and a security rule that only runs in Release is never tested (no CI runs Release tests). This also makes the membership and policy checks of machines run in Debug.

### 4. Endpoints

Key-scoped operations switch to `CheckAccessToAnnotationAsync` with the route `{key}`:

- `AnnotationsHttp`: `GetAnnotation`, `GetDescendants`, `SetAnnotation`, `DeleteAnnotation`.
- `AnnotationsHttp.Deleted`: `GetDeletedAnnotation`, `GetAnnotationHistory`.
- `ConfigurationHttp`: the ten `{key}` operations (get, resolved, set, patch, delete, versions, version, the two version diffs and the view diff).
- `ConfigurationSchemaHttp`: the annotation schema (get, set, delete), the descendant-type schema (get, set, delete) and the combined definition.
- `ConfigurationSchemaHttp.Versions`: `PrepareSchemaReadAsync` checks the key when the route has one, and the project otherwise.

Bulk operations switch to `CheckAccessToAnnotationsAsync` with the keys of the body: `SetAnnotations` (each `Annotation.AnnotationKey`), `FindDeletedAnnotations` and `RestoreAnnotations` (`AnnotationKeysRequest.Keys`).

Metadata switches to `CheckAccessToProjectMetadataAsync`: `ViewsHttp.GetViews` (discovery still refuses machines first) and `BindingsHttp.GetBindingCatalog`.

Everything else keeps `CheckAccessToProjectAsync` and is therefore refused to a restricted machine: the annotation lists and the deleted list, `SetAnnotationsSimple` (its compact lines are parsed in the service), `GetConfigurations`, `GetConfigurationSchemas`, the type-level schemas and their versions, templates, configuration exports and imports, every key/value operation, the key/value policy and recipients. Machines can't reach the access-management operations anyway (no `User.Impersonation`).

A restricted machine reads key/values only through the resolved configuration of K.

Side effects that stay, by design:

- Creating or restoring an annotation in scope creates or restores its missing ancestors and updates the relation arrays of its parents. That is the minimum for the key to exist.
- Deleting K deletes its descendants, all in scope.

### 5. The `annotationKey` claim

`MachineAuthenticationMiddleware` stops adding the `annotationKey` claim. It was a copy taken when the certificate was issued, it existed only for certificates, and nothing read it. The machine document is the single source.

### 6. Machine creation

`AccessHttp.PostMachines` normalizes `AnnotationKey` with `MachineAnnotationRestriction.TryNormalize` before it creates the machine:

- null, empty or whitespace: stored as null (the whole project);
- a valid key: stored in canonical form (`SBT..payments` becomes `sbt.payments`);
- anything else: `400` with `ErrorCode` `InvalidKey`, and nothing is created.

The annotation does not have to exist: the key applies to every view.

### 7. OpenAPI

`Definitions.Descriptions.ResponseForbidden` also names a machine restricted to an annotation (`AnnotationRestricted`). The committed `open.api.v*.json` files are snapshots of releases and are not regenerated here.

### 8. Tests

`Api.Functions/test`:

- `MachineAnnotationRestrictionTests`: none, K itself, descendants of each type, ancestors, siblings, another branch, case of the type code, empty segments, names stay case sensitive, invalid stored key, invalid requested key, `TryNormalize`.
- `MachineAccessCheckTests`: through `CheckAccessTo*Async` with a fake `IAccessService`, in Debug: an unrestricted machine passes every reach; a restricted machine passes K and a descendant, gets `AnnotationRestricted` for an ancestor, a sibling, the project and a bulk with one foreign key, and passes metadata; a missing machine gets `SecurityTokenException`.
- Handler tests: a restricted machine is refused before any service call on a key outside K (configuration, annotation, schema), on a list and on key/values, and reaches the service for K.
- `MachineHttpTests`: creation normalizes the key, turns whitespace into null and refuses an invalid key.
- The test doubles of `IAccessService` used with machines return a machine from `GetMachineAccessAsync`.

### 9. Documentation

- `docs/Platform/Storyteller/authentication.md`: the `AnnotationKey` row, "What each call does" (no container read, the restriction), the 401/403 table and the `DEV_AUTH` sentence.
- `docs/Platform/Storyteller/access.md`: the restriction next to the roles.
- `docs/Platform/Storyteller/key-values.md` and `configuration-transfer.md`: restricted machines are refused.
- `docs/42for.net/platform/admin-ui.md` and `using-the-platform.md`: what the annotation restriction does.

### 10. Out of scope

Found during the research and left for separate issues:

- A restricted machine with configuration write can bind any key/value of the view (and, with `SecretsReadWrite`, any secret) into the configuration of K and read it through the resolved configuration. Restricted machines that only read should get a read scope (`ConfigurationRead` or `SecretsRead`).
- Client-credentials machines are limited by the scopes of their token, not by the stored `Scope`.
- `ResetMachineAccessAsync` upserts the machine document it read before the reset, which may write the old API key hash back.
- Certificate renewal does not write a new `mcert.*` record, so a renewed certificate may fail authentication.
- `ApiKeyAuthenticationMiddleware` is not registered and is stale.
- The access-management operations rely only on the missing `User.Impersonation` scope to keep machines out.
- `Api.Web` (legacy) has its own annotation endpoints without projects or machines.
- The admin UI mock and the `sform machine create` command don't normalize the key; the server now does.
