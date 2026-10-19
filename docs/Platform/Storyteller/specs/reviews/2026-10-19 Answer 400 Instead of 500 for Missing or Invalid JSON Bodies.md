# Answer 400 Instead of 500 for Missing or Invalid JSON Bodies

## Overview

The 25 handlers of the Storyteller API that bound their JSON body with the worker's `[FromBody]` now read it themselves, through one reader shared with the federation routes. An empty body, `null`, a missing `Content-Type`, a body that is not UTF-8 or JSON that does not fit the type now answer **400**, never 500. Each handler keeps its existing missing-body message and code. Handlers that had none got one. The two optional bodies keep their defaults. The OpenAPI document and both SDKs are unchanged. This implements [Answer 400 Instead of 500 for Missing or Invalid JSON Bodies](../2026-10-18%20Answer%20400%20Instead%20of%20500%20for%20Missing%20or%20Invalid%20JSON%20Bodies.md) in full, for Linear 424-61. All three phases were delivered together, one commit per phase or handler group. After the pull request review, the reader also limits the size of each body (413). The single-annotation `PUT`/`POST`, left out at first, followed. The unit tests of `Api.Functions` went from 971 to 1123.

## What Was Done

### 1. The shared reader

`Api.Functions/src/V1/RequestBodyExtensions.cs` holds `internal static class RequestBodyExtensions` with `ReadJsonBodyAsync<T>(this HttpRequestData request, JsonSerializerOptions options, string description, string? errorCode = null) where T : class`. It returns `(T? Value, IActionResult? Error)`. The explanation of why a handler reads its own body moved there from `FederationHttp`.

It follows the spec: it reads the body with the cancellation token of the function context, a blank body and `null` give no value, `Content-Type` is not checked, and only a `JsonException` gives `The request body is not {description} (at {Path}).`, with the code only when one is given.

Deviation, from the review: the reader no longer decodes the body to a string, as the federation reader did. It copies the bytes to a buffer and deserializes them with `JsonSerializer.DeserializeAsync`. With the string decoding, a body that is not UTF-8 was not refused: `StreamReader` replaces the invalid bytes with U+FFFD, so a Latin-1 `"café"` would have been stored as `"caf�"`, possibly as a secret. The worker's binding refused such a body. Now it is a 400 at the path of the value. A body is blank when it holds only JSON whitespace (space, tab, CR, LF) after an optional UTF-8 byte order mark, rather than any Unicode whitespace. The bytes are read once, without a UTF-16 copy of the whole body.

### 2. JSON options for the handlers

Every converted class takes `IOptions<JsonOptions>` and keeps the serializer options in `_bodyJsonOptions`, as `FederationHttp` does. The constructors follow the spec:

- `AccessHttp(accessService, jsonOptions, logger, …optional)`
- `CertificatesHttp(accessService, jsonOptions, logger, …optional)`
- `InvitationsHttp(invitations, authOptions, jsonOptions, profileResolver = null)`
- `MembersHttp(accessService, jsonOptions)`
- `ViewsHttp(views, access, jsonOptions)`
- `KeyValuesHttp(keyValues, access, policies, jsonOptions, logger)`
- `KeyValuePolicyHttp(policies, policyStore, access, jsonOptions)`
- `AnnotationsHttp(annotations, access, timeProvider, jsonOptions, logger)`

`Program.cs` is unchanged, because the MVC `JsonOptions` were already registered. `FromBodyConversionFeature` of the AspNetCore extension binds through the MVC model binders. Reading with the same options therefore keeps valid bodies as they were: enum names, any property case and trailing commas.

### 3. `FederationHttp` on the shared reader

The private reader is gone. Both `PUT`s call the extension with `ErrorCodes.InvalidFederation`. Every federation test passes unchanged.

### 4. Converting the 25 parameters

Implemented as in the spec table. Every `FromBodyAttribute` alias is removed and every `[OpenApiRequestBody]` attribute is kept. No response attribute was added.

