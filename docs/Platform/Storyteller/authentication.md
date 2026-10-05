# User Authentication

Storyteller accepts user bearer tokens from one identity provider per deployment: **Microsoft Entra ID** or **WorkOS AuthKit**. The provider only proves who the caller is and which coarse scopes they hold. Access to organizations and projects is still decided by Storyteller's own account roles in Cosmos.

Machine credentials (structured `2s.` API keys and mTLS certificates) do not depend on the user provider. They work the same under either one.

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
| `Auth:AuthKit:ApiKey` | no | | WorkOS management key (`sk_…`). Used to look up a user's email and name at account registration when the token lacks them. Keep it in Key Vault, never in plain settings. |
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
     "name": "{{ user.first_name }} {{ user.last_name }}"
   }
   ```

   Set `Auth:AuthKit:Audience` to the same `aud`. When a user has no first or last name, the template renders a blank or partial `name`, and Storyteller falls back as described above.
4. If you use role-driven scopes, create the permissions (for example `storyteller:annotation-read`) and assign them to organization roles.
5. Enable **CLI Auth** for the environment so `sform` can use the device sign-in. Without it, `POST /user_management/authorize/device` is rejected and `sform account` reports the WorkOS message.

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

`machine create` prints the client ID (also the machine ID in Storyteller), the client secret (shown once), the token URL and, for Entra ID, the token scope, with a `curl` example. `sform machine reset <id>` replaces the secret and `sform machine delete <id>` removes the provider's client. Both follow the kind the machine was created with, even if the project policy changed since.

Every provider's tokens are checked for the same things:

* the provider's signature (RS256), issuer, audience and lifetime;
* that the project named by the request has the `ClientCredentials` policy;
* that the project owns the machine.

A token that fails any of these gets 401. Projects with the `ClientCredentials` policy in turn reject API keys and certificates. Scopes come only from what Storyteller put into the client; `roles` or `scp` claims from elsewhere in the provider are dropped.

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

   A new M2M application gets every mapped permission whose scopes all fall within the machine's scope (`DefaultRead` gets the three read permissions, `DefaultReadWrite` all six). If no permission matches, creation fails with 400. A permission that also grants something outside the machine scope, such as `Configuration.Secrets`, is never given to a machine.
3. Set `Auth:AuthKit:ClientId`, `Auth:AuthKit:AuthKitDomain`, `Auth:AuthKit:MachineOrganizationId` and `Auth:AuthKit:ApiKey` (from Key Vault). `Program.cs` registers AuthKit machine access when the domain and the organization are set. The host then refuses to start without the other two. AuthKit users are not required.

Tokens come from `{AuthKitDomain}/oauth2/token` and are checked as follows:

* `iss` is `AuthKitDomain`, and the keys come from `{AuthKitDomain}/oauth2/jwks`.
* `aud` is the environment client ID (`Auth:AuthKit:ClientId`). WorkOS does not let M2M applications choose it.
* `sub` equals `client_id`, and `org_id` equals `MachineOrganizationId`.
* The `scope` claim is mapped through `PermissionMap`. `DefaultUserScopes` never apply to machines.

Reset mints a new secret and then revokes the old ones (WorkOS allows five per application). `Examples/CustomClientApp` (`dotnet run -- authkit`) shows the exchange in C#.

### Entra ID app registrations

1. The Storyteller API app registration (`Auth:ClientId`) needs two application app roles. Their IDs go into `Auth:AppRoles:DefaultRead` and `Auth:AppRoles:DefaultReadWrite`. A role value is what the token's `roles` claim carries, so name the values after the API scopes; the API strips an `App.` prefix. A machine gets the read/write role for any `…ReadWrite` scope, and the read role otherwise.
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

## Switching an existing deployment

`Account.Id` is the provider's `sub`. Entra object IDs and WorkOS user IDs are unrelated, so **changing `Auth:Provider` on a deployment that already has accounts orphans them**. Access maps, ownership and the `account: {sub}` author stamped on annotations stop resolving. AuthKit can federate to Microsoft sign-in, but the `sub` still changes. A migration tool is not available yet.

## Not available yet

* User tokens issued by the AuthKit domain (Swagger UI sign-in) are not accepted alongside device-flow tokens. M2M tokens from the AuthKit domain are. See [OpenAPI and Swagger UI](#openapi-and-swagger-ui).
* A combination of an mTLS certificate and client credentials (`CertificateAndClientCredentials`) is not available.
