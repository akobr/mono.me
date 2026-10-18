# User Authentication

Storyteller accepts user bearer tokens from one identity provider per deployment: **Microsoft Entra ID** or **WorkOS AuthKit**. The provider only proves who the caller is and which coarse scopes they hold. Access to organizations and projects is still decided by Storyteller's own account roles in Cosmos.

Machine credentials (structured `2s.` API keys and mTLS certificates) do not depend on the user provider. They work the same under either one. Which kinds a project's machines may use is set by its [machine authentication policy](#machine-authentication-policy).

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
| `Auth:AppRoles:*` | no | App role IDs used by Entra machine access. |

### AuthKit settings

| Key | Required | Default | Notes |
|---|---|---|---|
| `Auth:AuthKit:ClientId` | yes | | WorkOS client ID (`client_…`). |
| `Auth:AuthKit:Issuer` | yes | `https://api.workos.com/` | Must match the token's `iss`. Use your custom auth domain if one is configured. |
| `Auth:AuthKit:JwksUri` | no | `{ApiBaseUrl}/sso/jwks/{ClientId}` | Must be HTTPS. |
| `Auth:AuthKit:ApiBaseUrl` | no | `https://api.workos.com` | Used to build the default JWKS URL. Must be HTTPS. |
| `Auth:AuthKit:Audience` | no, but set it in production | | When set, tokens must carry this `aud`. When empty, the audience is not checked. |
| `Auth:AuthKit:DefaultUserScopes:*` | no | none | Scopes given to every signed-in user. |
| `Auth:AuthKit:PermissionMap:<slug>` | no | none | Maps a WorkOS permission slug to one or more Storyteller scopes. |
| `Auth:AuthKit:ApiKey` | no | | WorkOS management key (`sk_…`). Used to look up a user's email and name at account registration when the token lacks them, and to send invitation emails. Keep it in Key Vault, never in plain settings. |
| `Auth:AuthKit:RequireVerifiedEmail` | no | `true` | Accepting an invitation needs a verified email. When the token has no `email_verified` claim, the email counts as verified only if this is `false`. |
| `Auth:AuthKit:AuthKitDomain` | no | | `https://<subdomain>.authkit.app` or your custom domain. Enables the AuthKit OAuth flows in the OpenAPI document and is returned by the discovery endpoint. Together with `MachineOrganizationId` it turns on M2M machine access. |
| `Auth:AuthKit:MachineOrganizationId` | no | | WorkOS organization (`org_…`) that owns the M2M applications Storyteller creates. Setting it (with `AuthKitDomain`) turns on M2M machine access, which then also requires `ApiKey`. |

Example `local.settings.json` values:

```json
"Auth:Provider": "AuthKit",
"Auth:AuthKit:ClientId": "client_01H...",
"Auth:AuthKit:Audience": "https://storyteller.42for.net",
"Auth:AuthKit:DefaultUserScopes:0": "User.Impersonation",
"Auth:AuthKit:DefaultUserScopes:1": "Default.ReadWrite",
"Auth:AuthKit:PermissionMap:storyteller:configuration-secrets": "Configuration.Secrets"
```

## How AuthKit tokens are checked

`BearerAuthenticationMiddleware` runs after the machine-credential middleware and hands `Authorization: Bearer` tokens to `AuthKitBearerTokenValidator`:

* The signature must be **RS256**, using a key from the AuthKit JWKS. The key set is cached and refreshed. A token signed with an unknown key ID returns 401 and triggers a refresh of the key set, so a rotated key is picked up.
* `iss` must equal `Auth:AuthKit:Issuer`. The comparison is case-sensitive, and only a trailing `/` may differ.
* `aud` is checked only when `Auth:AuthKit:Audience` is set.
* `exp` and `nbf` are enforced with a 30-second clock skew.
* `sub` must be a WorkOS user ID (`user_…`). Other subjects are rejected.

An invalid token gets **401**. If the JWKS cannot be fetched, the response is **503** with `Retry-After: 5`, so clients keep their token and retry.

Debug builds of the Functions app (`DEV_AUTH`) decode bearer tokens without checking them. They still run the configured provider's claim normalization.

## Claims seen by the API

AuthKit claims are mapped to the Entra-style claims the endpoints already read.

| Claim | Source in the AuthKit token |
|---|---|
| `sub` | `sub` (`user_…`). This is the Storyteller account ID. |
| `name` | `name`, else `first_name` + `last_name`, else `given_name` + `family_name`, else `email`. Blank values are skipped. |
| `preferred_username` | `preferred_username`, else `email`. |
| `scp` | `DefaultUserScopes` plus each `permissions` entry mapped through `PermissionMap`, as one space-separated value. |
| `org_id`, `sid`, `permissions`, `role`, … | Passed through unchanged. |

Any `scp`, `roles` or `azp` claim in the token itself is dropped, so a JWT template cannot grant scopes directly. Permissions without a `PermissionMap` entry grant nothing, and slug lookup is case-sensitive.

