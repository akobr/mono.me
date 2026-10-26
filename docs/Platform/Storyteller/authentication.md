# User Authentication

Storyteller accepts user bearer tokens from one identity provider per deployment: **Microsoft Entra ID** or **WorkOS AuthKit**. The provider only proves who the caller is, and a user token needs one scope, `User.Impersonation`. What a user may do in organizations and projects is decided by Storyteller's own account roles in Cosmos.

Machine credentials (structured `2s.` API keys, mTLS certificates and [workload identity federation](#machine-access-with-workload-identity-federation)) do not depend on the user provider. They work the same under either one. A federated machine has no secret: it exchanges a token of its own platform (GitHub Actions, Kubernetes, Vercel, Azure managed identity, AWS, Google Cloud) for a short-lived Storyteller token. Which kinds a project's machines may use is set by its [machine authentication policy](#machine-authentication-policy).

## Selecting the provider

`Auth:Provider` picks the provider when the Functions host starts.

| Value | Behaviour |
|---|---|
| missing or empty | Entra ID. Existing deployments keep working without a change. |
| `EntraId` | Entra ID. |
| `AuthKit` | WorkOS AuthKit. |
| anything else | The host fails to start. |

The value is case-insensitive. Settings are validated at startup, so a missing required value stops the host instead of failing on the first request.

### Entra ID settings

| Key | Required | Notes |
|---|---|---|
| `Auth:TenantId` | yes | Tenant used for the OpenID metadata. |
| `Auth:ClientId` | yes | API application ID. Tokens must have the audience `api://{ClientId}` or `{ClientId}`. |
| `Auth:AppRoles:<MachineScope>` | no | The app role ID of each machine scope, for example `Auth:AppRoles:AnnotationRead`, used by Entra ID machine access. See [Entra ID app registrations](#entra-id-app-registrations). |

### AuthKit settings

| Key | Required | Default | Notes |
|---|---|---|---|
| `Auth:AuthKit:ClientId` | yes | | WorkOS client ID (`client_…`). |
| `Auth:AuthKit:Issuer` | yes | `https://api.workos.com/` | Must match the token's `iss`. Use your custom auth domain if one is configured. |
| `Auth:AuthKit:JwksUri` | no | `{ApiBaseUrl}/sso/jwks/{ClientId}` | Must be HTTPS. |
| `Auth:AuthKit:ApiBaseUrl` | no | `https://api.workos.com` | Used to build the default JWKS URL. Must be HTTPS. |
| `Auth:AuthKit:Audience` | no, but set it in production | | When set, tokens must carry this `aud`. When empty, the audience is not checked. |
| `Auth:AuthKit:DefaultUserScopes:*` | no | none | Scopes given to every signed-in user. Only `User.Impersonation` counts, see [Scopes for AuthKit users](#scopes-for-authkit-users). |
| `Auth:AuthKit:PermissionMap:<slug>` | no | none | Maps a WorkOS permission slug to exactly one Storyteller scope: `User.Impersonation` for users, or a [machine scope](#scopes-of-machines). |
| `Auth:AuthKit:ApiKey` | no | | WorkOS management key (`sk_…`). Used to look up a user's email and name at account registration when the token lacks them, and to send invitation emails. Keep it in Key Vault, never in plain settings. |
| `Auth:AuthKit:RequireVerifiedEmail` | no | `true` | Accepting an invitation needs a verified email. When the token has no `email_verified` claim, the email counts as verified only if this is `false`. |
| `Auth:AuthKit:AuthKitDomain` | no | | `https://<subdomain>.authkit.app` or your custom domain. Enables the AuthKit OAuth flows in the OpenAPI document and is returned by the discovery endpoint. Together with `MachineOrganizationId` it turns on M2M machine access. |
| `Auth:AuthKit:MachineOrganizationId` | no | | WorkOS organization (`org_…`) that owns the M2M applications Storyteller creates. Setting it (with `AuthKitDomain`) turns on M2M machine access, which then also requires `ApiKey`. |

Example `local.settings.json` values:

```json
"Auth:Provider": "AuthKit",
"Auth:AuthKit:ClientId": "client_01H...",
"Auth:AuthKit:Audience": "https://storyteller.42for.net",
"Auth:AuthKit:DefaultUserScopes:0": "User.Impersonation"
```

## How AuthKit tokens are checked

`BearerAuthenticationMiddleware` runs after the machine-credential middleware and hands `Authorization: Bearer` tokens to `AuthKitBearerTokenValidator`:

* The signature must be **RS256**, using a key from the AuthKit JWKS. The key set is cached and refreshed. A token signed with an unknown key ID returns 401 and triggers a refresh of the key set, so a rotated key is picked up.
* `iss` must equal `Auth:AuthKit:Issuer`. The comparison is case-sensitive, and only a trailing `/` may differ.
* `aud` is checked only when `Auth:AuthKit:Audience` is set.
* `exp` and `nbf` are enforced with a 30-second clock skew.
* `sub` must be a WorkOS user ID (`user_…`). Other subjects are rejected.

An invalid token gets **401**. If the JWKS cannot be fetched, the response is **503** with `Retry-After: 5`, so clients keep their token and retry.

Debug builds of the Functions app (`DEV_AUTH`) decode bearer tokens without checking them. They still run the configured provider's claim normalization. [Storyteller tokens](#storyteller-tokens) of federated machines are the exception: their signature, issuer, audience and lifetime are always checked. For users, Debug builds also skip the `User.Impersonation` check and the project role check, so they also serve requests without an identity, which become `System`. The view transfer routes are the exception: they answer `401` to a request without credentials in every build. The checks of machines (the machine document, the credential policy, the annotation restriction and the [scopes](#scopes-of-machines), stored and carried), including the per-call checks of federated machines, run in every build, and so does the refusal of machines on the routes for users only.

## Claims seen by the API

AuthKit claims are mapped to the Entra-style claims the endpoints already read.

| Claim | Source in the AuthKit token |
|---|---|
| `sub` | `sub` (`user_…`). This is the Storyteller account ID. |
| `name` | `name`, else `first_name` + `last_name`, else `given_name` + `family_name`, else `email`. Blank values are skipped. |
| `preferred_username` | `preferred_username`, else `email`. |
| `scp` | `DefaultUserScopes` plus each `permissions` entry mapped through `PermissionMap`, as one space-separated value. For a user only `User.Impersonation` counts. |
| `org_id`, `sid`, `permissions`, `role`, … | Passed through unchanged. |

Any `scp`, `roles` or `azp` claim in the token itself is dropped, so a JWT template cannot grant scopes directly. Permissions without a `PermissionMap` entry grant nothing, and slug lookup is case-sensitive.

Account registration (`POST v1/access/account`) needs `preferred_username` and `name`. Token claims are used first. When either is missing and `Auth:AuthKit:ApiKey` is set, Storyteller reads the user from `GET /user_management/users/{sub}` and fills only the missing values: `email` becomes the user name, and `first_name last_name` (else `email`) becomes the name. The lookup happens only at registration, and a WorkOS error fails the request with 500. Without the key, the token must carry at least `email`; see the JWT template below. Entra ID has no lookup and still needs both claims in the token.

## Scopes for AuthKit users

A user needs one scope, `User.Impersonation`: it says that the token acts for a user. The user's role on the organization or project decides everything else, under either provider. Data scopes in a user token grant nothing, and secret rights come only from the role `ContributorWithSecrets` or higher. A token without `User.Impersonation` gets `401` with no body (Debug builds skip this check). The API only logs the reason, "The token does not act for a user: the scope User.Impersonation is missing.". The [scopes of machines](#scopes-of-machines) are for machines only.

Entra users get `User.Impersonation` as the delegated scope the CLI requests (see [Discovery endpoint](#discovery-endpoint)). AuthKit has no per-API delegated scopes, so pick one of these modes:

* **Default scope (recommended).** Give every user `User.Impersonation`, and let account roles decide, as with Entra:

  ```json
  "Auth:AuthKit:DefaultUserScopes:0": "User.Impersonation"
  ```

* **Role-driven.** Leave `DefaultUserScopes` empty, define a WorkOS permission on the organization roles whose members use Storyteller, and map it to `User.Impersonation`, for example `"Auth:AuthKit:PermissionMap:storyteller:user": "User.Impersonation"`. Users need an organization membership with a role, or `permissions` is empty and the API answers `401`.

A `PermissionMap` value is exactly one scope string: `User.Impersonation`, or one of the 11 [machine scopes](#scopes-of-machines). When AuthKit machine access is on, the host checks the map at startup and does not start when a value is anything else (for example several scopes separated by spaces, or an unknown string), or when two slugs map the same machine scope. Several slugs may map `User.Impersonation`. Slugs may contain `:`. In JSON, write the slug as an object key under `PermissionMap`. In flat keys and environment variables, append it to the path (`Auth__AuthKit__PermissionMap__storyteller__annotation-read`). Linux app settings restrict which characters a name may contain, so check that your slugs can be written as setting names on your hosting plan.

## AuthKit dashboard setup

1. Note the **client ID** of the environment and set `Auth:AuthKit:ClientId`.
2. Decode a real access token (for example one issued to the AuthKit sample app) and copy its `iss` into `Auth:AuthKit:Issuer`. It is `https://api.workos.com/` unless the environment uses a custom auth domain.
3. Add a **JWT template** so tokens carry an audience and the profile fields Storyteller stores on the account:

   ```json
   {
     "aud": "https://storyteller.42for.net",
     "email": {{ user.email }},
     "email_verified": {{ user.email_verified }},
     "name": "{{ user.first_name }} {{ user.last_name }}"
   }
   ```

   Set `Auth:AuthKit:Audience` to the same `aud`. `email_verified` is needed to accept invitations (see [Members and invitations](access.md)). Without it, invitations can only be accepted when `Auth:AuthKit:RequireVerifiedEmail` is `false`. When a user has no first or last name, the template renders a blank or partial `name`, and Storyteller falls back as described above.
4. If you use the role-driven mode, create the permission that maps to `User.Impersonation` and assign it to organization roles. No other permission matters for users; the permissions of machines are described in [AuthKit M2M applications](#authkit-m2m-applications).
5. Enable **CLI Auth** for the environment so `sform` can use the device sign-in. Without it, `POST /user_management/authorize/device` is rejected and `sform account` reports the WorkOS message.
6. For invitation emails, set `Auth:AuthKit:ApiKey` (the management key) and point the environment's invitation link at the admin UI's accept page, for example `https://admin.42for.net/invitations/accept`. Without the key, invitations are still created, but no email is sent.

## Discovery endpoint

`GET v1/auth/configuration` is anonymous and tells clients such as `sform` how to sign users in. It returns public values only. The management key, permission map and machine settings are never included. Property names follow the API's PascalCase convention.

```json
{ "Provider": "EntraId", "ClientId": "303a7632-…", "TenantId": "common",
  "Scopes": [ "api://303a7632-…/User.Impersonation" ] }

{ "Provider": "AuthKit", "ClientId": "client_01H…", "AuthKitDomain": "https://example.authkit.app" }
```

`AuthKitDomain` is left out when it is not configured. Send the request without an `Authorization` header: an invalid bearer token is rejected with 401 before any endpoint runs.

## OpenAPI and Swagger UI

The document declares four security schemes:

| Scheme | In the document | The caller sends |
|---|---|---|
| `manual` | HTTP bearer | `Authorization: Bearer` with a user's token, a client-credentials token of the identity provider, or the Storyteller token a federated machine gets from `ExchangeMachineToken`. |
| `integrated` | OAuth2, see below | The OAuth2 sign-in of a user or of a client-credentials client. |
| `apiKey` | API key in the `Authorization` header | `Authorization: ApiKey 2s.<…>.<secret>`, the `ApiKey` scheme included in the value. |
| `mtls` | API key in the `X-ARR-ClientCert` header | The machine's client certificate in the TLS handshake, which the platform's front end passes on in `X-ARR-ClientCert` (see [Certificates](#machine-sign-in-for-pipelines)). OpenAPI 3.0 has no scheme for mutual TLS. |

Each operation lists the requirements it accepts: any one requirement object is enough, and the schemes inside one object are needed together.

| Operations | Requirements | Meaning |
|---|---|---|
| For users and machines | `{manual}`, `{integrated}`, `{apiKey}`, `{mtls}`, `{mtls, apiKey}` | A bearer token (a user, client credentials or a federated machine), the `ApiKey` kind, the `Certificate` kind, or the `CertificateAndApiKey` kind, which is also the only way a shared certificate is accepted. The 403 of the operation names the scopes a machine needs. |
| For users only | `{manual}`, `{integrated}`; `{manual}` alone for the shared certificates | Machines are refused whatever they send. |
| `RenewMachineCertificate` | `{mtls}`, `{mtls, apiKey}` | The machine's own certificate, with its API key when its kind needs both. |
| `ExchangeMachineToken`, `GetCertificateAuthority`, `GetAuthConfiguration` | none | Anonymous. |

`{integrated}` is left out wherever the scheme is removed (AuthKit without `AuthKitDomain`, below). Whether a machine's kind is accepted also depends on the project's [machine authentication policy](#machine-authentication-policy), and the policy can deliver secrets only over mTLS (`403 MtlsRequired`, see [secret delivery](key-values.md#secret-delivery)). The scheme descriptions say so; the requirements cannot.

The operations for users only are listed in `OperationAccess` (`Api.Functions/src/OpenApi/`). `MachineSecurityRequirementDocumentFilter` writes the machine requirements from that list, because `[OpenApiSecurity]` attributes can only add alternatives and cannot say `{mtls, apiKey}`. The runtime decides in the handlers; a test checks the list against the tables of the OpenAPI tests.

Swagger UI lists the four schemes under **Authorize**, and each operation shows the ones it accepts. An `apiKey` value must include the `ApiKey ` prefix. `mtls` works only against a host that accepts `X-ARR-ClientCert` from clients, such as the local Functions host.

The `integrated` OAuth2 security scheme follows `Auth:Provider`:

* **Entra ID:** the implicit and client-credentials flows against `login.microsoftonline.com`. The implicit flow offers one scope, `api://{ClientId}/User.Impersonation`; the client-credentials flow uses `api://{ClientId}/.default`.
* **AuthKit with `AuthKitDomain`:** an authorization-code flow (`{AuthKitDomain}/oauth2/authorize`, `{AuthKitDomain}/oauth2/token`, scopes `openid profile email`) and a client-credentials flow (`{AuthKitDomain}/oauth2/token`). The flows need an OAuth application registered in WorkOS for Swagger UI.
* **AuthKit without `AuthKitDomain`:** the OAuth2 scheme is removed, and only the manual bearer scheme is documented.

Known limits of the AuthKit flows today:

* Tokens from the AuthKit domain's OAuth endpoints are issued by the AuthKit domain and signed with `{AuthKitDomain}/oauth2/jwks`. The API accepts them only if `Issuer` and `JwksUri` point there, which in turn rejects device-flow tokens from `https://api.workos.com/`. The API cannot yet accept both token sources at once.
* The client-credentials flow works only for an M2M application Storyteller created (see [Machine access with client credentials](#machine-access-with-client-credentials)), in a project with the `ClientCredentials` policy.
* OpenAPI 3.0 cannot mark a flow as PKCE, and this Swagger UI does not turn PKCE on. Use a confidential OAuth application and enter its client secret in Swagger UI.

## Signing in with sform

`sform account` (aliases `access`, `login`) signs in with a device code under either provider: it prints an address and a code, waits while you confirm in a browser, then stores the session. `sform account --browser` also opens the address. Commands that call the API pick up the stored session and refresh it silently.

### Which provider sform uses

The `authentication` section of `app.config.json` decides, in this order:

1. `provider` set to `EntraId` or `AuthKit`: used as configured, together with `clientId` (and `tenantId` for Entra ID).
2. Otherwise, a cached answer of `GET v1/auth/configuration` for the current `general.baseUrl`, kept in `auth.discovery.json` next to `access.default.json`.
3. Otherwise, the discovery endpoint itself, called without a token. The answer is cached.
4. If the server has no discovery endpoint (older deployments), Entra ID with the configured `tenantId` and `clientId`, and the scope `api://{clientId}/User.Impersonation`.

```jsonc
"authentication": {
  "provider": "AuthKit",                         // EntraId | AuthKit; leave out to discover
  "clientId": "client_01H…",                     // Entra application ID or AuthKit client ID
  "tenantId": "common",                          // Entra ID only
  "authKitApiBaseUrl": "https://api.workos.com"  // AuthKit only, optional
}
```

One `sform` build therefore follows whichever provider the server at `general.baseUrl` uses. Changing `general.baseUrl` discovers again. `sform account logout` signs out and also deletes `auth.discovery.json`, so the next sign-in asks the server again after a provider switch. A cached answer also keeps the scopes it lists until then, so sign out once when the server hands out other scopes. A [machine sign-in](#machine-sign-in-for-pipelines) uses neither the section nor the cache.

### AuthKit sign-in

* `sform` calls `POST /user_management/authorize/device`, shows `verification_uri` and the user code (plus the address with the code filled in), and polls `POST /user_management/authenticate` at the interval WorkOS asks for. `slow_down` adds a second to the interval. `access_denied`, `expired_token` or the end of the code lifetime stop the sign-in with a message.
* If the user belongs to several WorkOS organizations, `sform` lists them and continues with the chosen one.
* The session (access token, refresh token, user, organization) is stored in `~/.42for.net/authkit.cache`, encrypted with DPAPI on Windows, the Keychain on macOS and libsecret on Linux.
* An access token is reused until 60 seconds before it expires. Then the refresh token is exchanged and the stored one replaced, because WorkOS rotates refresh tokens. A file lock (`authkit.cache.lockfile`) keeps two `sform` processes from using the same refresh token. If WorkOS rejects the refresh token, the session is deleted and `sform account` asks for a new sign-in.

Entra ID sign-in still uses MSAL with the cache in `~/.42for.net/msal.cache`. The same directory keeps `telemetry.json`, the random installation id of the anonymous usage data `sform` sends to PostHog. It is not a secret and is not protected; [the telemetry section of the sform page](https://42for.net/cli/sform#telemetry) lists what is sent and how to turn it off.

### Machine sign-in for pipelines

`sform` signs in without a device code when machine credentials are in environment variables, for example in a CI/CD pipeline or a GitHub Action. Setting any credential variable switches `sform` to that machine:

* It never asks anyone to sign in: no device code, no browser.
* It never uses or changes the stored user session.
* It stores no credential, no token and no discovery answer. The `authentication` section of `app.config.json` and `auth.discovery.json` do not apply to a machine.

`sform` reads the variables with the shared credential contract of the C# SDK (`StorytellerCredentialVariables`) and signs in with the SDK's `StorytellerCredential`. So one set of variables drives `sform`, `StorytellerCredential.FromEnvironment()` and the [configuration provider](configuration-provider.md#credentials), with the same names, rules and messages ([SDK readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md#machine-credentials)).

The GitHub Actions variables `ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN` are not credential variables. A job with `permissions: id-token: write` that sets no `STORYTELLER_*` credential still uses the user sign-in.

Commands keep their own confirmations, for example before overwriting an `-e` export file or deleting without `--yes`; a pipeline should not depend on them.

`sform account machine create` prints the variables for the new machine, by their canonical names. The `.env` snippet of the admin UI uses the same names. The admin UI has no snippet for a `Federated` machine yet; use the variables `machine create` prints. Every call of a job needs the matching [scopes](#scopes-of-machines) of its machine: a read the read scope, a write the write scope. A job that exports or imports a view or key/values, writes schemas or templates, deletes, restores or bulk-creates annotations, or protects or restores key/values also needs the matching management scope.

| Variable | Deprecated name | Meaning |
|---|---|---|
| `STORYTELLER_ENDPOINT` | `STORYTELLER_API_URL` | API base URL including `/api`. Required for a machine, and replaces `general.baseUrl`. A user sign-in ignores it, so a stored user session is never sent to another host. |
| `STORYTELLER_PROJECT` | – | Target `<organization>.<project>`, the same format as `-p`. |
| `STORYTELLER_VIEW` | – | Target view. |
| `STORYTELLER_KEY` | – | A machine only: the full key `@{organization}.{project}.{view}.{annotation key}` of the configuration the [provider](configuration-provider.md) of the job reads. Its project and view are a target after `STORYTELLER_PROJECT`, see the target project below. |
| `STORYTELLER_API_KEY` | – | The structured `2s.` API key, sent as `Authorization: ApiKey`. A leading `ApiKey ` scheme, in any case, is removed; `ApiKey` alone is an error. |
| `STORYTELLER_CLIENT_CERTIFICATE` | `STORYTELLER_CERTIFICATE_FILE` | The machine's PKCS#12 file (`.pfx`) that `machine create` wrote. |
| `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` | `STORYTELLER_CERTIFICATE_PASSWORD` | Its password. |
| `STORYTELLER_CLIENT_ID` | – | The client ID of a `ClientCredentials` machine, or the machine id of a `Federated` one (the `client_id` of the exchange). |
| `STORYTELLER_CLIENT_SECRET` | – | The client secret of a `ClientCredentials` machine. |
| `STORYTELLER_TOKEN_URL` | – | The token URL `machine create` printed. |
| `STORYTELLER_TOKEN_SCOPE` | – | For client credentials, the token scope `machine create` printed; Entra ID needs it. For a `Federated` machine, optional: Storyteller scopes separated by spaces, a subset of the machine's (the `scope` of the exchange), for example `Annotation.Read Configuration.Read`. |
| `STORYTELLER_TOKEN_FILE` | `STORYTELLER_SUBJECT_TOKEN_FILE`, of a `Federated` machine only | With `STORYTELLER_CLIENT_ID` and no client-credentials variable: a `Federated` machine's file holding the platform token, read again before every exchange. Without `STORYTELLER_CLIENT_ID`: a file holding a bearer token, read again before every request and sent as it is. |
| `STORYTELLER_SUBJECT_TOKEN_TYPE` | – | A `Federated` machine, optional: `jwt` (default), `id_token` or `access_token`, in any case, or the full `urn:ietf:params:oauth:token-type:` URN. |
| `ACTIONS_ID_TOKEN_REQUEST_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN` | – | Set by GitHub Actions in a job with `permissions: id-token: write`. The platform token source of a `Federated` machine without a token file. |

**Deprecated names.** The older `sform` names still work: a deprecated name is read when its canonical name is unset. Both names set to different values stop `sform` with exit code `2`, for example "STORYTELLER_ENDPOINT and STORYTELLER_API_URL are both set, to different values; set one of them." The same value in both is accepted. `STORYTELLER_SUBJECT_TOKEN_FILE` is no plain alias: set, it always makes a `Federated` machine, never a bearer token file, so it needs `STORYTELLER_CLIENT_ID`. `sform account` prints a line for each deprecated name that is set, also next to its canonical name and also when the sign-in fails, for example "STORYTELLER_API_URL is deprecated; use STORYTELLER_ENDPOINT." Other commands stay quiet about them.

**URLs.** `STORYTELLER_ENDPOINT`, `STORYTELLER_TOKEN_URL` and `ACTIONS_ID_TOKEN_REQUEST_URL` must be `https`, or `http` to `localhost` or a loopback address literal (`127.0.0.1`, `[::1]`), and carry no user info. The endpoint, which paths are appended to, has no query or fragment either. Without an endpoint, `sform` stops with "Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api." Messages name a variable as it was set, a deprecated name by its own name, and never print a secret: they print file paths, and URLs without user info, query or fragment.

The credential variables decide the kind. Variables of two kinds at once, for example an API key and a client secret, stop `sform` before any call, with a message that names them ("Storyteller credential variables of different kinds are set: …"). `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_SCOPE` belong to client credentials or to federation, whichever of the two is set. So `STORYTELLER_CLIENT_SECRET` with `STORYTELLER_SUBJECT_TOKEN_FILE` stops, and so does an API key with `STORYTELLER_CLIENT_ID`. `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` and no client-credentials variable is a `Federated` machine, as for the provider.

| Kind | Variables |
|---|---|
| `ApiKey` | `STORYTELLER_API_KEY` |
| `Certificate` | `STORYTELLER_CLIENT_CERTIFICATE` and, when the file has one, `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` |
| `CertificateAndApiKey` | all three |
| `ClientCredentials` | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, and usually `STORYTELLER_TOKEN_URL` and `STORYTELLER_TOKEN_SCOPE` |
| `Federated` | `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_FILE`, without a client-credentials variable; or, in a GitHub Actions job with `permissions: id-token: write`, `STORYTELLER_CLIENT_ID` alone when it is a GUID. Optionally `STORYTELLER_SUBJECT_TOKEN_TYPE` and `STORYTELLER_TOKEN_SCOPE`. |
| token file | `STORYTELLER_TOKEN_FILE` without `STORYTELLER_CLIENT_ID` |

**Certificates.**

* The certificate goes in the TLS handshake.
* For `http://localhost`, which has no handshake, the SDK sends it in `X-ARR-ClientCert`. The local Functions host accepts that header (`MachineAuth:IsCertificateHeaderFromClientAllowed` in `local.settings.json`).
* **The API reads a client certificate only from that header, and only when `MachineAuth:IsCertificateHeaderFromClientAllowed` is `true`.**
  * The isolated worker never sees the TLS session. In Azure App Service, the front end forwards the handshake certificate in `X-ARR-ClientCert`.
  * The setting defaults to `false`. Then `MachineAuthenticationMiddleware.TryExtractCertificate` ignores the header completely, including the one App Service injects. So no `Certificate` or `CertificateAndApiKey` machine can sign in, and no machine counts as mTLS for [secret delivery](key-values.md#secret-delivery).
  * A deployment that uses certificates must set it to `true`. App Service must also require or accept client certificates (`clientCertMode`), so that it overwrites any `X-ARR-ClientCert` header a client sends. Otherwise anyone who holds a certificate's public part could present it without its private key.
  * The mTLS spec calls the setting development-only. The code does not separate the platform's header from a client's, so that is not how it works today.
* A shared certificate signs in only together with its machine's API key.
* A certificate and an API key of different machines or projects stop `sform` before any call.
* A certificate file that does not exist, cannot be read, does not load with its password or has no private key stops `sform` with exit code `2`; the message names the file and the variable as it was set.

**Client credentials.**

* `sform` posts `grant_type=client_credentials`, `client_id`, `client_secret` and, when it is set, `scope` to the token URL.
* The token is kept in memory for the process. It is renewed 60 seconds before it expires, or halfway through a token that lives less than two minutes; the expiry is `expires_in` (at most a day), else the JWT `exp`, else five minutes. A renewal that fails before the token expires keeps the token until then.
* After a `401` of the API, `sform` gets a new token and sends the request again, once, as for a `Federated` machine below.
* Without `STORYTELLER_TOKEN_URL`, only an AuthKit client (`client_…`) works. `sform` asks `GET {STORYTELLER_ENDPOINT}/v1/auth/configuration` for the AuthKit domain, in every process, and posts to `{AuthKitDomain}/oauth2/token`, only over `https`. This keeps a Keycloak or Entra ID secret from going to WorkOS. Every other client stops with exit code `2`: "Set STORYTELLER_TOKEN_URL to the token URL that sform account machine create printed; only an AuthKit client id (client_…) discovers it." A server that does not sign in with AuthKit, an AuthKit domain that is not `https` or has a path, or a `4xx` of discovery ends with `44`; a `5xx` or a network failure of discovery with `3`.

**Token file.** The file is read before every request, so a token that another process rotates is picked up. The content is trimmed, and a leading `Bearer ` is removed; a file that holds nothing else stops with exit code `2`. Any bearer token the API accepts works, for example a Storyteller token or a client-credentials token, and it is sent as it is: `sform` never renews it.

**Token requests.** The token request of client credentials, the exchange of a `Federated` machine, AuthKit discovery and the GitHub Actions ID token request go through the SDK's own HTTP client, which sends no `Authorization` header (except the runner's request token to the runner) and follows no redirect.

* Each attempt times out after 5 seconds. A network failure, a timeout, `408`, `429` and a `5xx` other than `501` and `505` are retried up to 3 times, with exponential backoff, and `Retry-After` is honoured. A circuit breaker stops the requests for 30 seconds after half of at least three calls in a minute failed.
* A token request ends after 100 seconds, retries and `Retry-After` waits included, with exit code `3`: "The machine got no token within 100 seconds, retries and Retry-After waits included: the token endpoint, the sign-in discovery or the GitHub Actions runner did not answer, or kept failing."
* A `4xx` other than `408` and `429` ends with exit code `44`: "The token request to {url} was rejected with 401." A redirect, which is not followed, ends with `44` too ("… was answered with a redirect (302), which is not followed."), and so does a `2xx` without a bearer token ("The token endpoint {url} returned no bearer token.").
* A `408`, `429` or `5xx` after the retries ends with exit code `3` ("The token request to {url} failed with 503."), and so does a network failure ("The token request to {url} failed: {reason}").
* Messages include the `error` and `error_description` of an OAuth answer. They name URLs without user info, query or fragment, and never contain a secret.

**Federation.** A `Federated` machine exchanges a platform token for a Storyteller token at the [token endpoint](#the-token-endpoint) of the target project.

* The endpoint is `{STORYTELLER_ENDPOINT}/v1/{organization}/{project}/access/token`, with a trailing `/` of the URL removed, and the organization and project escaped, in the case the command names them.
* `sform` posts `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`, `client_id`, `subject_token`, `subject_token_type` and, when it is set, `scope`.
* `STORYTELLER_SUBJECT_TOKEN_TYPE` is sent as its URN. `jwt` fits GitHub Actions, Kubernetes, AWS and Vercel, `access_token` an Azure managed identity, and `id_token` Google Cloud.
* **Platform token from a file.** `STORYTELLER_TOKEN_FILE` (or the deprecated `STORYTELLER_SUBJECT_TOKEN_FILE`) is read again before every exchange and trimmed, so a token the platform rotates, such as a Kubernetes projected token, is picked up. A file that is missing, cannot be read or is empty stops the command with exit code `2`. Both names set to different files stop it too.
* **Platform token from GitHub Actions.** Without the file, `sform` asks the runner for a new ID token before every exchange: `GET {ACTIONS_ID_TOKEN_REQUEST_URL}&audience={token endpoint, escaped}` (the runner's URL has a query already), with `ACTIONS_ID_TOKEN_REQUEST_TOKEN` as the bearer token. The request token goes on that request only, which follows no redirect, so it reaches no other host and never the token endpoint. A redirect, a `4xx` other than `408` and `429`, or an answer without a token ends with exit code `44` ("The GitHub Actions ID token request to {runner URL} was rejected with 403."); a network failure, a timeout, `408`, `429` or a `5xx`, after the retries, with `3`. Messages name the runner's URL without its query, never the request token.
* **The GUID rule.** When neither a token file nor `STORYTELLER_SUBJECT_TOKEN_TYPE` is set, the GitHub variables make a federated machine only when `STORYTELLER_CLIENT_ID` is a GUID, as every federated machine id is, and no client-credentials variable is set. Both GitHub variables are in every step of a job with `id-token: write`, which many jobs have for other reasons, and a missing GitHub secret expands to an empty `STORYTELLER_CLIENT_SECRET`, which counts as unset. So an AuthKit (`client_…`) or Keycloak (`42.sform.…`) client id without its secret is never exchanged. `STORYTELLER_CLIENT_ID` with neither a secret nor a platform token stops with exit code `2`, and the message names both ways. An Entra ID appId is a GUID too: when the exchange of such a job is refused, the message ends with "; the GitHub Actions token of this job was requested for the audience {audience}, which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead."
* **The audience.** `sform` asks GitHub for a token whose audience is the token endpoint it builds. The server compares the token's `aud` with the rule's audience ordinally, so the rule's audience must equal that endpoint byte for byte: the same base URL as `STORYTELLER_ENDPOINT` as it is written, and the organization and project in the same case. Use the machine's `TokenEndpoint` as the audience, and the `STORYTELLER_ENDPOINT` and `STORYTELLER_PROJECT` that `machine create` prints. A rule with another audience can still use a file that the job writes, but then the job renews the file itself. A projected Kubernetes token follows the same rule: its `audience` is the rule's.
* **Renewal.** The Storyteller token is kept in memory for the process, never on disk. It is reused until 60 seconds before it expires (after 14 minutes with the default 15-minute token), or until halfway through a token that lives less than two minutes; then `sform` gets a new platform token and exchanges again, so a long job keeps working. A renewal that fails before the token expires keeps the token until then. After a `401` of the API, `sform` exchanges once more and sends the request again, once; a burst of `401`s exchanges once. A second `401` ends with exit code `44`. A `403` is never retried, and neither is the `.zip` stream of `sform story import` nor a request that a redirect sent elsewhere.
* A command exchanges only for the project it has checked. `sform account ca`, whose endpoint is anonymous, never exchanges.
* A refused exchange (`400` or `401`, for example `invalid_request` or `invalid_scope`, but also `403`, `404`, a redirect or an answer without a bearer token) ends with exit code `44`. A `503 temporarily_unavailable` or another `5xx`, `408`, `429` or a network failure ends with `3`, after the retries.

Other platforms (Azure, AWS, Google Cloud, Vercel) have no built-in source: write their token to a file and set `STORYTELLER_TOKEN_FILE` next to `STORYTELLER_CLIENT_ID`, see [Setting up a platform](#setting-up-a-platform).

`STORYTELLER_CLIENT_ID` decides what `STORYTELLER_TOKEN_FILE` holds. Without it, the file holds a token the API accepts, which `sform` sends as it is; a Storyteller token that a job writes there expires after 15 minutes, and later calls fail with `401` unless the job renews the file. With it, the file holds a platform token, which the API never accepts: `sform` exchanges it for a Storyteller token and renews that token itself.

**Target project.** The first of these that is set wins:

1. `-p` on the command;
2. `STORYTELLER_PROJECT`;
3. the organization and project of `STORYTELLER_KEY`;
4. the project of the API key or the certificate.

`access.default.json` is never used for a machine, because it outlives the job on a self-hosted runner. A command without a project stops. So does a command whose target differs from the project of the API key or the certificate, whether `-p`, `STORYTELLER_PROJECT` or `STORYTELLER_KEY` names it: "The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web." Client credentials, a token file and a `Federated` machine name no project, so they need `-p`, `STORYTELLER_PROJECT` or `STORYTELLER_KEY`. For a `Federated` machine the project also picks the token endpoint, so `-p` wins over the variables there too. The view comes from `-v`, then `STORYTELLER_VIEW`, then the view of `STORYTELLER_KEY` while the target is the project of the key, then `default`. `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` also apply to a user sign-in, ahead of `access.default.json`.

`STORYTELLER_KEY` is the variable of the configuration provider, so a job whose application reads its configuration with the provider sets it once for both. A user sign-in never reads it. `STORYTELLER_PROJECT` wins over a key of another project, and the view of that key then does not apply. For a machine the key must be a full key, as the provider parses it (`FullKey`); any other value stops every command that signs the machine in with exit code `2`, also one with `-p`: "STORYTELLER_KEY must be a full key, @{organization}.{project}.{view}.{annotation key}."

`sform account` checks the variables without a prompt. For client credentials it also gets the token, and for a token file it refuses a JWT whose `exp` has passed. For a `Federated` machine it needs `STORYTELLER_PROJECT` or `STORYTELLER_KEY` first (`sform account` has no `-p`; without them, exit code `43` and no request), and then exchanges at that project's token endpoint, asking GitHub for the platform token first when that is the source. It then prints the machine, the project, the API URL and the expiry of the token, which is the `exp` of a JWT. For a client-credentials or Storyteller token without `exp` it prints "Signed in; the expiry of the token is unknown."; for an API key, a certificate and a token file without `exp`, "The API checks the credentials on the first call." A line for each deprecated name that is set comes last. A federated machine in GitHub Actions:

```
Signed in as machine 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07 with workload identity federation (STORYTELLER_CLIENT_ID, ACTIONS_ID_TOKEN_REQUEST_URL, ACTIONS_ID_TOKEN_REQUEST_TOKEN).
Project acme.billing with default view.
API https://api.example.com/api
Token valid until 2026-10-18T14:15:00.0000000+00:00.
```

`sform account logout` has nothing to do for a machine. Account, access point, machine and shared certificate management need a user sign-in; the API answers `403` to a machine, in every build, also when it sets the description of a project (`PUT v1/access/points/{key}`) or lists projects (`GET v1/access/projects`).

| Exit code | Meaning |
|---|---|
| `0` | Signed in. |
| `2` | The variables are missing or invalid: two kinds at once, a canonical and a deprecated name with different values, a URL the rule refuses, `STORYTELLER_API_KEY=ApiKey`, a `STORYTELLER_PROJECT` or `STORYTELLER_KEY` in another format, or a client id outside AuthKit without `STORYTELLER_TOKEN_URL`. Or the target project is wrong. Or a certificate, token file or platform token file is missing, cannot be read or is empty, or the certificate does not load. |
| `3` | A token request failed after its retries: the token endpoint, AuthKit discovery or the GitHub Actions runner answered `408`, `429` or `5xx`, could not be reached, or gave no token within 100 seconds; a retry may help. |
| `43` | `sform account` only: no project is set. |
| `44` | The credentials were rejected: a token request answered with a `4xx` other than `408` and `429`, a redirect or no token (the token endpoint, for a `Federated` machine the exchange, AuthKit discovery or the GitHub Actions runner); the server does not sign in with AuthKit; the token file has expired; or the API answered `401`. A `403` the command does not handle itself ends the same way. When the server sent a message with the `403`, for example "This operation needs the scope Annotation.Manage, which the machine was not created with.", `sform` prints it and its `ErrorCode` instead of the general sentence, and no hint about the credentials. |

**Changes against the earlier machine sign-in.** `sform` signs a machine in through the SDK's credential since the [alignment with the provider contract](specs/2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md) (API 0.10). Against the earlier `sform`:

1. `STORYTELLER_ENDPOINT`, `STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` are read; `STORYTELLER_API_URL`, `STORYTELLER_CERTIFICATE_FILE`, `STORYTELLER_CERTIFICATE_PASSWORD` and `STORYTELLER_SUBJECT_TOKEN_FILE` are deprecated, and `sform account` names them. `machine create` prints the canonical names.
2. `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` is a `Federated` machine; it stopped with exit code `2`. `STORYTELLER_API_KEY=ApiKey` is an error; it was sent as the key.
3. `STORYTELLER_KEY` is a target of a machine, after `-p` and `STORYTELLER_PROJECT`.
4. A URL with user info, an endpoint with a query or fragment, and `http` to a host named `loopback` are refused.
5. A `403`, `404` or redirect of the token endpoint, and a `2xx` without a bearer token, end with `44`; they ended with `3`. So do a redirect, a `4xx` or an answer without a token of the GitHub Actions runner.
6. AuthKit discovery asks the endpoint directly; `authentication.provider` and `auth.discovery.json` no longer affect a machine. A server without AuthKit, or an AuthKit domain that is not `https`, ends with `44`; it ended with `2`. An AuthKit domain with a path is refused with `44`; it was used. A `4xx` of discovery ends with `44`, a `5xx` or a network failure with `3`; both ended with `2`.
7. A token request has a 5-second attempt timeout and up to 3 retries; it was one attempt with the 100-second timeout of `HttpClient`, which is now the limit of the whole request.
8. A client-credentials token is renewed after a `401`, and the request sent again, once.
9. A token that lives less than two minutes is renewed halfway; a failed early renewal keeps the token.
10. `sform account` takes the expiry from the JWT `exp`, and says when it is unknown.
11. The messages about the variables are the SDK's, for example "Storyteller credential variables of different kinds are set: …".

```yaml
# GitHub Actions
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform account
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

A `Federated` machine in GitHub Actions needs no secret and no file. Its rule's audience is `https://api.example.com/api/v1/acme/billing/access/token`, the `TokenEndpoint` of the machine:

```yaml
# GitHub Actions, Federated machine
permissions:
  id-token: write
  contents: read

jobs:
  sync:
    runs-on: ubuntu-latest
    environment: production
    env:
      STORYTELLER_ENDPOINT: https://api.example.com/api
      STORYTELLER_PROJECT: acme.billing
      STORYTELLER_CLIENT_ID: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
    steps:
      - uses: actions/checkout@v4
      - run: dotnet tool install 42.Platform.Cli --global --prerelease
      - run: sform account
      - run: sform story config set sbt.northwind -i configuration/northwind.json
```

A job whose application reads its configuration with the provider can set `STORYTELLER_KEY: "@acme.billing.production.sbt.northwind"` instead of `STORYTELLER_PROJECT` (quoted, because YAML reserves `@`). `sform` then works on `acme.billing` with the `production` view, and the provider reads that key.

In Kubernetes, the pod projects a service account token whose `audience` is the same `TokenEndpoint`, and names its file:

```yaml
# Kubernetes, Federated machine
spec:
  serviceAccountName: billing-sync
  containers:
    - name: sync
      env:
        - name: STORYTELLER_ENDPOINT
          value: https://api.example.com/api
        - name: STORYTELLER_PROJECT
          value: acme.billing
        - name: STORYTELLER_CLIENT_ID
          value: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
        - name: STORYTELLER_TOKEN_FILE
          value: /var/run/secrets/storyteller/token
      volumeMounts:
        - name: storyteller-token
          mountPath: /var/run/secrets/storyteller
          readOnly: true
  volumes:
    - name: storyteller-token
      projected:
        sources:
          - serviceAccountToken:
              path: token
              audience: https://api.example.com/api/v1/acme/billing/access/token
              expirationSeconds: 3600
```

## Machine authentication policy

A project enables one or more **machine credential kinds**, and each machine uses one of them. Machines of different kinds can work side by side in one project, for example an API-key service next to an mTLS one.

| Kind | The machine sends |
|---|---|
| `ApiKey` | `Authorization: ApiKey` with its structured `2s.` key. |
| `Certificate` | Its client certificate. |
| `CertificateAndApiKey` | A certificate (its own, or a shared certificate of the project) and its API key. |
| `ClientCredentials` | `Authorization: Bearer` with a token of the identity provider, see [Machine access with client credentials](#machine-access-with-client-credentials). |
| `Federated` | `Authorization: Bearer` with a short-lived Storyteller token, exchanged for a token of its own platform. It has no secret. See [Machine access with workload identity federation](#machine-access-with-workload-identity-federation). |

`sform` signs in as a machine of any of these kinds from environment variables, see [Machine sign-in for pipelines](#machine-sign-in-for-pipelines).

**Shared certificates follow `CertificateAndApiKey`.** A shared certificate is the certificate half of a `CertificateAndApiKey` login, so the policy needs no setting of its own for it:

* A project issues shared certificates (`POST …/access/certificates`) only while it enables `CertificateAndApiKey`; otherwise the call returns `400`. Listing and revoking work under every policy, so they can be cleaned up after the kind is disabled.
* A shared certificate is accepted only together with the API key of a `CertificateAndApiKey` machine of the same project, and only while the project enables that kind. An API key whose machine kind is not recorded is refused with a shared certificate. A machine of another kind that sends a shared certificate is rejected, even if its API key alone would pass. So a shared certificate never makes an `ApiKey` machine look like an mTLS one, for example for [secret delivery](key-values.md#secret-delivery).
* Disabling `CertificateAndApiKey` stops every shared certificate of the project at once, without revoking them; enabling it again lets them work again.

The policy (`MachineAuthenticationPolicy`) is stored on the project's access point:

| Property | Description |
|---|---|
| `EnabledCredentialKinds` | The kinds the project's machines may use. |
| `CredentialKind` | The default kind: preselected for a new machine, and used when a create request names none. One of the enabled kinds. |
| `CertificateLifetimeDays` | The default certificate lifetime of new machines, in days. When it is not set, `MachineAuth:CertificateLifetimeDays` (365) applies. |

Without a stored policy a project enables only `MachineAuth:DefaultCredentialKind` (`ApiKey`). A policy stored before projects could enable several kinds has only `CredentialKind`; it enables that one kind. `Federated` machines also need a deployment that registers [federation](#configuration) and the project's [trusted issuers](#trusted-issuers). The issuers are kept apart from the policy, so a policy `PUT` never touches them.

`GET v1/access/points/{organization}.{project}/machine-authentication` returns the policy with every property filled, so a client does not have to know the deployment defaults. It needs `Contributor`, because Contributors create machines. `PUT` on the same route sets it, needs `Administrator`, and returns the filled policy too. The body may leave out `CredentialKind` (the first listed kind becomes the default) and `CertificateLifetimeDays` (the project then follows the deployment default). A body with only `CredentialKind` enables that one kind, as older clients send it. `PUT` returns `400` when:

* the body is empty, `null` or not a policy;
* no kind is enabled;
* a kind does not exist;
* `CredentialKind` is not one of `EnabledCredentialKinds`;
* `CertificateLifetimeDays` is outside `MachineAuth:MinCertificateLifetimeDays` to `MachineAuth:MaxCertificateLifetimeDays` (1 to 3,650 days).

```json
PUT v1/access/points/acme.billing/machine-authentication
{ "EnabledCredentialKinds": [ "ApiKey", "Certificate" ], "CredentialKind": "Certificate", "CertificateLifetimeDays": 90 }
```

The same with `sform`, which sets the whole policy (`-k` is required, the first `-k` is the default unless `-d` names another, and without `-l` the project follows the deployment default):

```
sform account machine machine-auth -k api-key -k certificate -d certificate -l 90
sform account machine machine-auth                 # prints the policy
sform account machine create -k api-key            # without -k: the project default
sform account machine create -s                    # CertificateAndApiKey with a shared certificate, no certificate of its own
sform account machine create -w -m configuration-transfer   # Annotation.Read/Write, Configuration.Read/Write and Configuration.Transfer (Administrator)
```

**Creating a machine.** `MachineAccessCreate.Scopes` lists the machine's [scopes](#scopes-of-machines) by their enum names (`AnnotationRead` to `KeyValueManage`), in any case. A number that is not a defined scope (for example `[99]`) answers `400` with "The machine scope 99 doesn't exist.". A name that is not a scope (for example `"Bogus"`, or the scope string `"Annotation.Read"`, which only `sform` accepts) is not a valid body and answers `400` with "The request body is not a machine (at $.Scopes[0]).". Each value is one scope, so names joined by commas (`"AnnotationWrite, ConfigurationRead"`) get the same `400`. Create then adds the read scope of each write scope (`AnnotationRead` for `AnnotationWrite`, `ConfigurationRead` for `ConfigurationWrite`, `SecretsRead` for `SecretsWrite`), drops duplicates and stores the list in enum order; the response carries it the same way. A list without a runtime scope (missing, empty, or management scopes only) answers `400` with "A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite.". None of these has an `ErrorCode`. A machine with a `Secrets.*` scope needs `ContributorWithSecrets` on the project, and one with a management scope `Administrator`, or `403 AccessDenied` with "No Administrator access to 'acme.billing'."; any other machine needs `Contributor` (see [Scopes of machines](#scopes-of-machines)). `MachineAccessCreate.CredentialKind` picks the kind; without it the project default is used. A kind the project does not enable fails with `400`. `MachineAccessCreate.UseSharedCertificate` creates a `CertificateAndApiKey` machine with no certificate of its own: it gets only its API key, `UsesSharedCertificate` is set, and it signs in with one of the project's shared certificates and that key. Any other kind with `UseSharedCertificate` fails with `400`. `MachineAccessCreate.FederationRules` is required for a `Federated` machine and fails with `400` on any other kind (see [Federation rules](#federation-rules)). A machine with a certificate of its own gets `MachineAccessCreate.CertificateLifetimeDays`, or else the project's default lifetime, clamped to the deployment's minimum and maximum. The machine keeps its kind and its lifetime: reset and delete follow the kind, and a certificate renewal issues the new certificate with the same lifetime. Machines created before the lifetime was stored renew with `MachineAuth:CertificateLifetimeDays`.

**Every machine call** is accepted only when the machine presents what its own kind requires, and the project still enables that kind. A `CertificateAndApiKey` machine that sends only its API key is rejected, even if the project also enables `ApiKey`. Likewise one that sends only its certificate is rejected, even if the project also enables `Certificate`: a machine certificate carries its machine's kind (`mcert.` documents stored before the kind was kept take it from the machine access document), and a machine certificate whose kind is unknown is refused. The scopes the credentials carry come from the same record as the kind: the machine access document for an API key, alone or with a shared certificate, and the `mcert.` document for a machine certificate. A record without scopes is refused with `401`. Each operation then checks its scopes against the machine document (see [Scopes of machines](#scopes-of-machines)). A `Federated` machine sends a Storyteller token, and each call reads the machine to check its stored kind (see [Storyteller tokens](#storyteller-tokens)). Disabling a kind stops every machine of that kind at once (up to the 60-second cache of the policy); enable it again and they work again. To move a machine to another kind, create a new machine and delete the old one.

## Machine access with client credentials

Storyteller API keys and mTLS certificates work under every user provider. In addition, a project can use **identity provider client credentials** (`ClientCredentials`): the machine exchanges a client ID and secret for a short-lived JWT at the identity provider and sends it as a bearer token.

One identity provider issues machine credentials per deployment, independently of the user provider. `Program.cs` registers it with one of `AddAuthKitMachineAccess`, `AddAzureAdMachineAccess` or `AddKeycloakMachineAccess`. Registering a second one fails at startup. The registered provider then:

* creates, resets and deletes machine credentials for `ClientCredentials` projects;
* validates its machine tokens. `BearerAuthenticationMiddleware` asks it when the user provider's validator rejects a token whose issuer belongs to it.

The common steps for every provider:

```
sform machine machine-auth --credential-kind client-credentials
sform machine create
```

The first command enables only `ClientCredentials`. A project can enable it next to other kinds; `machine create --credential-kind client-credentials` then picks it for one machine. `machine create` prints the client ID (also the machine ID in Storyteller), the client secret (shown once), the token URL and, for Entra ID, the token scope, with a `curl` example and the `STORYTELLER_*` variables of a [pipeline sign-in](#machine-sign-in-for-pipelines). `sform machine reset <id>` replaces the secret and `sform machine delete <id>` removes the provider's client. Both follow the kind the machine was created with, even if the project policy changed since.

Every provider's tokens are checked for the same things:

* the provider's signature (RS256), issuer, audience and lifetime;
* that the project named by the request enables `ClientCredentials`;
* that the project owns the machine.

A token that fails any of these gets 401. API keys and certificates are accepted only for machines of the kinds the project enables, see [Machine authentication policy](#machine-authentication-policy). Scopes come only from what Storyteller put into the client; `roles` or `scp` claims from elsewhere in the provider are dropped. Each operation needs its [scope](#scopes-of-machines) both stored on the machine and carried by the token, under every provider, Entra ID included. So a WorkOS permission or an Entra ID app role added to the client in the provider later grants nothing, and a token without the permission or role of a stored scope gets `403 AccessDenied` for the operations that need it.

### The `MachineAccess` object

Create, get, list, reset and the federation rules `PUT` return a `MachineAccess`. Property names are PascalCase. `Scopes` and `CredentialKind` are the enum names as strings. A null property is left out of the JSON.

Create returns the secret once. Get and list read the stored record: the secret is masked, and the one-time fields are absent. Reset returns that stored record with the new secret in `AccessKey`. A `Federated` machine has no secret and cannot be reset (`400`); its `FederationRules` and `TokenEndpoint` come with create, get, list and the rules `PUT`.

| Property | Type | Description |
|---|---|---|
| `Id` | string | Machine id, and the `sub` / `azp` the API expects from that machine. An API key, a certificate or a federated machine uses a GUID; a federated machine sends it as `client_id` to the token endpoint. Client credentials use the identity provider's client id: the WorkOS application client id, the Entra appId, or the Keycloak client id `42.sform.{organization}.{project}.{guid}`. |
| `ObjectId` | string | Id used to reset and delete the credential at its source. Same value as `Id` for an API key, a certificate or a federated machine. AuthKit uses the WorkOS application id, Entra ID the application object id, and Keycloak the client's internal id. |
| `AccessKey` | string | Secret. Create and reset return it in full: the structured `2s.` API key, or the client secret for client credentials. A certificate-only or federated machine returns an empty string. Stored copies keep the first three characters and then `***`, or `***` when the value is shorter. |
| `Scopes` | `MachineScope[]` | The [scopes](#scopes-of-machines) of the machine, which decide every operation it may call. Runtime: `AnnotationRead`, `AnnotationWrite`, `ConfigurationRead`, `ConfigurationWrite`, `SecretsRead` and `SecretsWrite`. Management: `ConfigurationTransfer`, `SchemaWrite`, `TemplateWrite`, `AnnotationManage` and `KeyValueManage`. Create adds the read scope of each write scope, and stores the list without duplicates and in this order. A machine with `SecretsRead` receives [key/value secrets](key-values.md#secret-delivery) under the project's mTLS rule, and one with `SecretsWrite` writes them. Every provider stores and carries the list as requested. The scopes cannot change after creation. |
| `AnnotationKey` | string | Optional [annotation restriction](#annotation-restriction-of-machines): the machine reaches this annotation and its descendants only. Create stores it in canonical form (type code in lower case, empty segments dropped), turns a blank value into null, and answers `400` `InvalidKey` for a key that does not parse. Null means the whole project. |
| `CredentialKind` | `MachineCredentialKind` | `ApiKey`, `Certificate`, `CertificateAndApiKey`, `ClientCredentials` or `Federated`. Chosen at creation from the kinds the project enables (the project default when the request names none) and kept when the policy later changes. |
| `CertificateThumbprint` | string | Thumbprint of the machine's current certificate. Present for `Certificate` and `CertificateAndApiKey`, except a machine that uses a shared certificate, and updated when the certificate is renewed. |
| `Certificate` | string | Base64 PKCS#12 of a newly issued machine certificate. Returned on create. Cosmos keeps no copy, so get, list and reset omit it. |
| `CertificatePassword` | string | Password for that PKCS#12. Returned on create. Cosmos keeps no copy, so get, list and reset omit it. |
| `LastRenewalAt` | date-time | UTC time of the last certificate renewal. Present after the first renewal. |
| `CertificateLifetimeDays` | integer | Lifetime of the machine's certificates in days, chosen at creation. Renewals keep it. Present for `Certificate` and `CertificateAndApiKey` machines created since it is stored. |
| `CertificateNotAfter` | date-time | When the current certificate expires. Present for machines created since it is stored, and updated by renewal. |
| `UsesSharedCertificate` | boolean | `true` for a `CertificateAndApiKey` machine created with `UseSharedCertificate`: it has no certificate, thumbprint, lifetime or expiry of its own, and presents a shared certificate of the project with its API key. Reset renews the API key; there is nothing to renew for the certificate. |
| `TokenEndpoint` | string | Token URL. For client credentials it is returned on create: AuthKit uses `{AuthKitDomain}/oauth2/token`, Entra ID `https://login.microsoftonline.com/{MachineAuth:AzureAd:TenantId}/oauth2/v2.0/token`, Keycloak `{ServerUrl}/realms/{Realm}/protocol/openid-connect/token`; Cosmos keeps no copy, so get, list and reset omit it. For `Federated` it is the project's [token endpoint](#the-token-endpoint), `{MachineAuth:Federation:PublicBaseUrl}/v1/{organization}/{project}/access/token` (without `PublicBaseUrl`, the scheme and host of the request plus `/api`), built on create, get, list and the rules `PUT`. |
| `TokenScope` | string | `scope` form field for the token request. Entra ID sets `api://{Auth:ClientId}/.default` on create. AuthKit and Keycloak leave it empty; their tokens carry the machine's scopes without this parameter. Cosmos keeps no copy, so get, list and reset omit it. Always empty for `Federated`. |
| `FederationRules` | `FederationRule[]` | `Federated` only: the rules a platform token must match, see [Federation rules](#federation-rules). Stored with the machine. |

### Scopes in the identity providers

A `ClientCredentials` machine carries its [scopes](#scopes-of-machines) in the provider's token: one WorkOS permission, one Entra ID app role or one value of the Keycloak `storyteller_scope` claim per scope. A provider grants exactly the requested scopes, or refuses the creation with `400` before it creates anything.

| Scope | Group | Entra ID app role key | WorkOS permission |
|---|---|---|---|
| `Annotation.Read` | Runtime | `Auth:AppRoles:AnnotationRead` | `storyteller:annotation-read` |
| `Annotation.Write` | Runtime | `Auth:AppRoles:AnnotationWrite` | `storyteller:annotation-write` |
| `Configuration.Read` | Runtime | `Auth:AppRoles:ConfigurationRead` | `storyteller:configuration-read` |
| `Configuration.Write` | Runtime | `Auth:AppRoles:ConfigurationWrite` | `storyteller:configuration-write` |
| `Secrets.Read` | Runtime | `Auth:AppRoles:SecretsRead` | `storyteller:secrets-read` |
| `Secrets.Write` | Runtime | `Auth:AppRoles:SecretsWrite` | `storyteller:secrets-write` |
| `Configuration.Transfer` | Management | `Auth:AppRoles:ConfigurationTransfer` | `storyteller:configuration-transfer` |
| `Schema.Write` | Management | `Auth:AppRoles:SchemaWrite` | `storyteller:schema-write` |
| `Template.Write` | Management | `Auth:AppRoles:TemplateWrite` | `storyteller:template-write` |
| `Annotation.Manage` | Management | `Auth:AppRoles:AnnotationManage` | `storyteller:annotation-manage` |
| `KeyValue.Manage` | Management | `Auth:AppRoles:KeyValueManage` | `storyteller:keyvalue-manage` |

The scope string is the value of the Entra ID app role (an `App.` prefix is allowed), the `PermissionMap` value of the WorkOS slug, and the value in `storyteller_scope`. The slugs are the recommended ones; any slug works once `PermissionMap` maps it. A deployment sets up only the scopes its machines use.

`src/Platform/Storyteller/scripts/Register-MachineScopes.ps1` (PowerShell 7) creates the app roles and the permissions that are missing, and prints the settings:

```
./Register-MachineScopes.ps1 -EntraApplicationId <appId>                   # Entra ID: the API app registration (Auth:ClientId), with az signed in
./Register-MachineScopes.ps1 -WorkOs                                       # WorkOS: with WORKOS_API_KEY, or -WorkOsApiKey sk_…
./Register-MachineScopes.ps1 -EntraApplicationId <appId> -WorkOs -WhatIf   # both, and only show what would be created
```

* `-EntraApplicationId` reads the app registration with `az ad app show`, adds an app role for each scope that has none (the scope string as the value, a new GUID, allowed member type `Application`), and writes the roles back in one `PATCH` through `az rest`, with the existing roles unchanged. It prints `Auth__AppRoles__<MachineScope>=<role id>` for all 11 scopes.
* `-WorkOs` lists the permissions of the environment and creates the missing slugs (`-WorkOsSlugPrefix`, default `storyteller:`). It prints the `PermissionMap` settings as JSON and as environment variables.
* The script can run again: a role whose value is already the scope string (also with `App.`) and a slug that exists are kept. It never changes or deletes anything else, so roles and permissions that are no longer used are deleted by hand. With `-WhatIf` the IDs it prints for new roles are examples.

### AuthKit M2M applications

1. In WorkOS, create an organization for the machines (for example `Storyteller machines`) and note its `org_…` ID.
2. Create one WorkOS permission per machine scope and map it in `PermissionMap`, one slug to exactly one scope string, as in the [table above](#scopes-in-the-identity-providers). `Register-MachineScopes.ps1 -WorkOs` creates the permissions and prints these settings:

   ```json
   "Auth:AuthKit:PermissionMap:storyteller:annotation-read": "Annotation.Read",
   "Auth:AuthKit:PermissionMap:storyteller:annotation-write": "Annotation.Write",
   "Auth:AuthKit:PermissionMap:storyteller:configuration-read": "Configuration.Read",
   "Auth:AuthKit:PermissionMap:storyteller:configuration-write": "Configuration.Write",
   "Auth:AuthKit:PermissionMap:storyteller:secrets-read": "Secrets.Read",
   "Auth:AuthKit:PermissionMap:storyteller:secrets-write": "Secrets.Write",
   "Auth:AuthKit:PermissionMap:storyteller:configuration-transfer": "Configuration.Transfer",
   "Auth:AuthKit:PermissionMap:storyteller:schema-write": "Schema.Write",
   "Auth:AuthKit:PermissionMap:storyteller:template-write": "Template.Write",
   "Auth:AuthKit:PermissionMap:storyteller:annotation-manage": "Annotation.Manage",
   "Auth:AuthKit:PermissionMap:storyteller:keyvalue-manage": "KeyValue.Manage"
   ```

   The host checks the map at startup (see [Scopes for AuthKit users](#scopes-for-authkit-users)): a value that is not exactly one scope string, or two slugs for the same scope, stop it. A new M2M application gets exactly the permission of each of its scopes. When a requested scope has no permission, creation fails with 400 before WorkOS is called, for example "No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope Secrets.Write.". The description of the application names the scopes, for example `organization=acme|project=billing|scopes=AnnotationRead,ConfigurationRead`.
3. Set `Auth:AuthKit:ClientId`, `Auth:AuthKit:AuthKitDomain`, `Auth:AuthKit:MachineOrganizationId` and `Auth:AuthKit:ApiKey` (from Key Vault). `Program.cs` registers AuthKit machine access when the domain and the organization are set. The host then refuses to start without the other two. AuthKit users are not required.

Tokens come from `{AuthKitDomain}/oauth2/token` and are checked as follows:

* `iss` is `AuthKitDomain`, and the keys come from `{AuthKitDomain}/oauth2/jwks`.
* `aud` is the environment client ID (`Auth:AuthKit:ClientId`). WorkOS does not let M2M applications choose it.
* `sub` equals `client_id`, and `org_id` equals `MachineOrganizationId`.
* The `scope` claim is mapped through `PermissionMap`. `DefaultUserScopes` never apply to machines.

Reset mints a new secret and then revokes the old ones (WorkOS allows five per application). `Examples/CustomClientApp` (`dotnet run -- authkit`) shows the exchange in C#.

### Entra ID app registrations

1. The Storyteller API app registration (`Auth:ClientId`) needs one application app role per scope its machines use, with allowed member type `Application`. A role value is what the token's `roles` claim carries, so the value is the scope string (`Annotation.Read`, `Schema.Write`, …); the API strips an `App.` prefix. The role IDs go into `Auth:AppRoles:<MachineScope>`, for example `Auth:AppRoles:AnnotationRead` (see the [table](#scopes-in-the-identity-providers)). `Register-MachineScopes.ps1 -EntraApplicationId` creates the roles and prints these settings.

   Every key is optional. A key that is set must be a GUID, and no two keys may share one, or the host does not start. The keys `Auth:AppRoles:DefaultRead`, `Auth:AppRoles:DefaultReadWrite` and `Auth:AppRoles:SecretsReadWrite` stop the start too, with a message that names the keys to set instead. A machine gets one role per scope, and none is collapsed into another: the application lists them all in `RequiredResourceAccess`, and admin consent assigns each, in the order of the table. Creation answers `400` before anything is created in Graph when a requested scope has no role, for example "Machines with the scope Annotation.Write need the app role Auth:AppRoles:AnnotationWrite, which this deployment does not configure.", and when the machine tenant has no service principal of the API application, because then no role can be assigned. The `Notes` of the application name the scopes (`organization=acme|project=billing|scopes=AnnotationRead,ConfigurationRead`).
2. Set `MachineAuth:AzureAd:TenantId` to the directory where machine app registrations are created. `Auth:TenantId` and `Auth:ClientId` must be set too, also when users sign in with AuthKit.
3. The Functions identity (`DefaultAzureCredential`) needs Microsoft Graph permissions to create applications and service principals and to assign app roles.
4. Use `services.AddAzureAdMachineAccess(context.Configuration)` in `Program.cs`.

The machine ID is the appId. Tokens come from `https://login.microsoftonline.com/{MachineAuth:AzureAd:TenantId}/oauth2/v2.0/token` with `scope=api://{Auth:ClientId}/.default`. They are checked like Entra user tokens (audience `api://{ClientId}` or `{ClientId}`, a Microsoft issuer), and `azp` / `appid` must name a different application than the API. Reset removes the generated secrets and adds a new one.

### Keycloak confidential clients

1. Configure the `Keycloak` section:
   * `ServerUrl` and `Realm`, the realm where machine clients are created;
   * `Audience`, default `storyteller`;
   * admin access, either `AdminClientSecret` for a client in `AdminRealm` (default `master`, client `AdminClientId`, default `admin-cli`) or `AdminUsername` with `AdminPassword`.
2. Use `services.AddKeycloakMachineAccess(context.Configuration)` in `Program.cs`.

Each machine client has a service account and two protocol mappers:

* an audience mapper adds `Audience` to `aud`;
* a hardcoded claim mapper adds `storyteller_scope` with the scope strings of the machine's [scopes](#scopes-of-machines), separated by spaces.

The attribute `42.scopes` and the description of the client name the scopes by their enum names (`organization=acme|project=billing|scopes=AnnotationRead,ConfigurationRead`). Keycloak needs no setting for them.

The machine ID is the `clientId` (`42.sform.…`). Tokens come from `{ServerUrl}/realms/{Realm}/protocol/openid-connect/token` and are checked as follows:

* `iss` is `{ServerUrl}/realms/{Realm}`, and the keys come from the realm's OpenID metadata (HTTP is allowed only for a local server).
* `aud` contains `Audience`.
* `azp` is the machine.

Reset regenerates the client secret.

## Annotation restriction of machines

A machine created with an `AnnotationKey` K is restricted to K. It reaches K and the descendants of K, in every view of its project, with what its [scopes](#scopes-of-machines) allow. The rule is the one of the [inheritance graph](inheritance.md): a machine restricted to the execution `exe.northwind.invoicing.prod` reaches that execution and its units of execution `uxe.northwind.invoicing.prod.*`, and a machine restricted to the subject `sbt.northwind` reaches its contexts, usages, executions and units of execution.

| Operation | Restricted machine |
| --- | --- |
| Annotations (with their history and descendants), configurations (raw, resolved, versions, diffs, the diff to another view), annotation schemas, descendant-type schemas and the combined schema definition of K or a descendant | Allowed |
| The same for an ancestor, a sibling or another branch of K | `403` |
| Bulk create (`POST …/annotations`), lookup and restore of deleted annotations | Allowed when every key of the body is K or a descendant, otherwise `403`; bulk create and restore need `Annotation.Write` and `Annotation.Manage` |
| Lists of annotations, deleted annotations, configurations and schemas, compact bulk create (`…/annotations/simple`), type-level schemas, templates, export and import of a view, key/values, the key/value policy and recipients | `403`: the operation spans the whole project |
| The views of the project (`GET …/views`) and the binding catalog | Allowed: they hold no annotation data |

A refusal is `403` with `ErrorCode` `AnnotationRestricted`. It comes before any read, so it does not tell whether the annotation exists. A key that does not parse is outside of the restriction.

Inside K, every operation still needs its scopes: a read of an annotation needs `Annotation.Read`, a bulk create, a soft delete and a restore need `Annotation.Write` and `Annotation.Manage`, and an annotation or descendant-type schema write needs `Configuration.Write` and `Schema.Write`. The restriction is checked before the scopes, so a restricted machine gets `AnnotationRestricted` for an operation of the whole project, such as a transfer or a template write, whatever its scopes.

The ancestors of K, and the other parents of its descendants (the unit `unt.invoicing.export` of the unit of execution `uxe.northwind.invoicing.prod.export`, for example), still reach the machine through the effective and resolved configurations, and the resolved configuration is the only way a restricted machine gets key/values. Creating or restoring an annotation inside K also creates or restores its missing ancestors, as for every caller; the answers of a bulk create and of a restore leave the annotations outside K out.

The restriction is read from the machine's document in the same point read that checks its membership, so it holds for every credential kind and costs nothing extra. Tokens and certificates carry no annotation claim. A stored key that does not parse (machines created before the key was validated) restricts the machine to nothing. A restricted machine that has `Configuration.Write` can still bind any key/value of the view into the configuration of K; give a machine that only reads no write scope: `Configuration.Read`, with `Secrets.Read` when it needs secret values.

## Scopes of machines

A machine has one list of scopes, `Scopes`, and they decide every operation it may call: machines have no role. Scopes describe machines, roles describe people. Users have no data scopes; their token needs `User.Impersonation`, and their role decides (see [401 and 403](#401-and-403)).

The scopes come in two groups. The **runtime** scopes are the reads and the per-document writes of annotations, configurations and key/values. The **management** scopes are bulk and structural operations, on top of their runtime scope.

| Scope | `MachineScope` | Group | Operations |
|---|---|---|---|
| `Annotation.Read` | `AnnotationRead` | Runtime | The reads of annotations: lists, single annotations, descendants, history, and deleted annotations with their lookup ([annotation-lifecycle.md](annotation-lifecycle.md#api)) |
| `Annotation.Write` | `AnnotationWrite` | Runtime | The write of one annotation (`PUT …/annotations/{key}`) |
| `Configuration.Read` | `ConfigurationRead` | Runtime | The reads of configurations (raw, resolved, versions, diffs, composition), schemas, templates, key/values with their policy and recipients ([key-values.md](key-values.md#who-may-do-what)), and the binding catalog |
| `Configuration.Write` | `ConfigurationWrite` | Runtime | The writes, patches and deletes of configurations, and the writes and soft deletes of key/values |
| `Secrets.Read` | `SecretsRead` | Runtime | The values of key/value secrets: in a resolved configuration, a reveal, a secret version or diff, and an export with secrets ([key-values.md](key-values.md#secret-delivery)) |
| `Secrets.Write` | `SecretsWrite` | Runtime | The writes of secrets: a secret level on a write, the soft delete of a secret key, a protect, and the restore or import of secret keys |
| `Configuration.Transfer` | `ConfigurationTransfer` | Management | The export and import of a view, with the reads of their jobs and the archive download ([export-import.md](export-import.md#access)); the export and import of key/values ([key-values.md](key-values.md#who-may-do-what)) |
| `Schema.Write` | `SchemaWrite` | Management | The writes and deletes of type, annotation and descendant-type schemas ([definitions.md](definitions.md#http)), and the import of schemas into a view |
| `Template.Write` | `TemplateWrite` | Management | The writes (`POST` and `PUT`), patches and deletes of templates ([templating.md](templating.md)), and the import of templates into a view |
| `Annotation.Manage` | `AnnotationManage` | Management | The bulk create of annotations (`POST …/annotations` and `…/annotations/simple`), their soft delete and their restore ([annotation-lifecycle.md](annotation-lifecycle.md#api)), and the import of annotations into a view |
| `KeyValue.Manage` | `KeyValueManage` | Management | The protect and the bulk restore of key/values ([key-values.md](key-values.md#who-may-do-what)) |

The API, Cosmos, the SDKs and the settings keys use the enum names (`AnnotationRead`). Tokens, claims, the identity providers and the error messages use the scope strings (`Annotation.Read`).

* **Each operation names its scopes.** A read needs its `….Read` scope and a write its `….Write` scope. A management operation needs its runtime scope and its management scope, for example `Configuration.Write` and `Schema.Write` for a schema write, or `Annotation.Write` and `Annotation.Manage` for a bulk create. The list of views (`GET …/views`) takes either read scope, `Annotation.Read` or `Configuration.Read`. Secret values also need `Secrets.Read`, and secret writes `Secrets.Write`, under the project's mTLS rule.
* **Write implies read, once, at creation.** Create adds `Annotation.Read` to `Annotation.Write`, `Configuration.Read` to `Configuration.Write` and `Secrets.Read` to `Secrets.Write`, and stores both. Nothing is added later, at request time. `Secrets.*` imply no `Configuration.*` scope, so a machine that reads secret values also needs `Configuration.Read`. Management scopes imply nothing.
* **A management scope adds to the runtime scopes and never replaces them.** Each operation still needs its runtime scope, its secret rights and its mTLS rule, and the [annotation restriction](#annotation-restriction-of-machines) still applies. A key/value protect needs `Configuration.Write`, `KeyValue.Manage` and `Secrets.Write`. A machine needs at least one runtime scope, or creation answers `400`.
* **One transfer scope covers both directions.** The scopes of the included kinds set the direction: an export of a view needs the read scope of each included kind (`Annotation.Read` for annotations, `Configuration.Read` for schemas, templates and configurations), an import the scopes of its direct writes. So `Annotation.Read`, `Configuration.Read` and `Configuration.Transfer` export and cannot import. A key/value export needs `Configuration.Read` (and `Secrets.Read` with secrets), an import `Configuration.Write` (and `Secrets.Write` for secret items).
* **An import of a view is no way around a management scope.** It writes what the direct routes write, so it needs their scopes for each included kind: `Annotation.Write` and `Annotation.Manage` for annotations (it creates them in bulk and re-creates deleted ones), `Configuration.Write` and `Schema.Write` for schemas, `Configuration.Write` and `Template.Write` for templates, and `Configuration.Write` for configurations ([export-import.md](export-import.md#access)). A machine with `Configuration.Write` and `Configuration.Transfer` alone imports only configurations.

**The check.** Every operation a machine may call names its scopes in the project check it already makes (`CheckAccessToProjectAsync`, `CheckAccessToAnnotationAsync`, `CheckAccessToAnnotationsAsync` or `CheckAccessToProjectMetadataAsync` of `HttpRequestDataExtensions`), which reads the machine's document anyway. For a machine, the checks run in this order:

1. the machine document (`401` without it; a federated machine also gets its project and kind checks, `401`);
2. the credential policy (`401`);
3. the annotation restriction (`403 AnnotationRestricted`);
4. each scope of the operation, the runtime scope first (`403 AccessDenied`).

Each scope must be:

* **Stored.** The machine's `Scopes` contain it. Otherwise: "This operation needs the scope Annotation.Write, which the machine was not created with."
* **Carried.** The credentials of the request carry its scope string. Otherwise: "This operation needs the scope Annotation.Write, which the credentials of the machine do not carry." This is a Storyteller token that asked for a subset, or a client-credentials token without the permission or app role.

The routes of the export and the import of a view, the reads of their jobs included, are the exception. Their project check names only `Configuration.Transfer`, so that scope is checked first. The scopes of the included kinds come after it, the management scopes of an import among them, on the scopes that are stored and carried, with their own message, for example "Missing scope for annotations in include: exporting them needs Annotation.Read." or "Missing scope for schemas in include: importing them needs Configuration.Write and Schema.Write." (see [export-import.md](export-import.md#access)).

The list of views names both read scopes, for example "This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with." The machine document is the source of truth, and the credentials can only narrow it: a scope the credentials carry but the machine does not store grants nothing. A missing scope is `403`, not `401`: the machine is authenticated, and signing in again does not help. The check runs in every build, Debug (`DEV_AUTH`) included. Users are not affected by it.

**Secret rights.** A machine receives secret values only with `Secrets.Read`, and writes secrets only with `Secrets.Write`, each stored and carried, in every build. When the project delivers secrets only over mTLS (`RequireMtlsForSecretDelivery`) and the request has none, both rights are withheld. `403 MtlsRequired` and the header `X-2S-Secrets-Withheld: mtls-required` then name the missing mTLS only for a right the machine has, where a certificate would help: a machine without `Secrets.Write` gets `403 AccessDenied` for a secret write, with mTLS or without. A restore of key/values answers neither: it reports each secret key the machine may not write as `Forbidden`, with `ErrorCode` `AccessDenied`. See [Secret delivery](key-values.md#secret-delivery).

**Routes for users only.** Accounts, access points, members, invitations, the management of machines, shared certificates, trusted issuers and federation rules, the key/value Administrator operations, the permanent delete of an annotation, and creating, updating or discovering views refuse every machine in every build, whatever its credentials carry, `User.Impersonation` included: `403 AccessDenied` with "Only users may call this operation; machines cannot.", or the route's own message. The renewal of a machine certificate is for machines only.

**How the credentials carry them:**

| Kind | Carried in |
|---|---|
| `ApiKey`, `Certificate`, `CertificateAndApiKey` | One `roles` claim per scope string. They come from the machine access document for an API key, alone or with a shared certificate, and from the `mcert.` document for a machine certificate. A record without scopes gets `401`. |
| `ClientCredentials` | The provider's token, set up when the machine is created: per scope one AuthKit permission mapped in [`PermissionMap`](#authkit-m2m-applications), one [Entra ID app role](#entra-id-app-registrations), or one value of the [Keycloak](#keycloak-confidential-clients) `storyteller_scope` claim (see [Scopes in the identity providers](#scopes-in-the-identity-providers)). A provider that cannot carry a requested scope refuses the creation with `400`. |
| `Federated` | The `scope` of the [Storyteller token](#storyteller-tokens). An exchange without `scope` gets every scope of the machine. A job that needs `Configuration.Transfer` on some runs only asks for it on those runs and leaves it out on the others. |

**Who manages a machine.** The creator of a machine can use its credentials, so a machine's scopes may not exceed what its creator may do or give. A Contributor must not reach secrets or management scopes through the credentials of an existing machine either:

| Operation | Runtime scopes without secrets | With a `Secrets.*` scope | With a management scope |
|---|---|---|---|
| Create (`POST v1/{organization}/{project}/access/machines`) | `Contributor` | `ContributorWithSecrets` | `Administrator` |
| Reset (`PUT …/access/machines/{id}`), which returns a new secret | `Contributor` | `ContributorWithSecrets` | `Administrator` |
| Replace the federation rules (`PUT …/access/machines/{id}/federation-rules`), which decide which workload gets the machine's tokens | `Contributor` | `ContributorWithSecrets` | `Administrator` |
| Delete (`DELETE …/access/machines/{id}`), which stops whatever runs with the machine's scopes | `Contributor` | `ContributorWithSecrets` | `Administrator` |
| Get, list | `Contributor` | `Contributor` | `Contributor` |

Create checks the requested scopes together with the read scopes it adds. Reset, the rules `PUT` and the delete read the stored machine first, in one point read; a machine that does not exist gets the usual answer of the route (the delete `404`). A role below the one needed answers `403 AccessDenied` with "No Administrator access to 'acme.billing'." or "No ContributorWithSecrets access to 'acme.billing'.". These are role checks of users, so Debug builds (`DEV_AUTH`) skip them.

**Typical machines.** The admin UI offers these as presets in **New machine**. A preset fills in the scopes, which can then be changed; nothing about presets is sent or stored. `sform account machine create` gives the runtime reader by default and the CI writer with `-w`.

| Machine | `Scopes` | Created by |
|---|---|---|
| Runtime reader | `Annotation.Read`, `Configuration.Read` | `Contributor` |
| Runtime with secrets | `Annotation.Read`, `Configuration.Read`, `Secrets.Read` | `ContributorWithSecrets` |
| CI writer (per-document writes) | `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write` | `Contributor` |
| Git sync of a view (imports every kind) | The scopes of the CI writer, `Configuration.Transfer`, `Schema.Write`, `Template.Write`, `Annotation.Manage` | `Administrator` |
| Backup | `Annotation.Read`, `Configuration.Read`, `Configuration.Transfer` | `Administrator` |
| Schema and template deployer | `Configuration.Read`, `Configuration.Write`, `Schema.Write`, `Template.Write` | `Administrator` |

Other machines pick their scopes one by one, for example `Annotation.Write` and `Annotation.Manage` for annotation provisioning or clean-up, or `Configuration.Write`, `Secrets.Write` and `KeyValue.Manage` for the re-protection of key/values; creation adds the read scopes.

```json
POST v1/acme/billing/access/machines
{ "Organization": "acme", "Project": "billing", "Scopes": [ "AnnotationWrite", "ConfigurationWrite", "ConfigurationTransfer", "SchemaWrite", "TemplateWrite", "AnnotationManage" ] }
```

This creates a git sync machine, and the response carries `"Scopes": [ "AnnotationRead", "AnnotationWrite", "ConfigurationRead", "ConfigurationWrite", "ConfigurationTransfer", "SchemaWrite", "TemplateWrite", "AnnotationManage" ]`.

`sform account machine create` picks the scopes with these options:

* `-r|--read-only`: `Annotation.Read` and `Configuration.Read`.
* `-w|--write`: the same, plus `Annotation.Write` and `Configuration.Write`.
* `--scope <scope>`: any of the 11 scopes. The option is repeatable or takes values separated by commas, and accepts the enum name, the scope string or kebab case, in any case (`SecretsRead`, `Secrets.Read`, `secrets-read`).
* `-m|--manage <scope>`: a management scope, in the same forms. A runtime scope there is refused with a hint to use `--scope`.

The command sends the union of the options. When it holds no runtime scope, the scopes of `-r` are added, so `machine create` alone, or with only `-m`, gives a runtime reader. The server adds the read scopes of the write scopes. An unknown value prints the allowed names and exits with `2` before any request. The printed machine shows `Scopes` and `CredentialKind` by their names, and the machine list (`sform account machine`) has the columns `Id`, `Scopes` (the runtime scope strings), `Kind`, `Secret`, `RestrictionTo` and `Management` (the management scope strings).

```
sform account machine create                                                          # a runtime reader
sform account machine create -r --scope secrets-read                                  # a runtime reader with secrets (ContributorWithSecrets)
sform account machine create -m configuration-transfer                                # a backup machine: the runtime reader and Configuration.Transfer
sform account machine create -w -m configuration-transfer,schema-write,template-write,annotation-manage   # a git sync machine: the CI writer and the management scopes of an import of every kind
sform account machine create --scope configuration-write,schema-write,template-write   # a schema and template deployer
```

**Limits:**

* The management scope of a bulk create limits what one call reaches, not what a runtime machine can do one key at a time. `SetAnnotation` (`PUT …/annotations/{key}`) needs only `Annotation.Write`: it still creates keys one by one, with their missing ancestors, and re-creates deleted ones.
* `ProtectKeyValues` only moves keys between the server-side levels. `SetKeyValue` with `Secrets.Write` still changes the level of one key, and a write to a deleted key re-creates it. The soft delete of one key/value stays runtime too: it touches one key, while the delete of an annotation takes its descendants with it.
* The scopes of a machine cannot change once it is created.

**Existing machines.** Machines created before this list of scopes have no `Scopes`, and neither have their `mcert.` documents. Nothing migrates them, and they keep no access: an API key or a certificate gets `401`, a `Federated` machine gets no token (`invalid_scope`), and a `ClientCredentials` machine gets `403` for every operation. The WorkOS applications and Keycloak clients that a provider created for them keep their old permissions and claims. To move a pipeline:

1. A user with the role its scopes need creates a new machine. For `ClientCredentials`, the provider maps every scope first: the AuthKit `PermissionMap` or the Entra ID app roles, which `Register-MachineScopes.ps1` creates (see [Scopes in the identity providers](#scopes-in-the-identity-providers)).
2. The pipeline switches to the credentials of the new machine.
3. The old machine is deleted.

No endpoint edits a machine, so the same steps change the scopes of a machine.

## Machine access with workload identity federation

A `Federated` machine has no secret. A workload that already holds a token of its platform (a GitHub Actions or Vercel OIDC token, a Kubernetes service account token, an Azure managed identity token, an AWS or Google Cloud identity token) exchanges it at the project's **token endpoint** ([RFC 8693](https://www.rfc-editor.org/rfc/rfc8693)) for a short-lived **Storyteller token**, and calls the API with `Authorization: Bearer <storyteller token>` until it expires.

```
workload ──(1) platform token (GitHub, K8s, Vercel, Entra, AWS, Google)──▶ POST v1/{organization}/{project}/access/token
                                                                              │ client_id = machine id
                                                                              │ verify with the trusted issuer's keys
                                                                              │ match one of the machine's federation rules
workload ◀─(2) Storyteller token (at+jwt, 15 min, machine scopes)────────────┘
workload ──(3) Authorization: Bearer <storyteller token>──▶ any machine-accessible API
```

Trust is split in two:

* **Trusted issuers** belong to the project and are managed by its Administrators. Each names an OIDC issuer, where its keys come from, and claims every token of that issuer must carry (for example the id of the company's GitHub organization).
* **Federation rules** belong to the machine and are managed by Contributors, like the machine itself; the rules of a machine with a secrets scope need `ContributorWithSecrets`, and those of a machine with a management scope an Administrator (see [Scopes of machines](#scopes-of-machines)). A rule picks a trusted issuer and pins the token's `aud`, `sub` and further claims.

The platform token never reaches the regular API. Discovery, key retrieval and rule matching run once per exchange, not on every call. The request names its project and machine, so an issuer the project does not trust never causes a fetch. Azure managed identity tokens are Entra tokens; sent to the API directly, the Entra validator would take them for `ClientCredentials` machines, so they go through the exchange like every other platform token. Every call with a Storyteller token still reads the machine, so deleting the machine cuts it off at once.

Setting up a federated machine:

1. An Administrator enables `Federated` in the [machine authentication policy](#machine-authentication-policy), next to other kinds if needed.
2. An Administrator adds a [trusted issuer](#trusted-issuers).
3. A Contributor creates the machine with `CredentialKind` `Federated`, its `Scopes` and its [federation rules](#federation-rules); a `ContributorWithSecrets` when it gets a secrets scope, an Administrator when it gets a management scope. The response carries the machine id and the `TokenEndpoint`, and no secret.
4. The workload gets a platform token with the rule's audience, [exchanges it](#the-token-endpoint) with `client_id` = the machine id, and calls the API with the Storyteller token. [Setting up a platform](#setting-up-a-platform) shows each platform.

```json
PUT v1/access/points/acme.billing/machine-authentication
{ "EnabledCredentialKinds": [ "ApiKey", "Federated" ], "CredentialKind": "ApiKey" }

PUT v1/acme/billing/access/issuers/github
{ "Issuer": "https://token.actions.githubusercontent.com",
  "RequiredClaims": { "repository_owner_id": [ "65" ] },
  "Description": "GitHub Actions of the acme organization" }

POST v1/acme/billing/access/machines
{ "Organization": "acme", "Project": "billing", "Scopes": [ "AnnotationRead" ], "CredentialKind": "Federated",
  "FederationRules": [ { "Issuer": "github",
                         "Audience": "https://api.example.com/api/v1/acme/billing/access/token",
                         "Subject": "repo:acme@65/billing@74:environment:production",
                         "Claims": { "repository_id": [ "74" ] } } ] }
```

The same with `sform` (the machine commands live under `sform account machine`, alias `agent`; `-p <organization>.<project>` picks another project):

```
sform account machine machine-auth -k api-key -k federated -d api-key
sform account machine issuer set github --issuer https://token.actions.githubusercontent.com --require-claim repository_owner_id=65 --description "GitHub Actions of the acme organization"
sform account machine create --issuer github --audience https://api.example.com/api/v1/acme/billing/access/token --subject repo:acme@65/billing@74:environment:production --claim repository_id=74
```

* `machine create` with `--issuer` (a trusted issuer's name), `--audience`, `--subject` and any `--claim key=value` creates a `Federated` machine with one rule; the rule options imply `-k federated`. `--audience` is the machine's `TokenEndpoint`, except for Azure, where it is the `aud` of the Storyteller app registration's tokens: `<appId>` with `requestedAccessTokenVersion` 2, `api://<appId>` with 1 (see [Azure managed identity](#azure-managed-identity)). `--rules-file <path>` takes a JSON array of rules instead, the body of the [rules `PUT`](#federation-rules); its strings are sent as written. A repeated `--claim` key adds an accepted value, and the key may be a JSON pointer (`--claim /kubernetes.io/namespace=billing`). `-k federated` without rules, rule options with another kind, both a file and rule options, an incomplete rule, or a rules file with a property other than `Issuer`, `Audience`, `Subject` and `Claims` (a misspelled `Claim`) or with a value that is not a JSON string (`[ true ]` as a claim value) fail before any request; the server validates the rules themselves and its `400` message is printed. The output shows the machine id (the `client_id` of an exchange), its `TokenEndpoint`, the rules and a `curl` exchange, and no secret. Then come the variables of a [pipeline sign-in](#machine-sign-in-for-pipelines): `STORYTELLER_ENDPOINT`, `STORYTELLER_PROJECT`, `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_FILE=<file with the platform token>`, with a note that a GitHub Actions job with `permissions: id-token: write` leaves out the file, and the optional `STORYTELLER_SUBJECT_TOKEN_TYPE` and `STORYTELLER_TOKEN_SCOPE`. `STORYTELLER_ENDPOINT` is the `TokenEndpoint` without its `/v1/{organization}/{project}/access/token`, and `STORYTELLER_PROJECT` is printed in the case the command named the project in, which is the case the server built the endpoint with. So the endpoint `sform` builds equals the `TokenEndpoint` byte for byte, even when `PublicBaseUrl` names another host than `general.baseUrl`. When the `TokenEndpoint` is missing or ends differently, `general.baseUrl` is printed, with a note that the rule's audience must then be `{STORYTELLER_ENDPOINT}/v1/{organization}/{project}/access/token`.
* `machine rules <id>` shows the machine's rules. With the same rule options or `--rules-file` it replaces them.
* `machine issuer` (or `machine issuer list`, alias `issuers`) lists the trusted issuers. `machine issuer set <name> --issuer <iss>` creates or replaces one and, like the issuer `PUT`, clears what it does not set. Its keys come from discovery, from `--jwks-uri <url>` (`JwksUri`), or from `--jwks-file <path>`, whose content is stored as `Jwks` (for example the output of `kubectl get --raw /openid/v1/jwks`). `--max-lifetime <seconds>`, a repeatable `--require-claim key=value` and `--description <text>` set the rest. `machine issuer delete <name>` deletes one.
* `machine reset <id>` of a `Federated` machine prints the server's `400` message. `machine list` shows `–` instead of a secret for federated machines, and for certificate machines, which have none either.

| Method and route | Role | Body and response |
|---|---|---|
| `GET v1/{organization}/{project}/access/issuers` | `Contributor` | `FederatedIssuer[]` |
| `GET v1/{organization}/{project}/access/issuers/{name}` | `Contributor` | `FederatedIssuer`; `404` |
| `PUT v1/{organization}/{project}/access/issuers/{name}` | `Administrator` | `FederatedIssuer` (the route name wins over `Name`) → `200 FederatedIssuer`; `400`, `409` |
| `DELETE v1/{organization}/{project}/access/issuers/{name}` | `Administrator` | `200`; `404` |
| `PUT v1/{organization}/{project}/access/machines/{id}/federation-rules` | `Contributor`; `ContributorWithSecrets` for a machine with a secrets scope, `Administrator` for one with a management scope | `FederationRule[]` → `200 MachineAccess`; `400`, `404`, `409` (the machine changed concurrently twice) |
| `POST v1/{organization}/{project}/access/token` | anonymous | [The token endpoint](#the-token-endpoint) |

The management routes are for users only, like the machine routes: they need `User.Impersonation` and the role on `{organization}.{project}`, and a machine gets `403` there in every build. Contributors read issuers to write rules, but only Administrators decide which issuers the project trusts.

Both `PUT`s take a JSON body. An empty body or `null` is validated as an issuer without `Issuer` (`"The issuer is required."`) or as no rules (`"A Federated machine needs at least one federation rule."`), and a body that is not an issuer or a list of rules answers `400` `InvalidFederation` as well.

`GET` and `DELETE` of a trusted issuer that does not exist answer `404` with no body, like `GET` and `DELETE` of a machine.

### Trusted issuers

| Property | Type | Description |
|---|---|---|
| `Name` | string | Project-unique name: 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit. The route segment, and what rules reference. |
| `Issuer` | string | The exact `iss` of the platform's tokens, 1 to 512 characters, compared ordinally: case and a trailing `/` matter. Cannot change once created. |
| `KeySource` | `FederatedIssuerKeySource` | Where the public keys come from. `Discovery` (default): `{Issuer}/.well-known/openid-configuration`, then its `jwks_uri`. `JwksUri`: the key set at `JwksUri`, for an issuer whose keys are public but whose discovery document is not (a Kubernetes cluster with `--service-account-jwks-uri`). `Jwks`: the stored `Jwks`; nothing is fetched (self-managed Kubernetes). |
| `JwksUri` | string | Required for `JwksUri`, refused otherwise. |
| `Jwks` | string | Required for `Jwks`, refused otherwise. The JSON Web Key Set as JSON text, at most 16 KiB, with 1 to 10 public RSA or EC keys, each with its own `kid`. Private members (`d`, `p`, `q`, `dp`, `dq`, `qi`, `k`) are refused. |
| `MaxTokenLifetimeSeconds` | integer | The longest accepted `exp - iat` of a platform token, 60 to 86,400. Null: 3,600. |
| `RequiredClaims` | object | Claim pins ANDed into every rule of this issuer, with the shape and matching of a rule's `Claims`. The Administrator's bound on what Contributors' rules can admit, for example `repository_owner_id` of the company's GitHub organization. |
| `Description` | string | At most 256 characters. |

* A project has at most 20 trusted issuers.
* With `Discovery`, `Issuer` is an absolute `https` URL without query or fragment. With `JwksUri` or `Jwks` it is any string, because it is never fetched.
* Every URL Storyteller fetches (a discovered `Issuer`, a `JwksUri`, and the `jwks_uri` of a discovery document) uses `https` on port 443, a DNS host (no IP literal in any notation, such as `https://2130706433/` or `https://0x7f.1/`) written in ASCII (punycode for international names), and no user info. `MachineAuth:Federation:AllowPrivateNetworks` lifts `https`, the port and the DNS host; user info, non-ASCII hosts and fragments stay refused.
* A discovery document must name exactly `Issuer` as its `issuer`, and its `jwks_uri` must pass the URL rules. Otherwise the issuer is misconfigured: it is logged at `Warning`, its tokens are refused like a rule mismatch, and its discovery is tried again after a minute.
* `PUT` on an existing name may change everything but `Issuer` (`400`, `"The issuer of a trusted issuer cannot change; create a new one."`), because its rules were written for that issuer.
* Creating a name that rules of machines still reference (left behind by a delete) answers `409`, naming up to 10 of those machines, so a re-created name never silently revives rules written for another issuer.
* Deleting a trusted issuer is allowed while rules reference it: it is the kill switch. Those rules stop matching within the 60-second cache of the issuer list.
* Keys are cached per issuer and its settings, so an edited issuer starts with fresh keys. They are fetched again after `MetadataRefreshMinutes` (60). A token naming an unknown `kid` makes Storyteller fetch again at once when the keys are older than a minute; within that minute, or when the `kid` is still unknown right after the fetch, the exchange answers 503, because the issuer may still be publishing a rotated key. With `Jwks` there is nothing to fetch, and an unknown `kid` is refused. A token without a `kid` is refused with every key source, without a fetch. Concurrent requests share one fetch, and a failed fetch answers the next requests with 503 for 5 seconds.

Storyteller keeps the issuers in the organization container, partition `{project}.federation`, one document per issuer with the name as id. The list is cached for 60 seconds per instance and dropped on a write by the same instance.

### Federation rules

| Property | Type | Description |
|---|---|---|
| `Issuer` | string | `Name` of a trusted issuer of the project. |
| `Audience` | string | Required, 1 to 512 characters. The token's `aud` must be this string, or a one-element array holding it. Use the machine's `TokenEndpoint` URL, except for Azure (see [Azure managed identity](#azure-managed-identity)). |
| `Subject` | string | Required, 1 to 512 characters. The token's `sub` exactly, or as a prefix when the value ends with `:*` or `/*`. |
| `Claims` | object | Optional further pins. Key: a top-level claim name (`repository`, `environment`, `repository_owner_id`) or an [RFC 6901](https://www.rfc-editor.org/rfc/rfc6901) JSON pointer for a nested claim (`/kubernetes.io/namespace`). Value: the accepted values. |

A machine has 1 to 10 rules. Create sets them with `MachineAccessCreate.FederationRules`; `PUT …/machines/{id}/federation-rules` replaces them and returns the machine. Rules on a machine of another kind answer `400` (`MachineNotFederated`). Rule changes affect new exchanges only.

```json
PUT v1/acme/billing/access/machines/3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07/federation-rules
[ { "Issuer": "aks-prod", "Audience": "https://api.example.com/api/v1/acme/billing/access/token",
    "Subject": "system:serviceaccount:billing:*", "Claims": { "/kubernetes.io/namespace": [ "billing" ] } },
  { "Issuer": "github", "Audience": "https://api.example.com/api/v1/acme/billing/access/token",
    "Subject": "repo:acme@65/billing@74:environment:production", "Claims": { "repository_id": [ "74" ] } } ]
```

**Matching.** An exchange verifies the platform token against the machine's rules:

1. Only rules whose trusted issuer has exactly the token's `iss` are candidates. Without one the token is refused, and nothing is fetched.
2. The candidates are grouped by trusted issuer, in rule order. Each group verifies the token with **that** issuer's keys and only its rules' audiences:
   * a signature with RS256, RS384, RS512, PS256, PS384, PS512, ES256, ES384 or ES512 (never `none` or HS*), with a `kid` that names one of the keys;
   * `exp` required, `nbf` when present, both with `ClockSkewSeconds`;
   * `iat` present and not in the future, and `exp - iat` at most the issuer's `MaxTokenLifetimeSeconds`;
   * `aud` a string or a one-element array: a token minted for several relying parties is refused;
   * `sub` a non-empty string.
3. Each rule of the group is checked again against the current trusted issuer. A rule that an issuer change made unsafe is skipped and logged.
4. A rule matches when the audience, the subject, every key of its `Claims` and every key of the issuer's `RequiredClaims` match. A claim matches when it is a string equal to an accepted value, a number or boolean whose JSON text equals one, or an array with any such element. Objects, null and missing claims never match. The values of a key are alternatives; all keys must match. Comparisons are ordinal and case-sensitive.
5. The first matching rule wins. A rule never matches a token verified by another trusted issuer, even one with the same `Issuer` string, so two clusters with the same issuer string are told apart by their keys.

**Validation.** Create and the rules `PUT` check each rule against its trusted issuer. A failure answers `400` with `ErrorCode` `InvalidFederation` and a message that names the rule and the field, for example `"Rule 2: the audience sts.amazonaws.com belongs to another service."`.

| Field | Rule |
|---|---|
| `Issuer` | Names a trusted issuer of the project. |
| `Audience` | Refused when it belongs to another relying party, so a token minted for that party cannot be replayed here: `sts.amazonaws.com`, `pods.eks.amazonaws.com`, `api://AzureADTokenExchange`, `https://kubernetes.default.svc`, `https://kubernetes.default.svc.cluster.local`; anything starting with `//iam.googleapis.com/`, `https://iam.googleapis.com/`, `https://github.com/` or `https://vercel.com/` (the GitHub and Vercel defaults); the Azure resources `https://management.azure.com`, `https://management.core.windows.net`, `https://graph.microsoft.com`, `https://vault.azure.net`, `https://storage.azure.com`, `https://database.windows.net`; and the trusted issuer's own `Issuer` (the default Kubernetes `--api-audiences`). Case and one trailing `/` are ignored. |
| `Subject` | `*` may appear once, as the last character, right after `:` or `/`: `system:serviceaccount:prod:*`, `repo:acme@65/billing@74:*`, `arn:aws:iam::123456789012:role/ci/*`. |
| `Claims` | At most 10 keys, 1 to 10 values per key, each value 1 to 512 characters. A claim name matches `^[A-Za-z0-9_.:-]{1,128}$`. A pointer is `/` and 1 to 256 characters, with `~1` for `/` and `~0` for `~`; any other `~` sequence is refused. `iss`, `sub`, `aud`, `exp`, `nbf`, `iat` and `jti` cannot be pinned, bare or as `/name`; use `Subject` and `Audience`. `RequiredClaims` follow the same rules. |
| GitHub Actions issuers | Host `token.actions.githubusercontent.com` (any path, so enterprise issuers too) or `token.actions.<subdomain>.ghe.com`, whatever the `KeySource`: the rule or the issuer's `RequiredClaims` must pin `repository_owner_id`, `repository_id` or `enterprise_id`. |
| Vercel issuers | Host `oidc.vercel.com` (global and team issuers): the rule or the issuer's `RequiredClaims` must pin `owner_id`. |
| Google and Entra issuers | `https://accounts.google.com`, `accounts.google.com`, `https://login.microsoftonline.com/…` and `https://sts.windows.net/…`: no subject wildcard. `sub` is the identity's immutable id, so a wildcard would admit every identity of the tenant or of Google. |
| Other issuers | A subject wildcard needs at least one pin in `Claims` or `RequiredClaims`. |

### The token endpoint

`POST v1/{organization}/{project}/access/token` is anonymous: the platform token is the credential. Send the request without `Authorization`: a bearer header is checked before any endpoint runs, and a request with any `Authorization` header is refused with `invalid_request`. The body is `application/x-www-form-urlencoded` (a `charset` parameter is allowed), at most 32 KiB.

| Parameter | Rule |
|---|---|
| `grant_type` | Required: `urn:ietf:params:oauth:grant-type:token-exchange`. Anything else answers `unsupported_grant_type`. |
| `subject_token` | Required: the platform token, a JWT of at most `MaxSubjectTokenBytes` (16 KiB). |
| `subject_token_type` | Required: `urn:ietf:params:oauth:token-type:jwt`, `…:id_token` or `…:access_token` (Azure managed identity tokens are access tokens). The token must be a JWT either way. |
| `client_id` | Required: the machine id. |
| `audience`, `resource` | Optional and repeatable. Every value must equal `MachineAuth:Federation:Audience`, otherwise `invalid_target`. They name the Storyteller token's audience, not the platform token's; leave them out. |
| `scope` | Optional: Storyteller scopes separated by spaces. It must be a subset of the scope strings of the machine's [scopes](#scopes-of-machines) (`invalid_scope` otherwise), and the token gets only that subset, for example `Annotation.Read` for a job that only reads. Without it, the token gets every scope of the machine. A pipeline that needs `Configuration.Transfer` on some runs only can ask for it on those runs and leave it out on the others. |
| `requested_token_type` | Optional: `urn:ietf:params:oauth:token-type:access_token` (default) or `…:jwt`; anything else answers `invalid_request`. `issued_token_type` echoes it. |
| `actor_token`, `actor_token_type` | Not supported (`invalid_request`). |

Any other parameter given twice answers `invalid_request`. A parameter with an empty value counts as absent.

The checks run cheapest first, so the anonymous endpoint reads nothing for unknown projects and untrusted issuers:

1. The request: body size, content type and parameters.
2. The subject token's size, its JWS form and its `iss`, not yet verified.
3. The project's access point must exist, and its policy must enable `Federated`. Nothing in the organization container is read before this.
4. A trusted issuer of the project must have exactly that `Issuer`; otherwise the machine is not read.
5. The machine must exist with kind `Federated`.
6. The platform token must [match one of its rules](#federation-rules); then `scope` must be a subset of the machine's scopes.
7. A [Storyteller token](#storyteller-tokens) is issued. An exchange writes nothing.

Every failure in steps 2 to 6, except the `scope` subset, answers the same `400 invalid_request` with `"The subject token is not accepted for this machine."`, so unknown projects, unknown machines, disabled kinds and mismatched rules look alike. The reason is logged with the machine id and the trusted issuer name, never with the token: an untrusted issuer at `Debug`, the rest at `Information`.

The one exception is the `503` of step 6. It comes when the keys of a `Discovery` or `JwksUri` issuer cannot be fetched, or when the token names a `kid` the issuer has not published (see [Trusted issuers](#trusted-issuers)). Step 6 runs only after steps 3 to 5 have passed and only for the machine's rules of the token's issuer, and the `kid` is looked up only after the token's `aud` has matched the audience of one of those rules. So a `503` tells the caller that the project enables `Federated`, trusts the token's issuer, and has a `Federated` machine with that id and a rule for that issuer; for an unpublished `kid`, also that the rule has the token's audience. The token itself needs no valid signature to learn this. With a `Jwks` issuer, step 6 never answers `503`.

Responses carry `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store` and `Pragma: no-cache`. Property names are the snake_case names of OAuth, not the API's PascalCase, and errors are OAuth error bodies, not `ErrorResponse`.

```json
200 { "access_token": "eyJ…", "issued_token_type": "urn:ietf:params:oauth:token-type:access_token",
      "token_type": "Bearer", "expires_in": 900, "scope": "Annotation.Read Configuration.Read" }
400 { "error": "invalid_request", "error_description": "The parameter subject_token is missing." }
503 { "error": "temporarily_unavailable", "error_description": "The keys of the trusted issuer could not be retrieved." }
```

| Status | `error` | When |
|---|---|---|
| `400` | `invalid_request` | A malformed request (a missing, repeated or unsupported parameter, a wrong content type, a body over 32 KiB, an `Authorization` header, an actor token), or any refused subject token, as above. |
| `400` | `unsupported_grant_type` | `grant_type` is not the token exchange. |
| `400` | `invalid_target` | An `audience` or `resource` other than `MachineAuth:Federation:Audience`. |
| `400` | `invalid_scope` | `scope` is not a list of scope names separated by single spaces ([RFC 6749 §3.3](https://www.rfc-editor.org/rfc/rfc6749#section-3.3)), checked before the token; or the token matched, but `scope` asks for more than the machine has. A machine without scopes gets it for every exchange. |
| `503` | `temporarily_unavailable` | The keys of the trusted issuer could not be retrieved, the token names a `kid` the issuer has not published yet, or no signing key is available. Only after the project, issuer and machine checks, as above. With `Retry-After: 5`; retry with the same platform token. |

`temporarily_unavailable` on a token endpoint is an extension of [RFC 6749 §5.2](https://www.rfc-editor.org/rfc/rfc6749#section-5.2); clients should key on the 503.

```bash
API=https://api.example.com/api
TOKEN_ENDPOINT=$API/v1/acme/billing/access/token

STORYTELLER_TOKEN=$(curl -sf "$TOKEN_ENDPOINT" \
  -d grant_type=urn:ietf:params:oauth:grant-type:token-exchange \
  -d client_id=3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07 \
  -d subject_token_type=urn:ietf:params:oauth:token-type:jwt \
  --data-urlencode "subject_token=$PLATFORM_TOKEN" \
  -d scope=Annotation.Read | jq -r .access_token)

curl "$API/v1/acme/billing/default/annotations" -H "Authorization: Bearer $STORYTELLER_TOKEN"
```

Reuse the Storyteller token until shortly before `expires_in` runs out, then exchange a fresh platform token. `sform` does this itself for a [pipeline sign-in](#machine-sign-in-for-pipelines).

### Storyteller tokens

A Storyteller token is an [RFC 9068](https://www.rfc-editor.org/rfc/rfc9068) access token signed by Storyteller:

| Part | Value |
|---|---|
| Header | `alg` `RS256`, `kid` of the signing key, `typ` `at+jwt`. |
| `iss` | `MachineAuth:Federation:Issuer`. |
| `aud` | `MachineAuth:Federation:Audience`. |
| `sub`, `client_id` | The machine id. |
| `iat`, `nbf`, `exp` | The time of the exchange, and `TokenLifetimeMinutes` (15) later. |
| `jti` | A GUID. |
| `scope` | The granted Storyteller scopes, separated by spaces. |
| `organization`, `project` | The project of the exchange. |
| `annotation_key` | The machine's `AnnotationKey`, when it has one. |
| `federation_issuer`, `federation_subject` | The trusted issuer name and the platform `sub`, for audit and logs. |

`BearerAuthenticationMiddleware` reads the unverified `iss` first. A Storyteller token goes only to the Storyteller validator, never to the user provider, and `DEV_AUTH` does not skip it. The validator requires `typ` `at+jwt` or `application/at+jwt`, RS256, the `kid` of the stored signing key, the exact issuer and audience, and a valid lifetime within `ClockSkewSeconds`. The API then sees `sub` and `azp` (the machine id), one `roles` claim per scope and `annotationKey`, as for an API key. The access check of every call point-reads the machine instead of only checking that it exists, and requires:

* the token's `organization` and `project` equal to the route's (case-insensitive);
* the machine in the route's project, with the stored kind `Federated`;
* the project still enabling `Federated`.

So a Storyteller token cannot act as an API key machine or outlive its machine, even when it was signed with a stolen key. Any failure is 401 with an empty body. No `WWW-Authenticate` header is sent, as for every other token. Each operation then needs its [scopes](#scopes-of-machines) stored on the machine and carried by the token. A scope the token carries but the machine does not store grants nothing, so the token cannot exceed its machine's scopes either, and a token that asked for a subset gets `403 AccessDenied` outside it. A token that carries `User.Impersonation` still gets `403` on every route for users only.

The deployment signs with one RSA 2048 key, stored in the core container, partition `signing-keys`, id `tsk.1`, its `kid` being the RFC 7638 thumbprint. With `SigningKeyAutoBootstrap` the first exchange creates it; concurrent instances settle on one key through a create conflict. An instance signs with the stored key for 10 minutes before it reads the store again. If the store then has no key and `SigningKeyAutoBootstrap` is off, or holds a key that is not RS256, the exchange answers 503 instead of signing with the old key, and the next exchange reads the store again. A store that cannot be read keeps the loaded key in use. Validation never creates a key. A validating instance reads the store again after 10 minutes too, and at most once per 30 seconds for an unknown `kid` or while no key is stored; in between, such tokens are refused without I/O.

### Revocation timing

| Change | Takes effect |
|---|---|
| Deleting the machine | At once: every call reads the machine. |
| Disabling `Federated` in the policy | New exchanges at once (they read the access point). Calls with issued tokens within 60 seconds (the policy cache). |
| Deleting or editing a trusted issuer | New exchanges, within the 60-second cache of the issuer list on the other instances. |
| Replacing the rules | New exchanges. |

Storyteller tokens already issued stay valid until they expire (`TokenLifetimeMinutes` plus `ClockSkewSeconds`), unless the machine is deleted or `Federated` is disabled. There is no revocation list and no replay store.

### Configuration

`Program.cs` registers federation with `services.AddFederatedMachineAccess(context.Configuration)` after `AddCertificateMachineAccess`. It works next to the one `ClientCredentials` provider of the deployment. Without it, creating a `Federated` machine answers `400` `"Federated machine access is not configured for this deployment."`. With Cosmos (`AddCosmosDbAnnotations`) the trusted issuers and the signing key are stored as described above; otherwise in-memory stores are used (tests).

The settings live under `MachineAuth:Federation` and are validated at startup:

| Key | Default | Notes |
|---|---|---|
| `Issuer` | `urn:storyteller:{MachineAuth:TrustDomain}` | `iss` of Storyteller tokens. Changing it rejects the tokens already issued. |
| `Audience` | `urn:storyteller:{MachineAuth:TrustDomain}:api` | `aud` of Storyteller tokens, and the only accepted `audience` or `resource` of an exchange. |
| `PublicBaseUrl` | none | The public base URL of the API (`https://host/api`), absolute http(s) without query or fragment. `TokenEndpoint` is built from it. Without it, from the scheme and host of the request URL, never from `X-Forwarded-Host`. Set it when the API is reached through another host name. |
| `TokenLifetimeMinutes` | `15` | Lifetime of a Storyteller token, 5 to 60. |
| `ClockSkewSeconds` | `30` | Skew for platform and Storyteller tokens, 0 to 120. |
| `MaxSubjectTokenBytes` | `16384` | Larger platform tokens are refused before parsing. 1,024 to 32,768. |
| `MetadataRefreshMinutes` | `60` | Age after which discovery documents and key sets are fetched again, 1 to 1,440. |
| `SigningKeyAutoBootstrap` | `true` | Create the signing key on the first exchange when none is stored. When `false` and no key is stored, exchanges answer 503. |
| `AllowPrivateNetworks` | `false` | Lifts the `https`, port 443, DNS host and public address limits of issuer URLs. Development and tests only. |

### Security notes

* **Name-derived issuers and immutable ids.** GitHub organization and repository names and Vercel team and project slugs can be renamed and taken by someone else, and the tokens of every GitHub or Vercel tenant are signed with the same keys. Only the ids tell tenants apart, so rules of these issuers must pin one (see the validation table), best in the issuer's `RequiredClaims`. For Google and Entra, `sub` is the identity's immutable id: pin it exactly. Never identify a Google service account by `email`: a deleted and re-created account keeps its email and gets a new id.
* **Foreign and default audiences.** A rule's audience must be specific to Storyteller. Each platform's default audience (`https://github.com/<owner>`, `https://vercel.com/<team>`, the Kubernetes issuer) goes to every relying party that asks for no audience of its own, and the audiences of AWS, Azure, Google and Kubernetes services belong to them. Rules refuse all of these, so a token handed to another service cannot be replayed here.
* **Multi-audience tokens.** A token whose `aud` lists several relying parties is refused: any of them could replay it.
* **Opaque refusals.** Every refused exchange looks the same to the caller (`400 invalid_request`), so it does not reveal projects, machines, scopes or rules. The `503` of a `Discovery` or `JwksUri` issuer is the exception. A key fetch failure answers `503` only once the project, its policy, the trusted issuer, a `Federated` machine with that id and a rule for that issuer have all matched, and a `kid` the issuer has not published only once the rule's audience has matched too. A caller who knows a machine id can therefore send tokens with a made-up `kid` and no valid signature, and learn whether the project trusts an issuer and which audience a rule uses. Such probes also make Storyteller fetch the issuer's keys again, at most once a minute per trusted issuer. A `Jwks` issuer has no such `503`. Machine ids are not secret: they appear in workflow files as `client_id`.
* **Private networks.** Storyteller fetches discovery documents and key sets from URLs an Administrator enters. The fetch uses no proxy, follows no redirect, times out after 10 seconds (for the whole response, body included) and reads at most 512 KiB. It resolves the host and connects only to an address that the IANA special-purpose address registries mark globally reachable, so no loopback, private, CGNAT (`100.64.0.0/10`), link-local (including `169.254.169.254`), multicast, `fc00::/7` or `fe80::/10` address. IPv4 embedded in IPv6 (mapped, compatible, NAT64, 6to4, Teredo) is checked as IPv4, and other IPv6 addresses must be global unicast (`2000::/3`). It connects to the address it checked, so DNS rebinding cannot redirect it. `AllowPrivateNetworks` lifts all of this; never set it in production.
* **The signing key in Cosmos.** The key is stored unprotected in the core container, like the machine CA. Whoever can read that container can mint Storyteller tokens. The per-call checks keep such tokens within existing `Federated` machines of projects that enable the kind, and within those machines' scopes; they cannot act as an API key, certificate or client-credentials machine. **Emergency rotation:** delete the document `tsk.1` (partition `signing-keys`) from the core container. Once each instance's next read of the store succeeds, about 10 minutes later, it stops signing with and accepting the old key, and the next exchange creates a new one (with `SigningKeyAutoBootstrap`; without it, exchanges answer 503 until a new key is stored); restarting the Function App makes it immediate. An instance that cannot read the store keeps the key it has loaded, so during a Cosmos read outage deleting `tsk.1` alone does not revoke the old key. When revocation comes first, restart the Function App right away: until Cosmos recovers, the restarted instances can neither issue nor validate Storyteller tokens. Workloads exchange again after their next 401.
* **Long-lived managed identity tokens.** An Azure managed identity token is cached by the platform for about 24 hours, so its issuer needs `MaxTokenLifetimeSeconds` 86,400, and a leaked token can be exchanged for that long. There is no replay store: managed identity, Vercel and Kubernetes tokens are reused by design, and single use would break them. Keep the subject exact and the machine's scopes small: give it only the scopes its jobs need, management scopes included, and let the jobs that need fewer leave the others out of `scope`.
* **Secret delivery.** A federated machine never presents a certificate, so [secret delivery](key-values.md#secret-delivery) treats it as a token caller: under `RequireMtlsForSecretDelivery` (the default) it gets no secret values, whatever its scopes.
* **Who writes rules.** Contributors create machines of runtime scopes without secrets, so they also write the rules of those federated machines. Creating, resetting or giving new rules to a machine with a `Secrets.*` scope needs `ContributorWithSecrets`, and to one with a management scope `Administrator` (see [Scopes of machines](#scopes-of-machines)), so a Contributor cannot point their tokens at another workload. Administrators bound what all rules can admit with the issuers they trust and their `RequiredClaims`.

### Setting up a platform

Each platform below names the trusted issuer, the rule's audience and subject, the issuer's `MaxTokenLifetimeSeconds`, the `subject_token_type`, and how the workload gets its token. `$AUD` is the rule's audience: the machine's `TokenEndpoint`, for example `https://api.example.com/api/v1/acme/billing/access/token`, everywhere except Azure. The exchange is the [curl request above](#the-token-endpoint).

#### GitHub Actions

* **Issuer:** `https://token.actions.githubusercontent.com`, `Discovery`. An enterprise with a unique issuer (`include_enterprise_slug`) uses `https://token.actions.githubusercontent.com/<enterprise>`; GHE.com uses `https://token.actions.<subdomain>.ghe.com`, optionally with `/<enterprise>`.
* **Audience:** `$AUD`, requested by the workflow. The default `https://github.com/<owner>` is refused.
* **Subject:** `repo:<owner>@<owner-id>/<repo>@<repo-id>:environment:<name>`, `…:ref:refs/heads/<branch>`, `…:pull_request`, or a prefix such as `repo:acme@65/billing@74:*`. Older repositories use the name-based `repo:<owner>/<repo>:…` (see below).
* **Pins:** `repository_owner_id` in the issuer's `RequiredClaims`, `repository_id` in the rule. One of `repository_owner_id`, `repository_id` or `enterprise_id` is required.
* **Lifetime:** the default. A token is valid for a single job, and GitHub's documented example lives 5 minutes.
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:jwt`.

GitHub's immutable subject, `repo:OWNER@OWNER_ID/REPO@REPO_ID:…` (for example `repo:acme@65/billing@74:ref:refs/heads/main`), is the default for repositories created, renamed or transferred after 2026-07-15. Older repositories keep the name-based `repo:OWNER/REPO:…` (`repo:acme/billing:environment:production`) unless the organization or repository opts in, and GitHub Enterprise Server offers only the name-based format. Write the subject in the format the repository uses: a name-based subject stops matching once the repository is renamed or transferred. The id pins exist because a recycled name produces the same name-based `sub`.

The job needs `permissions: id-token: write`. A JavaScript action calls `core.getIDToken('<aud>')`; a shell step asks the runner:

```yaml
permissions:
  id-token: write
  contents: read

jobs:
  sync:
    runs-on: ubuntu-latest
    environment: production
    steps:
      - name: Get a Storyteller token
        env:
          TOKEN_ENDPOINT: https://api.example.com/api/v1/acme/billing/access/token   # also the rule's audience
          MACHINE_ID: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
        run: |
          PLATFORM_TOKEN=$(curl -sf -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
            "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=$(jq -rn --arg a "$TOKEN_ENDPOINT" '$a|@uri')" | jq -r .value)
          STORYTELLER_TOKEN=$(curl -sf "$TOKEN_ENDPOINT" \
            -d grant_type=urn:ietf:params:oauth:grant-type:token-exchange \
            -d client_id=$MACHINE_ID \
            -d subject_token_type=urn:ietf:params:oauth:token-type:jwt \
            --data-urlencode "subject_token=$PLATFORM_TOKEN" | jq -r .access_token)
          echo "::add-mask::$STORYTELLER_TOKEN"
          echo "STORYTELLER_TOKEN=$STORYTELLER_TOKEN" >> "$GITHUB_ENV"
```

This token lives 15 minutes, and nothing renews it. `sform` needs neither step: with `STORYTELLER_ENDPOINT`, `STORYTELLER_PROJECT` (or `STORYTELLER_KEY`) and `STORYTELLER_CLIENT_ID` set, it asks the runner for the token itself and exchanges again as needed (see [Machine sign-in for pipelines](#machine-sign-in-for-pipelines)). So does the configuration provider with `STORYTELLER_ENDPOINT`, `STORYTELLER_KEY` and `STORYTELLER_CLIENT_ID`.

#### Kubernetes

* **Issuer:** the cluster's service account issuer, the `iss` of its tokens.
  * AKS: `oidcIssuerProfile.issuerUrl` (`az aks show -n <cluster> -g <group> --query oidcIssuerProfile.issuerUrl -o tsv`), `https://<region>.oic.prod-aks.azure.com/<tenant>/<id>/`, `Discovery`. Copy it verbatim: it ends with `/`.
  * EKS: `aws eks describe-cluster --name <cluster> --query cluster.identity.oidc.issuer --output text`, `https://oidc.eks.<region>.amazonaws.com/id/<id>`, `Discovery`.
  * GKE: `https://container.googleapis.com/v1/projects/<project>/locations/<location>/clusters/<cluster>`, with its discovery document under that URL. That it can be read without credentials is reported only by third parties; when discovery fails, use `Jwks`.
  * Self-managed: the discovery endpoints of a cluster are not anonymous by default. Use `KeySource` `Jwks` with the output of `kubectl get --raw /openid/v1/jwks`, or `JwksUri` when `--service-account-jwks-uri` points to a public copy. A stored set never refreshes: paste it again after every key rotation of the cluster. Clusters may share an issuer string; give each its own trusted issuer, and their keys tell them apart.
* **Audience:** `$AUD`, in a dedicated projected token. The default token of the `kube-api-access` volume is refused: its audience belongs to the API server. Tokens of AKS workload identity (`api://AzureADTokenExchange`), EKS IRSA (`sts.amazonaws.com`) and EKS Pod Identity (`pods.eks.amazonaws.com`) are refused too; project a separate token for Storyteller next to them.
* **Subject:** `system:serviceaccount:<namespace>:<name>`, or `system:serviceaccount:<namespace>:*` with a pin.
* **Pins:** `/kubernetes.io/namespace`; `/kubernetes.io/serviceaccount/uid` binds the rule to one service account object rather than its name.
* **Lifetime:** the default, which matches the default `expirationSeconds` of 3,600. Raise it to `expirationSeconds` when that is larger (AKS allows up to 24 hours).
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:jwt`.

```yaml
spec:
  serviceAccountName: billing-sync
  containers:
    - name: app
      volumeMounts:
        - name: storyteller-token
          mountPath: /var/run/secrets/storyteller
          readOnly: true
  volumes:
    - name: storyteller-token
      projected:
        sources:
          - serviceAccountToken:
              path: token
              audience: https://api.example.com/api/v1/acme/billing/access/token
              expirationSeconds: 3600
```

The kubelet replaces the token once it is older than 80 % of its lifetime or 24 hours. Read `/var/run/secrets/storyteller/token` again before each exchange; never keep its content. `sform` in the pod does this with `STORYTELLER_TOKEN_FILE=/var/run/secrets/storyteller/token` next to `STORYTELLER_ENDPOINT`, `STORYTELLER_PROJECT` (or `STORYTELLER_KEY`) and `STORYTELLER_CLIENT_ID` (see [Machine sign-in for pipelines](#machine-sign-in-for-pipelines)), and so does the configuration provider with the same variables and `STORYTELLER_KEY`.

#### Vercel

* **Issuer:** the team issuer `https://oidc.vercel.com/<team-slug>`, `Discovery`. A token with a custom audience always carries it, whatever issuer mode the project uses, so the global `https://oidc.vercel.com` never fits. Renaming the team changes the issuer, and `Issuer` cannot change: add a new trusted issuer and move the rules to it.
* **Audience:** `$AUD`. A build gets its token in `VERCEL_OIDC_TOKEN` and a function in the `x-vercel-oidc-token` request header, both with the default audience `https://vercel.com/<team-slug>`, which is refused. `getVercelOidcToken({ audience })` of `@vercel/oidc` 3.7.0 or later exchanges it at Vercel for a token with the rule's audience and the team issuer.
* **Subject:** `owner:<team-slug>:project:<project-name>:environment:<environment>`, where the environment is `development`, `preview`, `production` or a custom environment.
* **Pins:** `owner_id` (`team_…`, required), best in `RequiredClaims`; `project_id` (`prj_…`) in the rule. The slugs in `sub` change when a team or project is renamed; the ids do not.
* **Lifetime:** 7,200. Function tokens live 2 hours (Vercel reuses one for up to 90 minutes) and build tokens 1 hour. 43,200 when development tokens (12 hours) are exchanged too.
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:jwt`.

```ts
import { getVercelOidcToken } from '@vercel/oidc';

const subjectToken = await getVercelOidcToken({ audience: tokenEndpoint });
const response = await fetch(tokenEndpoint, {
  method: 'POST',
  body: new URLSearchParams({
    grant_type: 'urn:ietf:params:oauth:grant-type:token-exchange',
    client_id: machineId,
    subject_token_type: 'urn:ietf:params:oauth:token-type:jwt',
    subject_token: subjectToken,
  }),
});
const { access_token, expires_in } = await response.json();
```

#### Azure managed identity

* **Audience:** the `aud` of tokens for an app registration in your own tenant that stands for Storyteller and nothing else, with the identifier URI `api://<appId>`; the workload asks for `resource=api://<appId>`. The registration's `requestedAccessTokenVersion` decides the token version, whatever endpoint the workload calls: with `2`, `aud` is `<appId>`; with null or `1`, it is `api://<appId>`. A token for an Azure resource (management, Graph, Key Vault, Storage, SQL) is refused.
* **Issuer:** with version 2, `https://login.microsoftonline.com/<tenant-id>/v2.0`; with version 1, `https://sts.windows.net/<tenant-id>/` (with the trailing `/`). `<tenant-id>` is the tenant ID, a GUID (`az account show --query tenantId -o tsv`), never a domain such as `contoso.onmicrosoft.com`: tokens carry the GUID, and discovery under a domain answers with the GUID issuer, which does not equal `Issuer`. `Discovery` works for both versions. A token of the other version is refused.
* **Subject:** the managed identity's object (principal) id; for a managed identity, `sub` equals `oid`. Every identity of the tenant can get a token for the audience, so the subject is the only thing that picks yours, and a wildcard is refused.
* **Lifetime:** 86,400. The managed identity endpoint caches a token per resource for about 24 hours and cannot be made to refresh it.
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:access_token`.

```bash
# once: the app registration that stands for Storyteller
az ad app create --display-name "Storyteller acme.billing"
az ad app update --id <appId> --identifier-uris api://<appId>
az ad sp create --id <appId>
# then set "requestedAccessTokenVersion": 2 under "api" in its manifest

# VM or VM scale set (IMDS); add client_id=<id> to pick a user-assigned identity
curl -s -H "Metadata: true" \
  "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=api://<appId>" | jq -r .access_token

# App Service and Functions
curl -s -H "X-IDENTITY-HEADER: $IDENTITY_HEADER" \
  "$IDENTITY_ENDPOINT?api-version=2019-08-01&resource=api://<appId>" | jq -r .access_token
```

On AKS, use the [Kubernetes](#kubernetes) path instead of workload identity.

#### AWS

* **Issuer:** turn on outbound identity federation for the account once with `aws iam enable-outbound-web-identity-federation`, then read the account's issuer, `https://<id>.tokens.sts.global.api.aws`, from `aws iam get-outbound-web-identity-federation-info`. `Discovery`.
* **Audience:** `$AUD`, as the only audience of the token; a token with several audiences is refused.
* **Subject:** the ARN of the IAM principal, for example `arn:aws:iam::123456789012:role/billing-sync`, or a prefix such as `arn:aws:iam::123456789012:role/ci/*` with a pin.
* **Pins:** the STS claims sit in the object `https://sts.amazonaws.com/`, so their pointers escape the slashes: `/https:~1~1sts.amazonaws.com~1/aws_account`, `/https:~1~1sts.amazonaws.com~1/org_id`, `/https:~1~1sts.amazonaws.com~1/source_region`. Never pin `request_tags`: the caller chooses them.
* **Lifetime:** the default. `DurationSeconds` is 60 to 3,600 (300 when not given).
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:jwt`. Both signing algorithms, `ES384` and `RS256`, are accepted.

```bash
aws sts get-web-identity-token --audience "$AUD" --signing-algorithm ES384 --region eu-central-1 \
  --query WebIdentityToken --output text
```

Call a regional STS endpoint: the global one does not offer the operation. The response is JSON with `WebIdentityToken` and `Expiration`; `--query WebIdentityToken --output text` prints the token alone.

#### Google Cloud

* **Issuer:** `https://accounts.google.com`, `Discovery`.
* **Audience:** `$AUD`.
* **Subject:** the service account's numeric unique id (`gcloud iam service-accounts describe <email> --format='value(uniqueId)'`), never its email. No wildcard.
* **Pins:** with `format=full`, the Compute Engine claims under `google`, for example `/google/compute_engine/project_id`.
* **Lifetime:** the default; the tokens expire within an hour.
* **`subject_token_type`:** `urn:ietf:params:oauth:token-type:id_token`.

```bash
curl -sG -H "Metadata-Flavor: Google" --data-urlencode "audience=$AUD" -d format=full \
  "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity"
```

Compute Engine and Cloud Run serve this endpoint, and it returns the token. On GKE, use the [Kubernetes](#kubernetes) path: whether the GKE metadata server serves it to a workload without an IAM service account is not confirmed.

## Caveats and scale of machine access

A deployment has five machine credential kinds. `ClientCredentials` with AuthKit creates one WorkOS Connect M2M application per machine, and every one of those applications sits in the single organization `Auth:AuthKit:MachineOrganizationId`. `ApiKey`, `Certificate` and `CertificateAndApiKey` keep the credential inside Storyteller. `Federated` keeps no credential at all: trust comes from the platforms' issuers, and Storyteller signs its own short-lived tokens. The notes below are how that behaves at a few hundred thousand machines.

### What each call does

A machine call that names a project goes through one of the `CheckAccessTo*Async` checks of `HttpRequestDataExtensions` (the project, one annotation, the annotations of a bulk body, or the project metadata), which share one core. That point-reads the `MachineAccessEntity` whose id is the machine id, in partition `{project}.access`, in the organization container. A missing document (or a missing container) is rejected with `401`, and the document's `AnnotationKey` decides the [annotation restriction](#annotation-restriction-of-machines), and its `Scopes` the [scopes](#scopes-of-machines) of every operation; for a `Federated` machine the same read also checks the stored kind. Every machine call also reads the project policy (`IMachineAuthenticationPolicyStore`, cached for 60 seconds): an API key or certificate call checks that the machine's own kind is enabled, and a bearer call that its kind (`ClientCredentials` or `Federated`) is enabled.

| Kind | Sent on each call | Work beside the membership read |
|---|---|---|
| `ApiKey` | `Authorization: ApiKey` and the structured `2s.` key | The key carries the organization, project and machine id. Storyteller point-reads the same entity and compares a SHA-256 hash. |
| `Certificate` | The client certificate | The chain is built against the configured CA. Revocation checking is off. The thumbprint is then checked in `IClientCertificateStore`. |
| `CertificateAndApiKey` | The certificate and the API key | Both checks have to succeed, and the organization, project and machine id have to match. With a shared certificate, the certificate has to be a stored, unrevoked shared certificate of the same organization and project, and the machine id comes from the API key. |
| `ClientCredentials` | `Authorization: Bearer` and a JWT | RS256 against the provider's JWKS, which is cached. AuthKit also requires `sub` equal to `client_id`, `aud` equal to `Auth:AuthKit:ClientId`, and `org_id` equal to `MachineOrganizationId`. Scopes come only from `PermissionMap`. |
| `Federated` | `Authorization: Bearer` and a Storyteller token | RS256 against the one Storyteller signing key, held in memory; no user provider is asked. The token's organization and project are checked against the route, the machine read checks the kind, and each operation checks its scopes against the stored ones. |

The membership read stays a point read as the catalogue grows, as long as the machines are spread across projects. Every machine of one project shares the logical partition `{project}.access`. Cosmos serves a logical partition from one physical partition, and a physical partition is capped at 10,000 RU/s. A point read of this small document is about 1 RU, so one project cannot carry tens of thousands of machine calls per second.

### AuthKit at a few hundred thousand applications

Storyteller never lists WorkOS applications. Create, reset and delete address one application by id, and a live call is allowed by the Cosmos document. API traffic therefore does not grow with the size of the WorkOS catalogue. Provisioning and token issuance do.

WorkOS publishes a general management-API limit of 6,000 requests per 60 seconds per API key ([rate limits](https://workos.com/docs/reference/rate-limits)). `Auth:AuthKit:ApiKey` is that key. The same key also looks up users during account registration. Connect application routes have no separate published limit. WorkOS does not publish a maximum number of M2M applications, or of applications in one organization. It does say an environment can contain any number of organizations.

Create spends two management calls: the application, then the secret. A failed secret deletes the application. Held at 6,000 calls a minute and with the key otherwise idle, 100,000 machines are about 200,000 calls and about 35 minutes, and 500,000 machines are about a million calls and just under three hours. Storyteller retries HTTP 429 up to three times. Any other failure of a POST is left as it is, because repeating a POST can create a second application or a second secret. There is no provisioning queue and no idempotency key. A caller that retries a create which already succeeded gets a second application.

Reset lists the secrets, mints a new one, and revokes the previous ones. At five secrets the oldest is revoked first so the create has room. Two overlapping resets of the same application can revoke the secret the other call has just minted. Delete removes the WorkOS application and then the Cosmos document.

Machines call the token endpoint with their own client id and secret, so those calls do not spend the management key's 6,000 requests. The client-credentials example in the WorkOS token reference expires 300 seconds after it is issued. Storyteller does not choose that lifetime. If tokens in the environment match the example, 100,000 machines refreshing as each token expires send about 330 token requests a second to the AuthKit domain, and 500,000 send about 1,700, before any of them calls Storyteller. Read `expires_in` on a token from the environment before planning that load. Storyteller keeps no revocation list. A token that has already been issued stays valid until `exp`, and the Cosmos membership read still accepts it while the document exists.

`PermissionMap` is copied onto the application at create. A later edit of the map leaves existing applications as they were. Every requested scope needs its own permission, or the create fails before WorkOS is called, so the stored `Scopes` and the permissions of the application are the same set. `AnnotationKey` is stored on the document and is absent from the token; the [annotation restriction](#annotation-restriction-of-machines) is read from the document.

Every application shares one WorkOS organization and one audience, `Auth:AuthKit:ClientId`. `org_id` is that organization for every Storyteller organization and project. The project in the request URL is what the Cosmos read checks, so a token minted for one project fails in another project where that client id has no document. An application created in the WorkOS dashboard, rather than through Storyteller, receives a token that passes the signature checks and then fails the Cosmos read.

When the Cosmos write after a successful create fails, Storyteller asks WorkOS to delete the application. When that delete fails too, the application remains. It can still obtain tokens, and those tokens fail the Cosmos read. Nothing in Storyteller lists WorkOS applications and removes the ones without a document. Those leftovers stay in the one organization.

### Limits shared by every kind

`GET v1/{organization}/{project}/access/machines` runs `SELECT *` on `{project}.access` and returns the whole list in one response. `sform machine` does the same. A project that holds a large share of a few hundred thousand machines exhausts the function memory and the response. The query has no page size and no continuation token.

`GET v1/{organization}/{project}/access/machines/{id}` point-reads `{project}.access`, the same partition reset and delete use.

`MachineIds` accepts a GUID, a WorkOS `client_…` id, or a Keycloak client id `42.sform.{organization}.{project}.{32 hex}`. An Entra appId and a federated machine id are GUIDs, so they pass. Organization and project in the Keycloak id are one segment each (`A-Za-z0-9_-`).

### The other kinds at the same size

`ApiKey` creates no identity-provider object and uses no token endpoint. Create generates a secret, stores its hash, and upserts one Cosmos document. Each call parses the key and compares the hash. The secret stays valid until reset, so it travels on every call, and a leaked key works until then. This is the kind that can reach a few hundred thousand machines on Cosmos throughput alone. The list and the get-by-id behaviour above still apply, and one busy project is still one hot partition.

`Certificate` issues a PKCS#12 from Storyteller's CA and stores the thumbprint, the lifetime and the expiry on `MachineAccessEntity`. Renewal reads that entity. Authentication reads `IClientCertificateStore`. `AddCertificateMachineAccess` registers `InMemoryClientCertificateStore` as a fallback, and `AddCosmosDbAnnotations` replaces it with `CosmosClientCertificateStore`: one document per certificate in the organization container, partition `{project}.access`, id `mcert.{thumbprint}`, with a `ttl` that removes it 30 days after the certificate expires. A certificate issued on one Functions instance is therefore recognized on every other one and after a restart. Lookups are cached in the process, hits for 5 minutes and misses for 30 seconds, so a revoked certificate is refused everywhere within 5 minutes. A few hundred thousand certificates are a few hundred thousand small documents, which Cosmos holds without trouble; the hot partition is still one busy project. Chain building runs on every call. Shared certificates use a separate Cosmos store (`CosmosSharedCertificateStore`). Validating one is a point read of its document (`scr.{thumbprint}` in the project partition): a certificate the project never issued is refused even when Storyteller's CA signed it, and so is a revoked one. Lookups are cached in the process, hits for 5 minutes and misses for 30 seconds; the admin list is cached per project for 5 minutes (30 seconds when empty). Issuing or revoking evicts both on the instance that made the change, so other instances see a revoked shared certificate within 5 minutes.

`CertificateAndApiKey` does both. Both halves are durable in Cosmos, so the pair works on every instance.

`Federated` creates nothing outside Storyteller. Create checks the rules against the project's trusted issuers (cached) and upserts one Cosmos document. A call costs what an API key call costs: a signature check in memory and the point read of the machine. The work moves to the exchange, once per Storyteller token: a point read of the access point in the core container, the cached issuer list, a point read of the machine, the issuer's keys (cached per issuer, fetched again after `MetadataRefreshMinutes`, one fetch shared by concurrent requests), the signature and rule checks, and one RSA signature. An exchange writes nothing. A workload that keeps its token until it expires exchanges about four times an hour with the default 15 minutes, so 100,000 such machines make about 110 exchanges a second and 500,000 about 560. Storyteller sends the platforms nothing but key fetches; workloads get their platform tokens themselves. The signing key is one Cosmos document, read every 10 minutes per instance, and at most once per 30 seconds more for tokens with an unknown `kid`.

The thumbprint of a validated certificate is kept in the invocation (`PresentingCertificateThumbprint`), and the [key/value secret delivery](key-values.md#secret-delivery) uses it: a project whose policy has `RequireMtlsForSecretDelivery` (the default) hands secret values to a machine only when it authenticated with a certificate, whatever scopes its token or API key carries. A federated machine never presents one.

Entra ID and Keycloak `ClientCredentials` follow the same Storyteller shape as AuthKit: one external object per machine, a JWT checked locally, and the Cosmos document as the allow list. Entra provisioning is Microsoft Graph (application, service principal, and one app-role assignment per scope, so up to 11 assignments per machine) in one tenant, so the tenant's directory-object quota and Graph throttling are the figures to confirm before a large rollout. Keycloak provisioning is the admin API of one realm; how many clients that realm can hold is a property of that Keycloak deployment.

## 401 and 403

The two status codes mean different things, so a client knows whether signing in again can help:

| Status | Meaning | Body |
| --- | --- | --- |
| `401` | The credentials are missing, invalid or expired, or a user token does not carry `User.Impersonation` (Debug builds skip this check). Machine credentials that fail their checks also get 401: a machine the project does not know or whose kind it does not enable, an API key or a certificate whose record has no scopes, and a Storyteller token whose machine is gone or no longer `Federated`, or whose project is not the route's. | none |
| `403` | The user is authenticated, but their account role on the organization or project is too low for the operation. Signing in again does not help. | `ErrorResponse` with `ErrorCode` `AccessDenied` |
| `403` | A machine called a route for users only, in any build and whatever its credentials carry: "Only users may call this operation; machines cannot.", or the route's own message (see [Scopes of machines](#scopes-of-machines)). | `ErrorResponse` with `ErrorCode` `AccessDenied` |
| `403` | A machine restricted to an annotation addressed another annotation, or an operation of the whole project (see [Annotation restriction of machines](#annotation-restriction-of-machines)). | `ErrorResponse` with `ErrorCode` `AnnotationRestricted` |
| `403` | A machine called an operation without one of its scopes: the machine was not created with it ("This operation needs the scope Schema.Write, which the machine was not created with."), or its credentials do not carry it ("…, which the credentials of the machine do not carry."). See [Scopes of machines](#scopes-of-machines). | `ErrorResponse` with `ErrorCode` `AccessDenied` |

**Scopes describe machines, roles describe people.** A user role check runs on every project endpoint (`CheckAccessToProjectAsync`, `CheckAccessToAnnotationAsync`, `CheckAccessToAnnotationsAsync` and `CheckAccessToProjectMetadataAsync`) and inside the access-management operations. Users need no data scope there, only `User.Impersonation`. Reads need `Reader`. Writes to annotations, configurations, templates, schemas and machines need `Contributor`, and so do reading the machine authentication policy, reading trusted issuers and replacing federation rules. Secret values and secret writes need `ContributorWithSecrets`, and so do creating a machine with a secrets scope, resetting one, replacing its federation rules and deleting it. Shared certificates, access points, setting the machine authentication policy and writing trusted issuers need `Administrator`, and so do creating a machine with a management scope, resetting one, replacing its federation rules and deleting it, and the export and the import of a view, with the reads of their jobs ([export-import.md](export-import.md#access)). Granting or revoking `Owner` needs `Owner`.

Machines have no role. Each operation a machine may call needs its [scopes](#scopes-of-machines): a read its read scope, a write its write scope, and a bulk or structural operation also its management scope. That is `Configuration.Transfer` for the export and import of a view (with the read scopes of the included kinds, or the scopes of their direct writes) or of key/values, `Schema.Write` for schema writes and the import of schemas, `Template.Write` for template writes and the import of templates, `Annotation.Manage` for the bulk create, soft delete and restore of annotations and their import, and `KeyValue.Manage` for the protect and bulk restore of key/values. Debug builds (`DEV_AUTH`) skip the `User.Impersonation` check and the project role check of users. The checks of machines (their document, the credential policy, the annotation restriction and the scopes) and the refusal of machines on the routes for users only run in every build.

Other access-management errors use the same `ErrorResponse` shape, without exception details:

| Status | `ErrorCode` | When |
| --- | --- | --- |
| `400` | none | Grant or revoke with a missing account, a missing access point key, or the role `None`. |
| `400` | none | An empty body or `null` where an account, an access point, a machine, a permission, a machine authentication policy or a shared certificate is required: `The account is missing.`, `The permission is missing.` and so on. |
| `400` | none | An account, access point, project update, machine, permission, machine authentication policy or shared certificate body that does not fit its type: invalid JSON, another shape, or a required member left out. The message names the expected body and the JSON path, for example `The request body is not a shared certificate (at $).` |
| `400` | none | A machine of a kind the project does not enable or the deployment does not configure, a scope that does not exist, a list of scopes without a runtime scope, a scope that the identity provider cannot carry (no AuthKit permission, no Entra ID app role, or no service principal of the API application in the Entra ID machine tenant), or a reset of a `Federated` machine. |
| `400` | `InvalidFederation` | A trusted issuer or a federation rule fails [validation](#federation-rules), a `PUT` changes the `Issuer` of a trusted issuer, or the body of a federation `PUT` is not a trusted issuer or a list of federation rules. |
| `400` | `MachineNotFederated` | Federation rules for a machine of another kind. |
| `404` | `NotFound` | The target account, the access point or the machine access does not exist. `GET` and `DELETE` of a machine or a trusted issuer that does not exist answer `404` without a body. |
| `409` | `AccountExists`, `AccessPointExists` | The account or project already exists. |
| `409` | `FederatedIssuerInUse` | Creating a trusted issuer whose name rules of machines still reference. |
| `409` | `ElevatedRole` | Revoke names a lower role than the member holds. Revoke the stored role instead. |
| `409` | `LastOwner` | The revoke would leave the access point without an `Owner`. |
| `413` | `BodyTooLarge` | A body larger than its route reads: 8 KiB for an account, an access point, a project update, a permission, a machine authentication policy or a shared certificate, 1 MiB for a trusted issuer, and 4 MiB for a machine or its federation rules. |

Grant only raises a role. To lower one, revoke the current role and grant the new one, or set the exact role through the members endpoint. Members, invitations and the description of a project, with their own error codes, are described in [Access points](access.md).

The token endpoint of federated machines is anonymous and answers OAuth errors (`400` and `503`) instead of `ErrorResponse`; see [The token endpoint](#the-token-endpoint).

## CORS for browser clients

Browser clients, such as the admin UI, call the API from another origin. The deployed Function App's CORS settings must allow the origin of each such client (for example `https://admin.42for.net`). The clients send bearer tokens in the `Authorization` header and no cookies, so `Access-Control-Allow-Credentials` stays off and no cookie settings are needed. The anonymous `GET v1/auth/configuration` must be reachable from the same origins, because clients read it before they sign in. Local development keeps `"CORS": "*"` in `local.settings.json`.

```bash
az functionapp cors add --name <function-app> --resource-group <group> --allowed-origins https://admin.42for.net
```

## Switching an existing deployment

`Account.Id` is the provider's `sub`. Entra object IDs and WorkOS user IDs are unrelated, so **changing `Auth:Provider` on a deployment that already has accounts orphans them**. Access maps, ownership and the [audit actors](audit-stamps.md#the-actor) (`Account` with the `sub` as its id) on annotations, configurations and key/value pairs stop resolving. AuthKit can federate to Microsoft sign-in, but the `sub` still changes. A migration tool is not available yet.

## Not available yet

* User tokens issued by the AuthKit domain (Swagger UI sign-in) are not accepted alongside device-flow tokens. M2M tokens from the AuthKit domain are. See [OpenAPI and Swagger UI](#openapi-and-swagger-ui).
* A combination of an mTLS certificate and client credentials (`CertificateAndClientCredentials`) is not available.
* The scopes of an existing machine cannot be edited. Create a new machine and delete the old one (see [Scopes of machines](#scopes-of-machines)).
* The Storyteller signing key cannot be rotated except by the [emergency procedure](#security-notes), and it cannot live in Key Vault. Storyteller publishes no JWKS or RFC 8414 metadata for its tokens.
* The token endpoint has no replay protection and no rate limit. Rules know only exact values and a separator-anchored subject prefix; there are no glob, regex or CEL conditions.
* 401 responses carry no `WWW-Authenticate` header.
