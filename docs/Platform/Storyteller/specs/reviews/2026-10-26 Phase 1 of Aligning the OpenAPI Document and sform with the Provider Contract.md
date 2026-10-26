# Phase 1 of Aligning the OpenAPI Document and sform with the Provider Contract

Linear: [424-96](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security).

## Overview

Phase 1 covers sections 2-5 of [Align the OpenAPI Document and sform with the Provider Contract](../2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md). The OpenAPI document now says which machine credentials each operation accepts, `{mtls, apiKey}` included, from a list of the operations for users only that the source owns. The three configuration reads declare `If-None-Match` as an optional header parameter, and `ETag`, `Cache-Control` and (resolved read) `X-2S-Secrets-Withheld` on their `200` and `304`. The runtime did not change. The version, the snapshot and the SDKs are phase 2.

| Commit | Content |
|---|---|
| `aa9019f5` | Sections 2-5 and the "OpenAPI and Swagger UI" section of `authentication.md` |

| Project | Passed (end of the phase) |
|---|---|
| `Api.Functions/test` | 2265 (2253 before: 9 filter tests, 2 document tests, 1 header-name test) |

## What Was Done

### 2. The access class of an operation

- `Api.Functions/src/OpenApi/OperationAccess.cs`, internal: `UserOnly` (the 47 ids of spec 7.4, in the order of the test table) and `CertificateRenewal`, both `FrozenSet<string>` with ordinal comparison.
- The classes are as specified: an operation with `manual` that is not in `UserOnly` is for users and machines, the renewal is its own class, an operation without requirements is anonymous.
- The test tables of `OpenApiExportTests` stay the oracle.

### 3. Security requirements

- `MachineSecurityRequirementDocumentFilter` (`OpenApi/Filters/`), registered right after `OAuthSecuritySchemeDocumentFilter`, so before `ForbiddenResponseDocumentFilter`:
  - an operation for users and machines gets `{apiKey}`, `{mtls}` and `{mtls, apiKey}` when they are missing; a requirement counts as present when its reference ids equal the wanted set, so a second run changes nothing;
  - the renewal's requirements are set to exactly `[{mtls}, {mtls, apiKey}]`;
  - user-only, anonymous and other operations without `manual` are left alone;
  - every key is a `SecurityScheme` reference by id with no scopes, never a scheme without a reference (which would render as `{}`, no authentication).
- The inline `[OpenApiSecurity(Mtls, …)]` attribute of `PostRenewCertificate` was removed; a comment says why.
- `ApiKeySecuritySchemeDocumentFilter` and `MtlsSecuritySchemeDocumentFilter` set their scheme under the `Definitions.SecuritySchemas` constants (`apiKey` on `Authorization`, `mtls` on `X-ARR-ClientCert`, both of type `apiKey` in the header).
- `Definitions.Descriptions`: `SecureManual` rewritten (users, client-credentials tokens, the Storyteller token of a federated machine), new `SecureApiKey` and `SecureMtls`. **Beyond the spec:** both machine descriptions name the kinds they serve and the policy that must enable them, and `SecureMtls` names the `MtlsRequired` error.
- The rendered document (exported once to a temporary file): 84 operations with `[{manual}, {integrated}, {apiKey}, {mtls}, {mtls, apiKey}]`, 44 with `[{manual}, {integrated}]`, 3 with `[{manual}]`, the renewal with `[{mtls}, {mtls, apiKey}]`, 3 without `security`.

### 4. The conditional-read contract

- `Definitions.Parameters.IfNoneMatch = "If-None-Match"`, a literal const (`HeaderNames.IfNoneMatch` is not one), and `Definitions.Descriptions.IfNoneMatch`.
- The optional header parameter follows the four path parameters of `GetConfiguration`, `GetConfigurationResolved` and `GetConfigurationComposition`.
- `EntityTagResponseHeader` is gone. `ConfigurationReadResponseHeaders` (`ETag`, `Cache-Control`) serves the effective and composition reads, `ResolvedConfigurationReadResponseHeaders` (also `X-2S-Secrets-Withheld`) the resolved read, each on the `200` and the `304`. New descriptions `CacheControlConfigurationRead`, `CacheControlResolvedRead` and `SecretsWithheld`.

### 5. Tests and docs

- `OpenApiExportTests.Document_SecurityRequirements_MatchWhoMayCall`: exact requirement sets per class (with `{integrated}` when the render keeps it), no empty requirement object, the renewal in order, no `security` on the other anonymous operations, and the three schemes with their descriptions.
- `OpenApiExportTests.OperationAccess_MatchesTheTablesOfTheTests`: `UserOnly` equals the test table, and the one anonymous-table id with a requirement in the rendered document is `CertificateRenewal`. The completeness assertion is unchanged.
- `MachineSecurityRequirementDocumentFilterTests` (9, hand-built documents): the three requirements with and without `integrated`, idempotence, user-only operations untouched, operations without `manual` untouched, the renewal from `null` and from attribute-made requirements, and references by id.
- `ConfigurationConditionalReadTests`: the metadata theory became `Document_ConfigurationReads_DeclareTheConditionalReadContract(operationId, isResolved)`, with the parameter order and location, the header descriptions, `X-2S-Secrets-Withheld` on the resolved read only, and the old assertions; `IfNoneMatchParameter_IsTheHeaderName` pins the constant.
- `ForbiddenResponseDocumentFilterTests` and `OAuthFlowsTests` pass unchanged.
- `docs/Platform/Storyteller/authentication.md`, "OpenAPI and Swagger UI": a table of the four schemes, a table of requirements per class with their meaning, where `OperationAccess` and the filter live, and what Swagger UI needs (the `ApiKey ` prefix, a host that accepts `X-ARR-ClientCert`).
