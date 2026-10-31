# Resetting a Certificate machine answers 500 instead of 400

Linear: [424-102](https://linear.app/424fun/issue/424-102/resetting-a-certificate-machine-answers-500-instead-of-400).

## Overview

This review covers all of [Resetting a Certificate machine answers 500 instead of 400](../2026-10-31%20Resetting%20a%20Certificate%20machine%20answers%20500%20instead%20of%20400.md), delivered in one branch (`fix/424-102`). Every refusal of `MachineAccessNotSupportedException` now answers `400` with the stable code `MachineAccessNotSupported`, from the local catches and from the central mapping. The Certificate refusal uses the wording of the issue. A reset that finds a machine of a kind with an API key but without its hash answers `404`, not `500`. HTTP, mapping, OpenAPI and Cosmos tests cover all of it.

| Commit | Content |
| --- | --- |
| `dd1e492d` | The plan |
| `3c599381` | Phase 1: the code, the mapping, the HTTP catches, the OpenAPI descriptions and their tests |
| `708ee5fc` | Phase 2: the Certificate message, the reset without a hash, the admin UI mock, the MCP comment and the Cosmos tests |
| `810dbc75` | The documentation |
| This commit | The review |

## What Was Done

### 1. The error code and the exception

- `Backend.Core/src/ErrorCodes.cs`: `MachineAccessNotSupported`, after the federation codes, with a comment on its scope.
- `Backend.Core/src/Accessing/MachineAccessNotSupportedException.cs`: `public sealed class MachineAccessNotSupportedException(string message) : StorytellerException(message, ErrorCodes.MachineAccessNotSupported);`. All twelve throw sites are unchanged.
- The base type changed from `InvalidOperationException`. Before the change, a search covered catches, exception filters, switch arms, Polly predicates and test assertions. None relies on the old base on the path of this exception.

### 2. The HTTP answer

- `ErrorResponseMapping.TryMap` maps the exception to `400` with its code, after `InvalidInputException`.
- The catches in `AccessHttp.PostMachines`, `PutMachine` and `DeleteMachine` and in `CertificatesHttp.PostSharedCertificate` return `ErrorResponseMapping.ToActionResult(exception)`. The body now carries `ErrorCode`. The result is an `ObjectResult` with status `400`.
- OpenAPI:
  - The `400` of the reset, the delete and the shared certificate name `(MachineAccessNotSupported)`. The delete declares the `400` it already answered.
  - **Deviation:** the create description ends with `(ErrorCode MachineAccessNotSupported)`, not `(MachineAccessNotSupported)`, to match `(ErrorCode InvalidKey)` earlier in the same sentence.
  - **Addition:** the `404` of the reset also names a machine that has no API key (section 4).
  - The snapshots and the SDKs are not regenerated.

### 3. The Certificate message

`PolicyAwareMachineAccessService.CertificateResetNotSupportedMessage` is now "A Certificate machine has no key to reset: it renews its certificate itself over mTLS, and is revoked by deleting it." The comment above the routing switch says "key" instead of "secret". The switch itself is unchanged.

### 4. The last `500` of the reset

`CosmosAccessService.ResetMachineAccessAsync` handles a null key from the router like this:

- **The record had a hash when it was read:** `404` "The machine access {id} was deleted during its reset.", unchanged.
- **No hash, and the kind is `ApiKey` or `CertificateAndApiKey`:** `404` "The machine access {id} has no API key to reset; it is being deleted, or its creation failed."
- **Any other kind:** `InvalidOperationException` and `500`, unchanged.

**Deviation.** The plan reused "was deleted during its reset" for the record without a hash, on the premise that only a delete removes the hash of an API key. The review of the branch found two more callers that clear the hash:

- the compensation of a failed `CertificateAndApiKey` create (`PolicyAwareMachineAccessService.CreateCertificateAndApiKeyAsync`);
- the compensation of a failed upsert (`CosmosAccessService.CreateMachineAccessAsync`).

Neither deletes the document. So such a record can stay listed with no delete running. It now gets its own message, which names both causes. The status stays `404`: the record has no key that works, no retry gives it one, and the docs already promise `404` for a reset that meets a delete.

### 5. Clients

- `Mcp/src/Tools/MachineTools.cs`: the comment says the API refuses the reset with `400 MachineAccessNotSupported` and the tool refuses first with its own text. The check is unchanged.
- `ui.admin`:
  - `app/mock/federation.ts` has the new Certificate message and a `machineAccessNotSupported` constant.
  - `app/mock/access-handlers.ts` sends that code with the refusals of the reset (Federated, Certificate) and the create (kind not enabled, shared certificate on another kind).
  - **Addition:** it also sends the code with the shared certificate of a project without `CertificateAndApiKey`.
  - `test/unit/access/access.test.ts` asserts the new message and the code of these refusals. **Addition:** so does `test/unit/access/federation.test.ts`, for the Federated reset.

### 6. Tests

- `Api.Functions/test`:
  - `ErrorResponseMappingTests` and `ExceptionHandlingMiddlewareTests` each get a `MachineAccessNotSupportedException` row.
  - `MachineHttpTests` gets `RecordingAccess.Refusal`, which create, reset and delete throw after they record their call. A shared helper, `ShouldBeMachineAccessNotSupported`, asserts `400`, the code and no `Error`. New tests:
    - `PutMachine_OfACertificateMachine_IsBadRequestWithMachineAccessNotSupported`
    - `PostMachines_OfAKindTheProjectDoesNotEnable_IsBadRequestWithMachineAccessNotSupported`
    - `DeleteMachine_OfAKindTheDeploymentDoesNotConfigure_IsBadRequestWithMachineAccessNotSupported`
  - `CertificatesHttpTests`: `SharedCertificateBackend.EnabledCredentialKinds`, and `PostSharedCertificate_ProjectWithoutCertificateAndApiKey_IsBadRequestWithMachineAccessNotSupported`, which also checks that nothing is issued.
  - `OpenApiExportTests`: the pinned create text follows. `Document_MachineAccessRefusals_NameTheirErrorCodeInTheir400` checks the `400` of the create, reset, delete and shared certificate.
- `Access.Certificates/test`: the router's Certificate reset test also asserts `ErrorCode`.
- `Access.Certificates/testint`:
  - The Certificate reset test also asserts `ErrorCode`.
  - `Reset_HashClearedBeforeTheRead_AnswersNotFound_AndWritesNothing` runs for `ApiKey` and `CertificateAndApiKey`. It checks the new message, the unchanged ETag and that only the hash was gone. It was renamed from the plan's `Reset_OfAMachineWhoseDeleteRemovedItsKey_IsNotFound_AndWritesNothing` after the deviation in section 4.
  - `Reset_OfAClientCredentialsMachineWithoutASecret_IsNotAnsweredAsDeleted` uses a `SecretlessRouter` and checks the `500` and the unchanged ETag.

### 7. Documentation

`docs/Platform/Storyteller/authentication.md`:

- The shared certificate rule, the create refusals, the AuthKit and Entra ID scope refusals and `sform machine reset` name the code. Federation not configured names it too.
- The `MachineAccess` section has the new Certificate message and the coded refusals of both kinds. The reset paragraph has the `404` of a machine without an API key.
- The error table:
  - The old `400 none` row is split into `400 none` (an undefined scope, or no runtime scope) and `400 MachineAccessNotSupported`, which lists every refusal of the exception.
  - The `404 NotFound` row names the machine without an API key.
- **Deviation:** the sentence about federation not being configured is kept to creating a `Federated` machine. A delete without the federated service succeeds, and a reset refuses either way.

`access.md` is unchanged, as planned.

### 8. Commits

As planned, with the documentation in its own commit (see the table above).

## The review of the branch

A review with four dimensions ran over the whole diff: the HTTP contract, the reset logic, the tests, and the docs and cross-repository consistency. Each finding was checked by an agent that tried to refute it.

**Confirmed and fixed:**

- **A failed create also clears the hash (low).** The fallback answered "was deleted during its reset", and its comment said a delete removed the hash. A record left without its key by a failed create would have kept that message while it stayed listed. Fixed as described in section 4.

**Refuted:**

- **An old machine document would answer `404` although it still exists.** Documents written before mTLS never deserialize as a machine record with a kind and without a hash: the hash was a separate `akh.` document, and the old create could not store a valid machine document.
- **The new `400` of the delete would claim a refusal for every unconfigured kind.** The Api.Functions host always registers API keys, certificates and federation, and token exchange fails at startup without federation. Only a deployment without a `ClientCredentials` provider is refused, which is what the description says.
- **Some `400` mentions still lack the code.** These are the MCP tool description and similar texts. None of them is wrong, and the MCP server passes the code through to the assistant at runtime.

## Compared with the CodeRabbit coding plan

Adopted:

- the code and the `StorytellerException` base;
- the central mapping, with the catches kept and returning the mapped body;
- the Certificate wording;
- the HTTP test with an injectable failure;
- the mapping row and the code assertions in the router and Cosmos tests.

Different:

- **A record without a hash.** CodeRabbit throws `MachineAccessNotSupportedException` for every record without a hash. A `ClientCredentials` machine never has a hash, so a transient Keycloak failure would become `400` "not supported". A reset racing a delete would get `400` where the docs promise `404`. This change answers `404` for the kinds with an API key and keeps `500` for the rest.
- **Where the code is documented.** CodeRabbit documents it in `access.md`. That page does not cover the machine routes; their errors are in the table of `authentication.md`.
- **Additions.** The `400` of the delete in OpenAPI, the shared certificate HTTP test, the middleware row, the OpenAPI test, the admin UI mock and the MCP comment.

## Validation

| Suite | Result |
| --- | --- |
| `Api.Functions/test` | 2613 passed (2603 before, plus 10 new) |
| `Access.Certificates/test` | 114 passed |
| `Access.Certificates/testint`, `STORYTELLER_TESTS_USE_CONTAINER=true` | 73 passed (70 before, plus 3 new). The first run failed in the fixture: all tests failed within 1 ms, while the reused emulator container started. The run after it passed. |
| `Access.Federation`, `Access.AzureAd`, `Access.AuthKit`, `Access.Keycloak` tests | 472, 140, 180, 138 passed |
| `Mcp/test`, `Cli/test` | 727, 1062 passed |
| `ui.admin` `vitest run test/unit/access` | 106 passed (7 files) |

The builds of the changed projects add no warnings in the changed lines. Prettier passes on the two changed mock files. The two changed test files keep their earlier Prettier deviations in lines this change does not touch.

## Left for later

- Regenerate the OpenAPI snapshot, `Sdk.NSwag` and the TypeScript SDK (`chore(platform): regenerate the SDKs`).
- The compensations of a failed create clear the hash but leave the document: the partial one of `CosmosMergedApiKeyHashStore.StoreAsync`, or the full one after a failed upsert. They should delete it. Then a machine without its key could only be a delete in progress.
- The `500` of a `ClientCredentials` reset whose identity provider gives no secret. Entra ID could answer `404` for a deleted application. Keycloak cannot tell a missing client from a failure.
- `Cli/test/Commands/MachineListAndResetCommandTests.cs:127` says "ResetMachineAccess declares no 400". That has been stale since the reset declared its `400`.

## Follow-up from the PR review

- **Deleting a machine requests the revocation of its certificate.** The delete revokes the certificate best effort, so the Certificate refusal no longer says that deleting the machine revokes it (`065dd5a5`).
- **`MachineCredentialKind.HasApiKey()`** names the kinds with an API key once, with a test over every kind. The reset uses it, and the `404` of a machine without its API key ends with "Delete it and create a new one." (`b871b2ad`).
- **A configuration gap of the deployment answers `501`, not `400`.** The refusals that depend on the deployment and not on the request throw `FeatureNotConfiguredException` with the new code `MachineAccessNotConfigured`, which the central mapping answers with `501`:
  - no `ClientCredentials` or federated provider (`PolicyAwareMachineAccessService`);
  - no AuthKit machine organization, or no AuthKit permission for a requested scope (`AuthKitMachineAccessService`);
  - no Entra ID app role for a requested scope, or no service principal of the API application (`AzureAdMachineAccessService`).

  `MachineAccessNotSupported` (`400`) stays for what the request or the project decides: a kind the project does not enable, a shared certificate on another kind or in a project without `CertificateAndApiKey`, and the reset of a `Federated` or `Certificate` machine. The delete no longer throws `MachineAccessNotSupportedException`, so its catch is gone: its `400` is the invalid id alone. Create, reset and delete declare the `501` in OpenAPI. The only clients are in this repository: the MCP server replaces a `5xx` message with its generic one but keeps the code, `sform` exits with its crash code, and the Admin UI reports it as a server error.