- **Access:** `PostAccount` answers `The account is missing.` after the existing-account check. `PostAccessPoints` answers `The access point is missing.`. `PostMachines` answers `The machine is missing.` after the Contributor check, before the annotation key is normalized. Grant and revoke pass the value to `TryValidatePermission`, whose model parameter got `[NotNullWhen(true)]`. `PutMachineAuthentication` reads after the key format check and lets `MachineAuthenticationPolicies.Validate` name a missing policy. `Validate` returns a message and cannot tell the compiler that the policy is set, so `Normalize(policy!)` carries a comment, like `model!.Descendants` in `AnnotationsHttp.Deleted`.
- **Certificates, invitations, members, views:** these follow the spec order. `PostSharedCertificate` answers `The shared certificate is missing.` after the 501 check. `UpdateView` keeps `model ?? new ViewUpdate()`, so a missing body clears the description.
- **Key/values and policies:** `ImportKeyValues` passes `ImportInvalid` and `AddRecipient` passes `RecipientInvalid`. No other key/value read passes a code. `ExportKeyValues` reads after the project access check and before the `IncludeSecrets` check, and an empty body or `null` still exports the whole view without secret values. `ProtectKeyValues` still checks secret rights after its body checks.
- **Annotations:** `SetAnnotationsSimple` answers `The annotations are missing.` and keeps the service's skipping of blank lines. Section 5 covers the other three.

Because of C# `required` members, `{}` is the reader's 400 at `$` rather than the handler's own message for most DTOs, as the spec's current state notes. The tests record it per handler.

### 5. Authorization that depends on the body

Implemented as specified. `SetAnnotations` builds the reach `(annotations ?? []).Where(a => a is not null).Select(a => a.AnnotationKey)`, keeps it in a local, and runs `CheckAccessToAnnotationsAsync` before it returns the reader error, `The annotations are missing.` or `The annotations must not contain null entries.`. The null-entry check comes before the projection and before `MaxInputItems`. The deleted lookup and restore run their access check over `model?.Keys ?? []` before the reader error, and pass `InvalidKey`.

### 6. Server-assigned members

`AccessPointCreate.OwnerId` is no longer `required` and defaults to `string.Empty`, with a comment. The handler sets it from the caller, and replaces any value the client sends, which a test checks. `{ "Organization", "Project" }`, the body of the admin UI and the CLI, creates the access point again. The Cosmos service, the CLI and the `Access.Certificates` integration tests build unchanged. The OpenAPI schema is unchanged.

### 7. Tests

- **Fixture:** `FunctionTestDoubles` has `ApiJsonOptions` (as `Program.cs`, `JObjectConverter` included), `ApiJsonSerializerOptions`, `ToJsonBody<T>` and `JsonContentType()`. `FederationHttpTests` uses them instead of its private copy.
- **Reader:** `RequestBodyExtensionsTests` has 10 tests. They cover:
  - blank, `null`, malformed JSON, a wrong shape and an unknown enum name (with the path);
  - a missing required member;
  - the error code;
  - enum names, any case and trailing commas;
  - no `Content-Type` and a list;
  - a body that is not UTF-8 and a byte order mark;
  - cancellation.
- **Handlers:** every constructor call passes the fixture, every DTO argument became a JSON body, and every `null` argument an empty body. Request helpers that had no body got an optional one. It is sent with `Content-Type: application/json` unless a test opts out. For `KeyValueRequests.Machine`, the body comes before `params roles`. Every group has theories over `""`, `"null"` and a wrong shape, and most also over `{}`. Each asserts the message, the code and that no service was called. Each group also has a valid body without `Content-Type` and one with names in another case. Specific cases:
  - `AccessPointHttpTests` (new): no `OwnerId` in the body, and an `OwnerId` sent by the client is replaced.
  - `CertificatesHttpTests` (new, the first tests of `CertificatesHttp`): 501 without the service whatever the body; missing or invalid bodies without touching the dependencies of `SharedCertificateService`; a 200 through `IssueAsync`. A hand-written fake implements its policy store, certificate store and authority.
  - `ExportKeyValues` and `UpdateView` with an empty body or `null` answer 200 with their defaults.
  - `SetAnnotations`: a null entry is a 400, a restricted machine with an outside key still gets `AnnotationRestricted` when another entry is null, and a restricted machine with a broken body gets the 400.
- **Guards:** `RouteTableTests.Functions_ReadTheirJsonBodiesThemselves_NotThroughAFromBodyBinding` scans every `[Function]` method for the worker's or MVC's `FromBodyAttribute`. It replaces the federation-only test, which was removed. `OpenApiExportTests.Document_JsonRequestBodies_KeepTheirSchemaAndRequiredFlag` renders the document once and checks the 25 operations against a table. The content type is only `application/json`, the schema is the exact same `$ref` or inline array, and `required` is `true` for the nine key/value and policy operations and absent for the other 16. It is one fact rather than a theory, because each rendering takes almost a second.