Account registration (`POST v1/access/account`) needs `preferred_username` and `name`. Token claims are used first. When either is missing and `Auth:AuthKit:ApiKey` is set, Storyteller reads the user from `GET /user_management/users/{sub}` and fills only the missing values: `email` becomes the user name, and `first_name last_name` (else `email`) becomes the name. The lookup happens only at registration, and a WorkOS error fails the request with 500. Without the key, the token must carry at least `email`; see the JWT template below. Entra ID has no lookup and still needs both claims in the token.

## Scopes for AuthKit users

Entra users get their scopes as delegated scopes requested by the CLI. AuthKit has no per-API delegated scopes, so pick one of these modes:

* **Default scopes (recommended).** Give every user the coarse scopes and let account roles decide project access, as with Entra:

  ```json
  "Auth:AuthKit:DefaultUserScopes:0": "User.Impersonation",
  "Auth:AuthKit:DefaultUserScopes:1": "Default.Read",
  "Auth:AuthKit:DefaultUserScopes:2": "Default.ReadWrite",
  "Auth:AuthKit:DefaultUserScopes:3": "Annotation.Read",
  "Auth:AuthKit:DefaultUserScopes:4": "Annotation.ReadWrite",
  "Auth:AuthKit:DefaultUserScopes:5": "Configuration.Read",
  "Auth:AuthKit:DefaultUserScopes:6": "Configuration.ReadWrite"
  ```

  Leave `Configuration.Secrets` out, and grant it through a mapped WorkOS permission such as `storyteller:configuration-secrets`.

* **Role-driven.** Leave `DefaultUserScopes` empty, define WorkOS permissions on organization roles, and map each one. Users need an organization membership with a role, or `permissions` is empty. Account registration and the other `v1/access` endpoints need `User.Impersonation`, so map a permission to it too.

A `PermissionMap` value may list several scopes separated by spaces, for example `"Default.ReadWrite Configuration.Secrets"`. Slugs may contain `:`. In JSON, write the slug as an object key under `PermissionMap`. In flat keys and environment variables, append it to the path (`Auth__AuthKit__PermissionMap__storyteller__configuration-secrets`). Linux app settings restrict which characters a name may contain, so check that your slugs can be written as setting names on your hosting plan.

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
4. If you use role-driven scopes, create the permissions (for example `storyteller:annotation-read`) and assign them to organization roles.
5. Enable **CLI Auth** for the environment so `sform` can use the device sign-in. Without it, `POST /user_management/authorize/device` is rejected and `sform account` reports the WorkOS message.
6. For invitation emails, set `Auth:AuthKit:ApiKey` (the management key) and point the environment's invitation link at the admin UI's accept page, for example `https://admin.42for.net/invitations/accept`. Without the key, invitations are still created, but no email is sent.

## Discovery endpoint

`GET v1/auth/configuration` is anonymous and tells clients such as `sform` how to sign users in. It returns public values only. The management key, permission map and machine settings are never included. Property names follow the API's PascalCase convention.

```json
{ "Provider": "EntraId", "ClientId": "303a7632-…", "TenantId": "common",
  "Scopes": [ "api://303a7632-…/User.Impersonation", "api://303a7632-…/Default.ReadWrite" ] }

{ "Provider": "AuthKit", "ClientId": "client_01H…", "AuthKitDomain": "https://example.authkit.app" }
```

`AuthKitDomain` is left out when it is not configured. Send the request without an `Authorization` header: an invalid bearer token is rejected with 401 before any endpoint runs.

## OpenAPI and Swagger UI

The `integrated` OAuth2 security scheme follows `Auth:Provider`:

* **Entra ID:** the implicit and client-credentials flows against `login.microsoftonline.com`, as before.
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
4. If the server has no discovery endpoint (older deployments), Entra ID with the configured `tenantId` and `clientId`.

```jsonc
"authentication": {
  "provider": "AuthKit",                         // EntraId | AuthKit; leave out to discover
  "clientId": "client_01H…",                     // Entra application ID or AuthKit client ID
  "tenantId": "common",                          // Entra ID only
  "authKitApiBaseUrl": "https://api.workos.com"  // AuthKit only, optional
}
```

One `sform` build therefore follows whichever provider the server at `general.baseUrl` uses. Changing `general.baseUrl` discovers again. `sform account logout` signs out and also deletes `auth.discovery.json`, so the next sign-in asks the server again after a provider switch.

### AuthKit sign-in

* `sform` calls `POST /user_management/authorize/device`, shows `verification_uri` and the user code (plus the address with the code filled in), and polls `POST /user_management/authenticate` at the interval WorkOS asks for. `slow_down` adds a second to the interval. `access_denied`, `expired_token` or the end of the code lifetime stop the sign-in with a message.
* If the user belongs to several WorkOS organizations, `sform` lists them and continues with the chosen one.
* The session (access token, refresh token, user, organization) is stored in `~/.42for.net/authkit.cache`, encrypted with DPAPI on Windows, the Keychain on macOS and libsecret on Linux.
* An access token is reused until 60 seconds before it expires. Then the refresh token is exchanged and the stored one replaced, because WorkOS rotates refresh tokens. A file lock (`authkit.cache.lockfile`) keeps two `sform` processes from using the same refresh token. If WorkOS rejects the refresh token, the session is deleted and `sform account` asks for a new sign-in.

