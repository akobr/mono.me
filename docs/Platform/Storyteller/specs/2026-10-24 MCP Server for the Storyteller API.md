# MCP Server for the Storyteller API

Linear: [424-84 Create MCP interface](https://linear.app/424fun/issue/424-84/create-mcp-interface)

## Problem

The 2S platform has three clients: the Storyteller API, `sform` and the admin UI. An AI assistant can reach none of them on its own. A person who wants an assistant to read or change the platform has to copy JSON in and out of a chat. The issue asks for an MCP interface over the 2S platform API:

- create and manage annotations, configurations, schemas and templates;
- search configurations and help to understand them: explain where a value comes from and how it is used, and suggest where a new configuration should be placed;
- create and manage machines;
- create and manage records of the Values store (key/values);
- authentication per user, acting as that user;
- an HTTP MCP server over the Streamable HTTP transport, built on the official C# SDK of the [Model Context Protocol](https://modelcontextprotocol.io/).

The inputs to this spec are:

- the issue;
- the CodeRabbit coding plan attached to the issue (`2026-10-23 Create MCP interface.md`), used as a second opinion and a source of ideas, not as the decision. Section 16 lists where this spec decides differently;
- the source of `ModelContextProtocol.AspNetCore` 2.3.0 (tag `v2.3.0`, the latest release), `Microsoft.Identity.Web` 4.5.0 and MSAL 4.83.1, read for this spec;
- four reviews of a first draft of this spec (security, library feasibility, repository facts, usefulness for an assistant). Their accepted findings are part of the sections below.

The branch starts from `origin/main` at `c53ffbc0` (the flat set of machine scopes and the cheap 304s are merged). Line numbers refer to that tree.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### User tokens in the API

- `Auth:Provider` picks Entra ID or AuthKit (`Api.Functions/src/Program.cs`). The options are `UserAuthenticationOptions` (`Backend.Core/src/Accessing/UserAuthenticationOptions.cs`, section `Auth`: `Provider`, `TenantId`, `ClientId`, `AppRoles`, `AuthKit`), checked on start by `UserAuthenticationOptionsValidator`.
- `EntraIdBearerTokenValidator` (`Access.AzureAd/src/EntraIdBearerTokenValidator.cs`, called the Entra validator below) checks the signature with the keys of `Auth:TenantId`, the audience `api://{ClientId}` or `{ClientId}`, and an issuer that starts with `https://sts.windows.net/` or `https://login.microsoftonline.com/`; the tenant is not pinned (deployments use `common`). It uses `JwtSecurityTokenHandler` with the default inbound claim mapping, so `scp` arrives as `http://schemas.microsoft.com/identity/claims/scope` and `tid` as `http://schemas.microsoft.com/identity/claims/tenantid`, while `azp`, `appid` and `idtyp` keep their names.
- The only rule that tells a user from a machine is `TryGetMachineId` (`:54-65`): a token whose `azp` (or v1 `appid`) differs from `Auth:ClientId` is a machine with that id. Every user token today has `azp == ClientId`, because `sform` and Swagger sign in with the API's own registration.
- `EntraIdClaimNormalizer` (`Access.AzureAd/src/EntraIdClaimNormalizer.cs`) applies the same rule to decoded claims. It runs only on the decode path of `BearerAuthenticationMiddleware` (Debug builds, `DEV_AUTH`).
- Users need the scope `User.Impersonation`; the role on the project decides the rest (`Api.Functions/src/Security/HttpRequestDataExtensions.cs`, `EnsureUser` `:194`, `EnsureUserRoleAsync` `:444`, the comment at `:451`). The account id is the `sub` claim (`GetIdentityUniqueId`); accounts are stored with that id.
- User-only routes (accounts, members, machines, the Administrator key/value operations, the permanent delete of an annotation, views) refuse machines with `403 AccessDenied`.

What this means for a server that calls the API for a user: a standard Entra on-behalf-of (OBO) token, requested by a separate app registration, has `azp` = that app. The Entra validator classifies it as a machine with that id. The machine is not registered in any project, so every data route answers `401` and every user-only route answers `403`.

AuthKit issues no token for the Storyteller API on behalf of a user: WorkOS documents no token exchange and no on-behalf-of grant. Its MCP support issues tokens from the AuthKit domain; the API can be pointed at that issuer, but then it no longer accepts the user-management tokens that `sform`'s device flow gets (`docs/Platform/Storyteller/authentication.md`, "The API cannot yet accept both token sources at once"), and forwarding the MCP token would be token passthrough.

### The C# SDK

- `Sdk.NSwag` holds seven typed clients (`IAccessApiClient`, `IAnnotationsApiClient`, `IConfigurationsApiClient`, `ISchemasApiClient`, `IKeyValuesApiClient`, `ITemplatesApiClient`, `ITransfersApiClient`) generated from `open.api.v0.9.50.json`. `AddStorytellerSdk` (`Sdk.NSwag/src/ServicesCollectionExtensions.cs`) registers them with `AddHttpClient<TInterface, TImplementation>()` (transient typed clients) and registers `ISdkConfiguration` with `TryAddSingleton`, so a registration made before wins.
- `ISdkConfiguration` (`Sdk.NSwag/src/SdkConfiguration.cs`) has `BaseUrl`, a synchronous `Func<string>? AccessTokenFactory` and an optional client certificate. Every request calls the factory once (`ApiSdk.partial.cs`, `SdkRequestHelper.ApplyAuthorization`) and sends `Bearer {token}`; an empty token sends no header. The client copies `BaseUrl` in its constructor; the base URL includes `/api`.
- The SDK serializes with Newtonsoft.Json. Free-form JSON (configurations, schemas, templates, compositions, annotation `Values`) is typed `object` and arrives as `JObject`/`JArray`. A `System.Text.Json` `JsonElement` passed as a body would be written as `{"ValueKind":1}`; it has to be converted with `JToken.Parse(element.GetRawText())`.
- `Annotation` is polymorphic (discriminator `AnnotationType`); a write needs the concrete subtype and every required member (`ProjectName`, `ViewName`, `AnnotationKey`, `Name` and the type-specific keys and names). `sform` builds the subtype in `src/Platform/Cli/src/Model/AnnotationExtensions.cs`.
- `Abstractions.Annotations` declares records with the same names as SDK classes (`Annotation`, `Configuration`, `KeyValue`, …) in `_42.Platform.Storyteller`, which encloses any `_42.Platform.Storyteller.*` namespace; `sform` uses aliases for that reason (`AnnotationExtensions.cs:3-4`).
- Errors are `ApiException` (status, raw response, headers) or `ApiException<ErrorResponse>` (`Message`, `Hint`, `ErrorCode`, `Details`, `Error`) and, for configuration and schema writes, `ApiException<SchemaValidationErrorResponse>` (`Errors` per annotation and view). `ApiException.Message` holds up to 512 characters of the response and `ToString()` all of it. On a `500` the API puts the raw exception message into `Message` (`Api.Functions/src/ErrorHandling/ExceptionHandlingMiddleware.cs:56-57`) and can add a stack trace in `Error`; a `422 binding.evaluation` message can quote an evaluated operand (`Binding.Language/src/BindingEvaluator.cs:91`).
- `sform` wires the SDK with a singleton `SdkConfiguration` whose token factory blocks on an async call (`src/Platform/Cli/src/Startup.cs:111-146`) and sets the global `JsonConvert.DefaultSettings`. Both suit one process for one person; neither suits a server for many users.

### Configurations, compositions, bindings and key/values

- The plain read (`GET …/configuration/{key}`) returns the effective document (ancestors, the view template of the type and the own content, merged) with bindings unevaluated, and its `Version` is the version of the annotation's own stored document (0 when nothing is stored). The own document is read with `GET …/versions/{Version}` (`src/Platform/ui.admin/src/app/composables/useConfiguration.ts`).
- A write (`POST`/`PUT`) merges into the stored own document (objects merge, arrays union, `null` never erases; `$remove` takes JSONPaths, `$patch` RFC 6902 operations); `PATCH` applies RFC 6902 to the stored own document. A write on a missing annotation stores a configuration without creating the annotation; it is inherited but not listed (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:375-421`). A write on a deleted annotation answers `409 AnnotationDeleted`. Only annotation writes create missing ancestors. `sform` replaces a document with a JSON Patch computed from the stored and the desired document (`src/Platform/Cli/src/Services/StorytellerApiExtensions.cs:68-90`).
- `GET …/configuration/{key}/composition` (`GetConfigurationCompositionAsync`) returns `ConfigurationComposition`: `Content` (the effective document), `Hash`, `Sources` (`Id` `configuration:{key}` or `template:{typeCode}`, `Kind`, `AnnotationKey`, `Presence`, `Version`, `Content`), `Nodes` (`Index`, `AnnotationKey`, `MergedInto`, `Depth`), `Steps` (`Order`, `SourceId`, `Node`) and `Origins` (`Path` as an RFC 6901 pointer, `Step` as the `Order` of the step that set it). Bindings are not evaluated. A shared ancestor is merged once per branch, so on an execution the subject arrives through the usage and again through the context, after it (`docs/Platform/Storyteller/inheritance.md`, the `currency` example and "Replaying"). `sform story config composition` renders it (`src/Platform/Cli/src/Output/CompositionLines.cs`).
- A template is merged at every node of its type, after the node's ancestors and before the node's own content: it overrides every ancestor and loses only to the node's own content (`docs/Platform/Storyteller/templating.md`, "Where the template sits").
- `…/resolved` evaluates bindings. A string that starts with `@` or an object with a string `$binding` makes the server evaluate (`Binding.Object/src/ConfigurationBindingResolver.cs:62-90`). A resolved read fills in secret bindings when the caller has secret rights (`ContributorWithSecrets`) and the API records a `BindingRead` audit event for each secret it reads (`docs/Platform/Storyteller/key-values.md`).
- A key/value read returns a secret only with `reveal=true` and secret rights; listings never return secrets; a secret version or diff returns plaintext with secret rights alone, without `reveal` (`key-values.md:98`, `Sdk.NSwag/src/ApiSdk.g.cs:17183`); writes take `ExpectedVersion` (412 `KeyValueVersionMismatch`). Hyok values are JWE documents the server cannot read.
- No part of the platform records which application reads a configuration at runtime. The only data that links a runtime consumer to a configuration is the annotation restriction of a machine (`MachineAccess.AnnotationKey`, which reaches that annotation and its descendants, `Api.Functions/src/Security/MachineAnnotationRestriction.cs`) and its scopes.
- No endpoint suggests where a value belongs. The guidance is prose: the "Where a value belongs" table in `docs/42for.net/platform/modeling.md` (no row for units) and the merge order in `inheritance.md`.
- Lists page up to 1,000 items (`Backend.CosmosDb/src/CosmosConstants.cs:9`); an annotation carries up to 10,000 characters of description.

### Hosting

- `src/Platform/Aspire.Host/src/AppHost.cs` runs `api-functions` and passes its `http` endpoint with `/api` to the admin UI (`:68`). `db-creator` uses `WithExplicitStart()` (`:40`). `:19` suppresses an experimental Aspire diagnostic with `#pragma warning disable`.
- `Aspire.ServiceDefaults` (`AddServiceDefaults`, `MapDefaultEndpoints`) is used by no Platform host yet. It adds OpenTelemetry, health checks, service discovery and the standard resilience handler to every `HttpClient`; that handler retries `POST`, `PUT` and `DELETE` too. The catalog pins `Microsoft.Extensions.Http.Resilience` 10.0.5, which does not exist; restore resolves 10.1.0, where `RemoveAllResilienceHandlers` is `[Experimental("EXTEXP0001")]`.
- `Api.Web` is a stale scaffold. It turns on `IdentityModelEventSource.ShowPII` and `LogCompleteSecurityArtifact` unconditionally and holds a client secret in source. It is not a model.
- The API refuses plain HTTP outside Development (`Api.Functions/src/Security/TransportSecurityMiddleware.cs`); Development allows it, which is how the admin UI calls the local API.
- No MCP code and no `ModelContextProtocol` package exist in the repository.

### The MCP C# SDK 2.3.0 and Entra ID

- `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)` and `MapMcp(pattern)`. Stateless is the default in 2.x; it maps only `POST {pattern}`, needs no session affinity and runs every tool in the HTTP request (`HttpContext.RequestServices`, `RequestAborted`).
- Tools are methods with `[McpServerTool]` (`Name`, `Title`, `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`) and `[Description]`. `Destructive` and `OpenWorld` default to true, so a write that is not destructive must say so. `CancellationToken` and services are injected; other parameters come from the JSON arguments.
- Call-tool filters (`WithRequestFilters(f => f.AddCallToolFilter(next => …))`) wrap the tool invocation, so an exception of a tool passes through them. An exception that leaves the filters becomes a generic `isError` result ("An error occurred invoking '{tool}'."), and the SDK logs it at `Error`. A filter sees `Services` and `User`, not the `HttpContext`.
- `AddAuthentication().AddMcp(…)` can serve RFC 9728 metadata and challenge with it, but builds the URLs from the request's scheme and host.
- The SDK logs whole JSON-RPC messages, tool arguments and results included, at `Trace` only.
- Entra's v2 metadata has no `code_challenge_methods_supported` and no `registration_endpoint`. The MCP authorization specification makes clients refuse an authorization server without the first (the SDK's own client throws, `ModelContextProtocol.Core/Authentication/ClientOAuthProvider.cs:596-611`), and Entra has no dynamic client registration.
- MCP clients send the RFC 8707 `resource` parameter (the metadata `resource`); Entra v2 refuses a `resource` that is not the resource of the requested scope (`AADSTS9010010`).

## Proposed Changes

### 1. Delegated clients in the API (`Backend.Core`, `Access.AzureAd`)

The API learns one explicit trust: a configured list of client apps, each in one tenant, whose delegated tokens are tokens of the signed-in user.

- `UserAuthenticationOptions` gets `public List<DelegatedClientOptions> DelegatedClients { get; set; } = [];` with `DelegatedClientOptions { string ClientId; string TenantId; }` (config `Auth:DelegatedClients:0:ClientId`, `Auth:DelegatedClients:0:TenantId`). The default is empty, so nothing changes until a deployment lists a client.
- `UserAuthenticationOptionsValidator` rejects, for Entra ID, an entry whose `ClientId` or `TenantId` is not a GUID, and an entry whose `ClientId` equals `Auth:ClientId` ("Auth:DelegatedClients lists client apps other than Auth:ClientId."). For AuthKit a non-empty list fails the start ("Auth:DelegatedClients applies to Entra ID only."), so nobody believes it does something there.
- `EntraIdBearerTokenValidator.TryGetMachineId(claims, clientId, delegatedClients)` returns no machine id when **all** hold:
  - the `azp` (or v1 `appid`) equals a listed `ClientId` (case-insensitive);
  - the `tid` (raw or mapped) equals that entry's `TenantId` (case-insensitive);
  - the token is delegated: it carries a non-blank scope claim (`scp` or the mapped `…/identity/claims/scope`) and its `idtyp` is not `app`.

  Any other token is classified as today. An app-only token of the same app (client credentials, `roles` and no `scp`) stays a machine; so does a delegated token of the listed app from another tenant. The old two-argument overload stays and passes an empty list.
- The Entra validator and `EntraIdClaimNormalizer` read the list from the options and pass it, so the validated and the decoded paths agree.
- Nothing else changes: issuer, audience, signature and lifetime checks, machine scopes, `EnsureUser` and the role checks stay as they are. A delegated token from a listed client is a user: it needs `User.Impersonation`, its `sub` is looked up as an account, the role decides, and the audit actor is the account.
- The MCP app must be single-tenant and a confidential client only (no public-client flows, no SPA or mobile redirect URIs), so the only delegated tokens it can obtain for the API are OBO tokens of the MCP server. `docs/Platform/Storyteller/mcp.md` says so.

**Why an allow-list.** Three other ways were weighed. Registering the MCP app as a machine in each project would authorize the app, not the person, and lose roles and audit. Treating every token with `scp` as a user would let any app that obtains a delegated token for the API act as a user without the operator's say; today such tokens are machines and are refused. Forwarding the MCP token to the API is token passthrough, which the MCP authorization specification forbids. The allow-list adds one named relationship and keeps every current classification.

**Identity.** The account id is `sub`, and Entra documents `sub` as pairwise ("unique to a particular application ID"). The documentation does not say whether, for an access token, the application is the resource or the client. If it is the client, the OBO token of a person carries a different `sub` than the person's `sform` token and does not find the account. This spec does not change the identity mapping; it makes the outcome visible early:

- the integration test of section 13 compares the `sub` of an API token from `sform` with the `sub` of an OBO token for the same person, when the environment provides both. It ships in Phase 2, before the tools, and `mcp.md` makes it a step of the setup;
- the MCP tool `get_my_account` answers a missing account with a hint that names this cause;
- if the values differ in a tenant, the MCP server cannot act for existing accounts there. The fix is a separate issue (accounts found by `tid` and `oid` for listed delegated clients), not a change hidden in this one.

### 2. API tests (`Access.AzureAd/test`, `Api.Functions/test`)

- `EntraIdBearerTokenValidatorTests`: a listed `azp` with `scp` and the listed `tid` is a user; the same from another `tid` is a machine; a listed `azp` with only `roles` is a machine; a listed `azp` with `scp` and `idtyp=app` is a machine; an unlisted `azp` with `scp` is a machine; a listed v1 `appid` is a user; the match ignores case; an empty list gives the current results.
- `EntraIdClaimNormalizerTests`: the same rule on raw claims (`scp` and `tid` unmapped).
- `UserAuthenticationOptionsTests`: binding of `Auth:DelegatedClients:0:ClientId` and `:TenantId`; the validator rejects a non-GUID id, an entry equal to `ClientId`, and a list under AuthKit.
- `BearerAuthenticationMiddlewareTests`: on the decode path, a token whose `azp` and `tid` are listed and that has `scp` is stored as a user (no `MachineIdentity` item).
- `MachineAccessCheckTests` (or the user-role tests next to them): a request whose cached claims come from a listed delegated client passes `EnsureUser` and the stored role check, and is refused with `403` when the role is too low. Debug builds skip user role checks, so these run in Release (section 15).

### 3. The MCP host project (`Mcp/src`)

A new ASP.NET Core project `src/Platform/Storyteller/Mcp/src/Mcp.csproj`:

- `Microsoft.NET.Sdk.Web`, `net10.0`, nullable, `ImplicitUsings`, `InvariantGlobalization`, a `UserSecretsId`; assembly `42.Platform.Storyteller.Mcp`, root namespace `_42.Platform.Storyteller.Mcp`; `InternalsVisibleTo` the unit and integration test assemblies. Not packable (the Web SDK default).
- Packages: `ModelContextProtocol.AspNetCore` (new in `Directory.Packages.props`, 2.3.0), `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Identity.Client`, `Microsoft.Identity.Web.TokenCache` (new, 4.5.0, the version of the other `Microsoft.Identity.Web` entries; if restore reports its `System.Security.Cryptography.Pkcs` 10.0.0 as vulnerable, the catalog pins `System.Security.Cryptography.Pkcs` 10.0.12 and the project references it), `NJsonSchema` (section 10). No versions in the project file.
- Project references: `Sdk.NSwag`, `Abstractions.Annotations` (`AnnotationKey`, its extensions and the type codes), `src/Platform/Aspire.ServiceDefaults`.
- `GlobalUsings.cs` has `global using Sdk = _42.Platform.Storyteller.Sdk;`; SDK types are written `Sdk.Annotation` and so on, because the records of `Abstractions.Annotations` would shadow them.
- Added to `42.mono.slnx` in `/Platform/Storyteller/`, with the test projects. The traversal build picks it up without changes.
- `Properties/launchSettings.json` has an `applicationUrl` with an `https` and an `http` port, so Aspire creates both endpoints.
- `appsettings.json` holds every setting with empty values and `"Logging:LogLevel:ModelContextProtocol": "Information"`. No secrets in tracked files; local values go to user secrets.

`Program.cs`, in order:

1. `builder.AddServiceDefaults()`.
2. Options (section 4), validated on start.
3. Authentication, authorization and the OAuth metadata (section 4).
4. The downstream token provider and the SDK (section 5).
5. `AddMcpServer(o => { o.ServerInfo = { Name = "2s-platform", Title = "2S Platform", Version = <assembly informational version> }; o.ServerInstructions = McpInstructions.Text; })`, `.WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)`, `.WithRequestFilters(f => f.AddCallToolFilter(StorytellerCallFilter.Create))`, and `.WithTools<T>()` for each tool class (explicit, no assembly scan). `SecretTools` is registered only when `Mcp:SecretTools:Enabled` is true (section 9).
6. `app.UseAuthentication()`, `app.UseAuthorization()`, the metadata endpoints, `app.MapMcp("/mcp").RequireAuthorization(McpPolicies.User)`, `app.MapDefaultEndpoints()`.
7. Outside Development the logging rules are clamped (section 9).

No `DEV_AUTH`, no decode-only validation, no `ShowPII` outside Development, no Swagger and no sessions. `public partial class Program;` makes the host reachable for `WebApplicationFactory`.

**Server instructions** (`McpInstructions.Text`), short and factual:

- Everything is addressed by organization, project and view; `list_projects` gives `organization.project` keys, `view` defaults to `default`.
- Annotation keys are `{typeCode}.{name}[.{name}…]`: `rst.{responsibility}`, `sbt.{subject}`, `unt.{responsibility}.{unit}`, `usg.{subject}.{responsibility}`, `cnt.{subject}.{context}`, `exe.{subject}.{responsibility}.{context}`, `uxe.{subject}.{responsibility}.{context}.{unit}`.
- Configuration inherits along the annotation hierarchy and through view templates; `explain_configuration` shows where each value comes from. Writes address the annotation's own stored document, not the effective one.
- Lists return pages; pass `continuationToken` from the result to get the next page.
- Every call acts as the signed-in person; the API's roles decide.
- Ask the person before a destructive call and before any tool that returns secrets. Credentials returned by machine creation or reset are shown once; tell the person to copy them from the tool result rather than retyping them.

### 4. Inbound authentication, authorization and OAuth metadata

**Options.** `McpHostOptions`, section `Mcp`, validated on start (`IValidateOptions`):

| Key | Meaning |
| --- | --- |
| `Mcp:Provider` | `EntraId` (default) or `AuthKit` |
| `Mcp:ResourceUrl` | The public URL of the MCP endpoint, for example `https://mcp.example.com/mcp`, without a trailing slash; the `resource` of the metadata |
| `Mcp:EntraId:TenantId` | The tenant, a GUID (not `common` or `organizations`); normalized to lower case |
| `Mcp:EntraId:ClientId` | The MCP app registration |
| `Mcp:EntraId:Audience` | The App ID URI of the MCP app; default `Mcp:ResourceUrl` |
| `Mcp:EntraId:Scope` | The delegated scope the MCP app exposes; default `Mcp.Access` |
| `Mcp:EntraId:AllowedClientIds` | The MCP client apps (GUIDs) whose tokens the server accepts; required, at least one |
| `Mcp:EntraId:ClientSecret`, `Mcp:EntraId:CertificatePath`, `Mcp:EntraId:CertificatePassword`, `Mcp:EntraId:UseManagedIdentity`, `Mcp:EntraId:ManagedIdentityClientId` | The credential of the MCP app for OBO; exactly one of secret, certificate or managed identity |
| `Mcp:SecretTools:Enabled` | Registers the tools that return secrets (section 9); default false |
| `Storyteller:BaseUrl` | The API base URL with `/api` |
| `Storyteller:Scopes` | The delegated scopes requested by OBO, for example `api://{ApiClientId}/User.Impersonation` |

The validator fails the start when: `Provider` is `AuthKit` ("The MCP server signs in with Entra ID only: AuthKit issues no token for the Storyteller API on behalf of a user. See docs/Platform/Storyteller/mcp.md."); the tenant or a client id is not a GUID; `AllowedClientIds` is empty; no or more than one credential is set; `ResourceUrl` or `Storyteller:BaseUrl` is not absolute, ends with `/`, or is not HTTPS outside Development; `Storyteller:Scopes` is empty.

**Bearer validation.** `AddAuthentication` with JwtBearer as the default scheme for authenticate, challenge and forbid, then `.AddJwtBearer`:

- `MetadataAddress = https://login.microsoftonline.com/{TenantId}/v2.0/.well-known/openid-configuration` (signing keys).
- `ValidIssuers` exactly `https://login.microsoftonline.com/{TenantId}/v2.0` and `https://sts.windows.net/{TenantId}/`; `ValidAudiences` `{ClientId}` and `{Audience}`; `ValidAlgorithms` `RS256`; lifetime checked.
- `MapInboundClaims = false`, so the claims keep their names (`scp`, `tid`, `azp`, `appid`, `idtyp`, `roles`, `sub`, `name`); `NameClaimType = "name"`.
- `SaveToken = true`; the call filter reads the token through `IHttpContextAccessor` (registered explicitly) with `GetTokenAsync("access_token")`.
- `OnTokenValidated` runs `McpTokenRules.Check`: `tid` must equal `TenantId`; `azp` (or `appid`) must be one of `AllowedClientIds`; the token must be delegated (a non-blank `scp` and no `idtyp=app`). Otherwise `context.Fail(…)`, which answers `401`. App-only tokens and tokens of other client apps never reach a tool.
- `OnChallenge` answers `401` with `WWW-Authenticate: Bearer resource_metadata="{origin}/.well-known/oauth-protected-resource{path}"` (plus `error="invalid_token"` when a token was presented), where origin and path come from `Mcp:ResourceUrl`, never from the request's `Host`.
- `OnForbidden` answers `403` with `WWW-Authenticate: Bearer error="insufficient_scope", scope="{Audience}/{Scope}", resource_metadata="…"`.

Plain `JwtBearer` with explicit parameters is used instead of `Microsoft.Identity.Web`'s `AddMicrosoftIdentityWebApi`: the pinned issuers are visible in one place, and tests replace the signing keys without network access (section 13).

**Authorization.** The policy `McpPolicies.User` requires an authenticated user whose `scp` contains `Mcp:EntraId:Scope`. A missing scope fails authorization and gets the `403` above.

**Protected resource metadata (RFC 9728).** The host serves the document itself at `/.well-known/oauth-protected-resource{path of ResourceUrl}` (for `…/mcp`: `/.well-known/oauth-protected-resource/mcp`) and at `/.well-known/oauth-protected-resource`, built from the options with the SDK's `ProtectedResourceMetadata` type: `resource = ResourceUrl`, `authorization_servers = [{origin of ResourceUrl}]`, `scopes_supported = ["{Audience}/{Scope}"]`, `bearer_methods_supported = ["header"]`, `resource_name = "2S Platform"`, `resource_documentation` (the GitHub URL of `docs/Platform/Storyteller/mcp.md`). Serving it from the options keeps every URL right behind a TLS-terminating proxy.

**Authorization server metadata (RFC 8414).** The authorization server named in the metadata is the MCP host's origin, which serves `/.well-known/oauth-authorization-server`: `issuer` = the origin, `authorization_endpoint` and `token_endpoint` = the Entra v2 endpoints of the tenant, `code_challenge_methods_supported = ["S256"]`, `response_types_supported = ["code"]`, `grant_types_supported = ["authorization_code", "refresh_token"]`, `token_endpoint_auth_methods_supported = ["none"]`, `scopes_supported = ["{Audience}/{Scope}", "offline_access"]`. Entra issues the tokens; the document only describes its endpoints with the facts MCP clients need (Entra supports PKCE but does not publish it). There is no registration endpoint: clients are configured with a pre-registered client id, listed in `AllowedClientIds`.

**Resource and audience.** Entra accepts the `resource` parameter only when it is the App ID URI of the scope's app, so the MCP app's App ID URI is `Mcp:ResourceUrl` (an HTTPS URI on a verified domain of the tenant) and `Audience` defaults to it. A deployment that cannot use such an App ID URI (a local machine) sets `Audience` to `api://{ClientId}` and uses a client that does not send `resource`; `mcp.md` describes both.

**AuthKit.** Refused at start, as above. Accepting AuthKit-domain tokens in the API, or a Storyteller user-token exchange, is a separate decision (out of scope).

### 5. On-behalf-of and the SDK clients (`Mcp/src/Authentication`)

- `IStorytellerTokenProvider.GetTokenAsync(string userToken, CancellationToken)` returns an access token for `Storyteller:Scopes`.
- `EntraOnBehalfOfTokenProvider` holds one `IConfidentialClientApplication` (singleton), built with `ConfidentialClientApplicationBuilder.Create(ClientId).WithAuthority("https://login.microsoftonline.com/{TenantId}/")` and the configured credential: `WithClientSecret`, `WithCertificate` (a PKCS#12 file), or `WithClientAssertion` from a managed identity token for `api://AzureADTokenExchange` (a federated credential, no secret). `AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken)).ExecuteAsync(cancellationToken)` looks in the cache first.
- The token cache is `AddInMemoryTokenCache()` from `Microsoft.Identity.Web.TokenCache`, partitioned by assertion, with `MsalMemoryTokenCacheOptions.AbsoluteExpirationRelativeToNow = 2 hours` and `MemoryCacheOptions.SizeLimit = 100 MB`. Its defaults (a 14-day sliding expiration, no size limit) would keep tokens long after the hourly inbound token is gone.
- MSAL's `MsalUiRequiredException` becomes `StorytellerSignInRequiredException` with a fixed message: for consent, "The Storyteller API has not been granted to the MCP app for you. An administrator has to grant consent (or pre-authorize the MCP app on the Storyteller API registration)."; when the exception carries a claims challenge (Conditional Access, MFA), "The Storyteller API asks for an extra sign-in step that the MCP server cannot pass on. An administrator has to apply the same Conditional Access policy to the MCP app." Other MSAL failures become `StorytellerSignInFailedException` with the MSAL error code, without the token.
- `StorytellerAccessToken` is a scoped holder (`string? Value`).
- `StorytellerCallFilter` (a call-tool filter, the only one) does three things for every `tools/call`:
  1. reads the inbound token, calls the provider and stores the result in the request's `StorytellerAccessToken`;
  2. calls the tool;
  3. maps exceptions to tool results (section 6) and logs one line per failure with the tool name, status and error code. It never passes an exception object, an `ApiException.Message` or a body to a logger.

  The filter runs before the tool, so a sign-in problem never reaches the SDK clients.
- `ISdkConfiguration` is registered **scoped** before `AddStorytellerSdk`: `BaseUrl` from `Storyteller:BaseUrl`, `AccessTokenFactory = () => holder.Value ?? string.Empty` over the scoped holder of the same request. The typed clients are transient and resolved from the request scope, so each request sends its own token. The inbound MCP token is never sent to the API.
- The SDK clients keep the standard resilience handler of the service defaults but without retries for `POST`, `PUT`, `PATCH` and `DELETE`: `AddStorytellerSdk(…, b => { b.RemoveAllResilienceHandlers(); b.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods()); })`, with a local `#pragma warning disable EXTEXP0001` and a comment, as `AppHost.cs` does for its experimental call. A retried machine creation would create a second machine and hide the first secret.
- No global `JsonConvert.DefaultSettings`. Tools set every required member of the bodies they send (section 7).

### 6. Results, pages and errors (`Mcp/src/Tools`)

- **Results.** `ToolResults.Json(value)` serializes an SDK value with Newtonsoft (`NullValueHandling.Ignore`, no indentation) into one text block, and sets `StructuredContent` to the same JSON as a `JsonElement`. An array is wrapped as `{"Items": […]}`, because structured content is an object. Property names stay PascalCase, as in the API and its docs.
- **Pages.** `HostPage` pages API lists in the host: a tool takes `limit` (default 50, at most 200) and an opaque `continuationToken` (base64 of the API continuation token and an offset into that API page). The result has `Items`, `ContinuationToken` (null on the last page) and `Count`. Annotation lists return a compact projection (`AnnotationKey`, `AnnotationType`, `Name`, `Title`, `Status`, `Labels`, `Modified`); `get_annotation` returns the whole annotation.
- **Arguments.** Documents arrive as `JsonElement` and are converted with `JToken.Parse(element.GetRawText())`. A configuration, template or schema must be a JSON object; a patch must be an array.
- **API errors.** `ApiErrors.ToResult(ApiException)` returns `IsError = true` with the text `Storyteller API answered {status} {ErrorCode}: {Message} Hint: {Hint}` and the structured content `{Status, ErrorCode, Message, Hint, Errors}`:
  - `ApiException<ErrorResponse>` with a status below 500 except 422: its `Message`, `Hint` and `ErrorCode`. `Error` (an exception wrapper with a stack trace) and `Details` are never copied.
  - `ApiException<SchemaValidationErrorResponse>`: also `Errors` (annotation key, view, messages).
  - `422` (`binding.*`): the error code and a fixed message ("A binding could not be evaluated."), because the API's message can quote an evaluated secret.
  - `500` and above: the status, the error code when there is one, and a fixed message ("The Storyteller API failed. Try again later."); the API's message can be a raw storage or vault message.
  - Plain `ApiException`: the status and a fixed message per status. `401` names the likely cause: the API does not accept the delegated token ("Check that the API lists the MCP app and tenant in Auth:DelegatedClients and that the token carries User.Impersonation."). `404` says the resource does not exist or is not visible to the person. The raw response text is never copied.
- **Sign-in errors** become `IsError` results with the fixed messages of section 5.
- **Invalid input** found by a tool (`ToolInputException`: a bad key, a document that is not an object, a missing parameter, a deleted annotation in the way) becomes an `IsError` result with the tool's message, which names the next call when there is one.
- **Anything else** is left to the SDK, which answers with its generic error and logs the exception. Exceptions thrown by this code never carry secrets or tokens.
- Cancellation propagates.

### 7. Management tools (`Mcp/src/Tools`)

Every tool is a method of a tool class, named explicitly in `snake_case`, with a `[Description]` on the method and on each parameter. Project tools take `organization` and `project`; view tools also take `view`, defaulting to `default`. The flags are set explicitly on every tool:

- `ReadOnly = true` for reads that return no secret;
- `Destructive = true` for deletes, purges, resets and replacements of a value without version history (federation rules, authentication and key/value policies); `Destructive = false` for every other write, including writes of documents that keep versions;
- `Idempotent = true` for sets of a whole value and for deletes;
- `OpenWorld = false` for all.

Descriptions state the role a call needs, what it changes and the limits named below; they do not promise what the API does not do. A new annotation name follows the `sform` rule (not empty, at most 50 characters, letters, digits, `-`, `_`); existing keys are addressed as they are.

Discovery (`DiscoveryTools`):

| Tool | API | Notes |
| --- | --- | --- |
| `get_my_account` | `GetAccountAsync` | `404` adds a hint: no account for this sign-in; if the person has one through `sform` or the admin UI, the `sub` of the MCP sign-in may differ (section 1) |
| `list_projects` | `GetProjectsAsync` | Key, role and description |
| `list_views` | `GetViewsAsync(discover: null)` | |

Annotations (`AnnotationTools`):

| Tool | API | Notes |
| --- | --- | --- |
| `list_annotations` | `GetAnnotationsAsync` | Paged; `includeInactive`, `includeDeleted` |
| `get_annotation` | `GetAnnotationAsync` | The whole annotation |
| `find_annotations` | `GetResponsibilitiesAsync` … `GetUnitsOfExecutionAsync` by `annotationType` | Paged; name filters per type; exact names, or a pattern that starts with `^` or ends with `$` (a regular expression on the server) |
| `list_descendants` | `GetDescendantsAsync` | Paged; `descendants`: `usages`, `contexts`, `executions`, `units`, `units-of-execution`, `all` |
| `set_annotation` | `FindDeletedAnnotationsAsync`, `GetAnnotationAsync`, then `SetAnnotationAsync` | See below |
| `create_annotations` | `FindDeletedAnnotationsAsync`, then `SetAnnotationsSimpleAsync` | Up to 100 keys or compact lines (1 to 4 names: `sbt`, `usg`, `exe`, `uxe`); existing keys are skipped; same deleted-key rule as `set_annotation` |
| `delete_annotation` | `DeleteAnnotationAsync` | Destructive. Soft delete with all descendants; returns `DeletionId`, `DeletedKeys`, `PurgeAt` |
| `list_deleted_annotations` | `GetDeletedAnnotationsAsync` | Paged; `roots`, `deletionId` |
| `restore_annotations` | `RestoreAnnotationsAsync` | Up to 100 keys, `descendants` default true; can succeed partly (`Failed`) |
| `purge_annotation` | `PurgeAnnotationAsync` | Destructive and permanent; Administrator |
| `get_annotation_history` | `GetAnnotationHistoryAsync` | |

`set_annotation` creates or updates one annotation:

- It builds the SDK subtype from the key and fills `ProjectName`, `ViewName`, `AnnotationKey`, `Name` and the type-specific keys and names from it.
- Optional fields: `title`, `description`, `documentationLink`, `labels`, `values` (replaces the whole set), `isDisabled`, `validFrom`, `expiresAt`, `timeZone`, and for units `unitType` and `unitDefinition` (required on create). Fields left out keep their stored values; `clearFields` names fields to remove.
- Before a create it looks up the key and its ancestors among the deleted annotations. When some are deleted, it answers an input error that lists them and names `restore_annotations`, because a create would re-create them silently and leave their descendants deleted. `recreateDeleted: true` skips the check.
- Missing ancestors are created by the API.

Configurations (`ConfigurationTools`):

| Tool | API | Notes |
| --- | --- | --- |
| `list_configurations` | `GetConfigurationsAsync` | Paged; `annotationType`, `keyPrefix`; summaries only |
| `get_configuration` | `GetConfigurationAsync`; `GetConfigurationVersionAsync` | `form`: `effective` (default; ancestors and templates merged, bindings not evaluated), `own` (the stored document of this annotation, read with the effective `Version`; version 0 means nothing is stored), or a stored `version` |
| `set_configuration` | `GetAnnotationAsync`, then `SetConfigurationAsync` | Merges `content` into the stored document: objects merge, arrays union, `null` never erases; `$remove` (JSONPaths) and `$patch` inside the content remove values. `force` (default false) stores content that violates the schema |
| `replace_configuration` | `GetAnnotationAsync`, the own document, then `PatchConfigurationAsync` | Makes the stored document equal `content`: a JSON Patch from the stored to the desired document, with `test` operations on the stored values, so a concurrent change fails with `412` |
| `revert_configuration` | the stored `version`, then as `replace_configuration` | Makes the stored document equal a previous version (a new version) |
| `patch_configuration` | `PatchConfigurationAsync` | RFC 6902 operations on the stored document (pointers of `get_configuration` with `form: own`). A `test` operation is the only write guard; there is no version guard; a failed `test` is `412 PatchTestFailed` |
| `delete_configuration` | `DeleteConfigurationAsync` | Destructive; clears the stored document of the annotation |
| `list_configuration_versions` | `GetConfigurationVersionsAsync` | |
| `diff_configuration` | `GetConfigurationVersionDiffAsync`, `…DiffCustomAsync`, `GetConfigurationViewDiffAsync` | `version` (with optional `versionFrom`), or `viewTo` for the same key in another view; JSON format |

- The writes first check that the annotation exists (`GetAnnotationAsync` with `includeInactive`); for a missing one they answer an input error that names `set_annotation`, because a configuration without an annotation is inherited but not listed. A deleted annotation is refused by the API (`409 AnnotationDeleted`).
- `replace_configuration` and `replace_template` build the patch with `JsonPatchBuilder` (`Mcp/src/Json`): for each top-level property, `test` the stored value, then `remove` it or `replace` it, and `add` new properties. Top-level operations keep the patch small and exact.
- The descriptions say that writes address the stored document, not the effective one: writing back an edited effective document would copy every inherited value onto this annotation.

Schemas (`SchemaTools`). `level` is `type` (needs `annotationType`), `annotation` (needs `key`) or `descendantType` (needs `key` and `annotationType`):

| Tool | API |
| --- | --- |
| `list_schemas` | `GetConfigurationSchemasAsync` |
| `get_schema` | `Get{Configuration|Annotation|DescendantType}SchemaAsync`, or the `…VersionAsync` with `version` |
| `set_schema` | `Set…SchemaAsync`; replaces the schema (versions kept); fails `409` with the violating configurations unless `force` is true |
| `delete_schema` | `Delete…SchemaAsync`; destructive |
| `get_combined_schema` | `GetCombinedConfigurationSchemaAsync`: the schema a write of `key` is checked against |
| `list_schema_versions` | `Get…SchemaVersionsAsync` |
| `diff_schema` | `Get…SchemaVersionDiffAsync`, `…DiffCustomAsync` |

Templates (`TemplateTools`), by type code (`rst`, `unt`, `sbt`, `usg`, `cnt`, `exe`, `uxe`):

| Tool | API |
| --- | --- |
| `list_templates` | `GetTemplatesAsync` |
| `get_template` | `GetTemplateAsync`, or `GetTemplateVersionAsync` with `version` |
| `set_template` | `SetTemplateAsync`; merges like a configuration write |
| `replace_template` | `GetTemplateAsync`, then `PatchTemplateAsync` with the patch of `JsonPatchBuilder` |
| `patch_template` | `PatchTemplateAsync` |
| `delete_template` | `DeleteTemplateAsync`; destructive |
| `list_template_versions` | `GetTemplateVersionsAsync` |
| `diff_template` | `GetTemplateVersionDiffAsync`, `…DiffCustomAsync` |

The template descriptions say that a template is merged at every node of its type, after the ancestors and before the node's own content (it overrides every ancestor and loses only to the node's own content), appears as `template:{typeCode}` in compositions, and is not checked against schemas.

### 8. Machine tools (`Mcp/src/Tools/MachineTools.cs`)

| Tool | API | Notes |
| --- | --- | --- |
| `list_machines` | `GetMachineAccessesAsync` | Keys are masked by the API |
| `get_machine` | `GetMachineAccessAsync` | |
| `create_machine` | `CreateMachineAccessAsync` | `scopes` (the names of `MachineScope`, checked by the tool against the list, which the description gives), `annotationKey`, `credentialKind`, `certificateLifetimeDays`, `useSharedCertificate`, `federationRules`. The tool fills `Organization` and `Project` in the body. Not read-only, not destructive |
| `reset_machine` | `ResetMachineAccessAsync` | Destructive: the old key stops working. A `Federated` machine has no secret (`400`) |
| `delete_machine` | `DeleteMachineAccessAsync` | Destructive |
| `set_machine_federation_rules` | `SetMachineFederationRulesAsync` | Destructive (replaces the rules); 1 to 10 rules |
| `get_machine_authentication_policy` | `GetMachineAuthenticationAsync` | Key `organization.project` |
| `set_machine_authentication_policy` | `SetMachineAuthenticationAsync` | Destructive (replaces the policy); Administrator |

- Create returns the one-time credentials of its kind: `AccessKey` (an API key or a client secret), `Certificate` and `CertificatePassword` (certificate kinds without a shared certificate), and `TokenEndpoint` and `TokenScope` (client credentials). Reset returns only the new `AccessKey`. The result adds `CredentialNotice`, naming what it holds: "The API stores no copy of {these credentials}. Copy them from this result now; they cannot be read again." The text block puts the notice first.
- The `create_machine` description recommends the `Federated` kind for workloads that have their own identity: it returns no secret.
- The descriptions name the roles: `Contributor`; `ContributorWithSecrets` when the machine has a `Secrets.*` scope; `Administrator` when it has a management scope.
- Not exposed: `ExchangeMachineTokenAsync` (a machine flow), certificate renewal (a machine over mTLS; a user gets `401`), shared certificates and trusted issuers (Administrator configuration with PKCS#12 secrets), invitations and members.

### 9. Values store tools and secrets (`Mcp/src/Tools/KeyValueTools.cs`, `SecretTools.cs`)

| Tool | API | Notes |
| --- | --- | --- |
| `list_key_values` | `GetKeyValuesAsync` | Paged; `keyPrefix`, `protection`, `state`, `includeValues` (public values only) |
| `get_key_value` | `GetKeyValueAsync(reveal: false)`; `GetKeyValueVersionAsync(reveal: false)` | Never a secret value; `includeDeleted`, `version`. A Hyok value is returned as stored (an opaque JWE) with `ClientEncrypted: true`, derived from `Protection` |
| `set_key_value` | `SetKeyValueAsync` | `value`, `protection` (required on create), `contentType`, `expectedVersion`. A secret, or a change to or from a secret, needs `ContributorWithSecrets` |
| `delete_key_value` | `DeleteKeyValueAsync` | Destructive; `permanent` (Administrator), `expectedVersion`; a secret needs `ContributorWithSecrets` |
| `restore_key_values` | `RestoreKeyValuesAsync` | `keys` (up to 100) or `keyPrefix`; per-key outcomes; secrets need `ContributorWithSecrets` |
| `purge_key_values` | `PurgeKeyValuesAsync` | Destructive, permanent; Administrator |
| `protect_key_values` | `ProtectKeyValuesAsync` | Moves secrets between `Standard`, `Hsm` and `Byok`; `ContributorWithSecrets` |
| `list_key_value_versions` | `GetKeyValueVersionsAsync` | |
| `diff_key_value` | `GetKeyValueVersionsAsync`, then `GetKeyValueVersionDiffAsync`, `…DiffCustomAsync` | Public pairs only: when either version is a secret, the tool refuses before calling the diff, because a secret diff returns plaintext without `reveal`. Hyok values are not comparable |
| `get_key_value_policy` | `GetKeyValuePolicyAsync` | |
| `set_key_value_policy` | `SetKeyValuePolicyAsync` | Destructive (replaces the policy); Administrator; `expectedVersion` |
| `get_key_value_audit` | `GetKeyValueAuditAsync` | Paged; Administrator |
| `get_key_value_usage` | `GetKeyValueUsageAsync` | Administrator; counters per project and day, flushed every minute, so a crash can lose the last minute |

- The `set_key_value` description recommends reading `Version` first and passing `expectedVersion` (0 creates only when the key does not exist); a mismatch is `412 KeyValueVersionMismatch`.
- `set_key_value` refuses `protection: Hyok`: a client-encrypted value needs the recipients' public keys and an encryption step that `sform` does.
- Not exposed: export and import (bulk transfer of secrets in one document), BYOK setup and verification, key rotation and recipients.

**Tools that return secrets** (`SecretTools`), registered only with `Mcp:SecretTools:Enabled = true`, none of them read-only (so clients that confirm writes confirm them too), each a tool of its own (so clients that allow or deny per tool can tell them apart):

| Tool | API | Notes |
| --- | --- | --- |
| `reveal_key_value` | `GetKeyValueAsync(reveal: true)`; `GetKeyValueVersionAsync(reveal: true)` | Needs `ContributorWithSecrets`; an audited event |
| `get_resolved_configuration` | `GetResolvedConfigurationAsync` | Bindings evaluated; secret bindings filled in with secret rights; the API records each secret read |
| `check_configuration_bindings` | `GetConfigurationAsync`, `GetResolvedConfigurationAsync` | Per binding pointer, whether the resolved value differs from the stored expression (`Resolved: true|false`), never the value; a failed read gives `Resolved: unknown` and the error code only |

Their descriptions say: ask the person first; the value enters the conversation. Without the setting, a person can still read secrets with `sform` or the admin UI; the MCP server does not put them into a model's context.

**Logging and where secrets go.**

- The host logs tool names, statuses and error codes. It never logs arguments, results, tokens, exception objects of the SDK or response bodies.
- `"Logging:LogLevel:ModelContextProtocol": "Information"` in `appsettings.json`. Outside Development, `LoggingSafety.Clamp(LoggerFilterOptions)` (a `PostConfigure`) adds, for every provider named in the rules and for none, a rule for the category `ModelContextProtocol` at `Information`, and raises any rule whose category matches a `ModelContextProtocol` category (including the default rule and wildcards) below `Information` to `Information`, because the SDK writes whole messages at `Trace`.
- ASP.NET `HttpLogging` is not added. `IdentityModelEventSource.ShowPII` stays false.
- Results that carry secrets (machine credentials and, with the setting, revealed values and resolved reads) enter the model's context, the client's transcript and the model provider. The host does not cache them.

### 10. Understanding tools (`Mcp/src/Understanding`, `Mcp/src/Tools/UnderstandingTools.cs`)

The analysis is in pure classes over SDK types, tested with fixtures; the tools call the API and pass the results in. All four are read-only and read no secret.

**`search_configurations`** (`ConfigurationSearch`).

- Key filters: `annotationType` and `keyPrefix` (sent to `GetConfigurationsAsync`), and `subject`, `responsibility`, `context`, `unit` and `keyText`, matched against the parsed key (a case-insensitive part of a name segment).
- Content filters, applied to the stored own documents (`GetConfigurationVersionAsync` with each summary's `Version`, at most four reads at a time): `property` (a property name, or a pointer suffix such as `/database/host`), `valueText` (a case-insensitive part of a scalar value) and `bindsKey` (a key/value key that a binding refers to). A match reports `{AnnotationKey, Pointer, Value}`; binding expressions are reported as stored, so no secret is read.
- Budgets: up to five API pages (5,000 summaries) and 200 document reads; at most `maxResults` matches (default 50, at most 200). `IsTruncated` says when a budget stopped the search, and which.
- The description says it searches stored documents, not effective ones: a value inherited from an ancestor is found on the ancestor.

**`get_binding_catalog`.** `GetBindingCatalogAsync`: the binding functions and sources with examples, without values.

**`explain_configuration`** (`ConfigurationExplainer`). Inputs: the key and an optional JSON pointer; `includeConsumers` (default true).

1. `GetConfigurationCompositionAsync` and `GetConfigurationAsync`; `IsStale` is true when their `Hash` differs (the effective read can be served from a cache that has not caught up).
2. `Values`: for every origin (only the ones at or under the pointer when one is given), the value at the path in `Content` (leaves with their value, containers without), the step (`Order`), its source (`Id`, `Kind`, annotation key or template type code, `Version`, `Presence`), its node (annotation key, depth) and `Via`, the node path from it to the target (following `MergedInto`). For a member of an object, `AlsoSetBy` lists the other steps whose source content holds a value at the same path, with that value, its node path and `BeforeWinner` (merged before the winning step, so overridden) or not (merged after it within a node that was then merged again, so not applied). Array items are attributed by their origins only, because arrays merge by union and positions shift. At most 200 entries, with `IsTruncated`.
3. `Sources`: each source with the number of values it supplies; sources that are `Missing` or `Empty` are listed too, so the person sees where nothing is stored.
4. `Bindings`: every string that starts with `@` and every object binding (a string `$binding` with a known kind or a `$definition`) in the effective document, with its pointer and expression. Simple key/value references (`@path`, `@(path, source)`, `@secret(path)`, `@public(path)`) are parsed into the key and source, and for the default store the tool reads the pair's metadata without its value (`GetKeyValueAsync(reveal: false)`: exists, deleted, protection, version), at most 50 lookups. `@config` and `@annotation` are reported as reads of this document and of annotation values.
5. `Consumers`: a fixed statement that the platform does not record which applications or machines read a configuration at runtime; and, with `includeConsumers`, the machines of the project that can read the values, in two groups. `ReadsThisDocument`: machines with `ConfigurationRead` or `ConfigurationWrite` (and `SecretsRead` noted) whose `AnnotationKey` is empty, the key or an ancestor of the key. `ReadsThroughDescendants`: such machines whose `AnnotationKey` is a descendant of the key; they read these values through their own effective configuration, where a descendant may override them. Listing machines needs `Contributor`; a `403` gives `Machines: null` with the reason instead of an error.
6. Notes: bindings are not evaluated in this read (`check_configuration_bindings` does it, when enabled); templates are merged at nodes of their type; views never inherit.

**`suggest_configuration_placement`** (`PlacementAdvisor`). Inputs: `systems`, a list (1 to 20) of `{subject, responsibility, context, unit}` names; `pointers` (JSON pointers) or `fragment` (a JSON object, whose leaf pointers are used); `typeDefault` (default false); the view.

1. Each system gives its most specific key with the `AnnotationKey.Create*` factories: all four names give `uxe`; subject, responsibility and context give `exe`; subject and responsibility `usg`; subject and context `cnt`; responsibility and unit `unt`; one subject `sbt`; one responsibility `rst`. Other combinations are an input error that names what is missing.
2. Candidates, in this order:
   - with `typeDefault`, the template of the systems' type (`template:{typeCode}`; all systems must have the same type, otherwise an input error): "a value for every annotation of this type in the view, overriding what ancestors set";
   - one system: its key ("the value belongs to this system only"), then its ancestors from the most specific to the most general (`GetAllAncestorKeys`, deepest first; equal depth in reverse merge order, so the branch merged later comes first);
   - several systems: their lowest common ancestors ("the value is shared by these systems"), the keys that are the key or an ancestor of every system's key and have no descendant with that property; then the other common ancestors, deepest first. Without a common ancestor the tool says so and suggests writing on each key, or `typeDefault`.

   Each candidate carries the meaning of its level from the "Where a value belongs" table of `docs/42for.net/platform/modeling.md`, plus one for units: "this job of the capability, for every subject".
3. **Does it take effect?** For each system whose key exists, the tool replays the system's composition as `inheritance.md` "Replaying" describes (per node: the results of the nodes merged into it, then its steps in order), with the candidate's content changed: for an annotation candidate, every step of `configuration:{candidate}` gets its content merged with the fragment (or, with pointers only, a marker value at each pointer); for a template candidate, every node of the type gets the template step with the fragment merged in. Per system and pointer it reports `Effective: true|false` and, when false, the step that wins and its node path. This catches a value overridden by a descendant and a value lost to a shared ancestor merged again after the candidate (the `currency` case). Candidates that are effective for every system rank before the others.
4. **Evidence**, for the first three candidates: whether the annotation exists (`GetAnnotationAsync` with `includeInactive`), is deleted (restore it first) or is missing (create it with `set_annotation` or `create_annotations`, which create missing ancestors, then write the configuration); the reach (`GetDescendantsAsync(all)`, the count of the first page, "at least" when there are more; for a template, every annotation of the type in the view); and, per target pointer, where the value comes from today in each existing system.
5. **Schema preview**, for the first annotation candidate that exists and a `fragment`: the fragment is merged into the candidate's stored document (Newtonsoft merge with union arrays and nulls ignored, as the API merges) and checked with NJsonSchema against `GetCombinedConfigurationSchemaAsync`, as the API checks writes. Violations are reported as `{path}: {kind}`. External references (`$ref` to a URL or a file) are refused by a resolver that throws, and reported as "external references not previewed", so a schema cannot make the host fetch URLs or read files. The API decides at write time; this is a preview.
6. The tool writes nothing.

### 11. Aspire

`Aspire.Host.Platform.csproj` references `Mcp.csproj`. `AppHost.cs` adds `builder.AddProject<Projects.Mcp>("mcp")` with `.WithReference(apiFunctions)`, `.WaitFor(apiFunctions)`, `.WithEnvironment("Storyteller__BaseUrl", ReferenceExpression.Create($"{apiFunctions.GetEndpoint("http")}/api"))` and `.WithExplicitStart()`: the server needs an Entra app registration, so it is started from the dashboard, like `db-creator`. The `http` endpoint is the one the admin UI uses; the API allows HTTP in Development.

### 12. Documentation

- `docs/Platform/Storyteller/mcp.md` (new): what the server is, the code, the two app registrations and their settings (the MCP app: single-tenant, confidential, App ID URI = resource URL, the scope, `knownClientApplications` listing the MCP client apps; the Storyteller API: the MCP app pre-authorized for `User.Impersonation` or admin consent, `Auth:DelegatedClients`, the recommended `idtyp` optional claim), the identity check of section 1 as a setup step, how to connect a client (a pre-registered client id), the tool catalog grouped as above, the secret tools and their setting, the safety rules and the limits. It links the concepts the tool descriptions use: `inheritance.md`, `binding.md`, `templating.md`, `definitions.md`, `key-values.md`.
- `docs/Platform/Storyteller/authentication.md`: `Auth:DelegatedClients` in the Entra ID settings, and a section "Signing in through the MCP server" after "Signing in with sform", which summarizes the trust and links `mcp.md`.
- `docs/Platform/Storyteller/access.md`: one sentence under the roles that the MCP server acts with the person's role.
- `docs/Platform/Aspire.Host/local-development.md`: the `mcp` resource row.
- `CLAUDE.md`: `Mcp/` in the Storyteller list.
- Specs and earlier reviews are not changed.

### 13. Tests

`Mcp/test/Mcp.UnitTests.csproj` (xUnit, Shouldly, Moq, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, new in the catalog; `IsPackable=false`, `IsTestProject=true`):

- **The boundary**, with `WebApplicationFactory<Program>`; `PostConfigure<JwtBearerOptions>("Bearer", …)` replaces the `ConfigurationManager` with a static one holding a test RSA key, so no network is used:
  - no token: `401` with `WWW-Authenticate` naming `{ResourceUrl origin}/.well-known/oauth-protected-resource/mcp`, also when the request's `Host` is different;
  - the resource metadata and the authorization server metadata (issuer, Entra endpoints, `S256`);
  - wrong audience, wrong issuer tenant, a `tid` of another tenant, an expired token, an app-only token (`roles`, no `scp`), `idtyp=app` and an `azp` that is not in `AllowedClientIds`: `401`;
  - a token without the MCP scope: `403` with `error="insufficient_scope"`;
  - a valid token: `tools/list` through `McpClient` over the factory's `HttpClient` lists the tools; the secret tools only with the setting.
- **The token path**: a tool call with a stub `IStorytellerTokenProvider` and a recording API handler sends `Authorization: Bearer {downstream}` and never the inbound token; two calls with different users send their own tokens; a `StorytellerSignInRequiredException` becomes an `isError` result with the fixed text.
- **Options**: AuthKit, a non-GUID tenant, no allowed client, no credential, two credentials, an HTTP base URL outside Development and a trailing slash each fail the start.
- **Errors**: `ApiErrors` for `404`, `400` with an error code, `409` with schema errors, `409 AnnotationDeleted`, `412 KeyValueVersionMismatch`, `403`, `401`, `422` and `500`; a marker string in a `422` or `500` message, in `Error` or in a raw response never appears in the result.
- **Catalog**: every tool has a unique `snake_case` name and descriptions on the method and every parameter; reads are `ReadOnly`; every other tool has an explicit `Destructive` hint; deletes, purges and resets are destructive; none is open-world; secret tools are not read-only.
- **Tools**, against mocked SDK interfaces: the annotation subtype and its derived members per type; `set_annotation` keeps omitted fields, clears `clearFields`, and refuses a deleted key or ancestor; `create_annotations` computes keys from compact lines; configuration writes refuse a missing annotation; `JsonPatchBuilder` and `replace_configuration`; documents converted from `JsonElement`; host paging across API pages; `get_key_value` sends `reveal: false`; `diff_key_value` refuses secrets without calling the diff; `set_key_value` refuses Hyok; `create_machine` fills the body, checks scopes and adds the notice per kind.
- **Understanding**: the explainer over a composition ported to `Mcp/test/Fixtures/composition-execution.json` from the admin UI test `src/Platform/ui.admin/src/test/unit/configuration/configuration-composition.test.ts`, including `Via`, `AlsoSetBy`, array items, a pointer filter, bindings, consumers and `IsStale`; the search filters and budgets; the placement candidates for one system, for several systems with and without a common ancestor, `typeDefault`, an invalid name, the replay (`Effective` false for the `currency` case and for an overriding descendant), and the schema preview with an external reference refused.
- **Logging**: `LoggingSafety.Clamp` with default, provider-specific and wildcard `Trace` rules; a machine creation and an unexpected-status `ApiException` with a marker in its body, run with a recording logger provider, write neither the access key, the certificate password nor the marker to any log.

`Mcp/testint/Mcp.IntegrationTests.csproj`, in the style of `Access.AuthKit/testint` (it writes "Skipped: …" and returns without the environment): with a tenant, the MCP app's credential, a token for the MCP app and an API token from `sform` for the same person, it runs OBO and compares `sub` (and reports `oid` and `tid`) of the OBO token and the `sform` token.

The API tests are in section 2.

### 14. Phases and commits

1. `docs(platform): plan the MCP server for the Storyteller API` (this spec)
2. **Phase 1**, `feat(platform): accept delegated tokens of listed client apps as users`: sections 1 and 2.
3. **Phase 2**, `feat(platform): add the MCP host with Entra ID on-behalf-of sign-in`: sections 3 to 6, the logging part of section 9, section 11, the boundary, token, option, error and logging tests of section 13, and the integration test project. The discovery tools of section 7 ship here, so the host has tools to call.
4. **Phase 3**, `feat(platform): manage annotations, configurations, schemas and templates through MCP`: the rest of section 7 with its tests.
5. **Phase 4**, `feat(platform): manage machines and the values store through MCP`: sections 8 and 9 with their tests.
6. **Phase 5**, `feat(platform): search, explain and place configuration through MCP`: section 10 with its tests.
7. `docs(platform): document the MCP server`: section 12.
8. `docs(platform): review the MCP server` (the review).

Every commit body names `Refs: 424-84`.

### 15. Verification

- `dotnet build 42.mono.slnx`.
- `dotnet test` of `Access.AzureAd/test` and `Mcp/test`; `Api.Functions/test` in Debug and in Release (`-c Release`, where user role checks run).
- `Mcp/testint` reports skipped without its environment.
- `dotnet build src/Platform/Aspire.Host/src`.
- The host starts with valid settings and answers the metadata documents.

### 16. Differences from the CodeRabbit coding plan

The plan's choices that this spec keeps: a separate stateless Streamable HTTP host on `ModelContextProtocol.AspNetCore`; two OAuth resources with Entra OBO and no passthrough; RFC 9728 metadata; an allow-list of delegated clients, checked for `scp` and `idtyp`; the AuthKit refusal at start; the `sub` question verified, not changed; thin tools over the SDK with the API keeping all authorization; one-time credentials, no reveal by default, `ExpectedVersion`; the excluded features; the read-only explanation and placement tools built on compositions and `AnnotationKey` factories.

It differs in these points:

1. **Delegated clients are pinned to a tenant**, and the MCP host accepts tokens of listed client apps only. The plan checks neither, so any app consented for the MCP scope, or the listed app from another tenant, would act as a user.
2. **Plain `JwtBearer` instead of `AddMicrosoftIdentityWebApi`, and MSAL instead of Identity.Web's token acquisition.** The pinned issuers are explicit, the tests run without network, and OBO is one MSAL call with a bounded cache.
3. **The host serves both metadata documents from its options** instead of `AddMcp`'s request-derived URLs, and adds authorization server metadata, because Entra publishes no PKCE methods and MCP clients refuse such a server. The App ID URI of the MCP app is the resource URL, because Entra refuses a `resource` that differs from the scope's resource.
4. **A scoped `ISdkConfiguration` over a scoped token holder** instead of `HttpContext.Items`; the filter sets the holder, the transient clients read it.
5. **No retries of unsafe methods** on the SDK clients. The plan calls `AddServiceDefaults` without noticing that its resilience handler retries writes.
6. **The `http` endpoint in Aspire.** The plan asks for HTTPS because the API sends secrets over HTTPS only; that rule applies outside Development, and Aspire runs in Development, as the admin UI does. The MCP host requires HTTPS URLs outside Development.
7. **No `AddAuthorizationFilters()`.** No tool carries `[Authorize]`; the endpoint policy guards every call, and the API authorizes each operation.
8. **No certificate renewal or revocation tools.** Renewal is a machine operation over mTLS (a user gets `401`); revocation concerns shared certificates, which are excluded.
9. **Secrets leave only through separate tools behind a setting** (`reveal_key_value`, `get_resolved_configuration`, `check_configuration_bindings`), and `diff_key_value` refuses secrets. The plan exposes the resolved read without a guard and a `reveal` flag on a read-only tool, which clients cannot tell apart.
10. **API error messages are copied only where they are safe**; `500` and `422` get fixed texts, because they can carry storage messages and evaluated secrets.
11. **Writes are safer for an assistant**: configuration writes refuse a missing annotation, annotation writes refuse to re-create deleted keys silently, and `replace_configuration`, `revert_configuration` and `replace_template` exist, so the assistant never writes patches by hand to replace a document.
12. **Lists are paged in the host** with a compact projection, because API pages of 1,000 annotations do not fit an MCP result.
13. **Search reads stored documents** for properties, values and bound keys, with budgets, instead of typed name queries only.
14. **Explanation adds what was overridden, staleness, bound key/value metadata and who can read the configuration** (directly or through descendants).
15. **Placement replays compositions** to tell whether a candidate takes effect for each system, uses lowest common ancestors of several systems, and previews the schema check on the merged stored document with external references refused.
16. **Explicit tool registration** (`WithTools<T>`) instead of an assembly scan, and explicit `Destructive` hints, because the SDK's default marks every tool destructive.
17. **One spec, one review**, both in `docs/Platform/Storyteller/specs/` with dated names, as the repository's workflow requires; the plan names the spec `mcp-server.md`.
18. **Logging is clamped in code** outside Development, not only configured.

### Out of scope

- AuthKit sign-in for the MCP server (it needs the API to accept AuthKit-domain tokens or a Storyteller user-token exchange).
- Stateful sessions, subscriptions, server-initiated requests, elicitation, prompts and resources.
- Dynamic client registration.
- View export and import, key/value export and import, BYOK setup, key rotation, Hyok writes and decryption, recipients, shared certificates, trusted issuers, invitations and members.
- A read-only mode of the server, a public page on 42for.net, a Dockerfile and a deployment.
- Changing the account mapping from `sub` to `tid` and `oid`.

### Merge notes

- `Directory.Packages.props` gets `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `Microsoft.Identity.Web.TokenCache` 4.5.0, `ModelContextProtocol.AspNetCore` 2.3.0 and, if needed, `System.Security.Cryptography.Pkcs` 10.0.12, in alphabetical order. `ModelContextProtocol` 2.3.0 brings `Microsoft.Extensions.*.Abstractions` 10.0.13 transitively next to the catalog's 10.0.12; restore accepts it, and the MCP projects reference none of those packages directly.
- A deployment that does not list a client in `Auth:DelegatedClients` behaves as before.