The ordering tests for a user without access cannot fail in Debug, because `DEV_AUTH` skips the user role check. The order is shown for restricted machines, as in the existing restriction tests.

### 8. Contract check

- The OpenAPI document exported before and after the change has no differences, compared as parsed JSON and ignoring `info.version`, which follows the git height. With `info.version` set to the committed `0.9.31.35842`, it equals `open.api.v0.9.31.json`.
- NSwag 14.6.3, with the arguments of `Sdk.NSwag/README.md`, regenerates `ApiSdk.g.cs` byte for byte.
- `npm ci` and `SPEC=… npm run generate` regenerate `sdk.typescript/src/generated` with no content change. Git listed the files as modified only because of line endings: Orval writes LF, and the checkout has CRLF.
- Nothing regenerated was committed.

### 9. Documentation

- `access.md`: the account body is required, and `{}` creates an account without memberships. Views and members name the missing body. The error-codes section no longer claims that every error has a code. It also states the general rule: a route with a JSON body does not need a `Content-Type`, and answers 400 for an empty body, `null` or JSON that does not fit, unless the route documents a default.
- `authentication.md`: the access-management error table has rows for a missing body and for a body that does not fit. The rows name the six bodies they cover. The `InvalidFederation` row now includes the federation `PUT` body that is not an issuer or a list of rules. The machine authentication `PUT` lists the missing body.
- `key-values.md`: the export row lists 400 and the default.
- `annotation-lifecycle.md`: bulk create lists the missing list and null entries. Lookup and restore list a body without `Keys` under `InvalidKey`.

### Review

Four reviews ran over the branch: correctness, authorization and security, tests, and docs and style. Each finding was then checked by a verifier who tried to refute it.

- The confirmed finding: the misfit-body row in `authentication.md` also seemed to cover the federation `PUT`s, which answer `InvalidFederation`. It is fixed.
- The verifier refuted the findings about the reader's UTF-16 copy and its lenient decoding, because the spec moved the federation reader unchanged. They were still fixed (section 1): taking a body the old binding refused and storing altered bytes is the opposite of what this change is for.
- A Release-only gap in the `PutMachineAuthentication` test (the role check is skipped only in Debug) was refuted, because the existing tests of the file rely on `DEV_AUTH` in the same way.

No new warnings in the touched files, and no spec was edited.

### Pull request review: body-size limits

CodeRabbit noted that the reader copied the whole body into memory with no limit of its own. Only Kestrel's 30,000,000-byte request limit applied, and a body above it failed inside the copy as a 500. On six routes any signed-in user reaches the read, and on the bulk annotation create, lookup and restore it runs before the role check. The finding was valid and is fixed.

- `ReadJsonBodyAsync` takes a required `int maxBytes` after `description`, so no route gets a limit by default. A larger declared `Content-Length`, or a body that grows past the limit while it is copied, answers **413** with `The request body is larger than {maxBytes} bytes.` and the new `ErrorCodes.BodyTooLarge`. This happens before any JSON is parsed, whatever `errorCode` the handler passes. Each handler returns the 413 where it returned the reader's 400, so the order of the access checks is unchanged.
- The copy is `TryCopyBodyAsync` in `RequestBodyExtensions`: the Content-Length check and the bounded copy of the archive upload, moved from `ViewTransferHttp`, which now calls it. It does not copy the read that crosses the limit.
- `Api.Functions/src/V1/RequestBodyLimits.cs` names the limit of each kind of body. Where the domain bounds a body, its limit is above the largest one, serialized as a System.Text.Json client does by default (a quote, `<` or a non-ASCII character as `\uXXXX`, 6 bytes). Where it does not, the limit is a cap above what the clients send: a view description, and the labels and values of annotations, have no length limit, and 1,000 import items at their limit do not fit below Kestrel's limit. These caps refuse some bodies the routes took before: a bulk create of annotations over 16 MiB, an import between 28 MiB and Kestrel's 30,000,000 bytes, and a view over 64 KiB. Every limit is below Kestrel's, so even a chunked body is refused with the 413 rather than by Kestrel's 500. The 32 KiB of the token endpoint is not used: a trusted issuer with a JWKS at its 16 KiB limit is already larger than that once its quotes are escaped.

  | Limit | Bodies |
  | --- | --- |
  | `Control`, 8 KiB | account, access point, permission (grant and revoke), member role, invitation, shared certificate, machine authentication policy, key/value policy, key rotation, recipient update, key/value export request |
  | `View`, 64 KiB | view create and update; the description has no limit of its own |
  | `Recipient`, 64 KiB | a new client encryption recipient with its public key |
  | `KeyValueBulk`, 64 KiB | restore, purge and protect (100 keys of 128 characters) |
  | `KeyValueWrite`, 512 KiB | one key/value write (a client-encrypted value has at most 49,152 characters) |
  | `AnnotationKeys`, 1 MiB | `…/simple`, the deleted lookup and the restore (100 keys) |
  | `FederatedIssuer`, 1 MiB | a trusted issuer |
  | `FederationRules`, 4 MiB | a machine and its federation rules (10 rules, each with up to 10 claims of 10 values) |
  | `Annotations`, 16 MiB | a bulk create of up to 100 annotations, whose labels and values have no limit of their own |
  | `KeyValueImport`, 28 MiB | an import of up to 1,000 items, just below Kestrel's limit. 1,000 values at their limit would not fit below it anyway, so raising Kestrel's limit is a separate decision. |