Entra ID sign-in still uses MSAL with the cache in `~/.42for.net/msal.cache`.

### Machine sign-in for pipelines

`sform` signs in without a device code when machine credentials are in environment variables, for example in a CI/CD pipeline or a GitHub Action. Setting any credential variable switches `sform` to that machine:

* It never asks anyone to sign in: no device code, no browser.
* It never uses or changes the stored user session.
* It stores no credential and no token. Only the AuthKit token URL fallback below caches the discovery answer in `auth.discovery.json`, as a user sign-in does.

Commands keep their own confirmations, for example before overwriting an `-e` export file or deleting without `--yes`; a pipeline should not depend on them.

`sform account machine create` prints the variables for the new machine. The names match the `.env` snippet of the admin UI.

| Variable | Meaning |
|---|---|
| `STORYTELLER_API_URL` | API base URL including `/api`. Required for a machine, and replaces `general.baseUrl`. It must be `https`; `http` is allowed only for `localhost`. A user sign-in ignores it, so a stored user session is never sent to another host. |
| `STORYTELLER_PROJECT` | Target `<organization>.<project>`, the same format as `-p`. |
| `STORYTELLER_VIEW` | Target view. |
| `STORYTELLER_API_KEY` | The structured `2s.` API key, sent as `Authorization: ApiKey`. A leading `ApiKey ` is accepted. |
| `STORYTELLER_CERTIFICATE_FILE` | The machine's PKCS#12 file (`.pfx`) that `machine create` wrote. |
| `STORYTELLER_CERTIFICATE_PASSWORD` | Its password. |
| `STORYTELLER_CLIENT_ID` | Client ID of a `ClientCredentials` machine. |
| `STORYTELLER_CLIENT_SECRET` | Its client secret. |
| `STORYTELLER_TOKEN_URL` | The token URL `machine create` printed. |
| `STORYTELLER_TOKEN_SCOPE` | The token scope `machine create` printed; Entra ID needs it. |
| `STORYTELLER_TOKEN_FILE` | A file holding a bearer token, re-read before every request. |

The credential variables decide the kind. Variables of two kinds at once, for example an API key and a token file, stop `sform` before any call.

| Kind | Variables |
|---|---|
| `ApiKey` | `STORYTELLER_API_KEY` |
| `Certificate` | `STORYTELLER_CERTIFICATE_FILE` and, when the file has one, `STORYTELLER_CERTIFICATE_PASSWORD` |
| `CertificateAndApiKey` | all three |
| `ClientCredentials` | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, and usually `STORYTELLER_TOKEN_URL` and `STORYTELLER_TOKEN_SCOPE` |
| token file | `STORYTELLER_TOKEN_FILE` |

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

**Client credentials.**

* `sform` posts `grant_type=client_credentials`, `client_id`, `client_secret` and, when it is set, `scope` to the token URL. It does not follow redirects.
* The token is kept in memory for the process and renewed 60 seconds before it expires.
* Without `STORYTELLER_TOKEN_URL`, only an AuthKit client (`client_…`) uses `{AuthKitDomain}/oauth2/token` from discovery, and only over `https`. This keeps a Keycloak or Entra ID secret from going to WorkOS. Every other client needs the variable.

**Token file.** The file is read before every request, so a token that another process rotates is picked up, for example a Kubernetes projected token. The content is trimmed, and a leading `Bearer ` is removed. Any bearer token the API accepts works.

**Target project.** The first of these that is set wins:

1. `-p` on the command;
2. `STORYTELLER_PROJECT`;
3. the project of the API key or the certificate.

`access.default.json` is never used for a machine, because it outlives the job on a self-hosted runner. A command without a project stops. So does a command whose target differs from the project of the key or the certificate. The view comes from `-v`, then `STORYTELLER_VIEW`, then `default`. `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` also apply to a user sign-in, ahead of `access.default.json`.

`sform account` checks the variables without a prompt. For client credentials it also gets the token, and for a token file it refuses a JWT whose `exp` has passed. It then prints the machine, the project and the API URL. `sform account logout` has nothing to do for a machine. Account, access point, machine and shared certificate management need a user sign-in; the API answers `401` to a machine.

| Exit code | Meaning |
|---|---|
| `0` | Signed in. |
| `2` | The variables are missing or invalid, or the target project is wrong. |
| `3` | The token endpoint failed or could not be reached; a retry may help. |
| `43` | `sform account` only: no project is set. |
| `44` | The credentials were rejected: by the token endpoint, as an expired token file, or by the API with `401`. A `403` the command does not handle itself ends the same way. |

