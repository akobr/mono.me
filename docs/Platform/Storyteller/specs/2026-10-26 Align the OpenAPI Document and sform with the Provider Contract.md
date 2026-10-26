# Align the OpenAPI Document and sform with the Provider Contract

Linear: [424-96 Align OpenAPI document and sform with the provider contract (security schemes, conditional reads, credentials)](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security)

## Problem

PR #127 (424-85, merged) gave the C# SDK conditional reads and `StorytellerCredential`, and gave the .NET configuration provider a set of `STORYTELLER_*` variables. It left the OpenAPI document and `sform` out of scope. Three gaps remain:

1. **The document under-describes machine access (study gap G10, capability C10).** The `apiKey` and `mtls` schemes are declared, but no operation references `apiKey`, and only the certificate renewal references `mtls`. A client generated in another language does not learn that an API key or a client certificate works. A machine that combines a certificate with an API key (`CertificateAndApiKey`) needs both at once, which the document cannot say today.
2. **The conditional-read contract is only half in the document.** The three conditional reads document `304` and `ETag`, but `If-None-Match` exists only in descriptions, and the `Cache-Control` and `X-2S-Secrets-Withheld` response headers are not documented at all.
3. **There are two sets of variables and two credential implementations.** The SDK and the provider read `STORYTELLER_ENDPOINT`, `STORYTELLER_KEY`, `STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_TOKEN_FILE` (with `STORYTELLER_CLIENT_ID`, a federated subject token), and accept the `sform` names as fallbacks. `sform` reads only its own names, gives `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` another meaning (an error), and has its own token client, exchange, discovery and URL rules. The docs claim that "one set of variables drives `sform` and the provider" (`configuration-provider.md:104`); that is true in one direction only. A job configured with the canonical names alone silently signs `sform` in as the stored user, or prompts.

The product owner answered the open questions of the issue: bump the OpenAPI version to **0.10**, breaking changes are fine because there are no external clients yet; publishing the NuGet and npm packages is a separate, later task.

### Acceptance criteria

| Criterion of the issue | Where |
|---|---|
| Operations reference `apiKey`, `mtls` and bearer schemes; conditional-read headers and 304 responses documented on configuration reads | Sections 2-5 |
| Regenerate the C# and TypeScript clients; consider replacing the hand-written conditional methods | Section 6. Considered: NSwag `/WrapResponses` (exposes the 200 headers, but a 304 still throws) and Orval `includeHttpResponseReturnType` or a per-operation mutator (the fetcher rejects a 304, and the return types of the vue-query hooks that ui.admin uses would change). The twins stay |
| `sform` reuses `StorytellerCredential` and reads the same `STORYTELLER_*` variables | Sections 7-11 |
| Optional: `sform story config --format flat` (C5) | Deferred to 424-104 (section 14) |

The inputs to this spec are:

- the issue and the product owner's answers in its comments;
- a read-only map of the code, tests and docs at `origin/main` `52f4bd9a` (six parallel readers: OpenAPI generation and security, conditional reads end to end, the C# SDK credentials and generation, `sform` machine sign-in, the TypeScript SDK pipeline and its consumers, the studies and the documentation impact);
- four adversarial reviews of the first draft against the code (OpenAPI, SDK, `sform`, scope). They ran NSwag 14.6.3 and Orval 8.41.0 against a modified snapshot in a scratch folder and read the source of the OpenAPI extension fork;
- the studies `study-usage.md` and `study-evaluation.md` attached to the issue (sections 2.4, 3.1, 3.3, 14.3, 18, 19 and recommendation 9.1);
- the CodeRabbit coding plan attached to the issue (`2026-10-25 Align OpenAPI document and sform with the provider contract.md`), used as a second opinion and a source of ideas, not as the decision. Section 15 lists where this spec differs from it.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The OpenAPI document

- **Engine.** `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` and `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core`, both a custom build `2.0.0-preview-prop-naming` (`Directory.Packages.props:51,55`), over Microsoft.OpenApi 1.2.3. The document is OpenAPI **3.0.1** (`OpenApiConfigurationOptions.cs:30`, `OpenApiExportTests.cs:486`), so there is no `mutualTLS` scheme type; `mtls` is modelled as an `apiKey` scheme on the `X-ARR-ClientCert` header (`Api.Functions/src/OpenApi/Filters/MtlsSecuritySchemeDocumentFilter.cs:9-14`).
- **Version.** `info.version` is `ThisAssembly.AssemblyFileVersion` (`OpenApiConfigurationOptions.cs:14`), from NBGV with `src/Platform/version.json` = `0.9-alpha` (hierarchical; `Cli` and `ui.admin` pin their own versions). The 0.8 to 0.9 bump (`15666baa9`) changed only that file. The snapshot is named by the first three parts: `open.api.v0.9.50.json` (`info.version` `0.9.50.40497`).
- **Schemes.** `Definitions.SecuritySchemas` (`Definitions.cs:576-582`): `manual` (http bearer, description `SecureManual` at `:499`, "Manually by token in Authorization HTTP header."), `integrated` (OAuth2, `OAuthFlows`), `apiKey`, `mtls`. All four are already in `components.securitySchemes`, so Swagger UI's Authorize dialog lists them.
- **Security per operation** (135 operations, `open.api.v0.9.50.json`):

  | Requirements | Count | Operations |
  |---|---|---|
  | `[{manual}, {integrated}]` | 128 | 84 machine-capable and 44 user-only |
  | `[{manual}]` | 3 | Get/Issue/RevokeSharedCertificate (user-only) |
  | `[{mtls}]` | 1 | `RenewMachineCertificate` (`V1/CertificatesHttp.cs:42`, with an inline scheme definition and its own description) |
  | none | 3 | `ExchangeMachineToken`, `GetCertificateAuthority`, `GetAuthConfiguration` |

  Every `[OpenApiSecurity]` attribute is its own requirement (OR). An AND, such as `mtls` with `apiKey`, cannot be written with attributes.
- **Filters** (`OpenApiConfigurationOptions.cs:34-48`, in order): Annotation, AccountRoleSchema, MachineCredentialKind, MachineScope, ViewTransferSchema, `ApiKeySecuritySchemeDocumentFilter`, `MtlsSecuritySchemeDocumentFilter`, `OAuthSecuritySchemeDocumentFilter`, DiffUnifiedResponse, NullableEnumDefault, DateOnlySchema, `ForbiddenResponseDocumentFilter`.
  - The `apiKey` and `mtls` filters add their scheme to `components` when it is missing (with string literals, not the constants). They never touch operations. The inline `mtls` definition on the renewal wins, so the filter's text never appears.
  - `OAuthSecuritySchemeDocumentFilter` removes a flowless `integrated` scheme (AuthKit without a domain) and its requirements.
  - `ForbiddenResponseDocumentFilter` adds a `403` to every operation that references `manual` or `integrated` and has none.
- **Who may call what is not in the source.** The runtime decides in the handlers: `EnsureUser` and `CheckUserAccessTo*Async` refuse every machine; `CheckAccessTo{Project,ProjectMetadata,Annotation,Annotations}Async` admit machines by policy and scopes (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:194-291`). The only lists are test tables in `Api.Functions/test/OpenApiExportTests.cs`: `UserOnlyOperations` (47 ids, spec 7.4), `AnonymousOperations` (4, including the renewal) and `MachineOperations()` (84, with their 403 texts and scopes). A test asserts that every operation of the document is in exactly one table (`:303-340`).
- **Machine credentials on the wire.** `MachineAuthenticationMiddleware` takes `Authorization: ApiKey 2s.<…>.<secret>` and a client certificate (`X-ARR-ClientCert`, injected by App Service from the TLS handshake; read from a client only when `IsCertificateHeaderFromClientAllowed`). The project's policy must enable the kind; `CertificateAndApiKey` needs both, and a shared certificate is accepted only with an API key. `BearerAuthenticationMiddleware` takes user tokens, client-credentials tokens of the identity provider, and the Storyteller tokens of federated machines. Secret reads may require mTLS (`403 MtlsRequired`).

### Conditional reads

- **Runtime.** `GetConfiguration` and `GetConfigurationComposition` send `ETag` and `Cache-Control: private, no-cache` on 200 and 304 (`V1/ConfigurationHttp.cs:127-128`, `V1/ConfigurationHttp.Composition.cs:58-59`). `GetResolvedConfiguration` sends `ETag`, `Cache-Control: no-store` when the caller may read secrets (otherwise `private, no-cache`) and `X-2S-Secrets-Withheld: mtls-required` when mTLS policy withholds the secrets (`SetResolvedHeaders`, `ConfigurationHttp.cs:712-727`; `SecretRights.WithheldHeader`). `If-None-Match` is parsed strictly, weakly compared, `*` matches (`ConditionalReads/ConditionalRequestExtensions.cs:22-57`).
- **Metadata.** `OpenApi/EntityTagResponseHeader.cs` documents `ETag` on the 200 and 304 of the three reads. `If-None-Match` appears only in the descriptions (`Definitions.cs:510-513`); no operation of the document has a header parameter. `Api.Functions/test/ConfigurationConditionalReadTests.cs:746-765` checks `ETag` on 200 and 304, no content on 304, and the description.
- **Earlier decisions.** Spec 2026-10-17 §7 kept `If-None-Match` out of the parameters because NSwag would add a positional parameter and the `sform` callers would change. The provider spec (2026-10-23 §2, §4) added hand-written twins instead and left "changes to the OpenAPI document, regeneration of the SDKs, and changes to `sform`" out of scope (`:903-917`).

### The SDKs

- **C#.** `Sdk.NSwag/src/ApiSdk.g.cs` is generated by NSwag 14.6.3 with the command in `Sdk.NSwag/README.md:19-21` (no `nswag.json`; the tool install is unpinned). Regenerating from `open.api.v0.9.50.json` reproduces the committed file. A `304` throws `ApiException` in the generated reads (`ApiSdk.g.cs:10480, 10804, 11479`). `ApiSdk.Conditional.cs` adds `GetConfigurationConditionalAsync` and `GetResolvedConfigurationConditionalAsync`: raw HTTP over the generated private members, returning `ConditionalResponse` (`ETag`, `CacheControl`, `IsNoStore`, `SecretsWithheld`). Composition has no twin. CI checks nothing on the C# side.
- **TypeScript.** `sdk.typescript/src/scripts/generate.mjs` picks the newest `../open.api.v*.json` numerically (0.10 sorts above 0.9) and sets `package.json` `version`. `orval.config.ts` does not set `output.headers`, so header parameters do not change the fetch or vue-query signatures; the Zod output gains header schemas. `lib/conditional.ts` holds the conditional helpers; `lib/fetcher.ts` throws on any non-ok status, 304 included, and sends only `Bearer`. `.github/workflows/ui-admin.yml:58-62` regenerates and fails on a diff of `generated/`. `sdk.typescript/README.md:99` says to remove superseded snapshots there (`../Sdk.NSwag` keeps the history), but nine are still tracked; all of them are also in `Sdk.NSwag/`.
- **Credentials** (`Sdk.NSwag/src/Credentials/`). Public: `StorytellerCredential` (`FromApiKey`, `FromCertificate`, `FromClientCredentials`, `FromTokenFile`, `FromFederatedTokenFile`, `FromEnvironment`, `Bind`, `GetAuthorizationAsync`), `StorytellerCredentialBinding` (endpoint, organization, project, `ConfigureHandler`, `TimeProvider`, `Resilience`), `StorytellerCredentialHandler`, `StorytellerClientCertificate`, the options records and `StorytellerAuthenticationException` (`Configuration`, `Rejected`, `Unavailable`). Internal: the kinds, `EnvironmentCredential`, `EnvironmentVariables` (trim, blank as unset, alias conflict), `StorytellerVariables`, `LocalUrlRule`, `TokenEndpointClient` (own `HttpClient`, no redirect, no `Authorization`, resilience with POST retries), `UnboundTarget`, and the test seam `WithPrimaryHandler` (`InternalsVisibleTo` only for the SDK's tests). `FederatedTokenOptions.SubjectTokenPath` is a file; there is no GitHub Actions source (`Sdk.NSwag/README.md:104`). `EnvironmentCredential` treats `STORYTELLER_SUBJECT_TOKEN_FILE` as a federation marker, never as a bearer token file (`EnvironmentCredential.cs:36-38`; `FromEnvironmentTests.cs:175-181, 215`).
- **The provider** (`Configuration/src/Settings/`) reads `STORYTELLER_ENDPOINT` (fallback `STORYTELLER_API_URL`), `STORYTELLER_KEY` (`FullKey.Parse`), `STORYTELLER_MODE`, `STORYTELLER_REFRESH_INTERVAL`, `STORYTELLER_OPTIONAL`, `STORYTELLER_CACHE_DIR`, and delegates credentials to `StorytellerCredential.FromEnvironment` (`SettingsReader.cs:223-244`); code wins over the environment. It keeps its own copies of the reading and URL rules (`VariableReader.cs`, `LocalUrl.cs`) because the SDK's are internal. `Caching/CredentialFingerprint.cs:32,58` hashes the credential's `ToString()` and type name into the cache identity. A transient (`Unavailable`) credential failure falls back to the last-known-good cache; a `Rejected` one fails (`StorytellerConfigurationEngine.cs:686-700`).

### sform

- **Variables** (`src/Platform/Cli/src/Authentication/StorytellerVariables.cs`): `STORYTELLER_API_URL` (required for every machine kind), `STORYTELLER_PROJECT`, `STORYTELLER_VIEW`, `STORYTELLER_API_KEY`, `STORYTELLER_CERTIFICATE_FILE`, `STORYTELLER_CERTIFICATE_PASSWORD`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE`, `STORYTELLER_SUBJECT_TOKEN_FILE`, `STORYTELLER_SUBJECT_TOKEN_TYPE`, `STORYTELLER_TOKEN_FILE`; plus `ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN` (`GitHubActionsVariables.cs`), which never make a machine on their own (`MachineSignInSettings.cs:47-52`).
- **Parsing** (`MachineSignInSettings.cs`): trim, blank as unset, a literal `ApiKey ` prefix, five kind groups with a mixed-kinds error, the GUID rule for implicit GitHub federation (`:181`), required pairs, a certificate password needs the file, the subject token type, and its own URL rule (`IsSecureOrLocal`, no user-info check). `sform` reads none of `STORYTELLER_ENDPOINT`, `STORYTELLER_KEY`, `STORYTELLER_CLIENT_CERTIFICATE*`; `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` is a mixed-kinds error.
- **Tokens** (`MachineAuthenticationService.cs`, 672 lines): its own client-credentials POST, AuthKit token-URL discovery through the CLI resolver (`authentication.provider`, the `auth.discovery.json` cache), RFC 8693 exchange at `{STORYTELLER_API_URL raw}/v1/{org}/{project}/access/token`, the GitHub ID-token request (audience = that URL, byte for byte), a hint naming the audience when a GitHub-sourced exchange is refused (`:489-494`), a per-process token cache (60 s margin, expiry from `expires_in`), a cached load failure re-thrown as a new exception per call (`:251-265`), and certificate/API-key identity cross-checks. `MachineTokenRenewalHandler` re-sends once after a `401` for federated machines only. Exit codes: Configuration `2`, ServiceError `3`, else `44`.
- **Outputs.** `sform account machine create` prints the `sform` names (`Commands/MachineAccess/MachineCreateCommand.cs:277-368`); the admin UI one-time-secret dialog writes `STORYTELLER_API_URL` and `STORYTELLER_API_KEY` (`src/Platform/ui.admin/src/app/components/access/OneTimeSecretDialog.vue:73`; no test covers the snippet).
- **Wiring.** `Cli.csproj:47` already references `Sdk.NSwag` (and `Abstractions.Annotations`, `:49`), but nothing in `Cli/src` uses `Sdk.Credentials`. `Startup.cs:87-146` wires `MachineSignInSettings` (`ApiUrl` replaces `general.baseUrl`), a named `HttpClient` without redirects for the token requests, an `AccessTokenFactory` that turns non-authentication exceptions into an empty token, the development `X-ARR-ClientCert` header (`IsLocalHttp`), and `MachineTokenRenewalHandler`.
- **Configuration read.** `sform story config <key> [-r] [-e file] [--version n]` (`Commands/Configuration/ConfigGetCommand.cs`) prints the whole `Configuration` envelope. `docs/42for.net/cli/sform.md:102` calls it `config get`, which does not exist.

## Proposed Changes

### 1. Version 0.10

- `src/Platform/version.json`: `0.9-alpha` → `0.10-alpha`, as the 0.9 bump did. Every Platform project without its own `version.json` moves with it (Storyteller with `Sdk.NSwag` and `Configuration`, Scheduler, Supervisor, Aspire); `Cli` and `ui.admin` keep theirs. The API, the document and the SDK generated from it share the version, so a narrower `version.json` would split them. No test or doc pins the version beyond a pattern.
- The exported snapshot is `open.api.v0.10.<n>.json`, with `<n>` whatever NBGV computes (probably `0`).
- The break of section 4 (a new positional parameter in three generated C# methods) is acceptable under 0.x. No commit subject uses `!`, and no commit message or PR body contains the words `BREAKING CHANGE`: `mrepo` flags either anywhere in a message and would propose 1.0 (`ConventionalParser.cs:46`). The changes for clients are listed in the reviews and the PR instead. After the merge, `mrepo update version` would suggest 0.11 for the `feat` commits after the bump; 0.10 is the version the product owner chose, so that suggestion is ignored.

### 2. The access class of an operation, owned by the source

New `Api.Functions/src/OpenApi/OperationAccess.cs`:

```csharp
// The callers an operation admits, for its security requirements in the document; the handlers enforce them.
internal static class OperationAccess
{
    // Spec 7.4: these refuse every machine, whatever its credential carries (EnsureUser, CheckUserAccessTo*Async).
    public static readonly FrozenSet<string> UserOnly = FrozenSet.ToFrozenSet(
    [
        Definitions.RouteIds.Access.GetAccount,
        // … the 47 ids of OpenApiExportTests.UserOnlyOperations
    ], StringComparer.Ordinal);

    // A machine renews its certificate with the certificate itself, and with its API key when its kind needs both.
    public static readonly FrozenSet<string> CertificateRenewal = FrozenSet.ToFrozenSet([Definitions.RouteIds.Access.RenewMachineCertificate], StringComparer.Ordinal);
}
```

- **Classes:** an operation that references `manual` and is not in `UserOnly` is **machine-capable**; one in `UserOnly` is **user-only**; `CertificateRenewal` is its own class; an operation without any requirement (and not the renewal) is **anonymous**, with no list of its own.
- The test tables stay in the test as the independent oracle (spec 7.4/7.5). The rendered document is checked against them (section 5), so the source list and the tests are two entries of one fact. `Api.Functions.csproj` already grants `InternalsVisibleTo` to the test assembly.

### 3. Security requirements on the operations

New `Api.Functions/src/OpenApi/Filters/MachineSecurityRequirementDocumentFilter.cs`, registered after `OAuthSecuritySchemeDocumentFilter` and before `ForbiddenResponseDocumentFilter`:

| Class | Requirements after the filter (OR between objects, AND inside one) |
|---|---|
| Machine-capable | `{manual}`, `{integrated}` (when the scheme survives), `{apiKey}`, `{mtls}`, `{mtls, apiKey}` |
| User-only | unchanged: `{manual}`, `{integrated}` (or `{manual}` alone for the shared-certificate operations) |
| Certificate renewal | exactly `{mtls}`, `{mtls, apiKey}`, written by the filter whatever the attributes say |
| Anonymous | none |

- Requirements reference the component schemes by id (`Reference { Type = SecurityScheme, Id = Definitions.SecuritySchemas.* }`). A key without a reference would serialize as `{}`, which OpenAPI reads as "no authentication"; the filter never writes one. It compares requirements by their reference ids, so a second run changes nothing.
- The `[OpenApiSecurity(Mtls, …)]` attribute on `PostRenewCertificate` is removed: without it the extension renders no requirement (verified against the fork's `DocumentHelper`), and the filter writes both. Trimming the attribute instead is not safe: an `ApiKey` attribute without `Name` aborts the whole render ("Scheme name MUST be provided").
- **Meaning.** `{mtls}` alone is the `Certificate` kind with the machine's own certificate. `{mtls, apiKey}` is `CertificateAndApiKey`, and the only form a shared certificate is accepted in. `{apiKey}` alone is the `ApiKey` kind. `{manual}` covers users, client-credentials tokens and the Storyteller tokens of federated machines; `{integrated}` is the OAuth2 sign-in of a user or a client-credentials client. Whether a kind is accepted also depends on the project's machine authentication policy, and secret reads may need mTLS (`403 MtlsRequired`, already in the 403 descriptions); the scheme descriptions say so, the requirements cannot.
- **Scheme definitions** come from the filters only. `ApiKeySecuritySchemeDocumentFilter` and `MtlsSecuritySchemeDocumentFilter` use the `Definitions.SecuritySchemas` constants and **set** their scheme (replace, not add-if-missing). New descriptions in `Definitions.Descriptions`:
  - `SecureManual`: a bearer token in `Authorization`: a user's token, a client-credentials token of the identity provider, or the Storyteller token a federated machine gets from `ExchangeMachineToken`.
  - `SecureApiKey`: `Authorization: ApiKey 2s.<base64url(org:project:machineAccessId)>.<secret>`, the scheme included in the value; the `ApiKey` and `CertificateAndApiKey` kinds, when the project's policy enables them.
  - `SecureMtls`: the client certificate of the TLS handshake. The platform's front end passes it in `X-ARR-ClientCert`; a client never sets the header itself, except against a local development host that allows it. A shared certificate needs `apiKey` as well; project policy can require a certificate for secret reads.
- **Swagger UI** already lists the four schemes; the operations now show which ones they accept. An authorized `apiKey` value must include the `ApiKey ` prefix, and `mtls` works only against a host that accepts `X-ARR-ClientCert` from clients.
- `ForbiddenResponseDocumentFilter` and `OAuthSecuritySchemeDocumentFilter` need no change: the first still finds `manual`/`integrated` on every operation it covers, the second runs before the new filter and only strips `integrated`.

### 4. The conditional-read contract in the document

- **`If-None-Match`** becomes a formal, optional header parameter of `GetConfiguration`, `GetResolvedConfiguration` and `GetConfigurationComposition`: `[OpenApiParameter(Definitions.Parameters.IfNoneMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfNoneMatch)]`, placed **after the four path parameters**.
  - `Definitions.Parameters.IfNoneMatch = "If-None-Match"` is a literal `const` (`HeaderNames.IfNoneMatch` is `static readonly` and cannot be an attribute argument); a test asserts that it equals `HeaderNames.IfNoneMatch`.
  - The description: one or more entity tags from earlier reads, or `*`; weak comparison; a header that is not a valid list is ignored and the read answers 200. The operation descriptions keep their text.
  - This reverses spec 2026-10-17 §7. The product owner accepted the break with the 0.10 bump.
  - Verified with NSwag 14.6.3: the three generated methods gain `string if_None_Match` after `key` in both overloads (interface and class) and send it only when it is not null; a 304 still throws `ApiException`. Verified with Orval 8.41.0: only the Zod output changes (new `Get*Header` schemas); the client, vue-query, model and MSW outputs do not.
- **Response headers.** `EntityTagResponseHeader` is replaced by two `IOpenApiCustomResponseHeader` classes in `Api.Functions/src/OpenApi/`:
  - `ConfigurationReadResponseHeaders`: `ETag` and `Cache-Control` (`private, no-cache`), for the effective and the composition reads;
  - `ResolvedConfigurationReadResponseHeaders`: `ETag`, `Cache-Control` (`no-store` when the caller may read secrets, otherwise `private, no-cache`) and `X-2S-Secrets-Withheld` (`SecretRights.WithheldHeader`; value `mtls-required`, present only when mTLS policy withholds the secrets, which then stay as their `@` expressions).

  Both are attached to the 200 and the 304 of their operations. Header names come from `HeaderNames` and `SecretRights` (dictionary keys, not attribute arguments). The runtime does not change. Neither generator changes code for response headers.

### 5. Tests of the document

`Api.Functions/test/`:

- `OpenApiExportTests`, a new `Document_SecurityRequirements_MatchWhoMayCall` over the rendered document (`RenderAsync`):
  - every operation of `MachineOperations()` has **exactly** the requirement sets `{manual}`, `{apiKey}`, `{mtls}`, `{mtls, apiKey}`, plus `{integrated}` when the render kept that scheme;
  - every operation of `UserOnlyOperations` references neither `apiKey` nor `mtls`;
  - `RenewMachineCertificate` has exactly `[{mtls}, {mtls, apiKey}]`; the other three ids of `AnonymousOperations` have no `security`;
  - no operation has an empty requirement object;
  - `components.securitySchemes` holds `manual`, `apiKey` and `mtls` with the new descriptions.
- `OpenApiExportTests`: `OperationAccess.UserOnly` equals `UserOnlyOperations`, and `OperationAccess.CertificateRenewal` is the one id of `AnonymousOperations` with a requirement, so a route added to one list and not the other fails here first. The completeness assertion at `:313-314` stays as it is.
- A filter unit test on a hand-built document (pattern of `ForbiddenResponseDocumentFilterTests`): idempotence, no change for an operation without `manual`, the renewal rule, and references by id.
- `ConfigurationConditionalReadTests`: the rendered-metadata test also checks the `If-None-Match` header parameter (optional, `string`, after the path parameters) on the three reads, `Cache-Control` on 200 and 304 of all three, `X-2S-Secrets-Withheld` on 200 and 304 of the resolved read only, and still `ETag` and no 304 content. A test pins `Definitions.Parameters.IfNoneMatch == HeaderNames.IfNoneMatch`.
- `ForbiddenResponseDocumentFilterTests` and `OAuthFlowsTests` keep passing unchanged.
- `docs/Platform/Storyteller/authentication.md` "OpenAPI and Swagger UI" (`:132-144`) describes the four schemes and what each requirement means (phase 1).

### 6. Snapshot and regeneration

- **Export.** Build `Api.Functions`, then `STORYTELLER_OPENAPI_EXPORT=<path> dotnet test Api.Functions/test --filter "FullyQualifiedName~OpenApiExportTests"`, with no `Auth__*` variables set, as the 0.9.50 export was, so the `integrated` scheme renders as before and the diff shows only this change. The file goes to `Sdk.NSwag/` and `sdk.typescript/` as `open.api.v0.10.<n>.json`. `Sdk.NSwag/` keeps its history. `sdk.typescript/` follows its README and keeps only the newest snapshot: the nine superseded ones go (all of them are in `Sdk.NSwag/`).
- **C#.** NSwag **14.6.3**, now pinned in the README (`dotnet tool install --global NSwag.ConsoleCore --version 14.6.3`), with the README command and the new input. Then:
  - the callers of the generated reads pass `null` for `if_None_Match`: `Cli/src/Commands/Configuration/ConfigGetCommand.cs:70,75`, `ConfigSetCommand.cs:106`, `ConfigCompositionCommand.cs:41`; `Cli/test/Commands/ConfigCompositionCommandTests.cs:40` (Verify) and `:104, :118, :130, :331` (Setups of a strict mock: each needs the fifth argument), `Cli/test/Output/CompositionLinesTests.cs:154`, `Cli/test/Sdk/ConfigurationsApiClientDateTests.cs:54, 79`, `Sdk.NSwag/test/ConditionalReadsTests.cs:146-147, 164-165`. The provider (`DocumentReader.cs`, `TransportFactoryTests.cs`) uses only the twins and does not change;
  - the diff of `ApiSdk.g.cs` is reviewed: only the three reads gain the parameter, nothing else changes signature; the members `ApiSdk.Conditional.cs` uses still exist, and its copied exception messages still equal the generated ones;
  - the hand-written twins stay; `Sdk.NSwag/README.md` says why next to the conditional-reads section (a generated 304 is an exception, the generated reads drop the response headers, and `/WrapResponses` would not change the first).
- **TypeScript.** `npm run generate` in `sdk.typescript/src`, then `npm run build`, `lint`, `typecheck` and `test`. The generated files change their version header and the Zod output gains the header schemas; `lib/conditional.ts` and `lib/fetcher.ts` do not change. ui.admin is checked against the rebuilt SDK (`npm run lint`, `npm test`, `npm run build`); it does not change in this phase.
- **Docs.** `Sdk.NSwag/README.md` (input file, pinned tool, the twins), `sdk.typescript/README.md` (`:80` example file name, `:99` stays true, the conditional-reads section: the parameter is in the document, the client ignores it), `docs/Platform/Storyteller/export-import.md:587` (stale `open.api.v0.9.48.json`), `docs/Platform/Storyteller/inheritance.md:376` (the generated reads now take `If-None-Match` and still throw on 304).

### 7. One canonical set of variables

The contract of the study (§3.1), of the provider and of the SDK is canonical. The old `sform` names stay readable as **deprecated aliases**, read only when the canonical name is unset; both set to different values is an error (the SDK rule today).

| Canonical | Deprecated | Meaning |
|---|---|---|
| `STORYTELLER_ENDPOINT` | `STORYTELLER_API_URL` | The API base, `/api` included |
| `STORYTELLER_KEY` | — | Full key `@{organization}.{project}.{view}.{annotation key}` of the annotation a provider reads; for `sform` a machine target (section 10) |
| `STORYTELLER_API_KEY` | — | Structured API key, an optional `ApiKey` scheme in front |
| `STORYTELLER_CLIENT_CERTIFICATE` | `STORYTELLER_CERTIFICATE_FILE` | PKCS#12 file of the machine certificate |
| `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` | `STORYTELLER_CERTIFICATE_PASSWORD` | Its password |
| `STORYTELLER_CLIENT_ID` | — | Client id of client credentials, or the federated machine id |
| `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE` | — | Client credentials (the scope also for federation) |
| `STORYTELLER_TOKEN_FILE` | `STORYTELLER_SUBJECT_TOKEN_FILE`, federation only (below) | With `STORYTELLER_CLIENT_ID` and no client-credentials variable: the platform token a federated machine exchanges. Alone: a bearer token the API accepts |
| `STORYTELLER_SUBJECT_TOKEN_TYPE` | — | `jwt` (default), `id_token`, `access_token` or the URN |
| `ACTIONS_ID_TOKEN_REQUEST_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN` | — | Set by GitHub Actions in a job with `id-token: write`; the platform token source of a federated machine without a token file (section 9) |

- `STORYTELLER_SUBJECT_TOKEN_FILE` is **not a plain alias**: set, it always makes the credential federated (never a bearer token file) and needs `STORYTELLER_CLIENT_ID`, as today in both implementations. Its conflict check against `STORYTELLER_TOKEN_FILE` runs in the federation branch only, and `STORYTELLER_TOKEN_FILE` with it and without a client id stays a mixed-kinds error.
- The GitHub variables are **not credential variables**: alone they never configure a machine, in `sform` (a job with `id-token: write` may run `sform` as a user) or in the provider (anonymous). They only supply the platform token of a credential that `STORYTELLER_CLIENT_ID` already makes federated.
- `sform` keeps two variables of its own, which are not credentials: `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` (command target, also for users).

### 8. The shared credential contract in the SDK

All in `Sdk.NSwag/src/Credentials/`. The new public surface is what `sform` needs, and no secret is readable through it.

- **`StorytellerVariables` becomes public**: the canonical names, the deprecated names, the GitHub Actions names, and two lists: `Credentials` (every name that makes a credential, deprecated ones included; not `ENDPOINT`, `API_URL`, `KEY` or the GitHub names) and `Deprecated` (deprecated name → canonical name, `SUBJECT_TOKEN_FILE` → `TOKEN_FILE` among them, with the note above).
- **`EnvironmentVariables` becomes the public `StorytellerVariableReader`**: trimmed values, empty as unset, `ReadWithFallback` with the conflict error, `SetNames`. Messages name variables, never values.
- **`LocalUrlRule` becomes the public `StorytellerUrlRule`**: `IsLocalHttp(Uri)`, `IsAllowed(Uri)`, `Require(string value, string source)` and `RequireEndpoint(string value, string source)` (https anywhere, http only to `localhost` or a loopback literal, no user info; an endpoint also no query or fragment). The provider's copies (`VariableReader.cs`, `LocalUrl.cs`) are replaced by the public types, with no change of behaviour.
- **New public enums** `StorytellerCredentialKind` (`ApiKey`, `Certificate`, `CertificateAndApiKey`, `ClientCredentials`, `TokenFile`, `Federated`) and `StorytellerSubjectTokenSource` (`File`, `GitHubActions`).
- **New public `StorytellerCredentialVariables`**, the reading of the credential variables that `FromEnvironment` and `sform` share. A sealed class; `ToString()` prints the kind and the names that are set, never a value.

  ```csharp
  public sealed class StorytellerCredentialVariables
  {
      // null when no credential variable is set (the GitHub variables do not count); StorytellerAuthenticationException
      // (Configuration) when the variables do not describe one credential. ENDPOINT, API_URL and KEY are not read here.
      public static StorytellerCredentialVariables? Read(Func<string, string?>? getVariable = null);

      public StorytellerCredentialKind Kind { get; }
      public StorytellerSubjectTokenSource? SubjectTokenSource { get; }  // Federated only
      public IReadOnlyList<string> Names { get; }            // the credential variables set, as named
      public IReadOnlyList<string> DeprecatedNames { get; }  // the deprecated names among them
      public string? ApiKey { get; }                         // without the ApiKey scheme; for the machine identity
      public string? ClientId { get; }
      public string? TokenFile { get; }                      // the bearer token file of the TokenFile kind

      // Unbound, as FromEnvironment returns it; loads the certificate. The credential keeps the getVariable of Read for
      // its unbound fallbacks (STORYTELLER_ENDPOINT, STORYTELLER_KEY).
      public StorytellerCredential CreateCredential(IFileSystem? fileSystem = null, TimeProvider? timeProvider = null);
  }
  ```

  The secrets, the certificate file and password, the token URL and scope, the subject token file and the GitHub request URL and token are internal fields, read once by `Read` (the GitHub URL checked there with `StorytellerUrlRule.Require`). `StorytellerCredential.FromEnvironment(getVariable, fileSystem, timeProvider)` becomes `Read(getVariable)?.CreateCredential(fileSystem, timeProvider)`, with the same signature; `EnvironmentCredential` becomes the internal half that builds credentials.
- **The rules**, one place for both. Where the two implementations differ today, this column decides:

  | Case | Decision |
  |---|---|
  | Reading | trimmed; empty counts as unset; a deprecated name is read when the canonical one is unset; both set to different values is an error |
  | `ApiKey` scheme | stripped when followed by whitespace (any case), as the SDK does; `ApiKey` alone is an error ("holds no key"). `sform` used to send it as the key |
  | Kinds | five groups as today (key or certificate, client credentials, federation, bearer token file, client id or scope without either); more than one is an error that names the variables set |
  | `TOKEN_FILE` with `CLIENT_ID`, no client-credentials variable | federation (study §3.1, SDK). `sform` used to stop with exit code `2` |
  | `TOKEN_FILE` with `SUBJECT_TOKEN_TYPE` and no `CLIENT_ID` | mixed kinds, as today |
  | `CLIENT_ID`, optionally with `TOKEN_SCOPE`, and no other credential variable | federation through GitHub Actions when the id is a GUID and both GitHub variables are set (the `sform` GUID rule, `authentication.md:248`); otherwise an error naming both ways (a client secret, or a token file or a GitHub Actions job) |
  | `SUBJECT_TOKEN_TYPE` without a file | needs `CLIENT_ID` (any id); then the GitHub source when both GitHub variables are set; otherwise an error naming `TOKEN_FILE` and GitHub Actions |
  | `TOKEN_SCOPE` without `CLIENT_ID` | an error, the SDK's message |
  | Client credentials | both `CLIENT_ID` and `CLIENT_SECRET`; `TOKEN_URL` by `StorytellerUrlRule`; without it only a `client_` id, which discovers the AuthKit token URL |
  | Certificate | a password without a file is an error; the file loads in `CreateCredential` |
  | Subject token type | short form or URN, any case; jwt by default |
  | GitHub request URL | `StorytellerUrlRule.Require` (it has a query), checked when it is the source |

  Messages name the variables as they were set (a deprecated name by its own name), never a value. The existing `FromEnvironmentTests` keep passing, except the ones that pin an error this table turns into a credential (the GitHub cases), which change.
- **Fingerprints.** The type names and `ToString()` of the existing credential kinds do not change, so the provider's last-known-good cache entries (`CredentialFingerprint`) keep matching. A GitHub-sourced credential is a `FederatedTokenCredential` whose text is `StorytellerCredential { Kind = Federated, ClientId = {id}, SubjectTokenSource = GitHubActions }` (the client id keeps two machines apart). `CredentialFingerprintTests` pins both federated texts.
- **The provider** uses the public constants for `ENDPOINT`, `API_URL` and `KEY`, and the public reader and URL rule. Through `FromEnvironment` it gains one behaviour: in a GitHub Actions job with `id-token: write`, `STORYTELLER_CLIENT_ID` alone (a GUID) or with `STORYTELLER_SUBJECT_TOKEN_TYPE` now federates through the runner instead of failing. New `SettingsReaderTests` cases: a GUID with the GitHub variables gives a federated credential; a non-GUID gives the error; the GitHub variables alone give anonymous requests.

### 9. GitHub Actions as a platform token source

- `FederatedTokenCredential` gets its platform token from a source: the file (today) or the GitHub Actions runner, internal and reached through `FromEnvironment` and `StorytellerCredentialVariables.CreateCredential`. `FederatedTokenOptions` and the public factories do not change; a public `FromGitHubActions` is left to 424-104, whose GitHub Action may need it.
- **The request.** A new `TokenEndpointClient.RequestGitHubIdTokenAsync(requestUrl, requestToken, audience)`: `GET {ACTIONS_ID_TOKEN_REQUEST_URL}&audience={audience, escaped}` (`?` when the URL has no query), `Authorization: Bearer {ACTIONS_ID_TOKEN_REQUEST_TOKEN}` on that request only, no redirect, the same resilience pipeline. It reads `value` from the JSON answer and asks again before every exchange. The class comment and the README say that only this request carries an `Authorization` header.
- **The audience** is the token endpoint built from the endpoint **as written** (`Uri.OriginalString`, a trailing `/` removed) plus `/v1/{organization}/{project}/access/token` with escaped segments, so it stays byte for byte what `sform` requests today and what `machine create` prints (`authentication.md:249`). The endpoint `Uri` keeps its original text through `StorytellerUrlRule` and `UnboundTarget`. The exchange itself still posts to the normalised URI.
- **Failures:** a network error, a timeout, 408, 429 and 5xx are `Unavailable` (retried first); a redirect, another 4xx, or an answer without a token is `Rejected`. Messages name the runner URL without its query, never the request token.
- **The hint.** When the platform token came from the runner and the exchange is `Rejected`, the message ends with "; the GitHub Actions token of this job was requested for the audience {audience}, which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead." The audience is not a secret.

### 10. sform on the shared contract

- **Settings.** `MachineSignInSettings` keeps its role and is built from the shared contract:
  - `IsConfigured` and `CredentialVariables` come from `StorytellerVariables.Credentials` and the reader, so they exist even when the variables are invalid; they never throw.
  - `StorytellerCredentialVariables.Read` runs once; its error is kept, and `GetKind()` throws a new `AuthenticationException(Configuration, message, inner)` on every call, as today a load failure does.
  - `Endpoint` (was `ApiUrl`) is `STORYTELLER_ENDPOINT` or the deprecated `STORYTELLER_API_URL` through the reader, with the conflict rule; required for every machine kind and checked with `StorytellerUrlRule.RequireEndpoint`, naming the variable as set.
  - `MachineSignInKind` stays and maps one to one from `StorytellerCredentialKind`.
- **The target**, first that applies:
  1. `-p`/`-v`;
  2. `STORYTELLER_PROJECT`/`STORYTELLER_VIEW`;
  3. **for a machine only, `STORYTELLER_KEY`**, parsed with `FullKey.TryParse` (as the provider parses it). Its organization and project apply when neither `-p` nor `STORYTELLER_PROJECT` is set; its view applies only while the target project is the key's project, otherwise the view is `STORYTELLER_VIEW` or `default` (as with the view of `access.default.json`). An invalid key is `AuthenticationException(Configuration)`, "STORYTELLER_KEY must be a full key, @{organization}.{project}.{view}.{annotation key}" (exit `2`). `STORYTELLER_PROJECT` wins over a key of another project;
  4. the project of the API key or the certificate.

  A key of another project than the credential's is the same error as such a `STORYTELLER_PROJECT`. The "no project is set" messages (`CommandContext.cs:107`, `AccountCommand.cs:189`, `MachineAuthenticationService.cs:415`) name `STORYTELLER_KEY` too, and a federated `sform account` takes the key's project when `STORYTELLER_PROJECT` is unset. A user never reads `STORYTELLER_KEY`.
- **Credentials.** `MachineAuthenticationService` builds the SDK credential with `CreateCredential` and binds it with `StorytellerCredentialBinding` (the endpoint; for a federated machine the organization and project of `BindTarget`, bound again when the target changes). Then:
  - `GetAccessTokenAsync` answers from `GetAuthorizationAsync(null)`: `ApiKey <key>` for the API key kinds as today, no value for a certificate, the bearer token otherwise. A federated machine without a bound target returns null and never calls the SDK, as today;
  - `RenewAccessTokenAsync(rejected)` answers from `GetAuthorizationAsync(Bearer rejected)` for every credential that `CanRenew` (client credentials too, not only federation); null without a bound target;
  - the certificate is `credential.ClientCertificate` (loaded by `StorytellerClientCertificate.Load`, which replaces `MachineCertificate.Load`; the SAN parsing `MachineCertificate.ReadIdentity` stays);
  - `TokenExpiresAt` for `sform account` comes from the token's JWT `exp` (`JwtExpiry`). A token without one prints "Signed in; the expiry of the token is unknown." instead of today's line for credentials that are checked on the first call;
  - the identity's variables are `Names`, plus the two GitHub names when the source is GitHub Actions, as today.

  Its own token POST, discovery fallback, exchange, GitHub request and token cache are deleted; the SDK does them.
- **Errors.** Every call into the SDK (`Read`, `CreateCredential`, `Bind`, `GetAuthorizationAsync`) is wrapped. `StorytellerAuthenticationException` maps, keeping the message and the inner exception: `Configuration` → `Configuration` (exit `2`), `Rejected` → `Denied` (`44`), `Unavailable` → `ServiceError` (`3`). An `ArgumentException` of an SDK factory maps to `Configuration`. Nothing else from the SDK leaves the service, so `Startup`'s `AccessTokenFactory` (which turns other exceptions into an empty token) and `MachineTokenRenewalHandler` (which rethrows) see only `AuthenticationException`. One test per entry point.
- **Changes against today**, documented in `authentication.md`:
  1. `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` federates (was exit `2`); `STORYTELLER_API_KEY=ApiKey` is an error (was sent as the key);
  2. URL rule: user info in a URL, and `http://loopback`, are refused;
  3. a `403`, `404` or redirect of the token endpoint, and a 2xx without a bearer token, are `44` (were `3`);
  4. AuthKit discovery for a `client_` id without `STORYTELLER_TOKEN_URL` asks `GET {endpoint}/v1/auth/configuration` directly: `authentication.provider` and `auth.discovery.json` no longer affect a machine; a provider that is not AuthKit or a domain with a path is `44` (was `2`), a 4xx of discovery `44`, a 5xx or network failure `3`;
  5. token requests have a 5 s attempt timeout and up to 3 retries of transient failures (were one attempt with the 100 s default);
  6. a client-credentials token is renewed and the request sent again once after a `401`;
  7. a 3xx or 4xx of the GitHub runner, or an answer without a token, is `44` (was `3`); its 5xx and network failures stay `3`;
  8. the renewal margin is 60 s, or half the lifetime of a token that lives under 2 minutes; a failed early renewal keeps the unexpired token.
- **Deprecated names.** `sform account` adds a line per deprecated name in use (`DeprecatedNames`, and `STORYTELLER_API_URL` when it gave the endpoint): "`STORYTELLER_API_URL` is deprecated; use `STORYTELLER_ENDPOINT`." Other commands stay quiet.
- **CLI types.** Deleted: `StorytellerVariables` (credential names → SDK constants; `PROJECT` and `VIEW` move to a new `TargetVariables`), `GitHubActionsVariables`, `FederatedTokenSource` (→ `StorytellerSubjectTokenSource`), `TokenExchange` and `MachineCertificate.Load` when unused, `MachineSignInSettings.IsSecureOrLocal`/`IsLocalHttp`/`NormalizeSubjectTokenType` (→ SDK). Kept: `MachineSignInKind`, `MachineIdentity`, `MachineApiKey`, `MachineCertificate.ReadIdentity`, `JwtExpiry`, `MachineTokenRenewalHandler`, `AuthenticationException` and its reasons.
- **Startup** keeps `AccessTokenFactory`, the development certificate header and `MachineTokenRenewalHandler` on the seven clients. It reads `Endpoint` instead of `ApiUrl`, uses `StorytellerUrlRule.IsLocalHttp`, and drops the named `HttpClient` of `MachineAuthenticationService`, whose `IHttpClientFactory` and resolver parameters go too. `AuthenticationServiceSelector`, `MachineRenewCommand` and the user sign-in do not change.
- **Test seam.** The SDK grants `InternalsVisibleTo` to `42.Platform.Cli.UnitTests`. `MachineAuthenticationService` takes an internal `MachineCredentialHooks` (`Func<StorytellerCredential, StorytellerCredential>? Decorate`, `StorytellerResilienceOptions? Resilience`), applied to every bound copy. The tests pass `Decorate = c => c.WithPrimaryHandler(scripted)` (compiled in the test assembly) and a resilience without retries or attempt timeout. The tests that build the service through DI (`FederatedSignInWiringTests`, `CommandContextTests`) replace its registration with a factory that uses the internal constructor. The name clash of the two internal `JwtExpiry` classes is avoided in the test files that import both namespaces.
- **Tests** of phase 4, by file:

  | File | Change |
  |---|---|
  | `MachineSignInSettingsTests` (23) | rewritten on the shared contract; the reading and kind tests that duplicate `FromEnvironmentTests` move there or go; kept: `IsConfigured`, the GitHub variables alone, the endpoint rule, no secret in `ToString` |
  | `MachineAuthenticationServiceTests` (52) | about 30 change: messages (certificate, token file, rejected and unreachable texts), discovery through HTTP instead of the fake resolver, JWT test tokens for expiries, the renewal of client credentials, exit codes of the list above; the token POST, exchange and GitHub request form tests go (covered in `Sdk.NSwag/test/Credentials`) |
  | `AccountCommandTests` (14) | about 7: expiry lines, messages, deprecated-name lines |
  | `MachineTokenRenewalHandlerTests` (10) | the non-federated case flips for client credentials; the constructor |
  | `FederatedSignInWiringTests` (3) | the seam; the redirect test of the named client goes |
  | `CommandContextTests` (15) | messages, the constructor, new `STORYTELLER_KEY` tests |
  | `MachineCreateCommandTests`, `MachineCreateFederatedTests` | the printed names (section 11) |
  | `AccountLogoutCommandTests`, `CaDownloadCommandTests`, `AuthenticationServiceSelectorTests` | constants and constructors only |

### 11. Outputs that name variables

- `sform account machine create` prints the canonical names: `STORYTELLER_ENDPOINT` (for a federated machine still derived from its `TokenEndpoint`), `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_TOKEN_FILE=<file with the platform token>` with the GitHub hint, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE`, `STORYTELLER_API_KEY`, `STORYTELLER_CLIENT_CERTIFICATE`, `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`. A test asserts that every name it prints is an SDK constant and none is deprecated.
- The admin UI's one-time-secret `.env` snippet (`OneTimeSecretDialog.vue:73`) writes `STORYTELLER_ENDPOINT` and `STORYTELLER_API_KEY`; a new assertion in `test/unit/access/access.test.ts` ("OneTimeSecretDialog", an API-key secret) checks `STORYTELLER_ENDPOINT=` and no `STORYTELLER_API_URL`. No e2e or visual baseline changes (the snippet sits in a collapsed `<details>`).
- The `account` output, the `401`/`403` hint of `Program.cs` and `account logout` use the SDK names.

### 12. Documentation

Living docs, updated with the phase that changes them:

- Phase 1: `docs/Platform/Storyteller/authentication.md` "OpenAPI and Swagger UI".
- Phase 2: the generation docs of section 6.
- Phase 3: `src/Platform/Storyteller/Sdk.NSwag/README.md` and `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md` (the credential table with deprecated names, the public contract types, the GitHub source and its failures, the `Authorization` rule of token requests); `docs/Platform/Storyteller/configuration-provider.md:102-131` (the credential table and the GitHub source; `:127-128` no longer call `STORYTELLER_CLIENT_ID` alone an error); code comments that call the `sform` names "fallbacks" (`Configuration/src/Settings/ConfigurationVariables.cs:9`, `StorytellerConfigurationOptions.cs:12`, the SDK's `EnvironmentCredential.cs:7-8`, `StorytellerVariables.cs:3`, `UnboundTarget.cs:5`).
- Phase 4:
  - `docs/Platform/Storyteller/authentication.md`: the machine variables and `sform` sign-in sections (`:179-336`): canonical and deprecated names, `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID`, `STORYTELLER_KEY` as a machine target, discovery (`:185`), the GitHub hint (`:248`), the exit codes (`:252`, `:277-284`) and the changes of section 10; the CI and Kubernetes examples; the platform setups (`:1014`, `:1048`); the `machine create` printouts (`:425`, `:748`);
  - `docs/Platform/Storyteller/configuration-provider.md:104`: "one set of variables drives `sform` and the provider" becomes true, and the column says "deprecated";
  - `docs/42for.net/cli/sform.md`: the variable table and examples, the deprecated names, `:83` (a federated machine's project can come from `STORYTELLER_KEY`), and the configuration read (`story config <key>`, not `config get`, `:102`);
  - `docs/42for.net/platform/using-the-platform.md:49` if it names variables;
  - `src/Platform/Cli/src/Startup.cs:83-84` and other CLI comments that name the old variables.

### 13. Phases and commits

| Phase | Content | Commits |
|---|---|---|
| — | This spec | `docs(platform): plan …` |
| 1 | Sections 2-5 | `feat(platform): …` |
| 2 | Sections 1 and 6 | `chore(platform): bump version to 0.10-alpha`, `feat(platform): …` |
| 3 | Sections 7-9, phase 3 docs | `feat(platform): …` |
| 4a | Section 10: `sform` signs in through the shared contract and the SDK credential | `feat(platform): …` |
| 4b | Sections 10 (target) and 11, phase 4 docs | `feat(platform): …` |
| — | One review per phase | `docs(platform): review …` |

Each phase leaves the solution building and its tests green: `Api.Functions/test`, `Sdk.NSwag/test`, `Configuration/test`, `Configuration.KeyVault/test`, `src/Platform/Cli/test`; in phase 2 the TypeScript SDK and ui.admin checks; in phase 4b the ui.admin `lint`, `test` and `build`.

### 14. Out of scope

- Publishing the NuGet and npm packages, and generating Java, Python, Go or Rust clients (product owner: separate, later).
- **The optional `sform story config --format flat` (C5).** 424-104 owns `sform story config <key> --resolved --format flat|properties|env|yaml --content-only` together with `?format=` on the API; its acceptance criteria require identical output on both and an ETag per format. A CLI-only `--format flat` now would ship before the API format it must match, and the key/value flattener the CodeRabbit plan suggests (`.`, arrays as JSON, null as `""`) contradicts 424-104's `flat` (`:`, `DocumentFlattener`). The deferral is recorded on 424-96 and 424-104.
- A public `StorytellerCredential.FromGitHubActions` (424-104 may need it for its Action).
- First-class machine credentials (API key, mTLS) in the TypeScript fetcher, and a conditional helper for the composition read in either SDK.
- OpenAPI 3.1 and a native `mutualTLS` scheme (needs replacing the custom OpenAPI extension).
- Changing which callers the runtime admits; this spec only describes it.

### 15. Differences from the CodeRabbit plan

- **Canonical names.** The plan makes the `sform` names canonical, assuming `StorytellerCredential` was not merged. It is, and it already treats the study's names as the contract and the `sform` names as fallbacks; this spec keeps that direction and deprecates the `sform` names.
- **Version and `If-None-Match`.** The plan keeps 0.9.x and a description-only `If-None-Match`. The product owner chose 0.10 and accepted breaking changes, so the parameter is formal.
- **Depth of reuse.** The plan keeps `sform`'s token lifecycle and moves only names and parsing. The criterion says "`sform` reuses `StorytellerCredential`", and the rules that already drifted (URL checks, the `ApiKey` scheme, `TOKEN_FILE`, discovery) should exist once, so this spec also moves the token requests, exchange and GitHub source to the SDK. It keeps what is about the command in `sform`.
- **Tests.** The plan keeps the `sform` test files passing without assertion changes. That cannot hold where the behaviour intentionally converges; section 10 lists the changes per file.
- **C5.** The plan reuses the key/value flattener. This spec leaves C5 to 424-104, whose `flat` differs.
- **Taken from the plan:** one source-owned user-only list with the test tables kept as the oracle, a document filter for the requirements (attributes cannot express AND), the combined `{mtls, apiKey}` alternative, replacing `EntityTagResponseHeader` by header classes, keeping the hand-written conditional methods with the reason in the README, checking the generated diff for signature changes, and a test that the names `machine create` prints are the SDK's.

### 16. Risks

- **Merge order with 424-84/424-106** (open PRs with an `open.api.v0.9.54.json` snapshot, new operations and an MCP server that calls the generated configuration reads). Whichever merges second exports the snapshot again from the combined API, regenerates both SDKs, classifies the new operations in `OperationAccess` (the parity test fails until it does), and passes `null` for `if_None_Match` in the MCP calls.
- **The custom OpenAPI extension** (`2.0.0-preview-prop-naming`) comes from a local package folder that `nuget.config` does not list. Its fork renders header parameters and filter-written requirements as section 3 and 4 expect; phase 1 checks the rendered output first.
- **Behaviour changes in `sform`** (section 10), documented in `authentication.md`. The largest are exit codes of a refusing token endpoint or runner, and `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID`.
- **The size of phase 4a**: about 3,700 lines of CLI tests are touched. Phase 4b is kept apart so that 4a only changes how `sform` signs in.
