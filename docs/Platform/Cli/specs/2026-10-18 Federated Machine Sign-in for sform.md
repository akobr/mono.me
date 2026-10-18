# Federated Machine Sign-in for sform

Linear: [424-62](https://linear.app/424fun/issue/424-62/sign-in-to-sform-as-a-federated-machine).

Line numbers refer to the tree this plan was written against (`08f4a21d`). No application code is changed by this document.

The plan builds on [Non-interactive Sign-in for sform](2026-10-17%20Non-interactive%20Sign-in%20for%20sform.md) (424-45) and on the federation of 424-46 (`docs/Platform/Storyteller/specs/`). It takes the CodeRabbit coding plan attached to the issue as input; where this spec decides differently, the section says so.

## Problem

424-45 lets `sform` sign in from `STORYTELLER_*` variables as an `ApiKey`, `Certificate`, `CertificateAndApiKey` or `ClientCredentials` machine, or with any bearer token in `STORYTELLER_TOKEN_FILE`. 424-46 added `Federated` machines: a workload exchanges a token of its own platform (GitHub Actions, Kubernetes, Azure, AWS, Google Cloud, Vercel) for a Storyteller token at `POST {API}/v1/{org}/{project}/access/token` (RFC 8693). `sform` cannot do that exchange:

- A pipeline must exchange the token itself and write the Storyteller token to `STORYTELLER_TOKEN_FILE`. Storyteller tokens live 15 minutes, and nothing renews the file, so a longer job fails with `401`.
- `sform account machine create -k federated` prints the setup and a curl exchange, but no `STORYTELLER_*` variables (`MachineCreateCommand.cs`, lines 219-224 skip them for the `Federated` kind).

The issue asks for:

- the variables `STORYTELLER_CLIENT_ID` (reused), `STORYTELLER_SUBJECT_TOKEN_FILE`, and optionally `STORYTELLER_SUBJECT_TOKEN_TYPE` and `STORYTELLER_TOKEN_SCOPE` (reused);
- the GitHub Actions variables `ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN` as a built-in token source, with the token endpoint as the audience;
- a `MachineSignInKind.Federated` that exchanges without `Authorization` and without redirects, keeps the token in memory, and exchanges again 60 s before `expires_in` runs out or after a `401`;
- the project from `STORYTELLER_PROJECT` or `-p`, the existing mixed-kinds rule, and the existing exit codes (`invalid_request` → 44, `temporarily_unavailable` / network → 3, missing variables → 2);
- the variables in `machine create -k federated`, the machine and project in `sform account`;
- docs with a GitHub Actions and a Kubernetes example;
- unit tests of the variable resolution, the refresh and the exit codes.

## Current State

### Variables and kinds

- **Names.** `StorytellerVariables` (`src/Platform/Cli/src/Authentication/StorytellerVariables.cs`) has `API_URL`, `PROJECT`, `VIEW`, `API_KEY`, `CERTIFICATE_FILE`, `CERTIFICATE_PASSWORD`, `CLIENT_ID`, `CLIENT_SECRET`, `TOKEN_URL`, `TOKEN_SCOPE` and `TOKEN_FILE`.
- **Kinds.** `MachineSignInKind` has `ApiKey`, `Certificate`, `CertificateAndApiKey`, `ClientCredentials` and `TokenFile`.
- **Settings.** `MachineSignInSettings.FromEnvironment` (`MachineSignInSettings.cs`, lines 42-73) trims every value and treats an empty one as unset. `IsConfigured` is true when any credential variable is set (line 37); then `sform` never uses the stored user session.
- **`GetKind`** (lines 107-165) knows three families: key or certificate (`API_KEY`, `CERTIFICATE_FILE`, `CERTIFICATE_PASSWORD`), client credentials (`CLIENT_ID`, `CLIENT_SECRET`, `TOKEN_URL`, `TOKEN_SCOPE`) and token file (`TOKEN_FILE`). More than one family throws `Machine sign-in variables of different kinds are set: …` (`Configuration`). It then requires an https-or-localhost `STORYTELLER_API_URL` and a well-formed `STORYTELLER_PROJECT` when set. `CLIENT_ID` alone fails with `Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_SECRET is missing.`

### `MachineAuthenticationService`

- A singleton that implements `IAuthenticationService` and `IMachineSignIn` (`MachineAuthenticationService.cs`). `AuthenticationServiceSelector` delegates to it whenever `IsConfigured`.
- `Load` (lines 229-274) validates the settings once and builds the `MachineIdentity` from the credential, without network calls. A `ClientCredentials` identity has the client id and no project.
- **Client credentials.** `GetClientTokenAsync` (lines 351-368) keeps one `ClientToken` behind a `SemaphoreSlim` and requests a new one when it is within `RefreshMargin` (60 s) of `ExpiresAt`. `RequestClientTokenAsync` (lines 370-423) posts the form on the named client `nameof(MachineAuthenticationService)`, which does not follow redirects (`Startup.cs`, lines 136-138). It maps a network failure or a non-caller timeout to `ServiceError`, `400`/`401` to `Denied` with `DescribeTokenError`, and any other failure to `ServiceError`. `ReadToken` (lines 425-452) takes `expires_in` (capped at one day), else the JWT `exp`, else 5 minutes.
- **Token file.** `ReadTokenFile` (lines 305-337) reads the file through `IFileSystem` before every request and maps a missing, unreadable or empty file to `Configuration`.
- `SignInAsync` (lines 83-93) gets the token for client credentials and checks the `exp` of a token file. `GetAccessTokenAsync` (lines 109-128) returns `ApiKey <key>`, `null` (certificate), or the bearer token.

### Target project

- `CommandContext` (`Commands/CommandContext.cs`) is a singleton built with `IMachineSignIn`. The constructor (lines 19-55) takes `STORYTELLER_PROJECT`, else for a machine the project of its credential (`IsProjectKnown` false when the credential has none), else `access.default.json`.
- `TrySetExplicitTarget` (lines 67-116) applies `-p`/`-v`, then, for a machine, throws `Configuration` when no project is known or when it differs from the credential's project.
- `BaseContextCommand.ExecutePreconditionsAsync` (`Commands/BaseContextCommand.cs`, line 50) calls it before every command body. `CaDownloadCommand` skips it on purpose: the CA endpoint is anonymous and belongs to no project.
- `AccountCommand` derives from `BaseCommand` and has no `-p`. `SignInMachineAsync` (`Commands/Account/AccountCommand.cs`, lines 137-185) calls `SignInAsync` first, then returns `43` without a project, then calls `TrySetExplicitTarget(null, null)` and prints the project, the API URL and the token expiry. `DescribeKind` (lines 124-134) ends with `_ => "a token file"`.

### SDK transport

- `Startup.ConfigureStorytellerSdk` (`Startup.cs`, lines 149-182) registers an `SdkConfiguration` whose synchronous `AccessTokenFactory` calls `IAuthenticationService.GetAccessTokenAsync()`. An `AuthenticationException` propagates; any other exception ends as an empty token and a `401`.
- `SdkRequestHelper.ApplyAuthorization` (`Sdk.NSwag/src/ApiSdk.partial.cs`, lines 122-138) runs in each client's `PrepareRequest` and sets `Authorization: Bearer <token>` (or `ApiKey`).
- `AddStorytellerSdk` (`Sdk.NSwag/src/ServicesCollectionExtensions.cs`, lines 10-58) registers seven typed clients, each with an `HttpClientHandler` that carries the client certificate. There is no hook for a `DelegatingHandler`, and no SDK code sends a request twice.
- The generated clients send `StringContent` (42 operations), `FormUrlEncodedContent` (the token exchange) and one `StreamContent` (import, a caller-owned `.zip` stream), always with `HttpCompletionOption.ResponseHeadersRead`.
- `Program.cs` (lines 67-102) maps an escaping `AuthenticationException` to `2` (`Configuration`), `3` (`ServiceError`) or `44`, and an `ApiException` `401`/`403` to `44` with a machine hint.

### The token endpoint (server)

`FederationHttp.PostMachineToken` (`src/Platform/Storyteller/Api.Functions/src/V1/FederationHttp.cs`, lines 308-352) and `docs/Platform/Storyteller/authentication.md#the-token-endpoint`:

- Anonymous, `application/x-www-form-urlencoded`; any `Authorization` header is refused with `invalid_request`.
- Parameters: `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`, `client_id` (the machine id), `subject_token`, `subject_token_type` (`…:jwt`, `…:id_token` or `…:access_token`), optional `scope` (a subset of the machine's scopes).
- `200 { access_token, issued_token_type, token_type: "Bearer", expires_in: 900, scope }`.
- `400` with `invalid_request` (also for every refused subject token), `unsupported_grant_type`, `invalid_target` or `invalid_scope`; `503 temporarily_unavailable` with `Retry-After: 5`.
- The rule's audience is the machine's `TokenEndpoint`, `{PublicBaseUrl or scheme://host/api}/v1/{org}/{project}/access/token`, with escaped segments.

### Machine creation and the admin UI

- `MachineCreateCommand.WriteFederated` (`Commands/MachineAccess/MachineCreateCommand.cs`, lines 286-295) prints the machine id, the token endpoint, the rules and a curl exchange (`FederationConsoleExtensions.WriteTokenExchange`), then `The machine has no secret: …`. `WritePipelineVariables` (lines 231-283) is skipped for `Federated`. `MachineCreateFederatedTests` pins this output.
- `FederationConsoleExtensions` (`Output/FederationConsoleExtensions.cs`, lines 13-14) defines `TokenExchangeGrantType` and `JwtTokenType`.
- The admin UI's `FederatedSetupDialog.vue` prints a curl exchange into `STORYTELLER_TOKEN`; it has no `STORYTELLER_*` sign-in snippet for `sform`.

### GitHub Actions OIDC

A job with `permissions: id-token: write` gets `ACTIONS_ID_TOKEN_REQUEST_URL` (an https URL that already has a query, `…?api-version=2.0`) and `ACTIONS_ID_TOKEN_REQUEST_TOKEN` (valid for the job). `GET {URL}&audience=<aud>` with `Authorization: Bearer <request token>` returns `{ "count": …, "value": "<JWT>" }`. The JWT lives about 5 minutes, so a long job must ask again before each exchange. Both variables are present in every step of such a job, including jobs that never sign in to Storyteller.

## Proposed Changes

### 1. Variables (`StorytellerVariables`)

```csharp
public const string SUBJECT_TOKEN_FILE = "STORYTELLER_SUBJECT_TOKEN_FILE";
public const string SUBJECT_TOKEN_TYPE = "STORYTELLER_SUBJECT_TOKEN_TYPE";
```

A separate class in the same folder holds the names that are not `sform`'s own:

```csharp
// The variables a GitHub Actions job with permissions: id-token: write gets from the runner.
public static class GitHubActionsVariables
{
    public const string ID_TOKEN_REQUEST_URL = "ACTIONS_ID_TOKEN_REQUEST_URL";
    public const string ID_TOKEN_REQUEST_TOKEN = "ACTIONS_ID_TOKEN_REQUEST_TOKEN";
}
```

The token types live next to the grant type in a small `TokenExchange` class of the authentication folder, so the authentication code does not depend on `Output`:

```csharp
public static class TokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";
    public const string IdTokenType = "urn:ietf:params:oauth:token-type:id_token";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
}
```

`FederationConsoleExtensions.TokenExchangeGrantType` and `JwtTokenType` become aliases of these constants, so the printed curl stays the same.

| Variable | Meaning |
|---|---|
| `STORYTELLER_CLIENT_ID` | The machine id, the exchange's `client_id`. Shared with client credentials. |
| `STORYTELLER_SUBJECT_TOKEN_FILE` | A file holding the platform token; read again before every exchange. |
| `STORYTELLER_SUBJECT_TOKEN_TYPE` | Optional: `jwt` (default), `id_token`, `access_token`, or the full URN. |
| `STORYTELLER_TOKEN_SCOPE` | Optional: Storyteller scopes separated by spaces, a subset of the machine's. Shared with client credentials. |
| `ACTIONS_ID_TOKEN_REQUEST_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN` | Set by GitHub Actions; the subject token source when no file is set. |

### 2. Choosing the kind (`MachineSignInSettings`)

New properties, read and trimmed like the others in `FromEnvironment`:

```csharp
public string? SubjectTokenFile { get; init; }
public string? SubjectTokenType { get; init; }
public string? GitHubTokenRequestUrl { get; init; }
public string? GitHubTokenRequestToken { get; init; }

// Both GitHub Actions variables are set; on its own this never configures a machine.
public bool HasGitHubTokenSource => GitHubTokenRequestUrl is not null && GitHubTokenRequestToken is not null;
```

- **`IsConfigured` / `CredentialVariables`.** `SUBJECT_TOKEN_FILE` and `SUBJECT_TOKEN_TYPE` join the list, after `TOKEN_SCOPE`. The GitHub variables never do: a job with `id-token: write` that runs `sform` as a user must stay a user sign-in.
- **Families in `GetKind`:**

  | Family | Variables |
  |---|---|
  | key or certificate | `API_KEY`, `CERTIFICATE_FILE`, `CERTIFICATE_PASSWORD` (unchanged) |
  | client credentials | `CLIENT_SECRET`, `TOKEN_URL` |
  | federation | `SUBJECT_TOKEN_FILE`, `SUBJECT_TOKEN_TYPE` |
  | token file | `TOKEN_FILE` (unchanged) |
  | shared | `CLIENT_ID`, `TOKEN_SCOPE`: they belong to client credentials or federation, whichever is set; alone they count as one more family |

  More than one family throws the existing mixed-kinds message. So `STORYTELLER_CLIENT_SECRET` with `STORYTELLER_SUBJECT_TOKEN_FILE` stops, as does an API key with `STORYTELLER_CLIENT_ID`.
- **The order of checks stays:** mixed families, then `STORYTELLER_API_URL`, then `STORYTELLER_PROJECT` when it is set, then the kind's own rules. A project is not required here, because `-p` can still name it.
- **Federation is chosen** when the federation family is set, or when only the shared family is set, `CLIENT_ID` is among it and is a GUID, and `HasGitHubTokenSource`. Then:
  1. Without `CLIENT_ID`: `A federated machine needs STORYTELLER_CLIENT_ID, the machine id.`
  2. The subject source is `SUBJECT_TOKEN_FILE` when set, otherwise GitHub Actions when `HasGitHubTokenSource`, otherwise `A federated machine needs a platform token: set STORYTELLER_SUBJECT_TOKEN_FILE, or run in a GitHub Actions job with permissions: id-token: write.`
  3. For the GitHub source, `ACTIONS_ID_TOKEN_REQUEST_URL` must be absolute and https (http only for localhost, like the other URLs): `ACTIONS_ID_TOKEN_REQUEST_URL must be an absolute https URL; http is allowed only for localhost.`
  4. `SUBJECT_TOKEN_TYPE` must be one of the short names or URNs (below), else `STORYTELLER_SUBJECT_TOKEN_TYPE must be jwt, id_token or access_token, or the full urn:ietf:params:oauth:token-type: URN.`
- **Why a GUID for the ambient source.** Every federated machine id is a GUID (`authentication.md`, "The `MachineAccess` object"), while AuthKit (`client_…`) and Keycloak (`42.sform.…`) client ids are not. Many jobs have `id-token: write` for other reasons (cloud logins, attestations), and a GitHub secret that is missing expands to an empty `STORYTELLER_CLIENT_SECRET`. Without the GUID rule, such a client-credentials job would silently turn into a federated exchange that fails with an opaque `invalid_request`. An Entra appId is a GUID too; for that case the `Denied` message of a GitHub-sourced exchange says that the job's GitHub token was used and that client credentials need `STORYTELLER_CLIENT_SECRET` (section 5). The explicit `SUBJECT_TOKEN_FILE` needs no GUID.
- **`CLIENT_ID` without a secret or a subject source** (shared family only, no usable GitHub source) now explains both ways: `STORYTELLER_CLIENT_ID needs STORYTELLER_CLIENT_SECRET for client credentials, or a platform token for a federated machine: STORYTELLER_SUBJECT_TOKEN_FILE, or a GitHub Actions job with permissions: id-token: write.` This replaces the expected message of the existing `[InlineData(…, StorytellerVariables.CLIENT_ID)]` at `MachineSignInSettingsTests.cs` line 67. `TOKEN_SCOPE` alone (line 69) and `CLIENT_SECRET` with `TOKEN_URL` (line 68) keep today's client-credentials messages.
- **Client credentials** keep their rules (`CLIENT_ID` and `CLIENT_SECRET` required, `TOKEN_URL` checked when set).
- **Helpers**, both pure:

  ```csharp
  // jwt, id_token or access_token (any case), or one of the three URNs; null when the value is none of them.
  public static string? NormalizeSubjectTokenType(string? value); // null value → JwtTokenType

  // The source GetKind chose for a Federated machine; never a token value.
  public FederatedTokenSource GetFederatedTokenSource(); // File or GitHubActions
  ```

  `FederatedTokenSource` is a new enum `{ File, GitHubActions }`.
- The class stays a class without a generated `ToString`, so the request token cannot reach a log.

### 3. `IMachineSignIn`

Two members:

```csharp
// The project of the command, once CommandContext has checked it. Only a federated machine uses it:
// its token endpoint belongs to one project.
void BindTarget(string organization, string project);

// After the API refused rejectedToken with 401: forget it, unless a newer token replaced it already,
// and return a token to send again. Null for every kind but Federated. Throws AuthenticationException.
Task<string?> RenewAccessTokenAsync(string rejectedToken, CancellationToken cancellationToken = default);
```

The binding goes from `CommandContext` to the service, never the other way, so `MachineAuthenticationService` does not depend on `ICommandContext` (which already depends on `IMachineSignIn`), and the SDK's token factory keeps its signature. The CodeRabbit plan chose the same.

### 4. Identity

- `Load` builds `new MachineIdentity(Federated, variables, Settings.ClientId, null, null)`, like client credentials. For the GitHub source, `variables` is `CredentialVariables` plus the two GitHub names, so messages say where the token came from. Names only, never values.
- `GetIdentity`/`TryGetIdentity` stay what the credential tells: a federated identity has no project. `CommandContext` therefore never finds a conflict with `-p` or `STORYTELLER_PROJECT`, and binding a target cannot make a later check fail.
- `SignInAsync` returns the identity with `Organization`, `Project` and `TokenExpiresAt` of the exchange. **Deviation from the CodeRabbit plan**, which reported the bound project from `GetIdentity`.

### 5. The exchange (`MachineAuthenticationService`)

- **Target.** `SignInAsync` uses the bound target, else `STORYTELLER_PROJECT` split with `TrySplitProject`; without either it throws `Configuration`: `No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project> or pass -p.` (the text of `CommandContext`).
- **`GetAccessTokenAsync` and `RenewAccessTokenAsync` use only the bound target, and return `null` without one.** Every command that calls a project's API binds it in `TrySetExplicitTarget` first. The ones that do not are `sform account ca`, whose endpoint is anonymous, and the user-only commands, which the API answers with `401` anyway; they must not exchange (or ask GitHub for a token) just because `STORYTELLER_PROJECT` is set. **Deviation:** the CodeRabbit plan threw `Configuration` without a target, which would break `sform account ca` for a federated machine and turn the user-only `401` (exit `44` with its hint) into a misleading exit `2`.
- **Endpoint.** `{STORYTELLER_API_URL without a trailing /}/v1/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/access/token`, with the organization and project as the command names them.
- **Subject token, read before every exchange:**
  - *File:* through `IFileSystem`, trimmed. Missing: `The platform token file {path} in STORYTELLER_SUBJECT_TOKEN_FILE does not exist.`; unreadable: `… could not be read.`; empty: `… is empty.` All `Configuration`.
  - *GitHub Actions:* `GET {ACTIONS_ID_TOKEN_REQUEST_URL}{? or &}audience={Uri.EscapeDataString(endpoint)}` on the named client (no redirects: the request token must not go to another host). The headers `Authorization: Bearer {ACTIONS_ID_TOKEN_REQUEST_TOKEN}` and `Accept: application/json` are set on that `HttpRequestMessage` only, never on `DefaultRequestHeaders`, so the request token cannot reach the token endpoint. `Accept` is what `@actions/core` sends; the runner URL already carries `api-version=2.0`, so the plan's `application/json; api-version=2.0` is not needed (**deviation**, minor). The JWT is `value` of the JSON body. A network failure, a non-caller timeout, a non-2xx status or a body without `value` throws `ServiceError`: `The GitHub Actions ID token could not be requested.` / `… failed with {status}.` / `… returned no token.`
- **Audience.** The GitHub token is requested for the endpoint above. The server compares the JWT `aud` with the rule's audience ordinally (`SubjectTokenVerifier`), and `machine create` suggests the server's `TokenEndpoint` as that audience. So the endpoint must reproduce it byte for byte: the same base URL, and the organization and project in the same case. Section 9 prints a `STORYTELLER_API_URL` that does, and the docs say so.
- **Request.** `POST` of `FormUrlEncodedContent` with `grant_type`, `client_id`, `subject_token`, `subject_token_type` (normalized) and, when set, `scope`. No `Authorization` header, on the named client.
- **Response and failures**, as for client credentials (`ReadToken`, `DescribeTokenError` unchanged), with the endpoint in the messages. For the GitHub source, a `Denied` message adds what the token was requested for, which is not a secret: `…; the GitHub Actions token of this job was requested for the audience {endpoint}, which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead.` **Deviation:** the CodeRabbit plan also capped `error_description`; the server is the API the user chose, the message is already short, and a cap would change the client-credentials messages that tests pin, so it is left out.

  | Result | Reason | Exit code |
  |---|---|---|
  | `400`/`401` (`invalid_request`, `invalid_scope`, `invalid_target`, `unsupported_grant_type`) | `Denied` | `44` |
  | `503 temporarily_unavailable`, another non-2xx (including 3xx), network failure, non-caller timeout, a body without a bearer token | `ServiceError` | `3` |
  | invalid variables, no project, missing or empty subject file | `Configuration` | `2` |

  `invalid_scope` stays `Denied`, like every `400` of client credentials: the server decides what the machine may have. **CodeRabbit offered** `Configuration` for it as an alternative and chose `Denied` too.
- **Cache.** The existing `_gate` and token field, generalized: `ClientToken(AccessToken, ExpiresAt, Target)` where `Target` is `org.project` for a federated token and null for client credentials. A federated token is reused while `ExpiresAt - 60 s > now` and its target is the current one; otherwise it is exchanged again. The field changes only after a successful exchange. The token lives in memory only; nothing is written to disk.
- **Renewal.** `RenewAccessTokenAsync` waits on `_gate`; when the cached token equals `rejectedToken` it drops it; then it returns the cached token if it is still usable, else exchanges again. So a burst of `401`s exchanges once, and a `401` for an older token never discards a newer one. Without a target it returns `null`.
- **Logging.** No token, request token or response body is logged or put into a message.

### 6. Target binding (`CommandContext`)

`TrySetExplicitTarget` calls `_machine.BindTarget(OrganizationName, ProjectName)` at its end, after every check has passed, when a machine is configured. Kinds other than `Federated` ignore it. `-p` therefore wins over `STORYTELLER_PROJECT`, and a federated machine never uses `access.default.json` (the constructor already ignores it for machines).

### 7. `sform account`

- `DescribeKind`: `Federated => "workload identity federation"`, and an explicit `TokenFile` case.
- `SignInMachineAsync`, only when `TryGetIdentity()` is `Federated` (an invalid configuration falls through to `SignInAsync` and its exit code): first the project check (no project → the existing hint, `43`, no request), then `TrySetExplicitTarget(null, null)` (binds the target), then the existing flow. The rest of the flow runs the target check again, which is harmless. Other kinds keep their order.
- Output: `Signed in as machine <id> with workload identity federation (STORYTELLER_CLIENT_ID, STORYTELLER_SUBJECT_TOKEN_FILE).`, `Project <org>.<project> with <view> view.`, `API <url>`, `Token valid until <exp:O>.` `ToExitCode` is unchanged.
- `account` keeps no `-p`, like for every other kind; a federated machine names its project with `STORYTELLER_PROJECT`. The CodeRabbit plan chose the same.

### 8. One resend after a `401` (`MachineTokenRenewalHandler`)

- **SDK.** `ServicesCollectionExtensions` gets an overload; the existing signature stays for binary compatibility:

  ```csharp
  public static IServiceCollection AddStorytellerSdk(this IServiceCollection services, Func<ISdkConfiguration>? configurationFactory = null)
      => services.AddStorytellerSdk(configurationFactory, null);

  // configureHttpClient runs for each of the seven typed clients, after the primary handler is set,
  // for example to add a DelegatingHandler.
  public static IServiceCollection AddStorytellerSdk(
      this IServiceCollection services,
      Func<ISdkConfiguration>? configurationFactory,
      Action<IHttpClientBuilder>? configureHttpClient);
  ```

  `Sdk.NSwag/README.md` documents it. `ApiSdk.g.cs` is not touched.
- **Handler** (`src/Platform/Cli/src/Authentication/MachineTokenRenewalHandler.cs`), a `DelegatingHandler` with `IMachineSignIn`:
  1. Before sending, it captures `request.RequestUri` and the `Bearer` value of `Authorization`. Then it sends the request. Any response but `401` is returned as it is, unread, so streamed downloads pass through.
  2. On `401` it acts only when all of these hold: a machine is configured; a `Bearer` token was captured; **after** the response, `request.RequestUri` still equals the captured URI and `Authorization` still carries the captured token; and the content is `null` or a `ByteArrayContent` (which `StringContent` and `FormUrlEncodedContent` are). The typed clients' `HttpClientHandler` follows redirects, and its redirect handler rewrites `RequestUri` and clears `Authorization` on the same message (and may drop the content of a `POST`); without the after-check, a `302` to another host followed by a `401` there would get the fresh Storyteller token. A `StreamContent` (import) gets the original `401`: its stream belongs to the caller and may not rewind. A `403` is never retried.
  3. Calls `RenewAccessTokenAsync(rejected, CancellationToken.None)`, like the token factory, which also passes none; the named client's own timeout then ends a hanging exchange as `ServiceError` (`3`) instead of an unmapped cancellation. `null` → the original `401`. If renewal throws, the `401` is disposed and the `AuthenticationException` propagates, like one from the token factory (for example `Denied` → `44` when the exchange is refused).
  4. Disposes the `401`, sets `Authorization: Bearer <new>` on the same request, and sends it once more through the inner handler (the pattern of retry handlers: the content is buffered, and the inner handler does not check whether a request was sent). The second response is returned whatever it is; a second `401` ends in the existing `44` of `Program.cs`.
  - **Deviation:** the CodeRabbit plan built a new `HttpRequestMessage` and copied method, URI, version, headers and options. Resending the same message keeps everything without copying and is how `DelegatingHandler` retries work; the content and redirect rules above keep it safe.
- **Startup.** `services.AddStorytellerSdk(null, builder => builder.AddHttpMessageHandler(provider => new MachineTokenRenewalHandler(provider.GetRequiredService<IMachineSignIn>())))`. The named client of `MachineAuthenticationService` and the AuthKit client do not get it. `ConfigureMachineSignIn` and `ConfigureStorytellerSdk` become `internal static`, so a test can build exactly this wiring (section 10).
- `Program.cs` keeps its `401` hint unchanged; it already names the machine variables and the project.

### 9. `sform account machine create -k federated`

`WritePipelineVariables` handles `Federated`, and the skip goes away. After `The machine has no secret: …`:

```
To sign in sform in a pipeline, set:
  STORYTELLER_API_URL=<base of the token endpoint>
  STORYTELLER_PROJECT=<org>.<project>
  STORYTELLER_CLIENT_ID=<machine id>
  STORYTELLER_SUBJECT_TOKEN_FILE=<file with the platform token>
In a GitHub Actions job with permissions: id-token: write, leave out STORYTELLER_SUBJECT_TOKEN_FILE: sform asks the runner for a token for the token endpoint above.
Optional: STORYTELLER_SUBJECT_TOKEN_TYPE=jwt (access_token for Azure managed identity, id_token for Google Cloud) and STORYTELLER_TOKEN_SCOPE=<a subset of the machine's scopes>.
```

- **`STORYTELLER_API_URL` of a federated machine** is `machine.TokenEndpoint` without its `/v1/{org}/{project}/access/token` suffix (ordinal, with the escaped segments of the context's organization and project), so the endpoint `sform` builds, and the audience it asks GitHub for, equals the server's `TokenEndpoint` byte for byte. The server builds it from `PublicBaseUrl`, which can name another host than `general.baseUrl` (the test fixture already differs: `api.example.test` vs `api.example.com`). When the endpoint is missing or does not end with that suffix, the line falls back to `general.baseUrl` and a lowlight note says that the rule's audience must then equal `{STORYTELLER_API_URL}/v1/{org}/{project}/access/token`. `STORYTELLER_PROJECT` is printed in the case of the context, which is the case the server built the endpoint with.
- The curl exchange and `The machine has no secret` stay. `MachineCreateFederatedTests` pins the new lines (including the derived URL and the fallback note) and still asserts that no secret text appears.

### 10. Tests

All in `src/Platform/Cli/test` with xUnit, Shouldly, `ScriptedHttpHandler`, `StubHttpClientFactory`, `ManualTimeProvider` and `MockFileSystem`. No test asserts on a token value inside a message, except to assert that it is absent.

- **`MachineSignInSettingsTests`:** `CLIENT_ID` + subject file → `Federated` (`File`); `CLIENT_ID` (a GUID) + GitHub → `Federated` (`GitHubActions`); the file wins over GitHub; GitHub alone is not configured and `GetKind` says no variable is set; `client_…` or `42.sform.…` + GitHub without a secret → the new `CLIENT_ID` message, not `Federated`; `CLIENT_ID` alone (no GitHub) → the new message (the changed line 67); subject file without `CLIENT_ID` → its message; `SUBJECT_TOKEN_TYPE` + `CLIENT_ID` without a source → its message; the federation family with `CLIENT_SECRET`, `TOKEN_URL`, `API_KEY` or `TOKEN_FILE` → mixed kinds; `CLIENT_ID` + `CLIENT_SECRET` + GitHub stays `ClientCredentials`; `API_KEY` + GitHub stays `ApiKey`; token types (short, any case, URN; unknown rejected); a non-https GitHub URL; the GitHub names are not in `CredentialVariables`; `ToString_PrintsNoSecret` also sets `ACTIONS_ID_TOKEN_REQUEST_TOKEN`.
- **`MachineAuthenticationServiceTests`:** the exact endpoint (escaped, trailing `/` trimmed) and form, no `Authorization`, `scope` only when set; the file read again for each exchange (a rotated file sends the new token); a 900 s token reused at 13:59 and exchanged at 14:00, and again after 15 minutes (a job of 40 minutes keeps getting tokens); a target change exchanges again; the bound target over `STORYTELLER_PROJECT`; no bound target → `GetAccessTokenAsync` returns `null` with no request even when `STORYTELLER_PROJECT` is set, and `SignInAsync` falls back to `STORYTELLER_PROJECT` or throws `Configuration` without it; GitHub: URL with `&audience=` (and `?` when the URL has no query), the encoded endpoint, the bearer request token and `Accept: application/json` on the GET only (the exchange POST has no `Authorization`, and the request token appears in no other request), `value` read, a fresh GitHub token per exchange; GitHub failures → `ServiceError`; `400 invalid_request` → `Denied` without the subject token in the message, and for the GitHub source with the audience hint; `503`, `302` and a network failure → `ServiceError`; missing and empty files → `Configuration`; `SignInAsync` returns machine id, target and expiry; the identity lists the GitHub names for the GitHub source and not for a file; `RenewAccessTokenAsync` with the cached token exchanges again, with a stale token returns the newer one without a request, returns `null` for other kinds and without a target; concurrent `GetAccessTokenAsync` calls exchange once.
- **`MachineTokenRenewalHandlerTests` (new):** `401` then `200` → one resend with the new token, the first response disposed; two `401`s → one resend, the second returned; `403` → no renewal; a non-federated machine → no renewal; `ApiKey` scheme → no renewal; `StreamContent` → the original `401`; a `302` to another host followed by a `401` (the inner handler rewrites `RequestUri` and clears `Authorization`, as the redirect handler does) → no renewal and no request with a Storyteller token to that host; a renewal `AuthenticationException` propagates and the `401` is disposed.
- **Wiring (`FederatedSignInWiringTests`, new):** `Startup.ConfigureMachineSignIn` and `ConfigureStorytellerSdk` on a `ServiceCollection`, with federated settings for the GitHub source, a `MockFileSystem`, and one scripted primary handler for the named client and the typed clients. After `CommandContext.TrySetExplicitTarget("acme.billing", null)`, one SDK call through a typed client sends, in order: the GitHub `GET` with `&audience=<escaped endpoint>`, the exchange `POST` without `Authorization`, and the API call with `Bearer <exchanged>`. A variant answers the API call with `401` and asserts one renewal and one resend. This is the unit-level proof of the first acceptance criterion; a run in a real GitHub Actions job is out of reach of this change (it needs a deployed API with a GitHub issuer) and is listed as a manual check in the review.
- **`CommandContextTests`:** a federated machine binds `-p` over `STORYTELLER_PROJECT`; without a project it throws and binds nothing; it never uses `access.default.json`.
- **`AccountCommandTests`** (`Machine_Federated_*`): success with a file prints the machine, project and expiry; success with the GitHub source prints `(STORYTELLER_CLIENT_ID, ACTIONS_ID_TOKEN_REQUEST_URL, ACTIONS_ID_TOKEN_REQUEST_TOKEN)` after a GitHub `GET` and the exchange; no project → `43` and no request; exit codes `invalid_request` → `44`, `503` → `3`, network → `3`, a missing subject file → `2`, `STORYTELLER_SUBJECT_TOKEN_FILE` without `STORYTELLER_CLIENT_ID` → `2` with no request, no `STORYTELLER_API_URL` → `2`; a stored user session is not used and no device code starts.
- **`MachineCreateFederatedTests`:** the variable lines, the `STORYTELLER_API_URL` derived from `TokenEndpoint`, and the fallback with its note.
- **SDK:** the overload calls `configureHttpClient` for each of the seven clients (covered by the wiring test, which reaches a typed client through the handler).

### 11. Documentation

- **`docs/Platform/Storyteller/authentication.md`, "Machine sign-in for pipelines":**
  - the new variables in the variable table, and the `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_SCOPE` rows reworded for both kinds;
  - the sentence "The names match the `.env` snippet of the admin UI" qualified: the admin UI has no federated snippet yet;
  - a `Federated` row in the kind table;
  - a **Federation** paragraph: the exchange without `Authorization` and without redirects; the subject file read before every exchange; the GitHub source, the GUID rule, and its audience, which must equal the rule's audience byte for byte (base URL and the case of organization and project); the token in memory only; renewal 60 s before `expires_in` and once after a `401`;
  - the target rule: `-p` or `STORYTELLER_PROJECT` is required, as the credential has no project;
  - the difference between `STORYTELLER_TOKEN_FILE` (a Storyteller token, sent as it is) and `STORYTELLER_SUBJECT_TOKEN_FILE` (a platform token, exchanged);
  - `sform account` exchanging for a federated machine;
  - the exit-code rows extended: subject file missing or empty → `2`, GitHub ID token request failed → `3`, exchange refused → `44`;
  - a GitHub Actions example (`permissions: id-token: write`, `STORYTELLER_API_URL`, `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID`; no secret, no file) and a Kubernetes example (a projected `serviceAccountToken` with `audience` = the machine's `TokenEndpoint`, and `env` with `STORYTELLER_SUBJECT_TOKEN_FILE=/var/run/secrets/storyteller/token`, `STORYTELLER_CLIENT_ID`, `STORYTELLER_PROJECT`, `STORYTELLER_API_URL`).
- **`authentication.md`, elsewhere:** the `machine create` bullet of "Machine access with workload identity federation" (around line 506) mentions the pipeline variables; in "Setting up a platform", the GitHub Actions and Kubernetes subsections point to the `sform` variables; in "Not available yet", the `sform` part of the federation bullet goes, and the C# SDK helper part stays.
- **`docs/42for.net/cli/sform.md`, "Sign in from a pipeline":** a `Federated` row, the project rule, and the same GitHub Actions and Kubernetes examples, shortened.
- **`src/Platform/Storyteller/Sdk.NSwag/README.md`:** the `configureHttpClient` overload.
- A review per phase in `docs/Platform/Cli/specs/reviews/`.

Do not edit this spec to match later deviations; record those in the review.

### Phases

1. **Phase 1 — Federated sign-in:** sections 1–5 with their tests: the variables, the kind, and the exchange in `MachineAuthenticationService`. The `DescribeKind` case of section 7 lands here too, so `sform account` never calls a federated machine "a token file".
2. **Phase 2 — Commands, renewal and documentation:** sections 6–9 and 11 with their tests: target binding, `sform account`, the `401` renewal, `machine create`, and the docs.

### Out of scope

- A `.env` snippet for `sform` in the admin UI's `FederatedSetupDialog.vue` (the CodeRabbit plan agrees; it can follow with the same names).
- A custom audience for the GitHub source: the rule must name the token endpoint. A rule with another audience can still use a file that the job writes, at the cost of renewing it itself.
- Built-in sources for other platforms (Azure IMDS, AWS STS, Google metadata, Vercel); they write a file or use a projected token.
- `401` renewal for client credentials and token files.
- A C# SDK helper that exchanges and caches the Storyteller token.
- `-p` on `sform account`.
