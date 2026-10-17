# Non-interactive Sign-in for sform

Linear: [424-45](https://linear.app/424fun/issue/424-45/add-non-interactive-cli-sign-in).

Line numbers refer to the tree this plan was written against. No application code is changed by this document.

## Problem

`sform account` signs in with a device code only. Someone has to open a browser and type a code, so `sform` cannot run in a CI/CD pipeline or a GitHub Action. The platform already issues machine credentials (`ApiKey`, `Certificate`, `CertificateAndApiKey`, `ClientCredentials`), but `sform` cannot use any of them.

The issue asks for:

- a non-interactive sign-in with machine access: API key, certificate, certificate with API key, or client credentials;
- `sform` reads `STORYTELLER_API_KEY`, the client credentials, or `STORYTELLER_TOKEN_FILE` from the environment.

## Current State

### Sign-in

- **Contract.** `IAuthenticationService` (`src/Platform/Cli/src/Authentication/IAuthenticationService.cs`) has four members: `GetSignedInUserAsync`, `GetAccessTokenAsync`, `LoginWithDeviceCodeAsync`, and `LogoutAsync`. None of them is a machine sign-in.
- **Selector.** `AuthenticationServiceSelector` is the registered implementation. On first use it resolves the provider through `IAuthenticationConfigurationResolver`. The order is:
  1. `authentication.provider` in `app.config.json`;
  2. the `auth.discovery.json` cache;
  3. the anonymous `GET v1/auth/configuration`.

  It then creates `AuthKitAuthenticationService` or `EntraIdAuthenticationService`. Both keep a user session on disk (`~/.42for.net/authkit.cache` or `msal.cache`).
- **Account commands.**
  - `AccountCommand` (`Commands/Account/AccountCommand.cs`, lines 52-111) calls `GetSignedInUserAsync`. On null it calls `LoginWithDeviceCodeAsync`, then `IAccessApiClient.GetAccountAsync`.
  - `AccountLogoutCommand` clears the stored session.
  - `AccountRegisterCommand` and `AccountSetCommand` call `GetAccountAsync` first.
- **No environment variables.** The host is a bare `HostBuilder` (`Program.cs`, line 125). `Startup.ConfigureApplication` adds only `app.config.json`, `access.default.json`, and `editor.config.json`. Nothing in `src/Platform/Cli` reads an environment variable.

### SDK transport

- **Token factory.** `Startup.ConfigureStorytellerSdk` (lines 121-145) registers `SdkConfiguration`. Its `AccessTokenFactory` calls `IAuthenticationService.GetAccessTokenAsync()` and returns `""` on any exception. A failed sign-in therefore reaches the API with no `Authorization` header and ends as a `401`.
- **Authorization header.** `SdkRequestHelper.ApplyAuthorization` (`src/Platform/Storyteller/Sdk.NSwag/src/ApiSdk.partial.cs`, lines 104-119) builds the header from the factory value:
  - a value starting with `"ApiKey "` becomes `Authorization: ApiKey <key>`;
  - any other value becomes `Authorization: Bearer <token>`;
  - an empty value returns early, before anything else.
- **Client certificate.** `ISdkConfiguration.ClientCertificate` is added to the `HttpClientHandler` of every typed client (`ServicesCollectionExtensions.cs`, lines 38-56). A certificate without a private key throws.
  - The handler is built when a typed client is resolved, which happens inside the constructor of a command. The certificate must be set before that.
  - Only `MachineRenewCommand` sets it today.
- **Plain HTTP.** Nothing sends the certificate over plain HTTP. So `Certificate` machines cannot be tried against the local Functions host on `http://localhost:7071`.

### What the API expects from a machine

Sources: `Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`, `BearerAuthenticationMiddleware.cs`, and `docs/Platform/Storyteller/authentication.md`.

| Kind | Request | Identity |
| --- | --- | --- |
| `ApiKey` | `Authorization: ApiKey 2s.<base64url(org:project:machineId)>.<secret>` | from the key |
| `Certificate` | client certificate in the TLS handshake; the development host reads base64 DER from `X-ARR-ClientCert` when `MachineAuth:IsCertificateHeaderFromClientAllowed` is `true` (`local.settings.json`) | URI SAN `spiffe://<trust domain>/org/{org}/project/{project}/machine/{id}` |
| `CertificateAndApiKey` | both; a shared certificate (`…/shared/{label}`) is accepted only together with a key of the same project | certificate, or the key with a shared certificate |
| `ClientCredentials` | `Authorization: Bearer <JWT>` from the identity provider's token endpoint | client ID (= machine ID) |

- **Token URL and scope.** `sform account machine create` prints them (`MachineCreateCommand.cs`, lines 159-181). The API returns them only at creation.

  | Provider | Token URL | Scope |
  | --- | --- | --- |
  | AuthKit | `{AuthKitDomain}/oauth2/token` | none |
  | Entra ID | `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` | `api://{clientId}/.default` |
  | Keycloak | `{issuer}/protocol/openid-connect/token` | none |

- **Discovery.** It describes the *user* provider only. It exposes `AuthKitDomain`.
- **Client ID formats.**
  - AuthKit: `client_…`.
  - Keycloak: `42.sform.{org}.{project}.{guid}`.
  - Entra ID: a GUID.
- **Machine scope.** Machines are scoped to one project.
- **User-only operations.** These answer `401` to a machine: account, access points, machine management, shared certificates.
- **No whoami.** No endpoint returns the caller's identity.
- **Plain HTTP outside development.** It is refused only after the request, credentials included, has arrived (`TransportSecurityMiddleware.cs`).
- **Risk (server, not changed here).** `TryExtractCertificate` returns null whenever `IsCertificateHeaderFromClientAllowed` is `false`, the default. That includes the header App Service injects. Certificate kinds in a deployed API therefore depend on that setting.

### Target project

- **Resolution.** `CommandContext` (`Commands/CommandContext.cs`) takes the organization, project, and view from `AccessDefaultOptions`. That is the `access` section, written to `access.default.json` by `sform account set`.
  - It falls back to `house.main` and `default`.
  - `-p|--projectKey` and `-v|--view` override it per command through `TrySetExplicitTarget`, which `BaseContextCommand.ExecutePreconditionsAsync` calls.
- **Where the file lives.** `access.default.json` sits in the tool's own folder, so it survives between jobs on a self-hosted runner. A fresh install in CI has none.

### Errors

- **Exit codes.** `Program.Main` maps `AuthenticationException` to exit `44` (`WARNING_UNAUTHORIZED_ACCESS`). It also catches `UnauthorizedAccessException`, which the NSwag SDK never throws.
- **Uncaught 401 and 403.** An uncaught `ApiException` with `401` or `403` ends in `Total mayhem, the tool is broken!` and exit `1`.
- **401 caught by commands.** Many commands catch every `ApiException`:
  - The schema and configuration commands print `Error occurred: …` and return `1`.
  - The key/value commands return `44`.

  A wrong API key in a pipeline looks like a crash.
- **Exceptions after the host is disposed.** `Host.RunCommandLineApplicationAsync()` disposes the host before a command's exception reaches the `catch` blocks of `Program.Main`. Services cannot be resolved there.

## Proposed Changes

Machine sign-in lives in the CLI (`src/Platform/Cli/src/Authentication/`). The SDK gets one small addition (section 10). The API is not changed.

### 1. Environment variables

`sform` reads these variables once per process. Values are trimmed, and an empty value counts as unset. The names reuse `STORYTELLER_API_URL` and `STORYTELLER_API_KEY` exactly as the admin UI's `.env` snippet writes them (`OneTimeSecretDialog.vue`, line 73).

| Variable | Meaning |
| --- | --- |
| `STORYTELLER_API_URL` | API base URL including `/api`. Required for a machine sign-in, where it replaces `general.baseUrl`. |
| `STORYTELLER_PROJECT` | Target `<organization>.<project>`, the same format as `-p`. |
| `STORYTELLER_VIEW` | Target view. |
| `STORYTELLER_API_KEY` | Machine API key (`2s.…`). A leading `ApiKey ` is removed. |
| `STORYTELLER_CERTIFICATE_FILE` | Path to the machine's PKCS#12 file (`.pfx`). |
| `STORYTELLER_CERTIFICATE_PASSWORD` | Password of that file. Optional. |
| `STORYTELLER_CLIENT_ID` | Client ID of a `ClientCredentials` machine. |
| `STORYTELLER_CLIENT_SECRET` | Its client secret. |
| `STORYTELLER_TOKEN_URL` | Token endpoint. Optional for AuthKit machines. |
| `STORYTELLER_TOKEN_SCOPE` | Scope of the token request. Optional; Entra ID needs `api://<api app>/.default`. |
| `STORYTELLER_TOKEN_FILE` | Path to a file that holds a bearer token, re-read before every request. |

- **Credential variables.** Every variable except `STORYTELLER_API_URL`, `STORYTELLER_PROJECT`, and `STORYTELLER_VIEW` is a credential variable.
- **Machine mode.** When any credential variable is set, `sform` is in machine mode: it signs in as that machine and never uses the stored user session or the device code.
- **User mode.** Without credential variables, `sform` is in user mode and works as today. `STORYTELLER_API_URL` is ignored there, so a stored user session is never sent to another host. `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` still apply (section 8).
- **Names.** The names live in `Authentication/StorytellerVariables.cs`.

### 2. Choosing the credential (`MachineSignInSettings`)

```csharp
public enum MachineSignInKind { ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, TokenFile }

// A class, not a record: a generated ToString would print the secrets.
public sealed class MachineSignInSettings
{
    public string? ApiUrl { get; init; }
    public string? Project { get; init; }
    public string? View { get; init; }
    public string? ApiKey { get; init; }
    public string? CertificateFile { get; init; }
    public string? CertificatePassword { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? TokenUrl { get; init; }
    public string? TokenScope { get; init; }
    public string? TokenFile { get; init; }

    public bool IsConfigured { get; }                         // any credential variable is set
    public IReadOnlyList<string> CredentialVariables { get; } // names of the set credential variables, for messages

    public static MachineSignInSettings FromEnvironment(Func<string, string?> getVariable);

    // Throws AuthenticationException(Configuration) when the variables do not describe one usable credential.
    public MachineSignInKind GetKind();
}
```

| Credential variables set | Kind |
| --- | --- |
| `STORYTELLER_API_KEY` | `ApiKey` |
| `STORYTELLER_CERTIFICATE_FILE` (and optionally `…_PASSWORD`) | `Certificate` |
| both of the above | `CertificateAndApiKey` |
| `STORYTELLER_CLIENT_ID` and `STORYTELLER_CLIENT_SECRET` (and optionally `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE`) | `ClientCredentials` |
| `STORYTELLER_TOKEN_FILE` | `TokenFile` |

`GetKind` throws `AuthenticationException` with the new reason `AuthenticationFailureReason.Configuration`:

| Case | Message |
| --- | --- |
| Variables of more than one group, e.g. key and token file | `Machine sign-in variables of different kinds are set: STORYTELLER_API_KEY, STORYTELLER_TOKEN_FILE. Set the variables of one kind only.` |
| Only one of client ID and secret, or token URL/scope alone | `Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_SECRET is missing.` |
| Password without a certificate file | `STORYTELLER_CERTIFICATE_PASSWORD is set without STORYTELLER_CERTIFICATE_FILE.` |
| No API URL | `Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.` |
| API URL not absolute, not `http(s)`, or `http` to a host that is not loopback | `STORYTELLER_API_URL must be an absolute https URL; http is allowed only for localhost.` |
| `STORYTELLER_TOKEN_URL` with the same problem | `STORYTELLER_TOKEN_URL must be an absolute https URL; http is allowed only for localhost.` |
| `STORYTELLER_PROJECT` without a `.` | `STORYTELLER_PROJECT must be <organization>.<project>.` |

### 3. Machine identity from the credential

```csharp
public sealed record MachineIdentity(
    MachineSignInKind Kind,
    IReadOnlyList<string> Variables,
    string? MachineId,
    string? Organization,
    string? Project,
    DateTimeOffset? TokenExpiresAt);
```

`MachineIdentity` holds no secret.

- **API key** (`Authentication/MachineApiKey.cs`).
  - The parser is a copy of `StructuredApiKey.TryParse` (`Storyteller/Access.ApiKeys/src/StructuredApiKey.cs`): `2s.<base64url(organization:project:machineId)>.<secret>`. The CLI does not reference `Access.ApiKeys`, which depends on `Backend.Core`.
  - A key in another format is still sent, but gives no identity.
- **Certificate** (`Authentication/MachineCertificate.cs`).
  - Loading: `X509CertificateLoader.LoadPkcs12(bytes, password)`, with the bytes from `IFileSystem.File.ReadAllBytes`. It uses the default key storage flags, because Windows cannot use ephemeral keys for TLS client authentication.
  - Identity: read from the URI SAN with `System.Formats.Asn1`, as `CertificateIdentity.TryParse` does on the server, for any trust domain: `…/machine/{id}` or `…/shared/{label}`.
  - Errors (reason `Configuration`):
    - the file is missing;
    - the password is wrong or the file is not PKCS#12: `The certificate in STORYTELLER_CERTIFICATE_FILE could not be loaded; check the file and STORYTELLER_CERTIFICATE_PASSWORD.`;
    - the certificate has no private key;
    - a shared certificate without an API key: `A shared certificate signs in only together with its machine's API key; set STORYTELLER_API_KEY too.`
- **Certificate and API key.**
  - With a machine certificate, the organization, project, and machine ID must match the key's.
  - With a shared certificate, the organization and project must match.
  - Otherwise: `The certificate in STORYTELLER_CERTIFICATE_FILE belongs to acme.billing but the API key to acme.web.` (the API would answer `401`).
- **Client credentials.** The machine ID is the client ID. No project is known.
- **Token file.** No identity.
  - `TokenExpiresAt` is the unverified JWT `exp` when the file holds a JWT.
  - The parser moves from `AuthKitAuthenticationService.ReadExpiry` to `Authentication/JwtExpiry.cs`, used by both services. The existing `ReadExpiry` test moves with it.

### 4. `MachineAuthenticationService`

```csharp
public interface IMachineSignIn
{
    // True when a credential variable is set; sform then never uses the stored user session.
    bool IsConfigured { get; }

    MachineSignInSettings Settings { get; }

    // What the credential tells about the machine, without network calls. Null when no machine
    // credential is set or it is invalid; SignInAsync reports why.
    MachineIdentity? TryGetIdentity();

    // The certificate of STORYTELLER_CERTIFICATE_FILE when it loads and has a private key, else null.
    X509Certificate2? TryGetCertificate();

    // Validates the variables; for client credentials and token files it also gets the token.
    // Throws AuthenticationException.
    Task<MachineIdentity> SignInAsync(CancellationToken cancellationToken = default);
}

public sealed class MachineAuthenticationService : IAuthenticationService, IMachineSignIn, IDisposable
```

- **Constructors.** The public one takes `MachineSignInSettings`, `IFileSystem`, `IHttpClientFactory`, and `IAuthenticationConfigurationResolver`. An `internal` one adds a `TimeProvider` for tests.
- **Loading.** The API key and the certificate are loaded once, lazily. The result is cached, including any error.
- **Disposal.** The certificate is disposed with the service.

| `IAuthenticationService` member | Machine behaviour |
| --- | --- |
| `GetSignedInUserAsync` | `SignInAsync`, then `new SignedInUser(MachineId ?? "machine", "machine <id> (<org>.<project>)", null)` |
| `GetAccessTokenAsync` | <ul><li>`ApiKey`, `CertificateAndApiKey`: `"ApiKey <key>"`.</li><li>`Certificate`: validates and returns `null`, so no `Authorization` header is sent.</li><li>`ClientCredentials`: cached token (section 5).</li><li>`TokenFile`: the token in the file (section 6).</li><li>Invalid settings throw.</li></ul> |
| `LoginWithDeviceCodeAsync` | throws `AuthenticationException(Configuration, "sform signs in with the machine credentials in STORYTELLER_…; unset them to sign in as a user.")` |
| `LogoutAsync` | does nothing; nothing is stored |

### 5. Client credentials

**Token URL.**

- `STORYTELLER_TOKEN_URL` is used when set.
- Otherwise the URL is derived as `{AuthKitDomain}/oauth2/token` from `IAuthenticationConfigurationResolver.ResolveAsync()`. This is the fallback `MachineCreateCommand` uses, and it applies only when all of these hold:
  - the provider is AuthKit;
  - `STORYTELLER_CLIENT_ID` starts with `client_`, the AuthKit format;
  - `AuthKitDomain` is `https`.

  These conditions keep a Keycloak or Entra ID secret from being sent to WorkOS. They also stop an answer from an unauthenticated discovery call from choosing where the secret goes.
- Otherwise the sign-in fails: `Set STORYTELLER_TOKEN_URL to the token URL that sform account machine create printed.` (reason `Configuration`).

**Request.**

- `POST` the form `grant_type=client_credentials`, `client_id`, `client_secret`, and `scope`. `scope` is sent only when `STORYTELLER_TOKEN_SCOPE` is set.
- The secret is in the form (client_secret_post), as in the `curl` line `machine create` prints.
- The named client is `nameof(MachineAuthenticationService)`. It is registered with `AllowAutoRedirect = false`, so a redirect cannot resend the secret elsewhere.
- No SDK header or certificate goes to the identity provider.

**Response.**

- `access_token` is required.
- `token_type`, when present, must be `Bearer` (any case).
- The token's lifetime is `expires_in` seconds. Without it, the JWT `exp` applies. Without that, 5 minutes.
- The token is kept in memory for the process only, never on disk. It is renewed 60 seconds before it expires; a `SemaphoreSlim` serializes renewals.

**Errors.**

| Case | Reason | Message |
| --- | --- | --- |
| `400` or `401` from the token endpoint | `Denied` | `The token request to {url} was rejected: {error}: {error_description}.` (OAuth fields, or `message`) |
| other status, including a redirect | `ServiceError` | `The token request to {url} failed with {status}.` |
| network error or timeout | `ServiceError` | `The token endpoint {url} could not be reached.` |
| no `access_token`, or a type other than bearer | `ServiceError` | `The token endpoint {url} returned no bearer token.` |

Messages never contain the secret or a token.

### 6. Token file

- **Reading.** `STORYTELLER_TOKEN_FILE` is read through `IFileSystem` on every `GetAccessTokenAsync`. A token that a workload rotates is therefore picked up, for example a Kubernetes projected token or one a pipeline step writes.
- **Content.** The content is trimmed, and a leading `Bearer ` (any case) is removed.
- **Validation.** The token is sent as is; the API validates it like any bearer token.
- **Errors (reason `Configuration`).**
  - A missing file: `The token file {path} in STORYTELLER_TOKEN_FILE does not exist.`
  - An empty file: `The token file {path} in STORYTELLER_TOKEN_FILE is empty.`

### 7. Selection (`AuthenticationServiceSelector`)

- **Machine mode.** The selector gets `MachineAuthenticationService` in its constructor. When `IsConfigured` is true, every member delegates to it, and neither user provider nor the stored session is created. Discovery runs only for the AuthKit token URL fallback.
- **User mode.** Otherwise it behaves as today.

### 8. Target project (`CommandContext`)

`CommandContext` gets `IMachineSignIn` and resolves the target itself. `AccessDefaultOptions` is not post-configured, so `sform account set` keeps writing only what is in `access.default.json`.

**Organization and project.** The first source that applies wins:

1. `-p` on the command (`TrySetExplicitTarget`);
2. `STORYTELLER_PROJECT`;
3. machine mode: the organization and project of the credential (`TryGetIdentity`);
4. user mode only: `access.default.json`;
5. user mode only: `house.main`.

**View.** The first source that applies wins:

1. `-v`;
2. `STORYTELLER_VIEW`;
3. the view in `access.default.json`, only when the organization and project also came from that file;
4. `default`.

**Checks in `TrySetExplicitTarget`.** It runs for every context command. It throws `AuthenticationException(Configuration)`, which ends with exit `2` (section 11), when:

- `STORYTELLER_PROJECT` has no `.` and `-p` is not given (both modes): `STORYTELLER_PROJECT must be <organization>.<project>.`;
- in machine mode, no project is known: `No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project> or pass -p.`;
- in machine mode, the target differs from the project of the credential: `The API key in STORYTELLER_API_KEY belongs to acme.billing, not acme.web.` (or `The certificate in STORYTELLER_CERTIFICATE_FILE …`).

**Interface.** `ICommandContext` gets `bool IsProjectKnown { get; }`, which is false only in machine mode without a project. `sform account` uses it (section 12).

### 9. Startup wiring (`Startup.cs`)

- **Settings.** `services.AddSingleton(MachineSignInSettings.FromEnvironment(System.Environment.GetEnvironmentVariable))`.
- **Service.** `services.AddSingleton<MachineAuthenticationService>()` and `services.AddSingleton<IMachineSignIn>(sp => sp.GetRequiredService<MachineAuthenticationService>())`.
- **Token client.** `services.AddHttpClient(nameof(MachineAuthenticationService))` with a primary handler that has `AllowAutoRedirect = false`.
- **Base URL.** `AddOptions<GeneralOptions>().PostConfigure<MachineSignInSettings>(...)` replaces `BaseUrl` with `STORYTELLER_API_URL` in machine mode only. Discovery and the SDK then use the same URL.
- **SDK configuration.** `ConfigureStorytellerSdk` builds `SdkConfiguration` inside the factory:
  - `BaseUrl` comes from `IOptions<GeneralOptions>`.
  - `ClientCertificate = machineSignIn.TryGetCertificate()`.
  - `ClientCertificateHeaderName = "X-ARR-ClientCert"` only when the base URL is `http` to a loopback host, because the local Functions host has no TLS handshake to carry the certificate.
  - The type stays `SdkConfiguration`, so `MachineRenewCommand` can still set its own certificate.
- **Token factory.** `AccessTokenFactory` lets `AuthenticationException` through, in both modes; any other exception still sends no header. A misconfigured machine stops before any request is sent, with the reason.

### 10. SDK: certificate header for the local host (`Sdk.NSwag`)

```csharp
public interface ISdkConfiguration
{
    // ...
    // Development only: when set together with ClientCertificate, every request carries the
    // certificate (base64 DER) in this header, for hosts without TLS (X-ARR-ClientCert).
    string? ClientCertificateHeaderName => null;
}
```

- **`SdkConfiguration`.** It gets a settable `ClientCertificateHeaderName`. The default interface member keeps other implementations source compatible.
- **Request.** `SdkRequestHelper.ApplyAuthorization` adds `request.Headers.TryAddWithoutValidation(name, Convert.ToBase64String(certificate.RawData))`. This happens independently of the token, before the early return for an empty token, so a certificate-only machine sends it too.
- **Server.** The API reads the header only with `MachineAuth:IsCertificateHeaderFromClientAllowed=true`.
- **HTTPS.** Over `https` the certificate goes in the TLS handshake, as before.

### 11. Errors and exit codes

- **New reason.** `AuthenticationFailureReason.Configuration`: the sign-in settings are missing or invalid.
- **New exit code.** `ExitCodes.ERROR_SERVICE_FAILURE = 3`: the identity provider or the token endpoint failed. A pipeline can then tell a temporary failure from a rejected credential.
- **`AuthenticationException` in `Program.Main`.** The message is printed as today, and the exit code depends on the reason:

  | Reason | Exit code |
  | --- | --- |
  | `Configuration` | `2` |
  | `ServiceError` | `3` |
  | `Denied`, `Expired`, `Cancelled` | `44` |

- **401 and 403 in `Program.Main`.** A new `catch (ApiException exception) when (exception.StatusCode is 401 or 403)` prints the existing `! You are not authorized to execute the command or you don't have access to requested resources.` and returns `44`.
  - In machine mode it adds: `  sform signed in as a machine with STORYTELLER_API_KEY. Check the credentials and the project; account, access point and machine management need a user sign-in.`
  - The variable names are captured in `InitializeHost`, because the host is disposed before the catch runs.
- **Commands that catch every `ApiException`.** These get `when (e.StatusCode != (int)HttpStatusCode.Unauthorized)`, so a `401` reaches `Program.Main`:
  - `ConfigEditCommand`, `ConfigSetCommand`, `ConfigPatchCommand` (two catches);
  - `KeyValueCommandBase`;
  - `SchemaOperations` (eight catches);
  - `StorytellerSetCommand`, `TemplateEditCommand`, `TemplateSetCommand`.

  Their handling of `403` stays, because it carries command-specific messages such as `MtlsRequired`.

### 12. `sform account` and `sform account logout`

`AccountCommand` gets `IMachineSignIn` and `ICommandContext`. `AccountLogoutCommand` gets `IMachineSignIn`.

**`sform account` in machine mode** calls `SignInAsync`. It never calls the device code, `GetAccountAsync`, or a prompt; `--browser` is ignored.

```text
# Machine sign-in
Signed in as machine 3f2c… with an API key (STORYTELLER_API_KEY).
Project acme.billing with default view.
API https://example.com/api
The API checks the credentials on the first call.
```

- **Kind text.** One of `an API key`, `a certificate`, `a certificate and an API key`, `client credentials`, or `a token file`.
- **Token expiry.** For client credentials and token files, `Token valid until {expiry:O}.` replaces the last line when the expiry is known.
- **Unknown machine ID.** It is left out: `Signed in as a machine with a token file (STORYTELLER_TOKEN_FILE).`
- **No project.** When `IsProjectKnown` is false, the command prints `No project is set: set STORYTELLER_PROJECT=<organization>.<project> or pass -p to each command.` and returns `43`, like the user flow without a default project.
- **Exit codes.**

  | Outcome | Exit code |
  | --- | --- |
  | Success | `0` |
  | `Configuration` | `2` |
  | `ServiceError` | `3` |
  | `Denied` | `44` |

  Each failure prints the exception message.

**`sform account logout` in machine mode** prints `sform uses the machine credentials in STORYTELLER_API_KEY. Nothing is stored, so there is nothing to log out; unset the variables to sign in as a user.` and returns `0`. The stored user session is left alone.

**Other account commands** are unchanged: `register`, `set`, access points, machines, and shared certificates. The API answers `401`, which section 11 reports. `AccessPointListCommand` keeps reading `401` as "not registered"; it is a user command.

### 13. `sform account machine create`: pipeline variables

`MachineCreateCommand` gets `IOptions<GeneralOptions>` and prints, after its current output:

```text
To sign in sform in a pipeline, set:
  STORYTELLER_API_URL=https://example.com/api
  STORYTELLER_API_KEY=<access key above>
```

| Kind | Lines after `STORYTELLER_API_URL` |
| --- | --- |
| `ApiKey` | `STORYTELLER_API_KEY=<access key above>` |
| `Certificate` | `STORYTELLER_CERTIFICATE_FILE={written file}`, then `STORYTELLER_CERTIFICATE_PASSWORD=<certificate password above>` when a password was returned |
| `CertificateAndApiKey` | the key line, then the certificate lines. With `--shared-certificate`: `STORYTELLER_CERTIFICATE_FILE=<shared certificate .pfx>` and `STORYTELLER_CERTIFICATE_PASSWORD=<its password>` |
| `ClientCredentials` | `STORYTELLER_PROJECT={org}.{project}`, `STORYTELLER_CLIENT_ID={id}`, `STORYTELLER_CLIENT_SECRET=<client secret above>`, `STORYTELLER_TOKEN_URL={token url}` (when known), `STORYTELLER_TOKEN_SCOPE={scope}` (when set) |

- **Secrets.** Each is shown as a placeholder that points at the value printed above, so it is not printed twice.
- **Existing output.** The existing lines stay as they are, including the `curl` line with `$CLIENT_SECRET`.

### 14. Tests

`src/Platform/Cli/test` (xUnit, Shouldly, Moq):

- **`Authentication/MachineSignInSettingsTests`:**
  - reading, trimming, and empty values;
  - the kind for each combination;
  - every invalid combination and URL rule, with its message;
  - `IsConfigured` without credentials;
  - `ToString` prints no secret.
- **`Authentication/MachineApiKeyTests`:** a structured key, the `ApiKey ` prefix, and other formats.
- **`Authentication/MachineAuthenticationServiceTests`:** self-signed certificates with a SPIFFE SAN, created in the test and exported to PKCS#12 in a `MockFileSystem`.
  - **API key:** the token and the identity.
  - **Certificate:**
    - no token, and the identity from the SAN;
    - a wrong password;
    - no private key, where `TryGetCertificate` returns null;
    - a shared certificate alone;
    - a key and certificate mismatch.
  - **Client credentials:**
    - form fields, with the scope only when set;
    - caching and renewal with `ManualTimeProvider`;
    - `expires_in`, then the JWT `exp`, then the fallback;
    - rejected, failed, and unreachable endpoints, with their reasons;
    - the AuthKit fallback only for `client_` IDs and `https` domains;
    - a missing URL for other providers.
  - **Token file:** re-read on each call, `Bearer ` removed, a missing or empty file.
  - **Other members:** the device code throws, and logout does nothing.
- **`Commands/CommandContextTests`:** the precedence and the checks of section 8.
- **`AuthenticationServiceSelectorTests`:** machine mode never resolves the provider. Existing tests get a machine service without variables.
- **`Commands/AccountCommandTests` and `Commands/AccountLogoutCommandTests`:**
  - the machine mode output and exit codes;
  - `LoginWithDeviceCodeAsync` and `GetAccountAsync` are never called.
- **`Commands/MachineCreateCommandTests`:** the pipeline lines for each kind.
- **`Sdk/SdkCertificateHeaderTests`:** a real `AccessApiClient` over `ScriptedHttpHandler`. `RecordedRequest` gets the request headers. Sdk.NSwag has no test project, and the CLI is its only consumer.
  - `X-ARR-ClientCert` is sent only when a header name and a certificate are set.
  - It is sent with no token as well.

### 15. Documentation

- **`docs/Platform/Storyteller/authentication.md`:** a new `### Machine sign-in (CI/CD)` under `## Signing in with sform`.
  - It covers the variables, the kinds, the target project, the token URL fallback, the token file, the certificate header, the exit codes, and that nothing is stored.
  - It is cross-linked from the machine kinds table and the client credentials section.
- **`docs/42for.net/cli/sform.md`:** `## Sign in` and `## Sign in from a pipeline` replace the stub. The pipeline section has a GitHub Actions example.
- **`docs/42for.net/install.md`:** a pointer to the pipeline sign-in.
- **`src/Platform/Storyteller/Sdk.NSwag/README.md`:** `ClientCertificateHeaderName`.

Do not edit this spec to match later deviations; record those in the review.

### Phases

1. **Phase 1 — Machine sign-in:** sections 1–11 with their tests.
2. **Phase 2 — Commands and documentation:** sections 12, 13, and 15 with their tests.

### Out of scope

- Workload identity federation and an RFC 8693 token exchange (424-46). `STORYTELLER_TOKEN_FILE` is the hook a federated token can use later.
- A whoami endpoint in the API.
- The server's handling of `X-ARR-ClientCert` with `IsCertificateHeaderFromClientAllowed=false` (see Current State).
- `sform account machine renew` without its password prompt.
- A `sform` snippet in the admin UI's one-time secret dialog.
- Keycloak as a user sign-in provider of `sform`.
