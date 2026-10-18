# Answer 400 Instead of 500 for Missing or Invalid JSON Bodies

Linear: [424-61 Missing or null JSON body answers 500 instead of 400 in [FromBody] handlers](https://linear.app/424fun/issue/424-61/missing-or-null-json-body-answers-500-instead-of-400-in-frombody)

## Problem

25 HTTP functions of the Storyteller API take their JSON body through the worker's `[FromBody]` binding. The binding fails before the function runs when the body is empty, is the JSON literal `null`, has no `Content-Type`, or does not fit the type. `ExceptionHandlingMiddleware` cannot map that failure, so the caller gets **500** with exception details. This happens even where the parameter is nullable (`[FromBody] X? body`) and the handler has its own `body is null → 400` check: that check never runs.

The same failure hits a body that leaves out a C# `required` member of the DTO. System.Text.Json refuses such a body, even when the handler overwrites the member. The admin UI and the CLI create organizations and projects with `{ "Organization", "Project" }`, without the `OwnerId` of `AccessPointCreate` that the handler sets from the caller. So `POST v1/access/points` answers 500 to both of them today.

[Workload identity federation](2026-10-17%20Workload%20Identity%20Federation%20for%20Machine%20Access.md) (424-46) fixed this for its two `PUT`s: `FederationHttp` reads its body itself. This spec moves that reader to a shared helper and converts the other 25 parameters.

## Current State

All paths are relative to `src/Platform/Storyteller/`.

- **The binding.** `Program.cs` hosts the worker with `ConfigureFunctionsWebApplication`. There, `[FromBody]` (`Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute`) is converted by `FromBodyConversionFeature` of `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`. It binds through the MVC `IModelBinderFactory`, so the System.Text.Json input formatter and the MVC `JsonOptions` that `Program.cs` configures (lines 52-60) deserialize the body. These options are `NoChangeNamingPolicy`, `JsonStringEnumConverter`, `JObjectConverter`, `WhenWritingNull`, case-insensitive names and trailing commas. MVC rejects an empty body, `null`, a missing or unsupported content type and invalid JSON. The conversion then throws inside the function-execution step. The exception passes the authentication middlewares, `ErrorResponseMapping.TryMap` does not map it, and `ExceptionHandlingMiddleware` answers 500 with `Error` filled in.
- **The federation reader.** `Api.Functions/src/V1/FederationHttp.cs` has a private `ReadJsonBodyAsync<T>` (lines 584-607). It reads the body to the end with the cancellation token. An empty or whitespace body gives no value. Otherwise it deserializes with the injected `IOptions<JsonOptions>` (field `_bodyJsonOptions`), so a JSON `null` gives no value too. Only a `JsonException` gives a 400, built by `InvalidFederation` (ErrorCode `InvalidFederation`): `The request body is not {description}{at}.`. Here `{at}` is ` (at {Path})` when the exception has a path. It does not look at `Content-Type`. The callers are `PutFederatedIssuer` (line 176, `"a trusted issuer"`) and `PutMachineFederationRules` (line 281, `"a list of federation rules"`). `InvalidFederation` has three more callers.
- **The 25 parameters.** Each of these files has an alias `using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;` and the parameters below. Every body DTO is a reference type.

  | File | Handler (operation) | Body | Missing-body answer today (never reached) |
  |---|---|---|---|
  | `AccessHttp.cs` | `PostAccount` (CreateAccount) | `Models.AccountCreate` | none: dereferences |
  | | `PostAccessPoints` (CreateAccessPoint) | `AccessPointCreate` | none: dereferences |
  | | `PostGrantPermission`, `PostRevokePermission` | `Permission` | `TryValidatePermission`: `The permission is missing.` |
  | | `PostMachines` (CreateMachineAccess) | `MachineAccessCreate` | none: dereferences |
  | | `PutMachineAuthentication` | `MachineAuthenticationPolicy` | `MachineAuthenticationPolicies.Validate`: `The machine authentication policy is missing.` |
  | `CertificatesHttp.cs` | `PostSharedCertificate` | `SharedCertificateCreate` | none: dereferences |
  | `InvitationsHttp.cs` | `CreateInvitation` | `InvitationCreate?` | `The invitation is missing.` |
  | `MembersHttp.cs` | `SetMemberRole` | `MemberRoleUpdate?` | `A role other than None is required; remove the member instead.` |
  | `ViewsHttp.cs` | `CreateView` | `ViewCreate?` | `The view is missing.` |
  | | `UpdateView` | `ViewUpdate?` | default: `model ?? new ViewUpdate()` |
  | `KeyValuesHttp.cs` | `SetKeyValue` | `KeyValueWrite?` | `A body with Value is required.` |
  | | `RestoreKeyValues`, `PurgeKeyValues`, `ProtectKeyValues` | `KeyValueBulkRequest?` | `TryValidateBulk`: `A body with Keys or a KeyPrefix is required.` |
  | | `ExportKeyValues` | `KeyValueExportRequest?` | default: `export ??= new KeyValueExportRequest()` |
  | | `ImportKeyValues` | `KeyValueDocument?` | `A document with Items is required.` (`ImportInvalid`) |
  | `KeyValuePolicyHttp.cs` | `SetKeyValuePolicy` | `KeyValuePolicyUpdate?` | `A body with the policy members to change is required.` |
  | | `RotateKeyValueKey` | `KeyValueRotateRequest?` | `A body with the Protection to rotate is required.` |
  | | `AddRecipient` | `ClientEncryptionRecipientCreate?` | `A body with Name and PublicKey is required.` (`RecipientInvalid`) |
  | | `UpdateRecipient` | `ClientEncryptionRecipientUpdate?` | `A body with Name or IsEnabled is required.` |
  | `AnnotationsHttp.cs` | `SetAnnotations` | `List<Annotation>` | none: dereferences |
  | | `SetAnnotationsSimple` | `List<string>` | none: dereferences |
  | `AnnotationsHttp.Deleted.cs` | `FindDeletedAnnotations` | `AnnotationKeysRequest?` | `TryParseKeys`: `No annotation keys in the body.` (`InvalidKey`) |
  | | `RestoreAnnotations` | `AnnotationRestoreRequest?` | `TryParseKeys`: `No annotation keys in the body.` (`InvalidKey`) |

- **Authorization that depends on the body.** `SetAnnotations` authorizes the keys of its list (`CheckAccessToAnnotationsAsync(..., annotations.Select(a => a.AnnotationKey), Contributor)`) and filters its answer by the restriction. `FindDeletedAnnotations` and `RestoreAnnotations` authorize `model?.Keys ?? []`. `ExportKeyValues` checks secret rights only when the body asks for `IncludeSecrets`. Every other handler authorizes from the route and the identity only, or inside its service.
- **`required` members.** System.Text.Json refuses a body that leaves out a C# `required` member: `JsonException` at path `$`. The OpenAPI document does not list these members as required (the generator ignores the C# keyword), except for `Annotation`, which lists its five. Affected DTOs are `AccessPointCreate` (`Organization`, `OwnerId`), `Permission` (all four), `MachineAccessCreate` (`Organization`, `Project`, `Scope`), `SharedCertificateCreate.Label`, `InvitationCreate` (`Email`, `Role`), `MemberRoleUpdate.Role`, `ViewCreate.Name`, `KeyValueWrite.Value`, `KeyValueDocument.Items`, `KeyValueRotateRequest.Protection`, `ClientEncryptionRecipientCreate` (`Name`, `PublicKey`), `AnnotationKeysRequest.Keys`, `AnnotationRestoreRequest.Keys` and `Annotation`. `AccessPointCreate.OwnerId` is the only one that a shipped client leaves out: `OrganizationCreateDialog.vue`, `ProjectCreateDialog.vue` and the CLI's `AccessPointCreateCommand`. The handler always overwrites it (`AccessHttp.cs:170`).
- **OpenAPI.** Each body is described by an explicit `[OpenApiRequestBody]` attribute, not by `[FromBody]`. The nine key/value and policy operations publish `"required": true`. `ExportKeyValues` sets `Required = false`, and the other 15 set nothing. `SetAnnotations` and `SetAnnotationsSimple` publish inline array schemas. Both SDKs (NSwag C# in `Sdk.NSwag`, Orval TypeScript in `sdk.typescript`) are generated from `open.api.v0.9.31.json`.
- **Tests.** `Api.Functions/test` calls the handlers directly and passes each DTO as an argument, so no test sees the binding. `FederationHttpTests` already sends raw bodies, and its private `CreateApiJsonOptions()` mirrors `Program.cs` without `JObjectConverter`. `FederationHttpTests.PutRoutes_ReadTheirBodiesThemselves_NotThroughTheWorkersFromBodyBinding` guards only the two federation `PUT`s. `CertificatesHttp` has no tests.

## Proposed Changes

### 1. The shared reader

Add `Api.Functions/src/V1/RequestBodyExtensions.cs`, next to the handlers and like `DiffResultHttpExtensions`:

```csharp
namespace _42.Platform.Storyteller.Api.V1;

// The comment that explains why a handler reads its own JSON body moves here from FederationHttp.
internal static class RequestBodyExtensions
{
    public static async Task<(T? Value, IActionResult? Error)> ReadJsonBodyAsync<T>(
        this HttpRequestData request,
        JsonSerializerOptions options,
        string description,
        string? errorCode = null)
        where T : class;
}
```

The semantics of the federation reader stay exactly the same:

1. It reads `request.Body` to the end with `request.FunctionContext.CancellationToken`.
2. An empty or whitespace body gives `(null, null)`. A JSON `null` gives `(null, null)` through deserialization.
3. It does not look at `Content-Type`.
4. Only a `JsonException` gives a 400: `BadRequestObjectResult(new ErrorResponse($"The request body is not {description}{at}.") { ErrorCode = errorCode })`. `{at}` is ` (at {Path})` when the path is not empty. A missing `required` member is a `JsonException` at `$`.
5. Cancellation and I/O exceptions propagate.

The reader is internal: the test project already sees the API's internals. It does not throw `InvalidInputException`, because handlers already return their 400s as results and some 400s have no code.

### 2. JSON options for the handlers

Each converted handler class takes `IOptions<JsonOptions>` (`Microsoft.AspNetCore.Mvc.JsonOptions`, the options the binding used) as a constructor parameter and keeps `jsonOptions.Value.JsonSerializerOptions` in a field, as `FederationHttp` does. The parameter is not optional. It goes directly before the logger, as in `FederationHttp`, or last among the required parameters where the class has no logger. It always comes before the optional parameters:

- `AccessHttp(accessService, jsonOptions, logger, caProvider = null, …)`
- `CertificatesHttp(accessService, jsonOptions, logger, renewalService = null, sharedCertService = null)`
- `InvitationsHttp(invitations, authOptions, jsonOptions, profileResolver = null)`
- `MembersHttp(accessService, jsonOptions)`, `ViewsHttp(views, access, jsonOptions)`
- `KeyValuesHttp(keyValues, access, policies, jsonOptions, logger)`, `KeyValuePolicyHttp(policies, policyStore, access, jsonOptions)`
- `AnnotationsHttp(annotations, access, timeProvider, jsonOptions, logger)`

DI already registers the options (`services.Configure<JsonOptions>` in `Program.cs`). Resolving them from `FunctionContext.InstanceServices` was rejected: it hides the dependency, and many test helpers dispose their provider before the handler runs.

### 3. `FederationHttp` on the shared reader

`FederationHttp` drops its private copy. The two call sites become `request.ReadJsonBodyAsync<FederatedIssuer>(_bodyJsonOptions, "a trusted issuer", ErrorCodes.InvalidFederation)` and `request.ReadJsonBodyAsync<List<FederationRule>>(_bodyJsonOptions, "a list of federation rules", ErrorCodes.InvalidFederation)`. The null-to-default substitutions and `InvalidFederation` stay. The federation contract does not change, and every `FederationHttpTests` case keeps passing.

### 4. Converting the 25 parameters

For every handler:

1. Remove the `[FromBody]` parameter and, per file, the `FromBodyAttribute` alias. Parameters after it move one position to the left.
2. Keep the `[OpenApiRequestBody]` attribute unchanged. Add no response attributes, so the OpenAPI document and the SDKs do not change.
3. Read the body directly before its first use, after every check that does not need it (scope, identity, role, feature 501, route validation). Return a non-null reader error unchanged. The exception is the body-dependent authorization in section 5.
4. Feed the value into the existing missing-body check, so that an empty body and `null` give the same message (and code) as before.
5. Pass the reader the error code of that existing missing-body answer, and no code where the answer has none.
6. Where no missing-body check exists, add one with the established wording `The X is missing.` and no code.

| Handler | Read after | Description | Code | Missing body |
|---|---|---|---|---|
| `PostAccount` | the existing-account check | `an account` | — | new `The account is missing.` |
| `PostAccessPoints` | scope, caller id | `an access point` | — | new `The access point is missing.` |
| `PostGrantPermission`, `PostRevokePermission` | scope | `a permission` | — | `TryValidatePermission` (gets `[NotNullWhen(true)]` on its model) |
| `PostMachines` | scope, Contributor access | `a machine` | — | new `The machine is missing.` |
| `PutMachineAuthentication` | scope, Administrator, 501, key format | `a machine authentication policy` | — | `MachineAuthenticationPolicies.Validate` |
| `PostSharedCertificate` | scope, Administrator, 501 | `a shared certificate` | — | new `The shared certificate is missing.` |
| `CreateInvitation` | scope | `an invitation` | — | `The invitation is missing.` |
| `SetMemberRole` | scope | `a member role` | — | the role check |
| `CreateView` | scope, not a machine, Administrator | `a view` | — | `The view is missing.` |
| `UpdateView` | scope, not a machine, Administrator | `a view update` | — | default `new ViewUpdate()` (200) |
| `SetKeyValue` | scope, Contributor | `a key/value write` | — | `A body with Value is required.` |
| `Restore…`, `Purge…`, `ProtectKeyValues` | scope and Contributor, or Administrator | `a key/value bulk request` | — | `TryValidateBulk` |
| `ExportKeyValues` | scope, project access | `a key/value export request` | — | default `new KeyValueExportRequest()` (200, no secrets) |
| `ImportKeyValues` | scope, Contributor | `a key/value document` | `ImportInvalid` | `A document with Items is required.` |
| `SetKeyValuePolicy` | Administrator | `a key/value policy update` | — | its existing check |
| `RotateKeyValueKey` | Administrator | `a key rotation request` | — | its existing check |
| `AddRecipient` | Administrator | `a client encryption recipient` | `RecipientInvalid` | its existing check |
| `UpdateRecipient` | Administrator | `a client encryption recipient update` | — | its existing check |
| `SetAnnotations` | see section 5 | `a list of annotations` | — | new `The annotations are missing.` and `The annotations must not contain null entries.` |
| `SetAnnotationsSimple` | scope, Contributor | `a list of annotation keys or lines` | — | new `The annotations are missing.` |
| `FindDeletedAnnotations` | see section 5 | `an annotation lookup request` | `InvalidKey` | `TryParseKeys` |
| `RestoreAnnotations` | see section 5 | `an annotation restore request` | `InvalidKey` | `TryParseKeys` |

The two optional bodies keep their contract: for `ExportKeyValues` and `UpdateView`, an empty body or `null` keeps the default and answers 200. Only a body that does not fit answers 400. An empty list keeps its meaning: `SetAnnotations` and `SetAnnotationsSimple` answer `200 []`. Null and blank lines of `SetAnnotationsSimple` are still skipped by the service.

`PostAccount` answers a missing body with 400 rather than creating an account without memberships. Such an account is created with `{}` or with null members, and that is what the admin UI and the CLI send.

### 5. Authorization that depends on the body

`SetAnnotations`, `FindDeletedAnnotations` and `RestoreAnnotations` read the body after the scope check, and run the access check before any body 400:

1. Read the body.
2. Run `CheckAccessToAnnotationsAsync` over the keys of the body, null-safely. For `SetAnnotations` the reach is `(annotations ?? []).Where(a => a is not null).Select(a => a.AnnotationKey)`. For the deleted routes it is `model?.Keys ?? []`, as today. When the reader returned an error, the reach is empty.
3. Return the reader error, then the missing-body or null-entry 400, then the existing checks (`MaxInputItems`, `TryParseKeys`).

A caller without access to the project thus gets 403 before a body 400. A restricted machine that names a key outside its restriction still gets `403 AnnotationRestricted`. An empty reach passes the restriction (`EnsureAllows([])`), so a restricted machine with a broken body gets the 400. The null-entry check comes before the projection `a with { ProjectName = project, ViewName = view }`, which would dereference the entry.

`ExportKeyValues` reads its body after the project access check and before the `IncludeSecrets` check. `ProtectKeyValues` keeps checking secret rights after its body checks, as today.

### 6. Server-assigned members

`AccessPointCreate.OwnerId` (`Backend.Core/src/Accessing/Model/AccessPointCreate.cs`) stops being `required` and gets the default `string.Empty`. The handler sets it from the caller, and every other construction sets it explicitly. Without this change, the conversion would turn the 500 that the admin UI and the CLI get on `POST v1/access/points` into a 400. With it, they create organizations and projects again. The OpenAPI schema does not list the member as required now, so it does not change.

The other `required` members stay. A body that leaves one out answers the reader's 400 `The request body is not … (at $).` instead of 500. The shipped clients send them: `Permission.CreatedById` is overwritten by the handler but sent by the CLI, and `MachineAccessCreate.Organization` and `Project` are overwritten from the route but sent by the UI. Relaxing them is not needed for this fix.

### 7. Tests

`Api.Functions/test`:

1. **Shared fixture.** `FunctionTestDoubles` gets the API JSON options that `Program.cs` configures, including `JObjectConverter`. It exposes them as `IOptions<JsonOptions>` and `JsonSerializerOptions`, plus a helper that serializes a DTO the way a client sends it. `FederationHttpTests` uses the fixture instead of its private copy.
2. **Reader tests** (`RequestBodyExtensionsTests`): empty, whitespace and `null` give no value and no error. Malformed JSON and a wrong root shape give the 400 with the path. A missing `required` member gives `(at $)`. The error code is set only when given. Enum names, any property case and trailing commas are read. A body without `Content-Type` is read.
3. **Existing tests.** Every constructor call gets the fixture options. Every DTO argument becomes a JSON body sent with `Content-Type: application/json`, and every `null` argument an empty body. Request helpers that cannot send a body get an optional body parameter. `KeyValueRequests.Machine` gets it before `params roles`. Existing assertions stay.
4. **Body tests per handler group.** These cover Access (`PermissionHttpTests`, `MachineHttpTests`, `MembersHttpTests` for `PostAccount`, plus `PostAccessPoints`), key/values and policies (`KeyValuesHttpTests`), annotations (`AnnotationsHttpTests` and `MachineRestrictionHttpTests`), and members, invitations and views. A new `CertificatesHttpTests` adds the first tests of `CertificatesHttp`. For every required body, `""`, `"null"` and a wrong shape answer `BadRequestObjectResult`, with the expected message and code and no service call. The wrong shape is `[1, 2]` for objects and `{}` for lists. Each group also has one valid body without `Content-Type`, and one with enum names in another case. Further cases:
   - `ExportKeyValues` and `UpdateView`: an empty body and `null` answer 200 with the default, and a wrong shape answers 400.
   - `PostAccessPoints`: `{ "Organization", "Project" }` without `OwnerId` creates the access point with the caller as owner.
   - `SetAnnotations`: a null entry answers 400. A restricted machine with an outside key still gets `AnnotationRestricted` before any body 400. A restricted machine with a broken body gets the 400.
   - `CertificatesHttp` without the shared certificate service answers 501 whatever the body.
5. **Guards.** `RouteTableTests` gets an assembly scan: no `[Function]` method has a parameter marked with the worker's or MVC's `FromBodyAttribute`. The federation-only test in `FederationHttpTests` is removed, because the scan covers it. `OpenApiExportTests` checks that each of the 25 operations still publishes its `application/json` request body with the same schema (reference or inline array) and the same `required` flag. That flag is `true` for the nine key/value and policy operations and absent for the others.

### 8. Contract check

1. Export the OpenAPI document (`STORYTELLER_OPENAPI_EXPORT` with `OpenApiExportTests`) before and after the change. Compare them as parsed JSON, ignoring only `info.version`, which follows the git height.
2. Regenerate the NSwag SDK (NSwag 14.6.3, the arguments in `Sdk.NSwag/README.md`) and the TypeScript SDK (`npm ci`, `SPEC=… npm run generate`) from the new document, with `info.version` set to the committed `0.9.31.35842`.
3. Expect no diff in `Sdk.NSwag/src/ApiSdk.g.cs` or `sdk.typescript/src`. Regenerated files are committed only if a real difference appears.

### 9. Documentation

Update the pages that list per-route 400s:

- `access.md`: the account body is required (`{}` for no memberships), and the body 400s have no `ErrorCode`.
- `authentication.md`: the access-management error table gets the missing or invalid body of the account, access point, machine, permission, machine authentication policy and shared certificate.
- `key-values.md`: the export row gets 400 for a body that does not fit.
- `annotation-lifecycle.md`: bulk create gets a missing list and null entries.

One sentence states the rule for every JSON body: an empty body, `null` or JSON that does not fit answers 400, and `Content-Type` is not required.

### Out of scope

- The single-annotation `PUT`/`POST` (`AnnotationsHttp.SetAnnotation`) reads its own body with its own options and is not a `[FromBody]` handler. A JSON `null` or array root there still answers 500, because `RootElement.TryGetProperty` throws on a non-object. This is a follow-up.
- `ExceptionHandlingMiddleware`, `ErrorResponseMapping` and `Program.cs` do not change.

## Phases

1. **Shared reader** (sections 1, 3, 7.1, 7.2).
2. **Handlers**, one commit per group, each with its tests (sections 2, 4, 5, 6, 7.3, 7.4): Access, then certificates/invitations/members/views, then key/values and policies, then annotations.
3. **Guards, contract check and documentation** (sections 7.5, 8, 9).
