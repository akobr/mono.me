# Resetting a Certificate machine answers 500 instead of 400

Linear: [424-102](https://linear.app/424fun/issue/424-102/resetting-a-certificate-machine-answers-500-instead-of-400). Found while building the MCP server (424-84, review `reviews/2026-10-24 MCP Server for the Storyteller API.md`, "Left for later").

Line numbers refer to the tree this plan was written against (`main` at `08afd3d9`). The CodeRabbit coding plan attached to the issue was read as a second opinion; where this plan differs, the reason is given in the section.

## Problem

`PUT v1/{org}/{project}/access/machines/{id}` (reset) of a `Certificate` machine answered `500`, which tells the client to try again later, and no retry helps. The issue asks for `400` with the stable code `MachineAccessNotSupported`, a message that says what to do instead (a Certificate machine has no key to reset, it renews its certificate over mTLS, and it is revoked by deleting it), and a test.

Since the issue was written, the race-free reset (`specs/2026-10-26 Race-Free Reset of Machine Credentials.md`, section 5) added a `Certificate` arm to the router, so the reset of a Certificate machine already answers `400`. What is still wrong:

1. **No error code.** `MachineAccessNotSupportedException` derives from `InvalidOperationException` and carries no code. The four HTTP catches build `ErrorResponse { Message }` by hand, so clients cannot branch on the refusal. The same holds for every other refusal of this exception: a credential kind the project does not enable or the deployment does not configure, a shared certificate on another kind, a scope the identity provider cannot carry, and a shared certificate of a project without `CertificateAndApiKey`.
2. **No central mapping.** `ErrorResponseMapping` does not know the exception, so a throw that misses a local catch answers `500`.
3. **The message** says "no secret to reset" and "create a new machine and delete this one"; the issue asks for the key, mTLS renewal and revocation by delete.
4. **One `500` is left in the reset.** `CosmosAccessService.ResetMachineAccessAsync` throws `InvalidOperationException("The machine access {id} reset failed.")` when the router returns no key and the record it read had no API key hash. The 2026-10-26 review lists it under "Left for later": "A machine that had no hash at the first read still answers `500` "reset failed", as before."
5. **No test** covers any of the four HTTP catches: the reset, create and delete in `AccessHttp` and the shared certificate in `CertificatesHttp`.

## Current State

- `Backend.Core/src/Accessing/MachineAccessNotSupportedException.cs:4`: `public sealed class MachineAccessNotSupportedException : InvalidOperationException`, with a message-only constructor.
- `Backend.Core/src/StorytellerException.cs`: the base of the coded domain errors, with `ErrorCode`. `ConflictException`, `InvalidInputException` and `FeatureNotConfiguredException` are one-line primary-constructor records of it.
- `Backend.Core/src/ErrorCodes.cs:39-42`: the federation codes (`InvalidFederation`, `MachineNotFederated`, `FederatedIssuerInUse`). No `MachineAccessNotSupported`.
- Throw sites (all keep their messages):
  - `Access.Certificates/src/PolicyAwareMachineAccessService.cs:50` (kind not enabled), `:58` (shared certificate on another kind), `:125` (Certificate reset), `:145` (ClientCredentials not configured), `:151` (Federated not configured).
  - `Access.Federation/src/FederatedMachineAccessService.cs:48`, `:53` (Federated reset).
  - `Access.AuthKit/src/AuthKitMachineAccessService.cs:38`, `:118` and `Access.AzureAd/src/AzureAdMachineAccessService.cs:63`, `:189` (the provider cannot carry a scope, or is not configured).
  - `Access.Certificates/src/SharedCertificateService.cs:42` (the project does not enable `CertificateAndApiKey`).
- `Access.Certificates/src/PolicyAwareMachineAccessService.cs:11`: `CertificateResetNotSupportedMessage = "A Certificate machine has no secret to reset. Its certificate is renewed in the renewal window; to replace it, create a new machine and delete this one."`; `:119-128` routes the reset by the stored kind.
- `Api.Functions/src/ErrorHandling/ErrorResponseMapping.cs:15-29`: a switch over the coded exception types; anything else is `(500, null)`, and `ToActionResult` throws `ArgumentException` for it. `ExceptionHandlingMiddleware.cs:35` answers mapped exceptions with their status and logs a warning; the rest is a logged `500`.
- `Api.Functions/src/V1/AccessHttp.cs:470` (`PostMachines`), `:513` (`PutMachine`), `:554` (`DeleteMachine`) and `CertificatesHttp.cs:185` (`PostSharedCertificate`): `catch (MachineAccessNotSupportedException exception) { return new BadRequestObjectResult(new ErrorResponse { Message = exception.Message }); }`.
- OpenAPI: the `400` of `PutMachine` (`AccessHttp.cs:486`) is "The machine id is not valid, or the machine has no secret to reset (a Federated or Certificate machine)."; of `PostMachines` `Definitions.Descriptions.ResponseBadRequestMachineAccessCreate` (`Definitions.cs:483`, pinned by `OpenApiExportTests.cs:542-543`); of `PostSharedCertificate` `CertificatesHttp.cs:147`. `DeleteMachine` (`AccessHttp.cs:519-530`) declares no `400`, although an invalid id and the refusal both answer one.
- `Backend.CosmosDb/src/Accessing/CosmosAccessService.cs:580-641`, the reset:
  - `:594` calls the router with the record it read.
  - `:599-604`: a null key is `NotFoundException` ("was deleted during its reset") when the record had a hash, else `InvalidOperationException` ("reset failed", `500`).
  - Who returns a null key: `ApiKeyMachineAccessService.ResetMachineAccessAsync` (`Access.ApiKeys/src/ApiKeyMachineAccessService.cs:42-49`) when no hash is stored, and the identity providers when they give no secret: Entra ID when the application is gone (`AzureAdMachineAccessService.cs:135-142`), Keycloak on any non-success answer (`KeycloakMachineAccessService.cs:78-87`).
  - Who removes a hash: only `CosmosMergedApiKeyHashStore.DeleteAsync` (`:158-163`), called by a machine delete (it removes the key before the document, `CosmosAccessService.cs:656-657`) and by the compensation of a failed create.
  - `MachineCredentialKind` and `CertificateThumbprint` came in the same commit (`3a6f9829c`, mTLS), so every Certificate machine has its kind stored; a Certificate machine never reaches the API key service.
- Tests:
  - `Api.Functions/test/MachineHttpTests.cs:823-901`: `RecordingAccess` records calls; its reset fails only with `NotFoundException` for an unknown machine, and create and delete never fail. `ErrorResponseMappingTests.cs:12-27` and `ExceptionHandlingMiddlewareTests.cs:20-27` hold the `ClientErrors` rows. `CertificatesHttpTests.cs:172-224`: `SharedCertificateBackend` always enables `CertificateAndApiKey`.
  - `Access.Certificates/test/MachineResetTests.cs:79-87` and `testint/MachineResetTests.cs:81-93`: the Certificate reset throws the exception with the constant message and writes nothing. No test covers a record without a hash at the first read.
- Clients and docs that quote the message or the status:
  - `ui.admin/src/app/mock/federation.ts:14-15` (`certificateResetNotSupportedMessage`), used by `app/mock/access-handlers.ts:576`; `test/unit/access/access.test.ts:493` asserts the literal. The mock's refusals (`access-handlers.ts:508`, `:511`, `:572`, `:576`) carry no `ErrorCode`.
  - `Mcp/src/Tools/MachineTools.cs:121`: "The API has nothing to reset on a Certificate machine and answers 500, which no retry fixes."; the tool refuses before the call with its own text (`MachineText.CertificateReset`). `docs/Platform/Storyteller/mcp.md:339` already uses the wording the issue asks for.
  - `docs/Platform/Storyteller/authentication.md`: `:904` (shared certificates `400`), `:941` (create `400`s), `:975` (the Certificate message), `:977` (a reset that meets a delete answers `404` "whenever the delete lands"), `:1049` (AuthKit), `:1065` (Entra ID), `:1292` (`sform machine reset`), `:1485` (Federated not configured) and the error table row `:1762` (`400`, `ErrorCode` none). `access.md` does not document the machine routes.
- No error code list exists in the conformance suite, the TypeScript SDK, `ui.admin`, `sform` or the MCP server; `ErrorCode` is a free string everywhere, so the new code needs no contract entry or version bump.

## Proposed Changes

### 1. The error code and the exception

- `Backend.Core/src/ErrorCodes.cs`: `public const string MachineAccessNotSupported = "MachineAccessNotSupported";`, next to the machine codes, with a comment: a machine credential kind or an operation the project or the deployment does not support, or that the machine's kind does not have.
- `Backend.Core/src/Accessing/MachineAccessNotSupportedException.cs`: `public sealed class MachineAccessNotSupportedException(string message) : StorytellerException(message, ErrorCodes.MachineAccessNotSupported);`, in the pattern of `ConflictException`. The comment says the API answers it with `400` and `MachineAccessNotSupported`. The throw sites stay as they are.
- Changing the base type is safe: a search found no catch, filter, switch arm or assertion that relies on `InvalidOperationException` on the path of this exception; the four HTTP catches match the exact type, and no `catch (StorytellerException)` precedes them.

### 2. The HTTP answer

- `ErrorResponseMapping.TryMap`: `MachineAccessNotSupportedException e => (HttpStatusCode.BadRequest, e.ErrorCode)`, after `InvalidInputException`. A throw that reaches the middleware gets the same coded `400`.
- The four catches return `ErrorResponseMapping.ToActionResult(exception)`. They stay, so each handler keeps its `400` without the middleware's warning log; the body now carries `ErrorCode`. (`ToActionResult` returns an `ObjectResult` with status `400`, not a `BadRequestObjectResult`.)
- OpenAPI descriptions name the code:
  - `PutMachine` `400`: "The machine id is not valid, or the machine has no key to reset: a Federated or Certificate machine, or a kind the deployment does not configure (MachineAccessNotSupported)."
  - `DeleteMachine` gets the `400` it answers: "The machine id is not valid, or the deployment does not configure the machine's credential kind (MachineAccessNotSupported)."
  - `ResponseBadRequestMachineAccessCreate` ends with "… or the credential kind is not supported or its identity provider cannot carry one of the scopes (MachineAccessNotSupported)."
  - `PostSharedCertificate` `400`: "An invalid label, or the project does not enable CertificateAndApiKey, which shared certificates belong to (MachineAccessNotSupported)."
- The committed OpenAPI snapshots and the SDKs are not regenerated here; this repository regenerates them in a separate `chore(platform): regenerate the SDKs` change. The descriptions change no schema.

### 3. The Certificate message

`PolicyAwareMachineAccessService.CertificateResetNotSupportedMessage` becomes "A Certificate machine has no key to reset: it renews its certificate itself over mTLS, and is revoked by deleting it." That is the issue's wording and the text of `mcp.md:339`. The routing switch does not change.

### 4. The last `500` of the reset

In `CosmosAccessService.ResetMachineAccessAsync`, a null key from the router for a record **without** a hash:

- **`ApiKey` and `CertificateAndApiKey`:** only a delete removes the hash of an API key, before it deletes the document. A record of these kinds without a hash is being deleted (or its delete stopped after the key), so the reset answers `404` with the same "The machine access {id} was deleted during its reset." as a delete that lands after the read. That is what `authentication.md:977` promises: the reset answers `404` "whenever the delete lands".
- **Any other kind** (in practice `ClientCredentials`, whose identity provider gave no secret): unchanged, `InvalidOperationException` and `500`. Keycloak returns no secret on any failed answer, including a transient one, so a client error would be wrong there.

**Differs from the CodeRabbit plan**, which throws `MachineAccessNotSupportedException` for every record without a hash. A `ClientCredentials` machine never has a hash, so a provider failure would become `400` "not supported", and a reset racing a delete would get `400` where the docs promise `404`. A Certificate machine stored with another kind, the case CodeRabbit guards against, cannot exist: the kind and the certificate fields were added together.

### 5. Clients

- `Mcp/src/Tools/MachineTools.cs:121`: the comment says the API refuses the reset of a Certificate machine with `400 MachineAccessNotSupported`, and that the tool refuses first with its own explanation. The check stays.
- `ui.admin` mock: `certificateResetNotSupportedMessage` gets the new text, and the mock's refusals of the reset (Federated, Certificate) and of the create (kind not enabled, shared certificate on another kind) carry `'MachineAccessNotSupported'`, as the API does. `access.test.ts:493` asserts the new text and the code.

### 6. Tests

`Api.Functions/test` (Shouldly):

- `ErrorResponseMappingTests` and `ExceptionHandlingMiddlewareTests`: a `ClientErrors` row `MachineAccessNotSupportedException` → `400`, `MachineAccessNotSupported`.
- `MachineHttpTests`: `RecordingAccess` gets an optional `Refusal` exception that create, reset and delete throw after recording their call. New tests:
  - `PutMachine_OfACertificateMachine_IsBadRequestWithMachineAccessNotSupported`: a stored `Certificate` machine and the router's refusal answer `400` with `CertificateResetNotSupportedMessage`, `ErrorCodes.MachineAccessNotSupported` and no `Error`, after the lookup and the reset call.
  - `PostMachines_OfAKindTheProjectDoesNotEnable_IsBadRequestWithMachineAccessNotSupported` and `DeleteMachine_OfAKindTheDeploymentDoesNotConfigure_IsBadRequestWithMachineAccessNotSupported`: the same answer for create and delete.
- `CertificatesHttpTests`: `SharedCertificateBackend` takes the enabled kinds; `PostSharedCertificate_ProjectWithoutCertificateAndApiKey_IsBadRequestWithMachineAccessNotSupported` asserts the coded `400` and that nothing is issued.
- `OpenApiExportTests`: the pinned create `400` text follows; the `400` of the reset, the delete and the shared certificate name `MachineAccessNotSupported`.

`Access.Certificates/test`: `RouterReset_OfACertificateMachine_IsNotSupported_AndNeverReachesTheApiKeyService` also asserts `ErrorCode`.

`Access.Certificates/testint` (Cosmos; run with `STORYTELLER_TESTS_USE_CONTAINER=true`, because the fixture deletes every database of the emulator it uses):

- `Reset_OfACertificateMachine_IsNotSupported_AndWritesNothing` also asserts `ErrorCode`.
- `Reset_OfAMachineWhoseDeleteRemovedItsKey_IsNotFound_AndWritesNothing`, a theory over `ApiKey` and `CertificateAndApiKey`: the hash is removed before the reset, as a delete does first; the reset answers `NotFoundException` with "was deleted during its reset." and the document keeps its ETag.
- `Reset_OfAClientCredentialsMachineWithoutASecret_IsNotAnsweredAsDeleted`: a stored `ClientCredentials` machine and a router that returns no secret still answer `InvalidOperationException`, so the `404` stays limited to the kinds with an API key.

### 7. Documentation

`docs/Platform/Storyteller/authentication.md`:

- `:975`: the new Certificate message, and the code of both refusals (`400 MachineAccessNotSupported`).
- `:904`, `:941`, `:1049`, `:1065`, `:1485`: the `400`s that come from this exception name the code.
- `:977`: the `404` also covers a delete that removed the key before the reset read the machine.
- `:1292`: `sform machine reset` prints the server's message and its error code.
- The error table: row `:1762` splits into `400` none (a scope that does not exist, a list without a runtime scope) and `400` `MachineAccessNotSupported` (a kind the project does not enable or the deployment does not configure, a shared certificate on another kind or in a project without `CertificateAndApiKey`, a scope the identity provider cannot carry, a reset of a `Federated` or `Certificate` machine, and the create, reset or delete of a kind the deployment does not configure).

**Differs from the CodeRabbit plan**, which adds the code to `access.md`. That page does not document the machine routes; their errors are in the table of `authentication.md`.

Historical specs and reviews under `docs/**/specs/` are not edited (CLAUDE.md).

### 8. Commits

1. This plan.
2. Phase 1: the error code, the mapping, the HTTP catches, the OpenAPI descriptions and their tests (sections 1, 2 and 6 for `Api.Functions` and the router).
3. Phase 2: the Certificate message, the reset of a record without a hash, the clients and their tests (sections 3, 4, 5 and 6 for `testint`).
4. The documentation (section 7).
5. The review.

### Out of scope

- Regenerating the OpenAPI snapshots, `Sdk.NSwag` and the TypeScript SDK.
- The `500` of a `ClientCredentials` reset whose identity provider gives no secret: Entra ID could answer `404` for a deleted application, but Keycloak cannot tell a missing client from a failure.
- Api.Web, which has no machine routes.