```yaml
# GitHub Actions
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform account
  env:
    STORYTELLER_API_URL: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_API_URL: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

## Machine authentication policy

A project enables one or more **machine credential kinds**, and each machine uses one of them. Machines of different kinds can work side by side in one project, for example an API-key service next to an mTLS one.

| Kind | The machine sends |
|---|---|
| `ApiKey` | `Authorization: ApiKey` with its structured `2s.` key. |
| `Certificate` | Its client certificate. |
| `CertificateAndApiKey` | A certificate (its own, or a shared certificate of the project) and its API key. |
| `ClientCredentials` | `Authorization: Bearer` with a token of the identity provider, see [Machine access with client credentials](#machine-access-with-client-credentials). |

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

Without a stored policy a project enables only `MachineAuth:DefaultCredentialKind` (`ApiKey`). A policy stored before projects could enable several kinds has only `CredentialKind`; it enables that one kind.

`GET v1/access/points/{organization}.{project}/machine-authentication` returns the policy with every property filled, so a client does not have to know the deployment defaults. It needs `Contributor`, because Contributors create machines. `PUT` on the same route sets it, needs `Administrator`, and returns the filled policy too. The body may leave out `CredentialKind` (the first listed kind becomes the default) and `CertificateLifetimeDays` (the project then follows the deployment default). A body with only `CredentialKind` enables that one kind, as older clients send it. `PUT` returns `400` when:

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
sform machine machine-auth -k api-key -k certificate -d certificate -l 90
sform machine machine-auth                 # prints the policy
sform machine create -k api-key            # without -k: the project default
sform machine create -s                    # CertificateAndApiKey with a shared certificate, no certificate of its own
```

**Creating a machine.** `MachineAccessCreate.CredentialKind` picks the kind; without it the project default is used. A kind the project does not enable fails with `400`. `MachineAccessCreate.UseSharedCertificate` creates a `CertificateAndApiKey` machine with no certificate of its own: it gets only its API key, `UsesSharedCertificate` is set, and it signs in with one of the project's shared certificates and that key. Any other kind with `UseSharedCertificate` fails with `400`. A machine with a certificate of its own gets `MachineAccessCreate.CertificateLifetimeDays`, or else the project's default lifetime, clamped to the deployment's minimum and maximum. The machine keeps its kind and its lifetime: reset and delete follow the kind, and a certificate renewal issues the new certificate with the same lifetime. Machines created before the lifetime was stored renew with `MachineAuth:CertificateLifetimeDays`.

**Every machine call** is accepted only when the machine presents what its own kind requires, and the project still enables that kind. A `CertificateAndApiKey` machine that sends only its API key is rejected, even if the project also enables `ApiKey`. Likewise one that sends only its certificate is rejected, even if the project also enables `Certificate`: a machine certificate carries its machine's kind (`mcert.` documents stored before the kind was kept take it from the machine access document), and a machine certificate whose kind is unknown is refused. Disabling a kind stops every machine of that kind at once (up to the 60-second cache of the policy); enable it again and they work again. To move a machine to another kind, create a new machine and delete the old one.

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

