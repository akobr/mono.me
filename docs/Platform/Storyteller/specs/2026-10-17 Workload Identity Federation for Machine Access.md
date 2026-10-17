# Workload Identity Federation for Machine Access

Linear: [424-46 Add workload identity federation](https://linear.app/424fun/issue/424-46/add-workload-identity-federation)

Line numbers refer to the tree this plan was written against (`origin/main` at `e02fe2d9`). No application code is changed by this document. Do not edit this spec to match later deviations; record those in the reviews.

## Problem

Every deployment target of a Storyteller client needs a stored secret ("secret zero"). Machines prove themselves with a static API key, a client certificate or an identity provider client secret. Workloads that already hold a platform-issued identity — a Kubernetes service account token, a GitHub Actions or Vercel OIDC token, an Azure managed identity, an AWS or Google Cloud workload identity — cannot use it, so the operator copies a long-lived secret into each pipeline, cluster and function.

The issue asks for:

- federated machine identity for Kubernetes, GitHub Actions, Vercel, Azure managed identities, AWS and Google Cloud, without stored secrets;
- a credential kind `Federated`;
- per project, trusted OIDC issuers;
- claim rules (`sub`, `aud`, repository, environment, namespace) mapped to a machine;
- optionally an [RFC 8693](https://www.rfc-editor.org/rfc/rfc8693) token exchange that returns a short-lived Storyteller token.

## Current State

### Machine model and kinds (`Abstractions.Access`, `Backend.Core/src/Accessing`)

- `MachineCredentialKind` (`Abstractions.Access/src/MachineCredentialKind.cs:7-15`) is `ApiKey = 0, Certificate, CertificateAndApiKey, ClientCredentials`, serialized as a string (`StringEnumConverter`). Kinds were appended so stored values keep their meaning.
- `MachineAccess` (`Abstractions.Access/src/Model/MachineAccess.cs`) carries `Id`, `ObjectId`, `AccessKey`, `Scope`, `AnnotationKey`, `CredentialKind`, certificate fields and the `ClientCredentials`-only `TokenEndpoint`/`TokenScope`, which are returned on create and not stored (comment at `:35-37`).
- `MachineAccessCreate` (`Backend.Core/src/Accessing/Model/MachineAccessCreate.cs`) has `Organization`, `Project`, `Scope`, `AnnotationKey`, `CredentialKind`, `CertificateLifetimeDays`, `UseSharedCertificate`. `AccessHttp.PostMachines` binds it with `[FromBody]` (`Api.Functions/src/V1/AccessHttp.cs:293`), which uses System.Text.Json and enforces C# `required` members.
- `MachineAuthenticationPolicy` (`Model/MachineAuthenticationPolicy.cs`) holds `EnabledCredentialKinds`, the default `CredentialKind` and `CertificateLifetimeDays`. It is stored on `AccessPointEntity.MachineAuthentication` (core container, id `apt.{org}.{project}`) by `CosmosMachineAuthenticationPolicyStore`, cached 60 s per instance, and edited through `GET`/`PUT v1/access/points/{key}/machine-authentication` (`AccessHttp.cs:410-477`; Contributor / Administrator). The `PUT` replaces the whole policy.
- `MachineAuthenticationPolicies` (`Backend.Core/src/Accessing/MachineAuthenticationPolicies.cs`) resolves, validates and normalizes the policy. `Resolve(null, …)` falls back to `MachineAuth:DefaultCredentialKind` (`:14-17`). `IsSatisfiedBy` (`:112-121`) ends in `_ => false`, so a bearer kind can never sign in through the API key or certificate path.
- `MachineAccessScopes.Get(scope)` (`MachineAccessScopes.cs:6-20`) maps a `MachineAccessScope` to Storyteller scope strings.
- `MachineAuthenticationOptions` (section `MachineAuth`, `MachineAuthenticationOptions.cs`) is bound in `Access.Certificates/src/EntryPoint.cs:29-30`; `TrustDomain` defaults to `2s.platform`.

### Creating, resetting and deleting machines

- `PolicyAwareMachineAccessService` (`Access.Certificates/src/PolicyAwareMachineAccessService.cs`) is the effective `IMachineAccessService` (it replaces the API key service in `Access.Certificates/src/EntryPoint.cs:40-41`).
  - Create (`:36-72`) takes `model.CredentialKind ?? policy.CredentialKind`, refuses a kind the project has not enabled (`MachineAccessNotSupportedException`, which `AccessHttp` answers with 400), rewrites the model (`:55-62`) and routes by kind; the default arm throws `InvalidOperationException` (500).
  - Reset and delete route by the **stored** kind (`:103-115`): `ClientCredentials` goes to the optional `IIdentityProviderMachineAccessService`, everything else to the API key service (and a certificate revoke on delete).
- `CosmosAccessService` (`Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`) persists `MachineAccessEntity` documents in container `org.{org}`, partition `{project}.access`, id = machine id.
  - `CreateMachineAccessAsync` (`:425-481`) copies every persisted field explicitly.
  - `ResetMachineAccessAsync` (`:483-512`) lets router exceptions through; `AccessHttp.PutMachine` (`:333-346`) answers `MachineAccessNotSupportedException` with 400.
  - `GetMachineAccessAsync` (`:413-423`) is an uncached point read.
  - `VerifyAccessForMachineAsync` (`:546-562`) only checks that a document with the machine id exists in the route's project; it ignores the stored kind and scope.
  - `GetMachineAccessesAsync` (`:391-411`) runs `SELECT * FROM ma` over the whole `{project}.access` partition.
  - There is no project deletion.
- `MachineAccessEntity` carries `_etag`; `CosmosCertificateRenewalService.cs:156` replaces with `IfMatchEtag`.
- `AccessMappings.ToMachineAccess` (`Backend.CosmosDb/src/AccessMappings.cs:37-50`) does not map `TokenEndpoint`/`TokenScope`.
- `UserAuthenticationEntryPoint.EnsureNoOtherIdentityProviderMachineAccess` (`Backend.Core/src/Accessing/UserAuthenticationEntryPoint.cs:53-60`) allows one `IIdentityProviderMachineAccessService` per deployment.
- `IAccessService` is implemented by five test fakes (`AnnotationsHttpTests.cs:503`, `KeyValueTestDoubles.cs:67`, `MachineHttpTests.cs:181`, `MembersHttpTests.cs:111`, `PermissionHttpTests.cs:96`).

### Request authentication (`Api.Functions`)

- Middleware order (`Api.Functions/src/Program.cs:33-36`): `ExceptionHandling` → `TransportSecurity` → `MachineAuthentication` → `BearerAuthentication`. All HTTP triggers are `AuthorizationLevel.Anonymous`; each handler authorizes itself with `CheckScope` / `CheckAccessTo*Async`.
- `MachineAuthenticationMiddleware` handles `Authorization: ApiKey …` and the client certificate header and builds the claims `azp`, `sub`, `roles` (from `MachineScopeClaims`) and, from a machine certificate only, `annotationKey`.
- `BearerAuthenticationMiddleware` (`Security/BearerAuthenticationMiddleware.cs`):
  - Under `DEV_AUTH` (Debug builds) it decodes tokens without validation before any validator runs (`:50-65`).
  - Otherwise it validates `Authorization: Bearer …` with the user `IBearerTokenValidator` first (`:67-75`), then — when that returns null — with each `IMachineTokenValidator` whose synchronous `CanValidate(issuer)` accepts the **unverified** `iss` (`:125-145`).
  - `StoreIdentity` (`:189-200`) hard-codes `FunctionContextItemKeys.MachineCredentialKind = ClientCredentials` for every machine result.
  - `BearerKeyRetrievalException` becomes 503 with `Retry-After: 5`.
- `BearerValidationResult(Claims, IsMachine, MachineId)` (`Backend.Core/src/Accessing/BearerValidationResult.cs`) carries no kind and no project.
- `MachineCredentialPolicy.EnsureAllowedAsync` (`Security/MachineCredentialPolicy.cs:16-34`) runs inside `CheckAccessToAsync` for bearer machines and checks only that the route project enables `ClientCredentials`.
- `HttpRequestDataExtensions.CheckAccessToAsync` (`Security/HttpRequestDataExtensions.cs:187-214`) verifies a machine by `VerifyAccessForMachineAsync(route org, route project, machine id)` and skips role checks. `GetSecretRightsAsync` (`:134-168`) treats a machine without a presented certificate as the `MachineToken` channel. `SetResponseHeader`/`MarkNoStore` (`:171-185`) write through the ASP.NET `HttpContext` and are skipped without one.
- `EntraIdBearerTokenValidator` (`Access.AzureAd/src/EntraIdBearerTokenValidator.cs:47-52`) accepts any `login.microsoftonline.com` / `sts.windows.net` issuer and claims Entra application tokens as `ClientCredentials` machines; on an unknown key it refreshes and retries once (`:91-102`).
- The anonymous endpoints are `GET v1/auth/configuration` (`V1/AuthConfigurationHttp.cs`) and `GET v1/access/certificate-authority`. No endpoint reads a form body; `[FromBody]` is JSON only.
- `TransportSecurityMiddleware` (`:39-48`) trusts `X-Forwarded-Proto` only.

### Token validation building blocks

- `KeycloakMachineTokenValidator` and `AuthKitTokenSource` use `JsonWebTokenHandler`, `ConfigurationManager<OpenIdConnectConfiguration>` / `ConfigurationManager<JsonWebKeySet>` (`Access.AuthKit/src/JsonWebKeySetRetriever.cs`, `internal`), an RS256 allow-list and 30 s skew; on an unknown key they call `RequestRefresh()` and refuse without retrying. `EntraIdBearerTokenValidator` uses `JwtSecurityTokenHandler`, no algorithm allow-list and the default 5-minute skew.
- In IdentityModel 8.18 `ConfigurationManager.RequestRefresh()` refreshes in the background, so a retry right after it usually sees the stale keys.
- Package versions (`Directory.Packages.props`): `Microsoft.IdentityModel.JsonWebTokens`, `.Protocols`, `.Protocols.OpenIdConnect` 8.18.0.
- The Functions OpenAPI extension names properties through Newtonsoft (`OpenApiConfigurationOptions.cs:32`, `DefaultNamingStrategy`); responses go out through System.Text.Json with `NoChangeNamingPolicy` (`Program.cs:51-59`). No API model uses snake_case today.
- `CachedAsync<T>` (`src/Libraries/Utils/Async/src/CachedAsync.cs`) offers `GetValueAsync()` and `Invalidate()`.

### Signing material

- Storyteller issues no tokens today. Its only private key is the machine CA, bootstrapped by `LocalCertificateAuthorityProvider` (`Access.Certificates/src/LocalCertificateAuthorityProvider.cs`) and stored as an unprotected PKCS#12 in the core container, partition `certificates`, id `cau.{version}` (`CosmosCertificateAuthorityStore.cs:57`); concurrent bootstraps resolve through a create conflict and a re-read. A stolen CA key alone cannot impersonate a machine, because `ClientCertificateValidator.cs:112-135` also requires a stored certificate record.

### Clients and docs

- OpenAPI: `MachineCredentialKindDocumentFilter` lists `Enum.GetNames<MachineCredentialKind>()`; `MachineCredentialKindDocumentFilterTests.cs:43` pins the four names. The document is exported by `Api.Functions/test/OpenApiExportTests.cs` into `sdk.typescript/open.api.v*.json` and `Sdk.NSwag/open.api.v*.json`; both SDKs are generated and committed (`ui-admin.yml:58-62` fails CI when `generated/` is stale).
- sform: `Cli/src/Commands/MachineAccess/CredentialKinds.cs:9` lists the kind names; `MachineCreateCommand.cs:140-147` prints "copy the access key" for every kind that is not a certificate or `ClientCredentials`.
- Admin UI: `ui.admin/src/app/utils/machines.ts:21` is an exhaustive `Record<MachineCredentialKind, string>` (the build fails on a new kind); `machines.vue` always opens the one-time secret dialog after create and shows Reset for every kind; the mock API lists the kinds in `app/mock/access-handlers.ts:55`.
- Docs: `docs/Platform/Storyteller/authentication.md` (kinds table at 189-197, client credentials at 239-343, scale at 345-398, 401/403 at 400-421, "Not available yet" at 435-438); `docs/Platform/Storyteller/key-values.md:104` (secret delivery to bearer machines); `docs/42for.net/platform/admin-ui.md:174-195`.
- Open PR #104 (424-45, sform machine sign-in) is not merged. Its spec defers this issue and names `STORYTELLER_TOKEN_FILE` as the later hook. It edits `MachineCreateCommand.cs`, `authentication.md` and `docs/42for.net/cli/sform.md`.

## Design

A federated machine has no secret. Its trust is split in two:

1. **Trusted issuers** belong to the project and are managed by its Administrators. Each one names an OIDC issuer, where its public keys come from, and claims every token of that issuer must carry (for example "our GitHub organization").
2. **Federation rules** belong to the machine and are managed by Contributors, like the machine itself. A rule picks a trusted issuer and pins the token's `aud`, `sub` and further claims.

The workload never sends its platform token to the regular API. It exchanges it at the project's **token endpoint** (RFC 8693) for a short-lived **Storyteller token** (an RFC 9068 `at+jwt` signed by Storyteller), and calls the API with `Authorization: Bearer <storyteller token>` until the token expires.

```
workload ──(1) platform token (GitHub, K8s, Vercel, Entra, AWS, Google)──▶ POST v1/{org}/{project}/access/token
                                                                              │ client_id = machine id
                                                                              │ verify with the trusted issuer's keys
                                                                              │ match one of the machine's federation rules
workload ◀─(2) Storyteller token (at+jwt, 15 min, machine scopes)────────────┘
workload ──(3) Authorization: Bearer <storyteller token>──▶ any machine-accessible API
```

Why an exchange instead of accepting platform tokens directly on every call:

- The request names its project and machine explicitly (the route and `client_id`), so an issuer the project does not trust never causes a metadata fetch, and identical issuer strings of different clusters are told apart by the trusted issuer record.
- The expensive work — discovery, JWKS, claim rules — runs once per exchange, not on every API call.
- Azure managed identity tokens are Entra tokens; sent directly they would be claimed by `EntraIdBearerTokenValidator` as `ClientCredentials` machines. Through the exchange they never reach the bearer pipeline.
- Every call with a Storyteller token still reads the machine document and the project policy, so deleting the machine cuts it off at once.

## Proposed Changes

### 1. Credential kind and models (`Abstractions.Access`, `Backend.Core`)

Append the kind; stored integers and strings keep their meaning:

```csharp
public enum MachineCredentialKind
{
    ApiKey = 0,
    Certificate,
    CertificateAndApiKey,
    ClientCredentials,

    // No secret: the workload exchanges a token of a trusted OIDC issuer for a short-lived Storyteller token.
    Federated,
}
```

New project-level trusted issuer (`Abstractions.Access/src/Model/FederatedIssuer.cs`). Members that the API requires are plain strings, not C# `required`, so that a missing value reaches `FederationValidation` (400) instead of failing in the System.Text.Json input converter (500):

```csharp
public record FederatedIssuer
{
    // Project-unique name, the route segment and the reference used by rules: ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$
    // The route value wins over the body.
    public string Name { get; init; } = string.Empty;

    // The exact `iss` value, compared ordinally (a trailing slash matters). Immutable after creation.
    public string Issuer { get; init; } = string.Empty;

    public FederatedIssuerKeySource KeySource { get; init; } = FederatedIssuerKeySource.Discovery;

    // KeySource == JwksUri: where the JSON Web Key Set is fetched.
    public string? JwksUri { get; init; }

    // KeySource == Jwks: the JSON Web Key Set as JSON text (public keys only), for issuers without public discovery.
    public string? Jwks { get; init; }

    // The longest accepted `exp - iat` of a platform token, in seconds. Null: 3600. Range 60..86400.
    public int? MaxTokenLifetimeSeconds { get; init; }

    // Administrator-owned pins, same shape and matching as FederationRule.Claims, ANDed into every rule
    // of this issuer (for example repository_owner_id of the company's GitHub organization).
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? RequiredClaims { get; init; }

    public string? Description { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum FederatedIssuerKeySource
{
    Discovery = 0,   // {Issuer}/.well-known/openid-configuration, then its jwks_uri
    JwksUri,         // keys public but no discovery document (for example a cluster with --service-account-jwks-uri)
    Jwks,            // the stored Jwks; nothing is fetched (self-managed Kubernetes)
}
```

New per-machine rule (`Abstractions.Access/src/Model/FederationRule.cs`):

```csharp
public record FederationRule
{
    // FederatedIssuer.Name of the same project.
    public string Issuer { get; init; } = string.Empty;

    // Required. The token's `aud` must be this string, or a one-element array holding it (ordinal).
    public string Audience { get; init; } = string.Empty;

    // Required. Exact match of `sub`, or a prefix match when it ends with `:*` or `/*`.
    public string Subject { get; init; } = string.Empty;

    // Optional further pins. Key: a top-level claim name ("repository", "environment", "repository_owner_id")
    // or an RFC 6901 JSON pointer for nested claims ("/kubernetes.io/namespace").
    // Value: accepted values, any of them; every key must match (AND).
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Claims { get; init; }
}
```

`MachineAccess` / `IMachineAccess` gain:

```csharp
// Federated only: the rules a platform token must match. Returned by create, get and list.
public IReadOnlyList<FederationRule>? FederationRules { get; init; }
```

`MachineAccessCreate` (`Backend.Core/src/Accessing/Model/MachineAccessCreate.cs`) gains the same `FederationRules` (Federated only).

For a `Federated` machine `AccessKey` is `""` (as for `Certificate`), and `TokenEndpoint` carries the absolute URL of the project's token endpoint (section 10) on create, get and list; the comment at `MachineAccess.cs:35-37` changes accordingly. `TokenScope` stays null.

### 2. Validation (`Backend.Core/src/Accessing/Federation/FederationValidation.cs`)

A static, pure helper returns null or a 400 message, so the API, the router, the exchange and the tests share the rules:

```csharp
public static class FederationValidation
{
    public static string? ValidateIssuer(FederatedIssuer issuer, FederationOptions options);
    public static string? ValidateRules(IReadOnlyList<FederationRule>? rules, IReadOnlyCollection<FederatedIssuer> projectIssuers);
    public static string? ValidateRule(FederationRule rule, FederatedIssuer issuer);   // the issuer-dependent part, also run at exchange time
    public static SharedIssuerKind ClassifyIssuer(string issuer);                       // None, GitHub, Vercel, Google, Entra
}
```

Issuer rules (a project has at most 20 trusted issuers):

| Field | Rule |
|---|---|
| `Name` | `^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$` |
| `Issuer` | 1..512 characters. With `Discovery`: an absolute `https` URI without query or fragment. With `JwksUri` / `Jwks`: any non-empty string (it is never fetched). Cannot change on an existing issuer (400 `"The issuer of a trusted issuer cannot change; create a new one."`). |
| `JwksUri` | Required for `JwksUri`, forbidden otherwise; absolute `https` URI. |
| `Jwks` | Required for `Jwks`, forbidden otherwise; ≤ 16 KiB; parses as a JWK Set with 1..10 RSA or EC keys, each with a `kid`, none with private members (`d`, `p`, `q`, `dp`, `dq`, `qi`, `k`). |
| URLs (`Issuer` with `Discovery`, `JwksUri`) | Port 443, `Uri.HostNameType == Dns` (no IP literals in any notation, such as `https://2130706433/` or `https://0x7f.1/`), ASCII host (`IdnHost == Host`), no user info — unless `MachineAuth:Federation:AllowPrivateNetworks` is true (development and tests). |
| `MaxTokenLifetimeSeconds` | Null or 60..86400. |
| `RequiredClaims` | Same rules as `FederationRule.Claims`. |
| `Description` | ≤ 256 characters. |

Rule rules (a machine has 1..10 rules):

| Field | Rule |
|---|---|
| `Issuer` | Names a trusted issuer of the project. |
| `Audience` | 1..512 characters. Refused when it is an audience of another relying party, so a token minted for that party cannot be replayed here: `sts.amazonaws.com`, `pods.eks.amazonaws.com`, `api://AzureADTokenExchange`, `https://kubernetes.default.svc`, `https://kubernetes.default.svc.cluster.local`; anything starting with `//iam.googleapis.com/`, `https://iam.googleapis.com/`, `https://github.com/` or `https://vercel.com/`; the Azure resource audiences `https://management.azure.com`, `https://management.core.windows.net`, `https://graph.microsoft.com`, `https://vault.azure.net`, `https://storage.azure.com`, `https://database.windows.net` (with or without a trailing `/`); and the referenced issuer's own `Issuer` (ignoring one trailing `/`; the Kubernetes default `--api-audiences`). Comparison is ordinal, case-insensitive. |
| `Subject` | 1..512 characters. `*` may appear once, as the last character, right after `:` or `/` (`system:serviceaccount:prod:*`, `repo:acme/app:*`, `arn:aws:iam::123456789012:role/ci/*`). |
| `Claims` | ≤ 10 keys, 1..10 values per key, each value 1..512 characters. A key is a claim name (`^[A-Za-z0-9_.:-]{1,128}$`) or an RFC 6901 JSON pointer (`/` + 1..256 characters; `~1` → `/`, `~0` → `~`, any other `~` sequence is refused). `iss`, `sub`, `aud`, `exp`, `nbf`, `iat`, `jti` (bare or as `/name`) are refused; use `Subject` and `Audience`. |
| Name-derived issuers | Detected by host, whatever the `KeySource`. Host `token.actions.githubusercontent.com` (any path, so enterprise issuers too) or `token.actions.<sub>.ghe.com` → the rule or the issuer's `RequiredClaims` must pin `repository_owner_id`, `repository_id` or `enterprise_id`. Host `oidc.vercel.com` (global and team issuers) → must pin `owner_id`. Names and slugs can be recycled, ids cannot. |
| Immutable-id issuers | `https://accounts.google.com`, `accounts.google.com` and Entra issuers (`https://login.microsoftonline.com/…`, `https://sts.windows.net/…`): no subject wildcard, because `sub` is the identity's immutable id and a wildcard would admit every identity of the tenant or of Google. |
| Other issuers | A subject wildcard needs at least one `Claims` or `RequiredClaims` pin. |

The 400 messages name the rule and the field, for example `"Rule 2: the audience sts.amazonaws.com belongs to another service."`.

### 3. Options (`Backend.Core/src/Accessing/FederationOptions.cs`)

Bound from `MachineAuth:Federation` with `AddOptions<FederationOptions>().Bind(…).PostConfigure<IOptions<MachineAuthenticationOptions>>(…)` (to derive the defaults from `TrustDomain`), an `IValidateOptions<FederationOptions>` and `ValidateOnStart()`:

| Setting | Default | Meaning |
|---|---|---|
| `Issuer` | `urn:storyteller:{MachineAuth:TrustDomain}` | `iss` of Storyteller tokens. |
| `Audience` | `urn:storyteller:{MachineAuth:TrustDomain}:api` | `aud` of Storyteller tokens; also the only accepted `audience`/`resource` of an exchange. |
| `PublicBaseUrl` | null | The public base URL of the API (`https://host/api`), used for `TokenEndpoint`. Null: the scheme and host of the request URL (never `X-Forwarded-Host`). |
| `TokenLifetimeMinutes` | 15 | Lifetime of a Storyteller token, 5..60. |
| `ClockSkewSeconds` | 30 | Skew for platform and Storyteller tokens, 0..120. |
| `MaxSubjectTokenBytes` | 16384 | Larger platform tokens are refused before parsing. |
| `MetadataRefreshMinutes` | 60 | Age after which discovery documents and key sets are fetched again. |
| `SigningKeyAutoBootstrap` | true | Create the signing key on first issuance when none is stored. |
| `AllowPrivateNetworks` | false | Lifts the https/443/DNS-host/public-address limits for issuer URLs (development, tests). |

### 4. Persistence (`Backend.Core`, `Backend.CosmosDb`)

- **Trusted issuers.** `IFederatedIssuerStore` (`Backend.Core`): `ListAsync(org, project)`, `GetAsync(org, project, name)`, `SetAsync(org, project, issuer)` (create or replace), `DeleteAsync(org, project, name)`. `CosmosFederatedIssuerStore` keeps one `FederatedIssuerEntity` per issuer in container `org.{org}`, partition **`{project}.federation`** (next to the existing `{project}.meta`, `.audit` and `.usage` partitions, and outside the unfiltered machine list query), id = name, with `CreatedAt`/`UpdatedAt`. The list is cached 60 s per instance (key `fedissuers:{org}.{project}`) and invalidated locally on write, like the policy store. `InMemoryFederatedIssuerStore` is the default when Cosmos is not registered (tests). A future project deletion must remove this partition too.
- **Rules.** `MachineAccessEntity` gains `FederationRules`. `CosmosAccessService.CreateMachineAccessAsync` copies it, `AccessMappings.ToMachineAccess` maps it.
- **Changing rules.** `IAccessService.SetMachineFederationRulesAsync(org, project, id, rules)` (a default interface member that throws `NotSupportedException`, so the test fakes keep compiling) reads the entity (404 when missing), refuses a machine that is not `Federated` (400), and replaces `FederationRules` with an `IfMatchEtag` replace (a 412 retries once).
- **Rule references.** `IAccessService.FindMachinesUsingIssuerAsync(org, project, name, max)` (default member as above) queries `{project}.access` for machines whose `FederationRules` reference the issuer name; used by the issuer `PUT` (section 11).
- **Signing key.** `ITokenSigningKeyStore` (`Backend.Core`): `GetAsync()` and `TryCreateAsync(key)`. `CosmosTokenSigningKeyStore` stores `TokenSigningKeyEntity { Id = "tsk.1", Kid, Algorithm = "RS256", PrivateKeyPkcs8, CreatedAt }` in the core container, partition `signing-keys`. `InMemoryTokenSigningKeyStore` is the default.
- Reset needs no change in `CosmosAccessService`: the federated service throws `MachineAccessNotSupportedException`, which `PutMachine` already answers with 400.

### 5. Machine lifecycle (`Access.Federation`, new project; `Access.Certificates` router)

New project `src/Platform/Storyteller/Access.Federation/src/Access.Federation.csproj` (assembly `42.Platform.Storyteller.Backend.Federation`, root namespace `_42.Platform.Storyteller`, `InternalsVisibleTo 42.Platform.Storyteller.Access.Federation.UnitTests`; references `Backend.Core` and `Libraries/Utils/Async`) with `test/`, both added to `42.mono.slnx` next to the other `Access.*` projects. `Api.Functions.csproj` references it.

`IFederatedMachineAccessService : IMachineAccessService` (`Backend.Core`), implemented by `FederatedMachineAccessService`:

- **Create.** Requires `FederationRules`; validates them against `IFederatedIssuerStore.ListAsync` (400 with the validation message). Returns `MachineAccess { Id = ObjectId = new Guid, AccessKey = "", Scope, AnnotationKey, CredentialKind = Federated, FederationRules }`. Nothing external is created.
- **Reset.** Throws `MachineAccessNotSupportedException("A Federated machine has no secret to reset. Change its federation rules instead.")` → 400.
- **Delete.** Returns true; nothing external exists.

`PolicyAwareMachineAccessService` gains a trailing optional `IFederatedMachineAccessService? federatedService = null`:

- Create: a `Federated` arm; the `model with { … }` rewrite (`:55-62`) keeps `FederationRules`. `FederationRules` on any other kind → 400 `"Only Federated machines have federation rules."`. No service registered → 400 `"Federated machine access is not configured for this deployment."`.
- Reset and delete route a stored `Federated` machine to the federated service (never to the API key or certificate services).

`MachineAuthenticationPolicies.IsSatisfiedBy` gets an explicit `Federated => false` arm (no behaviour change; a federated machine never signs in with a key or certificate).

Who may do what follows the existing machine model: Contributors create, reset and delete machines of any scope today, so a Contributor may also write the rules of a federated machine. Administrators bound that with the issuers they trust and their `RequiredClaims`.

### 6. Platform token verification (`Access.Federation`)

**Keys.** `FederatedIssuerKeyResolver` returns the signing keys of a trusted issuer from its own cache instead of `ConfigurationManager` (whose background refresh would make an unknown-`kid` retry see stale keys):

- `Discovery`: fetch `{Issuer without trailing /}/.well-known/openid-configuration`, require its `issuer` to equal `Issuer` exactly and its `jwks_uri` to pass the URL rules of section 2, then fetch the key set. (This works for Entra v1 `https://sts.windows.net/{tenant}/` and v2 `https://login.microsoftonline.com/{tenant}/v2.0`, AKS, EKS, GitHub, Vercel, AWS and Google.)
- `JwksUri`: fetch `JwksUri`.
- `Jwks`: parse the stored set; nothing is fetched.
- Entries are cached in `IMemoryCache` per `(org, project, name, fingerprint of Issuer/KeySource/JwksUri/Jwks)` with a 1 h sliding expiration. An entry older than `MetadataRefreshMinutes` is fetched again; when a token names an unknown `kid` and the entry is older than 1 minute, it is fetched again synchronously and the token is checked once more. Concurrent requests share one fetch per entry.
- A discovery `issuer` or `jwks_uri` that does not pass is a misconfiguration: logged at `Warning` and refused like a rule mismatch. A network or HTTP failure, or an unknown `kid` within the 1-minute window, raises `BearerKeyRetrievalException` (the exchange answers 503).
- Fetches go through one named `HttpClient` (`"federation-metadata"`) whose `SocketsHttpHandler` sets `UseProxy = false`, `AllowAutoRedirect = false`, a 10 s timeout and a 512 KiB response limit. Its `ConnectCallback` resolves the host and connects only to addresses that the IANA IPv4/IPv6 Special-Purpose Address Registries mark globally reachable (refusing among others `0.0.0.0/8`, loopback, private, CGNAT `100.64.0.0/10`, link-local including `169.254.169.254`, `192.0.0.0/24`, `198.18.0.0/15`, `240.0.0.0/4`, multicast, `fc00::/7`, `fe80::/10`); IPv4 embedded in IPv6 (mapped, compatible, NAT64 `64:ff9b::/96` and `64:ff9b:1::/48`, 6to4 `2002::/16`, Teredo `2001::/32`) is checked as IPv4. It connects to the address it checked, so DNS rebinding cannot reach an internal one. `AllowPrivateNetworks` lifts the address and https checks.

**Verification.** `SubjectTokenVerifier.VerifyAsync(subjectToken, machine, issuers)`:

1. Refuse tokens over `MaxSubjectTokenBytes` or not in JWS compact form.
2. Read the unverified `iss`; keep the machine's rules whose trusted issuer exists and has exactly that `Issuer`. None → refused; nothing is fetched.
3. Group these rules by trusted issuer name, keeping rule order. For each group, in order:
   1. Validate with **that** issuer's keys and only that group's audiences, using `JsonWebTokenHandler.ValidateTokenAsync`: exact issuer (a custom ordinal validator), `ValidAudiences` with `IgnoreTrailingSlashWhenValidatingAudience = false`, `ValidAlgorithms` = RS256/384/512, PS256/384/512, ES256/384/512 (never `none` or HS*), `TryAllIssuerSigningKeys = false` (a `kid` is required), `RequireSignedTokens`, `RequireExpirationTime`, `ClockSkew`.
   2. Require `aud` to be a string or a one-element array (a token minted for several relying parties is refused), `iat` present and not later than now + skew, `exp - iat ≤ MaxTokenLifetimeSeconds ?? 3600`, and a non-empty string `sub`.
   3. Re-run `FederationValidation.ValidateRule` for each rule of the group against the current issuer; a rule that no longer passes (for example after a policy-relevant issuer change) is skipped and logged.
   4. Match only this group's rules: audience, subject (exact, or prefix for `:*` / `/*`), every `Claims` key and every `RequiredClaims` key of the issuer. A claim value matches when it is a string equal to an accepted value, a number or boolean whose JSON text equals it, or an array with any such element; objects, null and missing claims never match. Comparisons are ordinal and case-sensitive.
   5. The first matching rule wins. A rule never matches a token verified by another trusted issuer, even one with the same `Issuer` string.
4. No match → refused.

Every refusal returns one opaque result to the caller. The reason (issuer not trusted, misconfigured issuer, signature, lifetime, audience, rule n mismatched on claim x) is logged with the machine id and the trusted issuer name, never with the token; "issuer not trusted" at `Debug`, the rest at `Information`.

### 7. Token endpoint (`Api.Functions/src/V1/FederationHttp.cs`)

`POST v1/{organization}/{project}/access/token`, anonymous (no `CheckScope`), `Content-Type: application/x-www-form-urlencoded` ([RFC 8693 §2.1](https://www.rfc-editor.org/rfc/rfc8693#section-2.1)).

| Parameter | Rule |
|---|---|
| `grant_type` | Required, `urn:ietf:params:oauth:grant-type:token-exchange`; otherwise `unsupported_grant_type`. |
| `subject_token` | Required. |
| `subject_token_type` | Required, one of `urn:ietf:params:oauth:token-type:jwt`, `…:id_token`, `…:access_token` (Azure managed identity tokens are access tokens). The token must be a JWT either way. |
| `client_id` | Required, the machine id (a GUID). |
| `audience`, `resource` | Optional and repeatable (RFC 8693 §2.1); every value must equal `MachineAuth:Federation:Audience`, otherwise `invalid_target`. |
| `scope` | Optional, space-separated Storyteller scopes. Only its syntax is checked before verification. After verification it must be a subset of the machine's scopes (`invalid_scope` otherwise), and the token gets that subset (least privilege for a single job). |
| `requested_token_type` | Optional; `…:access_token` (default) or `…:jwt`; anything else → `invalid_request` `"The requested token type is not supported."`. `issued_token_type` echoes it. |
| `actor_token`, `actor_token_type` | Not supported → `invalid_request`. |

- Any other parameter given twice → `invalid_request` (RFC 6749 §3.2). A parameter with an empty value counts as absent.
- `Content-Type` must be `application/x-www-form-urlencoded` (a `charset` parameter is allowed); otherwise `invalid_request`.
- The request must not carry `Authorization`; a bearer header would be validated by `BearerAuthenticationMiddleware` first.

The handler takes `HttpRequestData`, reads at most 32 KiB of the body (more → `invalid_request`), parses it with `Microsoft.AspNetCore.WebUtilities` (`QueryHelpers.ParseQuery` keeps repeated values for the duplicate check), calls `IFederatedTokenExchange` (`Backend.Core`, implemented by `FederatedTokenExchangeService` in `Access.Federation`), and writes an `HttpResponseData` from `request.CreateResponse(…)` with its headers set directly on that response.

Processing, in this order, so that the anonymous endpoint does no work for unknown projects and machines:

1. Body size, content type, form shape and parameter checks (above).
2. Subject token size, JWS form and its unverified `iss`.
3. The project's access point must exist (`IAccessService.GetAccessPointAsync`, core container) and its effective policy must enable `Federated`. No org container is touched before this.
4. The project's trusted issuers (cached); if none has that `Issuer`, refuse without reading the machine.
5. The machine (`IAccessService.GetMachineAccessAsync`) must exist with kind `Federated`.
6. Verify the platform token (section 6), then the `scope` subset.
7. Issue a Storyteller token (section 8).

Any failure in steps 2–6 except the `scope` subset → `400 invalid_request` with `error_description` `"The subject token is not accepted for this machine."`, so unknown projects, unknown machines, disabled kinds and mismatched rules look the same ([RFC 8693 §2.2.2](https://www.rfc-editor.org/rfc/rfc8693#section-2.2.2) requires `invalid_request` for a rejected subject token).

Responses carry `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store` and `Pragma: no-cache`:

```json
200 {
  "access_token": "eyJ…",
  "issued_token_type": "urn:ietf:params:oauth:token-type:access_token",
  "token_type": "Bearer",
  "expires_in": 900,
  "scope": "Default.Read Annotation.Read Configuration.Read"
}
400 { "error": "invalid_request", "error_description": "The parameter subject_token is missing." }
503 { "error": "temporarily_unavailable", "error_description": "The keys of the trusted issuer could not be retrieved." }   + Retry-After: 5
```

`temporarily_unavailable` on the token endpoint is an extension of RFC 6749 §5.2; clients should key on the 503. The OAuth error body is written by the handler; it does not go through `ErrorResponse`.

OpenAPI: route constant `Definitions.Routes.Access.V1.MachineToken`, route id `ExchangeMachineToken`, tag `Access`, request body `application/x-www-form-urlencoded` with model `MachineTokenRequest`, responses `MachineTokenResponse` and `OAuthErrorResponse`. These models carry both `[Newtonsoft.Json.JsonProperty("access_token")]` (the OpenAPI extension names properties through Newtonsoft) and `[System.Text.Json.Serialization.JsonPropertyName("access_token")]` (the wire), so the document and the responses agree on snake_case.

### 8. Storyteller tokens (`Access.Federation`)

`TokenSigningKeyProvider` holds the signing key:

- For **issuing**, it loads `tsk.1` (cached 10 min with `CachedAsync`). Without a stored key and with `SigningKeyAutoBootstrap`, it creates one (RSA 2048, `kid` = RFC 7638 thumbprint) with `TryCreateAsync`; a create conflict means another instance won, and the stored key is re-read (the CA pattern). Without a key and without bootstrap, the exchange answers 503.
- For **validating**, it never bootstraps. It loads the stored key lazily; while none is stored, every Storyteller token is refused, and the store is read again at most once per 30 s per instance ("no key" is not cached longer). A token whose `kid` differs from the loaded key reloads the store at most once per 30 s per instance (one gate for all requests); between reloads it is refused without I/O.
- Rotation is out of scope; deleting `tsk.1` is the emergency procedure (documented).

`StorytellerTokenIssuer.Issue(…)` creates, with `JsonWebTokenHandler.CreateToken`:

- header `alg = RS256`, `kid`, `typ = at+jwt` ([RFC 9068](https://www.rfc-editor.org/rfc/rfc9068));
- `iss` = `Federation:Issuer`, `aud` = `Federation:Audience`, `sub` = `client_id` = machine id, `iat`, `nbf`, `exp` (now + `TokenLifetimeMinutes`), `jti` (GUID);
- `scope` = granted Storyteller scopes, space separated;
- `organization`, `project`, `annotation_key` (when the machine has one), `federation_issuer` (trusted issuer name) and `federation_subject` (the platform `sub`), for audit and logs.

`StorytellerTokenValidator : IMachineTokenValidator`:

- `CanValidate(issuer)` and the new `OwnsIssuer(issuer)` are true only for `Federation:Issuer`.
- Validates `typ` `at+jwt` or `application/at+jwt`, RS256 only, exact issuer and audience, lifetime with `ClockSkew`, `kid` required (see the reload gate above).
- Returns `BearerValidationResult` with `IsMachine = true`, `MachineId = sub`, `CredentialKind = Federated`, `Organization`, `Project`, and the claims `sub`, `azp` (machine id), one `roles` claim per scope, and `annotationKey` when present — the shape `MachineAuthenticationMiddleware` builds for API keys.

### 9. Bearer pipeline (`Backend.Core`, `Api.Functions/src/Security`)

- `BearerValidationResult` gains trailing optional `MachineCredentialKind? CredentialKind = null, string? Organization = null, string? Project = null`.
- `IMachineTokenValidator` gains `bool OwnsIssuer(string issuer) => false;`. `BearerAuthenticationMiddleware` reads the unverified `iss` first; when a validator owns it, only that validator runs and the user validator is skipped (a Storyteller token never pays an Entra or AuthKit attempt). A null result is a 401 as today. Under `DEV_AUTH` an owned issuer is still validated by its validator, so federated machines behave locally as in production.
- `StoreIdentity` stores `result.CredentialKind ?? ClientCredentials`, and new items `MachineOrganization` / `MachineProject` when the result names them.
- `CheckAccessToAsync`, for a `Federated` identity, reads the machine with `GetMachineAccessAsync` (one point read, replacing `VerifyAccessForMachineAsync` for this kind) and requires: the machine exists in the route project, its stored kind is `Federated`, every token scope is in `MachineAccessScopes.Get(stored.Scope)`, and the token's organization and project equal the route's (ordinal, case-insensitive). So a token signed with a stolen key cannot act as an API key machine or exceed a federated machine's scope.
- `MachineCredentialPolicy.EnsureAllowedAsync` checks every recorded bearer kind (`ClientCredentials`, `Federated`) with `policy.IsEnabled(kind)`.
- All of these failures are `SecurityTokenException` (401, empty body as today). RFC 9068 asks for `WWW-Authenticate: Bearer error="invalid_token"`; the pipeline writes none for any token today, and adding it is a pipeline-wide change left out here.
- `GetSecretRightsAsync` is unchanged: a federated machine uses the `MachineToken` channel, so `RequireMtlsForSecretDelivery` withholds secrets from it.

### 10. Management API (`Api.Functions/src/V1/FederationHttp.cs`, `AccessHttp.cs`)

| Method and route | Role | Body / response |
|---|---|---|
| `GET v1/{org}/{project}/access/issuers` | Contributor | `FederatedIssuer[]` |
| `GET v1/{org}/{project}/access/issuers/{name}` | Contributor | `FederatedIssuer`, 404 |
| `PUT v1/{org}/{project}/access/issuers/{name}` | Administrator | `FederatedIssuer` (the route name wins) → 200 `FederatedIssuer`; 400 on validation; 409 (below) |
| `DELETE v1/{org}/{project}/access/issuers/{name}` | Administrator | 200, 404 |
| `PUT v1/{org}/{project}/access/machines/{id}/federation-rules` | Contributor | `FederationRule[]` → 200 `MachineAccess`; 400, 404 |
| `POST v1/{org}/{project}/access/token` | anonymous | section 7 |

- All management routes require `User.Impersonation` and the role on `{org}.{project}`, like the machine routes; machine credentials get 401 / 403 as there.
- `PUT` of an existing issuer may change everything but `Issuer`. Creating an issuer whose name rules still reference (left behind by a delete) is refused with 409 `ConflictException` naming up to 10 machines, so a re-created name never silently re-activates rules written for another issuer.
- Deleting a trusted issuer is allowed while rules reference it: it is the kill switch. Those rules stop matching within the 60 s cache.
- Revocation timing: deleting a machine cuts it off at once; disabling `Federated` in the policy takes up to 60 s (policy cache); rule and issuer changes affect new exchanges only, and Storyteller tokens already issued stay valid until they expire (`TokenLifetimeMinutes` + skew).
- `PostMachines`, `GetMachines`, `GetMachine` and the rules `PUT` set `TokenEndpoint` of `Federated` machines to `{PublicBaseUrl or request scheme://host + /api}/v1/{org}/{project}/access/token`.
- Route constants and ids in `Definitions.cs` (`FederatedIssuers`, `FederatedIssuer`, `MachineFederationRules`, `MachineToken`; a `Name` route parameter), covered by `RouteTableTests`.

### 11. Composition (`Api.Functions/src/Program.cs`, `Access.Federation/src/EntryPoint.cs`, `Backend.CosmosDb/src/EntryPoint.cs`)

`AddFederatedMachineAccess(IConfiguration)` binds and validates `FederationOptions`, registers the named `HttpClient`, `FederatedIssuerKeyResolver`, `SubjectTokenVerifier`, `FederatedTokenExchangeService` as `IFederatedTokenExchange`, `TokenSigningKeyProvider`, `StorytellerTokenIssuer`, `FederatedMachineAccessService` as `IFederatedMachineAccessService`, `StorytellerTokenValidator` as an additional `IMachineTokenValidator`, and `TryAdd`s the in-memory stores. It does **not** call `EnsureNoOtherIdentityProviderMachineAccess`: federation works next to the one `ClientCredentials` provider. `Program.cs` calls it after `AddCertificateMachineAccess`. `AddCosmosDbAnnotations` (which runs first) registers `CosmosFederatedIssuerStore` and `CosmosTokenSigningKeyStore`, so they win.

### 12. Tests

- `Backend.Core/test`: `FederationValidationTests` (every row of section 2, including IP literal notations, IDN hosts, pointer escapes, wildcard separators, name-derived issuer pins by host, `RequiredClaims` satisfying a pin), `MachineAuthenticationPoliciesTests` (`Federated` enabled/disabled, `IsSatisfiedBy`).
- `Access.Federation/test` (new, xUnit + Shouldly):
  - `SubjectTokenVerifierTests` with locally signed RSA and EC tokens: issuer exact match and trailing slash, two trusted issuers with the same `Issuer` and different keys (a token of one never matches the other's rule), audience string / one-element array / several audiences, subject exact and `:*` / `/*` prefix, claim string/number/bool/array/pointer, `RequiredClaims`, lifetime limits, missing `iat`/`sub`/`kid`, `alg` none and HS256, oversized token, issuer not trusted → no fetch, a rule invalidated by an issuer change is skipped.
  - `FederatedIssuerKeyResolverTests` with a stub handler: discovery issuer and `jwks_uri` checks, JWKS URI, static JWKS, cache by fingerprint, refresh age, unknown `kid` refetch after 1 minute and 503 within it, shared fetch.
  - `PublicAddressFilterTests`: address classes and embedded IPv4.
  - `FederatedTokenExchangeServiceTests`: each error code, the order of checks (no machine read for unknown projects or issuers), scope narrowing, disabled kind, wrong kind, unknown machine.
  - `StorytellerTokenIssuerAndValidatorTests`: round trip, `typ`, expiry, foreign issuer, tampered token, unknown `kid` gate.
  - `TokenSigningKeyProviderTests`: bootstrap, conflict re-read, no bootstrap on the validation path, bootstrap disabled.
  - `FederatedMachineAccessServiceTests`.
- `Access.Certificates/test`: `PolicyAwareFederatedTests` (create routing, rules on other kinds, missing service, reset and delete by stored kind).
- `Api.Functions/test`:
  - `BearerAuthenticationMiddlewareTests` / `MachineTokenFallbackTests`: an owned issuer skips the user validator, kind and project items; these tests set `BearerValidationMode.DecodeWithoutValidation = false` in try/finally, and role/403 assertions branch on `HttpRequestDataExtensions.IsDevelopmentAuthentication`.
  - `MachineAccessRulesTests`: `Federated` policy, project mismatch, stored kind and scope checks.
  - `FederationHttpTests`: form parsing, duplicate and repeatable parameters, empty values, content type, body limit, error bodies, response headers on the `HttpResponseData`, issuer CRUD roles, immutable `Issuer`, 409 on re-creation, rules `PUT`, `TokenEndpoint`.
  - `RouteTableTests`, `MachineCredentialKindDocumentFilterTests` (five names), an OpenAPI test that the token models use snake_case names.
- `Access.Certificates/testint` (Cosmos; references `Access.Federation`): issuer store round trip and cache invalidation, rules on `MachineAccessEntity`, `SetMachineFederationRulesAsync`, `FindMachinesUsingIssuerAsync`, signing key bootstrap conflict. Runs only where the Cosmos emulator or Docker is available, like the existing suite.

### 13. OpenAPI and SDKs

- Export the document (`STORYTELLER_OPENAPI_EXPORT`) into `sdk.typescript/open.api.v{version}.json` and `Sdk.NSwag/open.api.v{version}.json`; remove superseded copies from `sdk.typescript` as its README asks.
- Regenerate `Sdk.NSwag/src/ApiSdk.g.cs` (bump the README command) and the TypeScript SDK (`npm run generate`); typecheck, lint and test both.
- TypeScript SDK helper `src/lib/federation.ts`, exported from the root entry point: `exchangeFederatedToken({ baseUrl, organization, project, machineId, subjectToken, subjectTokenType?, scope? })` posts the form with plain `fetch` (never through `storytellerFetch`, which would recurse through `getAccessToken`) and returns `{ accessToken, expiresAt, scope }`; `createFederatedTokenProvider(options & { getSubjectToken })` caches the Storyteller token until 60 s before expiry and is meant for `getAccessToken`. OAuth errors become a `StorytellerError` carrying the `error` code. Tests with MSW.
- If either generator cannot express the form-encoded body, keep the operation in the document and record the workaround in the review.

### 14. sform (`src/Platform/Cli`)

- `CredentialKinds.Names` lists `Federated`.
- `machine create -k federated --issuer <name> --audience <aud> --subject <sub> [--claim key=value]…` creates a machine with one rule; `--rules-file <path>` takes a JSON array of rules instead. The output prints the machine id, the token endpoint, the rule and a `curl` exchange example, and no secret.
- `machine rules <id> (--rules-file <path> | --issuer … --audience … --subject … [--claim …])` replaces the rules.
- `machine issuer list`, `machine issuer set <name> --issuer <url> [--jwks-uri <url> | --jwks-file <path>] [--max-lifetime <seconds>] [--require-claim key=value]… [--description <text>]`, `machine issuer delete <name>`.
- `machine reset` of a `Federated` machine prints the server's 400 message; `machine list` shows `–` instead of a secret for it.
- Signing in **as** a federated machine (`STORYTELLER_*` variables) belongs to the sign-in of 424-45, which is not merged; it is out of scope here.

### 15. Admin UI (`src/Platform/ui.admin/src`)

- `utils/machines.ts`: a `Federated` description; helpers `hasSecret(kind)` and `isFederated(kind)`.
- **Trusted issuers** section on the machines page for Administrators (`#trusted-issuers`, next to the shared certificates): a table (name, issuer, key source, max lifetime, required claims) and a dialog to add, edit (the issuer URL is read-only once created) and delete. The dialog offers templates that fill the issuer, the lifetime and the required-claim rows, and explain the claims:

  | Template | Issuer | Max lifetime | Required claims / hints |
  |---|---|---|---|
  | GitHub Actions | `https://token.actions.githubusercontent.com` (or `/<enterprise>`) | default | `repository_owner_id` |
  | Kubernetes | the cluster issuer (AKS: copy it verbatim, it ends with `/`); discovery, JWKS URI or pasted JWKS | default (raise to the projected `expirationSeconds`) | rules pin `/kubernetes.io/namespace` |
  | Vercel | `https://oidc.vercel.com/<team>` | 7200 (43200 for development tokens) | `owner_id` |
  | Azure managed identity | `https://login.microsoftonline.com/<tenant>/v2.0` (app registration with `requestedAccessTokenVersion: 2`) or `https://sts.windows.net/<tenant>/` | 86400 | Subject = the identity's object id; Audience = the app registration |
  | AWS | `https://<id>.tokens.sts.global.api.aws` | default | Subject = the role ARN |
  | Google Cloud | `https://accounts.google.com` | default | Subject = the service account's numeric unique id |
  | Custom | — | default | — |

- `MachineCreateDialog`: with kind `Federated`, a rule editor (trusted issuer select, audience, subject, claim rows) instead of certificate fields; the audience is prefilled with the project's token endpoint URL (a Storyteller-specific value). Create stays disabled until one rule is complete. Without trusted issuers it links Administrators to the section and tells Contributors to ask one.
- After creating a `Federated` machine the page shows a setup dialog (machine id, token endpoint, `curl` exchange and a GitHub Actions step) instead of the one-time secret dialog. Reset is hidden for `Federated` machines; the kind column shows labels.
- `machines/[id].vue` lists the rules and lets Contributors edit them (the rules `PUT`).
- Mock API: issuer CRUD, rules `PUT`, `Federated` in `credentialKinds`, `withCredentials` without a secret, the token endpoint on federated machines, a seed issuer.
- Tests: unit (helpers, dialogs, mock handlers), e2e (an Administrator adds an issuer, a Contributor creates a federated machine), the accessibility list; visual baselines are refreshed where the Playwright Docker image is available.

### 16. Documentation

- `docs/Platform/Storyteller/authentication.md`:
  - the opening summary, the kinds table (`Federated` row), the policy section;
  - a new **Machine access with workload identity federation** section: design, trusted issuers, rules and matching, the token endpoint (request, response, errors), Storyteller tokens, revocation timing, configuration (`MachineAuth:Federation:*`), and security notes (name-derived issuers and immutable ids, foreign and default audiences, multi-audience tokens, private networks, the signing key in Cosmos and its emergency rotation, long-lived managed identity tokens, secrets delivery);
  - per-platform setup, each with the issuer, audience, subject, lifetime and a token request:
    - **GitHub Actions:** `permissions: id-token: write`; `core.getIDToken('<aud>')` or `$ACTIONS_ID_TOKEN_REQUEST_URL&audience=`; `subject_token_type` `…:jwt`; enterprise issuers; the immutable `sub` format (`repo:OWNER@OWNER_ID/REPO@REPO_ID:…`, the default for repositories created, renamed or transferred after 2026-07-15) and why to pin `repository_owner_id` / `repository_id`.
    - **Kubernetes:** a dedicated projected `serviceAccountToken` volume with the rule's audience (the default `kube-api-access` token is refused); re-read the file before each exchange; AKS (`oidcIssuerProfile.issuerUrl`, trailing `/`), EKS, GKE; AKS workload identity, EKS IRSA and Pod Identity tokens are refused; self-managed clusters with `kubectl get --raw /openid/v1/jwks` and re-pasting after key rotation; pins on `/kubernetes.io/namespace` and `/kubernetes.io/serviceaccount/uid`.
    - **Vercel:** the team issuer, `getVercelOidcToken({ audience })` / the `x-vercel-oidc-token` header, lifetime 7200 (43200 for development), pins `owner_id` and `project_id`, a team rename changes the issuer.
    - **Azure managed identity:** an app registration representing Storyteller (identifier URI `api://<appId>`, `requestedAccessTokenVersion`), IMDS `resource=api://<appId>` or App Service `IDENTITY_ENDPOINT`, `subject_token_type` `…:access_token`, lifetime 86400, Subject = object id (every identity of the tenant can get a token for that audience).
    - **AWS:** `aws iam enable-outbound-web-identity-federation`, the issuer from `get-outbound-web-identity-federation-info`, `aws sts get-web-identity-token --audience … --signing-algorithm ES384|RS256` on a regional endpoint, `sub` = role ARN, nested claims through `/https:~1~1sts.amazonaws.com~1/aws_account`, never pin `request_tags`.
    - **Google Cloud:** the metadata server `…/identity?audience=<aud>&format=full` with `Metadata-Flavor: Google`, `subject_token_type` `…:id_token`, Subject = the numeric unique id, never the email; on GKE prefer the Kubernetes path.
  - the `MachineAccess` table (`FederationRules`, `TokenEndpoint`), the scale section ("five kinds", per-exchange and per-call work), 401/403 and the token endpoint errors.
- `docs/Platform/Storyteller/key-values.md:104`: name federated machines in the bearer-token row.
- `docs/42for.net/platform/admin-ui.md`: machines (federated kind, trusted issuers, rules) and the one-time secret note.
- `Sdk.NSwag/README.md`, `sdk.typescript/README.md`: document version and the federation helper.

### Phases

1. **Phase 1 — API:** sections 1–12 and the `authentication.md` / `key-values.md` parts of 16.
2. **Phase 2 — OpenAPI and SDKs:** section 13 and the README parts of 16.
3. **Phase 3 — sform:** section 14.
4. **Phase 4 — Admin UI:** section 15 and `admin-ui.md`.

Whichever of this branch and #104 merges second resolves `MachineCreateCommand.cs` and `authentication.md`; regenerated SDK files follow the merge-order rule of the 424-14 spec (the later branch exports the document again).

### Decisions

- **Exchange, not direct platform tokens.** Accepting GitHub, Kubernetes or Entra tokens on every call would need a deployment-wide issuer index for the bearer middleware, would collide with the Entra user validator for managed identities, and would repeat discovery and rule matching on every call. The exchange gives one explicit decision per token.
- **Signed `at+jwt`, not an opaque token.** Stateless signature check, no write per exchange, and one machine read per call (which the per-call checks need anyway). The cost: read access to the core container is enough to mint tokens; the per-call kind and scope checks keep such a token within existing federated machines and their scopes. A Key Vault signer is the follow-up.
- **URN issuer.** `urn:storyteller:{TrustDomain}` is not an RFC 8414 issuer. Publishing metadata later means changing `Federation:Issuer`, which rejects outstanding tokens for at most `TokenLifetimeMinutes`.
- **Issuers as separate documents, not in `MachineAuthenticationPolicy`.** The policy `PUT` replaces the whole document, so every existing client (sform, the admin UI) would wipe issuers it does not know about. Separate documents also let Contributors read issuers without editing the policy.
- **No `jti` replay store.** Azure managed identity tokens are cached for up to 24 h, Vercel reuses its tokens for 90 min and a Kubernetes pod reuses its projected token until rotation, so single use would break exactly the platforms the issue names. Tokens are audience-bound, single-audience and short-lived instead.
- **Exact and separator-anchored prefix matching only.** No glob, regex or CEL: predictable, and the pins for name-derived issuers cover what wildcards would otherwise make dangerous.
- **Opaque errors.** One `invalid_request` description for every verification failure, with the reason in the logs, so the endpoint cannot be used to probe projects, machines, scopes or rules.

### Out of scope

- Signing in to sform as a federated machine (after 424-45 merges, through `STORYTELLER_TOKEN_FILE` or a dedicated exchange).
- A C# SDK exchange helper.
- Rotation of the Storyteller signing key, a Key Vault-backed signer, and publishing a JWKS / RFC 8414 metadata document.
- An endpoint that tests a trusted issuer's discovery on demand.
- Replay protection, CEL or glob claim conditions, rate limiting of the token endpoint.
- `WWW-Authenticate` on 401 responses.
- Enforcing `AnnotationKey` (424-48); the Storyteller token already carries `annotation_key` for it.
- `ContributorWithSecrets` for creating Secrets-scope machines (an existing gap for every kind).
- Filtering certificate records out of the machine list query (`CosmosAccessService.cs:391-411`), noticed while planning.