- Tests: `RequestBodyExtensionsTests` covers a body one byte over the limit and one at it, for a limit that takes one read and one that takes several. It also covers a declared length over the limit, which leaves the body unread, and the 413 code when the handler passes its own. The new `RequestBodyLimitsTests` serializes the largest accepted bodies as the System.Text.Json default encoder does and checks that each fits its limit: a client-encrypted write, a bulk request, an import of 1,000 plain values, a trusted issuer (also over 32 KiB), ten federation rules and 100 annotation keys.
- Docs: `access.md` states the 413 in the general rule and in its table. `authentication.md`, `key-values.md` (the code list and the Errors column of the body routes) and `annotation-lifecycle.md` list `BodyTooLarge` with the limits of their routes.
- Review: four reviews (the reader, the limits, the handlers, and docs and tests), and a verifier who tried to refute each finding. Two were confirmed and fixed: the comments promised the escaped worst case for the capped bodies too, and a test comment had lost its escape text. The other four were refuted, among them these two. The recipient comment's 13 KiB is the size of a private-key PEM, which the route also parses. The federation route table of `authentication.md` lists no 413, but its status lists were never complete, and its error table lists the 413. The reader and the handlers had no findings.

### Follow-up: the single annotation

`AnnotationsHttp.SetAnnotation` reads its body itself, because it parses it twice: once for `AnnotationType`, then into the runtime type of that type. It answered 500 for a root that is not an object (`null`, an array, a number, a string): `JsonDocument.TryGetProperty` throws `InvalidOperationException` there, and only `JsonException` was caught.

- The root kind is checked first; anything but an object answers `400` with the existing `No annotation object in body.`.
- The body is copied with `TryCopyBodyAsync` under the new `RequestBodyLimits.Annotation` of 4 MiB (one annotation is at most a 2 MB Cosmos item, stored in the escaped form), after the access check as before; a larger body or declared `Content-Length` answers `413 BodyTooLarge`. `RequestBodyExtensions.BodyTooLarge` became public for it.
- The bytes are parsed as they came, after an optional byte order mark (`WithoutByteOrderMark`), instead of a `StreamReader` string: a body that is not UTF-8 is a `400` rather than a title with U+FFFD. The runtime type is deserialized from the parsed `JsonDocument`, so the body is parsed once and not copied to UTF-16.
- Tests in `AnnotationsHttpTests`: the four root kinds, a body that is not UTF-8, a byte order mark, a body over the limit and a declared length over it, each without calling the service.
- Docs: the `PUT`/`POST` row and `BodyTooLarge` in `annotation-lifecycle.md`.

### Left out
- The new 400s and the 413 are described in the docs, not in the OpenAPI responses, so the SDKs stay unchanged. The NSwag client throws its generic `ApiException` for the 413, and the response text keeps the message.
- The body readers that do not use `ReadJsonBodyAsync` have no limit of their own yet: configurations, schemas, templates and the export start.