A token that fails any of these gets 401. API keys and certificates are accepted only for machines of the kinds the project enables, see [Machine authentication policy](#machine-authentication-policy). Scopes come only from what Storyteller put into the client; `roles` or `scp` claims from elsewhere in the provider are dropped.

### The `MachineAccess` object

Create, get, list and reset return a `MachineAccess`. Property names are PascalCase. `Scope` and `CredentialKind` are the enum names as strings. A null property is left out of the JSON.

Create returns the secret once. Get and list read the stored record: the secret is masked, and the one-time fields are absent. Reset returns that stored record with the new secret in `AccessKey`.

| Property | Type | Description |
|---|---|---|
| `Id` | string | Machine id, and the `sub` / `azp` the API expects from that machine. An API key or a certificate uses a GUID. Client credentials use the identity provider's client id: the WorkOS application client id, the Entra appId, or the Keycloak client id `42.sform.{organization}.{project}.{guid}`. |
| `ObjectId` | string | Id used to reset and delete the credential at its source. Same value as `Id` for an API key or a certificate. AuthKit uses the WorkOS application id, Entra ID the application object id, and Keycloak the client's internal id. |
| `AccessKey` | string | Secret. Create and reset return it in full: the structured `2s.` API key, or the client secret for client credentials. A certificate-only machine returns an empty string. Stored copies keep the first three characters and then `***`, or `***` when the value is shorter. |
| `Scope` | `MachineAccessScope` | What the machine may call. `AnnotationRead` grants `Annotation.Read`. `AnnotationReadWrite` grants `Annotation.Read` and `Annotation.ReadWrite`. `ConfigurationRead` grants `Configuration.Read`. `ConfigurationReadWrite` grants `Configuration.Read` and `Configuration.ReadWrite`. `DefaultRead` grants `Default.Read`, `Annotation.Read` and `Configuration.Read`. `DefaultReadWrite` grants those three plus `Default.ReadWrite`, `Annotation.ReadWrite` and `Configuration.ReadWrite`. `SecretsRead` grants `Configuration.Read` and `Configuration.Secrets`, `SecretsReadWrite` adds `Configuration.ReadWrite`; such a machine receives [key/value secrets](key-values.md#secret-delivery) under the project's mTLS rule. Entra ID stores `DefaultReadWrite` for any `…ReadWrite` request and `DefaultRead` otherwise, and keeps the two secrets scopes as they are. |
| `AnnotationKey` | string | Optional [annotation restriction](#annotation-restriction-of-machines): the machine reaches this annotation and its descendants only. Create stores it in canonical form (type code in lower case, empty segments dropped), turns a blank value into null, and answers `400` `InvalidKey` for a key that does not parse. Null means the whole project. |
| `CredentialKind` | `MachineCredentialKind` | `ApiKey`, `Certificate`, `CertificateAndApiKey` or `ClientCredentials`. Chosen at creation from the kinds the project enables (the project default when the request names none) and kept when the policy later changes. |
| `CertificateThumbprint` | string | Thumbprint of the machine's current certificate. Present for `Certificate` and `CertificateAndApiKey`, except a machine that uses a shared certificate, and updated when the certificate is renewed. |
| `Certificate` | string | Base64 PKCS#12 of a newly issued machine certificate. Returned on create. Cosmos keeps no copy, so get, list and reset omit it. |
| `CertificatePassword` | string | Password for that PKCS#12. Returned on create. Cosmos keeps no copy, so get, list and reset omit it. |
| `LastRenewalAt` | date-time | UTC time of the last certificate renewal. Present after the first renewal. |
| `CertificateLifetimeDays` | integer | Lifetime of the machine's certificates in days, chosen at creation. Renewals keep it. Present for `Certificate` and `CertificateAndApiKey` machines created since it is stored. |
| `CertificateNotAfter` | date-time | When the current certificate expires. Present for machines created since it is stored, and updated by renewal. |
| `UsesSharedCertificate` | boolean | `true` for a `CertificateAndApiKey` machine created with `UseSharedCertificate`: it has no certificate, thumbprint, lifetime or expiry of its own, and presents a shared certificate of the project with its API key. Reset renews the API key; there is nothing to renew for the certificate. |
| `TokenEndpoint` | string | Token URL for client credentials, returned on create. AuthKit uses `{AuthKitDomain}/oauth2/token`. Entra ID uses `https://login.microsoftonline.com/{MachineAuth:AzureAd:TenantId}/oauth2/v2.0/token`. Keycloak uses `{ServerUrl}/realms/{Realm}/protocol/openid-connect/token`. Cosmos keeps no copy, so get, list and reset omit it. |
| `TokenScope` | string | `scope` form field for the token request. Entra ID sets `api://{Auth:ClientId}/.default` on create. AuthKit and Keycloak leave it empty; their tokens carry the machine's scopes without this parameter. Cosmos keeps no copy, so get, list and reset omit it. |

### AuthKit M2M applications

1. In WorkOS, create an organization for the machines (for example `Storyteller machines`) and note its `org_…` ID.
2. Create WorkOS permissions for the machine scopes and map them in `PermissionMap`, for example:

   ```json
   "Auth:AuthKit:PermissionMap:storyteller:annotation-read": "Annotation.Read",
   "Auth:AuthKit:PermissionMap:storyteller:annotation-write": "Annotation.ReadWrite",
   "Auth:AuthKit:PermissionMap:storyteller:configuration-read": "Configuration.Read",
   "Auth:AuthKit:PermissionMap:storyteller:configuration-write": "Configuration.ReadWrite",
   "Auth:AuthKit:PermissionMap:storyteller:default-read": "Default.Read",
   "Auth:AuthKit:PermissionMap:storyteller:default-write": "Default.ReadWrite"
   ```

   A new M2M application gets every mapped permission whose scopes all fall within the machine's scope (`DefaultRead` gets the three read permissions, `DefaultReadWrite` all six). If no permission matches, creation fails with 400. A permission that also grants something outside the machine scope is never given to a machine: `Configuration.Secrets` reaches only machines created with `SecretsRead` or `SecretsReadWrite`, so map it from its own permission (for example `storyteller:configuration-secrets`).
3. Set `Auth:AuthKit:ClientId`, `Auth:AuthKit:AuthKitDomain`, `Auth:AuthKit:MachineOrganizationId` and `Auth:AuthKit:ApiKey` (from Key Vault). `Program.cs` registers AuthKit machine access when the domain and the organization are set. The host then refuses to start without the other two. AuthKit users are not required.

Tokens come from `{AuthKitDomain}/oauth2/token` and are checked as follows:

* `iss` is `AuthKitDomain`, and the keys come from `{AuthKitDomain}/oauth2/jwks`.
* `aud` is the environment client ID (`Auth:AuthKit:ClientId`). WorkOS does not let M2M applications choose it.
* `sub` equals `client_id`, and `org_id` equals `MachineOrganizationId`.
* The `scope` claim is mapped through `PermissionMap`. `DefaultUserScopes` never apply to machines.

Reset mints a new secret and then revokes the old ones (WorkOS allows five per application). `Examples/CustomClientApp` (`dotnet run -- authkit`) shows the exchange in C#.

### Entra ID app registrations

1. The Storyteller API app registration (`Auth:ClientId`) needs two application app roles. Their IDs go into `Auth:AppRoles:DefaultRead` and `Auth:AppRoles:DefaultReadWrite`. A role value is what the token's `roles` claim carries, so name the values after the API scopes; the API strips an `App.` prefix. A machine gets the read/write role for any `…ReadWrite` scope, and the read role otherwise. Two more roles are optional: `Auth:AppRoles:SecretsRead` and `Auth:AppRoles:SecretsReadWrite`, whose values carry `Configuration.Secrets` as well. Without them, creating a machine with the scope `SecretsRead` or `SecretsReadWrite` answers `400 ProtectionNotConfigured`.
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
* a hardcoded claim mapper adds `storyteller_scope` with the machine's Storyteller scopes.

The machine ID is the `clientId` (`42.sform.…`). Tokens come from `{ServerUrl}/realms/{Realm}/protocol/openid-connect/token` and are checked as follows:

* `iss` is `{ServerUrl}/realms/{Realm}`, and the keys come from the realm's OpenID metadata (HTTP is allowed only for a local server).
* `aud` contains `Audience`.
* `azp` is the machine.

Reset regenerates the client secret.

## Annotation restriction of machines

A machine created with an `AnnotationKey` K is restricted to K. It reaches K and the descendants of K, in every view of its project, with what its `Scope` allows. The rule is the one of the [inheritance graph](inheritance.md): a machine restricted to the execution `exe.northwind.invoicing.prod` reaches that execution and its units of execution `uxe.northwind.invoicing.prod.*`, and a machine restricted to the subject `sbt.northwind` reaches its contexts, usages, executions and units of execution.

| Operation | Restricted machine |
| --- | --- |
| Annotations (with their history and descendants), configurations (raw, resolved, versions, diffs, the diff to another view), annotation schemas, descendant-type schemas and the combined schema definition of K or a descendant | Allowed |
| The same for an ancestor, a sibling or another branch of K | `403` |
| Bulk create (`POST …/annotations`), lookup and restore of deleted annotations | Allowed when every key of the body is K or a descendant, otherwise `403` |
| Lists of annotations, deleted annotations, configurations and schemas, compact bulk create (`…/annotations/simple`), type-level schemas, templates, export and import of configurations, key/values, the key/value policy and recipients | `403`: the operation spans the whole project |
| The views of the project (`GET …/views`) and the binding catalog | Allowed: they hold no annotation data |

A refusal is `403` with `ErrorCode` `AnnotationRestricted`. It comes before any read, so it does not tell whether the annotation exists. A key that does not parse is outside of the restriction.

The ancestors of K, and the other parents of its descendants (the unit `unt.invoicing.export` of the unit of execution `uxe.northwind.invoicing.prod.export`, for example), still reach the machine through the effective and resolved configurations, and the resolved configuration is the only way a restricted machine gets key/values. Creating or restoring an annotation inside K also creates or restores its missing ancestors, as for every caller; the answers of a bulk create and of a restore leave the annotations outside K out.

The restriction is read from the machine's document in the same point read that checks its membership, so it holds for every credential kind and costs nothing extra. Tokens and certificates carry no annotation claim. A stored key that does not parse (machines created before the key was validated) restricts the machine to nothing. A restricted machine that has a configuration write scope can still bind any key/value of the view into the configuration of K; give machines that only read a read scope (`ConfigurationRead` or `SecretsRead`).

## Caveats and scale of machine access

A deployment has four machine credential kinds. `ClientCredentials` with AuthKit creates one WorkOS Connect M2M application per machine, and every one of those applications sits in the single organization `Auth:AuthKit:MachineOrganizationId`. `ApiKey`, `Certificate` and `CertificateAndApiKey` keep the credential inside Storyteller. The notes below are how that behaves at a few hundred thousand machines.

### What each call does

A machine call that names a project goes through one of the `CheckAccessTo*Async` checks of `HttpRequestDataExtensions` (the project, one annotation, the annotations of a bulk body, or the project metadata), which share one core. That point-reads the `MachineAccessEntity` whose id is the machine id, in partition `{project}.access`, in the organization container. A missing document (or a missing container) is rejected with `401`, and the document's `AnnotationKey` decides the [annotation restriction](#annotation-restriction-of-machines). Every machine call also reads the project policy (`IMachineAuthenticationPolicyStore`, cached for 60 seconds): an API key or certificate call checks that the machine's own kind is enabled, and a client-credentials call that `ClientCredentials` is enabled.

| Kind | Sent on each call | Work beside the membership read |
|---|---|---|
| `ApiKey` | `Authorization: ApiKey` and the structured `2s.` key | The key carries the organization, project and machine id. Storyteller point-reads the same entity and compares a SHA-256 hash. |
| `Certificate` | The client certificate | The chain is built against the configured CA. Revocation checking is off. The thumbprint is then checked in `IClientCertificateStore`. |
| `CertificateAndApiKey` | The certificate and the API key | Both checks have to succeed, and the organization, project and machine id have to match. With a shared certificate, the certificate has to be a stored, unrevoked shared certificate of the same organization and project, and the machine id comes from the API key. |
| `ClientCredentials` | `Authorization: Bearer` and a JWT | RS256 against the provider's JWKS, which is cached. AuthKit also requires `sub` equal to `client_id`, `aud` equal to `Auth:AuthKit:ClientId`, and `org_id` equal to `MachineOrganizationId`. Scopes come only from `PermissionMap`. |

The membership read stays a point read as the catalogue grows, as long as the machines are spread across projects. Every machine of one project shares the logical partition `{project}.access`. Cosmos serves a logical partition from one physical partition, and a physical partition is capped at 10,000 RU/s. A point read of this small document is about 1 RU, so one project cannot carry tens of thousands of machine calls per second.

### AuthKit at a few hundred thousand applications

Storyteller never lists WorkOS applications. Create, reset and delete address one application by id, and a live call is allowed by the Cosmos document. API traffic therefore does not grow with the size of the WorkOS catalogue. Provisioning and token issuance do.

WorkOS publishes a general management-API limit of 6,000 requests per 60 seconds per API key ([rate limits](https://workos.com/docs/reference/rate-limits)). `Auth:AuthKit:ApiKey` is that key. The same key also looks up users during account registration. Connect application routes have no separate published limit. WorkOS does not publish a maximum number of M2M applications, or of applications in one organization. It does say an environment can contain any number of organizations.

Create spends two management calls: the application, then the secret. A failed secret deletes the application. Held at 6,000 calls a minute and with the key otherwise idle, 100,000 machines are about 200,000 calls and about 35 minutes, and 500,000 machines are about a million calls and just under three hours. Storyteller retries HTTP 429 up to three times. Any other failure of a POST is left as it is, because repeating a POST can create a second application or a second secret. There is no provisioning queue and no idempotency key. A caller that retries a create which already succeeded gets a second application.

Reset lists the secrets, mints a new one, and revokes the previous ones. At five secrets the oldest is revoked first so the create has room. Two overlapping resets of the same application can revoke the secret the other call has just minted. Delete removes the WorkOS application and then the Cosmos document.

Machines call the token endpoint with their own client id and secret, so those calls do not spend the management key's 6,000 requests. The client-credentials example in the WorkOS token reference expires 300 seconds after it is issued. Storyteller does not choose that lifetime. If tokens in the environment match the example, 100,000 machines refreshing as each token expires send about 330 token requests a second to the AuthKit domain, and 500,000 send about 1,700, before any of them calls Storyteller. Read `expires_in` on a token from the environment before planning that load. Storyteller keeps no revocation list. A token that has already been issued stays valid until `exp`, and the Cosmos membership read still accepts it while the document exists.

`PermissionMap` is copied onto the application at create. A later edit of the map leaves existing applications as they were. A map that only partly covers the requested `Scope` still creates the application and writes a warning; the stored `Scope` then describes more access than the token carries. `AnnotationKey` is stored on the document and is absent from the token; the [annotation restriction](#annotation-restriction-of-machines) is read from the document.

Every application shares one WorkOS organization and one audience, `Auth:AuthKit:ClientId`. `org_id` is that organization for every Storyteller organization and project. The project in the request URL is what the Cosmos read checks, so a token minted for one project fails in another project where that client id has no document. An application created in the WorkOS dashboard, rather than through Storyteller, receives a token that passes the signature checks and then fails the Cosmos read.

When the Cosmos write after a successful create fails, Storyteller asks WorkOS to delete the application. When that delete fails too, the application remains. It can still obtain tokens, and those tokens fail the Cosmos read. Nothing in Storyteller lists WorkOS applications and removes the ones without a document. Those leftovers stay in the one organization.

### Limits shared by every kind

`GET v1/{organization}/{project}/access/machines` runs `SELECT *` on `{project}.access` and returns the whole list in one response. `sform machine` does the same. A project that holds a large share of a few hundred thousand machines exhausts the function memory and the response. The query has no page size and no continuation token.

`GET v1/{organization}/{project}/access/machines/{id}` point-reads `{project}.access`, the same partition reset and delete use.

`MachineIds` accepts a GUID, a WorkOS `client_…` id, or a Keycloak client id `42.sform.{organization}.{project}.{32 hex}`. An Entra appId is a GUID, so it passes. Organization and project in the Keycloak id are one segment each (`A-Za-z0-9_-`).

### The other kinds at the same size

`ApiKey` creates no identity-provider object and uses no token endpoint. Create generates a secret, stores its hash, and upserts one Cosmos document. Each call parses the key and compares the hash. The secret stays valid until reset, so it travels on every call, and a leaked key works until then. This is the kind that can reach a few hundred thousand machines on Cosmos throughput alone. The list and the get-by-id behaviour above still apply, and one busy project is still one hot partition.

`Certificate` issues a PKCS#12 from Storyteller's CA and stores the thumbprint, the lifetime and the expiry on `MachineAccessEntity`. Renewal reads that entity. Authentication reads `IClientCertificateStore`. `AddCertificateMachineAccess` registers `InMemoryClientCertificateStore` as a fallback, and `AddCosmosDbAnnotations` replaces it with `CosmosClientCertificateStore`: one document per certificate in the organization container, partition `{project}.access`, id `mcert.{thumbprint}`, with a `ttl` that removes it 30 days after the certificate expires. A certificate issued on one Functions instance is therefore recognized on every other one and after a restart. Lookups are cached in the process, hits for 5 minutes and misses for 30 seconds, so a revoked certificate is refused everywhere within 5 minutes. A few hundred thousand certificates are a few hundred thousand small documents, which Cosmos holds without trouble; the hot partition is still one busy project. Chain building runs on every call. Shared certificates use a separate Cosmos store (`CosmosSharedCertificateStore`). Validating one is a point read of its document (`scr.{thumbprint}` in the project partition): a certificate the project never issued is refused even when Storyteller's CA signed it, and so is a revoked one. Lookups are cached in the process, hits for 5 minutes and misses for 30 seconds; the admin list is cached per project for 5 minutes (30 seconds when empty). Issuing or revoking evicts both on the instance that made the change, so other instances see a revoked shared certificate within 5 minutes.

`CertificateAndApiKey` does both. Both halves are durable in Cosmos, so the pair works on every instance.

The thumbprint of a validated certificate is kept in the invocation (`PresentingCertificateThumbprint`), and the [key/value secret delivery](key-values.md#secret-delivery) uses it: a project whose policy has `RequireMtlsForSecretDelivery` (the default) hands secret values to a machine only when it authenticated with a certificate, whatever scopes its token or API key carries.

Entra ID and Keycloak `ClientCredentials` follow the same Storyteller shape as AuthKit: one external object per machine, a JWT checked locally, and the Cosmos document as the allow list. Entra provisioning is Microsoft Graph (application, service principal, and app-role assignment) in one tenant, so the tenant's directory-object quota and Graph throttling are the figures to confirm before a large rollout. Keycloak provisioning is the admin API of one realm; how many clients that realm can hold is a property of that Keycloak deployment.

## 401 and 403

The two status codes mean different things, so a client knows whether signing in again can help:

| Status | Meaning | Body |
| --- | --- | --- |
| `401` | The credentials are missing, invalid or expired, or the token lacks the required scope. Machine credentials that fail their checks also get 401. | none |
| `403` | The user is authenticated, but their account role on the organization or project is too low for the operation. Signing in again does not help. | `ErrorResponse` with `ErrorCode` `AccessDenied` |
| `403` | A machine restricted to an annotation addressed another annotation, or an operation of the whole project (see [Annotation restriction of machines](#annotation-restriction-of-machines)). | `ErrorResponse` with `ErrorCode` `AnnotationRestricted` |

A user role check runs on every project endpoint (`CheckAccessToProjectAsync`, `CheckAccessToAnnotationAsync`, `CheckAccessToAnnotationsAsync` and `CheckAccessToProjectMetadataAsync`) and inside the access-management operations. Reads need `Reader`. Writes to annotations, configurations, templates, schemas and machines need `Contributor`, and so does reading the machine authentication policy. Shared certificates, access points and setting the machine authentication policy need `Administrator`. Granting or revoking `Owner` needs `Owner`. Debug builds (`DEV_AUTH`) skip the project role check of users. The checks of machines (their document, the credential policy and the annotation restriction) run in every build.

Other access-management errors use the same `ErrorResponse` shape, without exception details:

| Status | `ErrorCode` | When |
| --- | --- | --- |
| `400` | none | Grant or revoke with a missing account, a missing access point key, or the role `None`. |
| `404` | `NotFound` | The target account, the access point, or the machine access does not exist. |
| `409` | `AccountExists`, `AccessPointExists` | The account or project already exists. |
| `409` | `ElevatedRole` | Revoke names a lower role than the member holds. Revoke the stored role instead. |
| `409` | `LastOwner` | The revoke would leave the access point without an `Owner`. |

Grant only raises a role. To lower one, revoke the current role and grant the new one, or set the exact role through the members endpoint. Members and invitations, with their own error codes, are described in [Members and invitations](access.md).

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
