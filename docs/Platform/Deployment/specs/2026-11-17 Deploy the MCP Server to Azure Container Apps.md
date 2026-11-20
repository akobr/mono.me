# Deploy the MCP Server to Azure Container Apps

## Problem

The 2S Platform has an MCP server (`src/Platform/Storyteller/Mcp/src`, docs in [MCP Server](../../Storyteller/mcp.md)) that lets an AI assistant work with the platform as the signed-in person, but production does not run it. The [production spec](2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md) left it out of the first deployment (section 13: "The MCP server on Container Apps (consumption, scale to zero, image from GHCR) with `Auth:TokenExchange:Enabled=true` and its actor"), and the API runs with the user token exchange off (`Auth__TokenExchange__Enabled=false` in `modules/functions.bicep`).

Nothing builds a container image of the server, no template creates where it runs, the API does not trust it as an actor, and WorkOS production is not set up for MCP sign-ins. This spec adds all of it to the Bicep templates, scripts and workflows of `src/Platform/Deployment`, so the server runs at `https://mcp.2s.42for.net/mcp` at almost no cost.

**Assumption:** [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api) is done. WorkOS production has Multiple Applications enabled, the API accepts a **list** of 2S audiences for bearer tokens (named `Auth:AuthKit:Audiences` below; use the name 424-239 settled on), and a token of another 42for.net application is refused. The MCP server's tokens must stay outside that list (section 6).

## Current State

**The server** (`src/Platform/Storyteller/Mcp/src`):

- ASP.NET Core (`Microsoft.NET.Sdk.Web`, `net10.0`, `InvariantGlobalization`), Streamable HTTP, stateless; one endpoint `POST /mcp` plus the discovery documents (`/.well-known/oauth-protected-resource/mcp`, `/.well-known/jwks.json`). `Program.cs` calls `AddStorytellerMcp` / `UseStorytellerMcp` (`Hosting/McpHostExtensions.cs`).
- Settings (`appsettings.json`, validated at start by `Hosting/McpHostOptionsValidator.cs`): `Mcp:Provider`, `Mcp:ResourceUrl`, `Mcp:Authentication:*`, `Mcp:Actor:*` (exactly one credential), `Mcp:SecretTools:Enabled`, `Storyteller:BaseUrl`. Outside Development `ResourceUrl`, `Issuer` and `BaseUrl` must be HTTPS, and `Mcp:Actor:ClientSecret` is refused.
- Under AuthKit, people sign in at the **AuthKit domain** (`Mcp:Authentication:Issuer`), not through the WorkOS API: MCP clients register themselves dynamically there and request the MCP resource URL as `resource`, so the token's `iss` is the AuthKit domain and its `aud` the resource URL (mcp.md, "AuthKit").
- The actor's credentials: a managed identity (`Mcp:Actor:WorkloadIdentity`, `Kind` `ManagedIdentity`, `Authentication/McpActorCredential.cs` asks Azure.Identity for `{Audience}/.default`), a token file, a client assertion key, or a client secret (Development only).
- Health endpoints (`/health`, `/alive`) are mapped only in Development (`Aspire.ServiceDefaults/src/Extensions.cs:112`), and telemetry is exported only with `OTEL_EXPORTER_OTLP_ENDPOINT` (line 85); there is no Azure Monitor exporter.
- There is no Dockerfile and no container publish settings.
- Locally Aspire starts it (`Aspire.Host/src/AppHost.cs:119`) against the local API with a client secret actor.

**The API** (Function App `prod-2s-api-fa`, `modules/functions.bicep`):

- `Auth__Provider=AuthKit`, `Auth__AuthKit__Issuer` is the WorkOS API's per-client issuer `https://api.workos.com/user_management/<client id>` without the custom domain (line 90), `Auth__TokenExchange__Enabled='false'` (line 111).
- The exchange (`POST v1/access/token`, `Access.Federation/src/UserTokenExchangeService.cs`) is configured by `Auth:TokenExchange` (`Backend.Core/src/Accessing/TokenExchangeOptions.cs`): `Enabled`, `AdditionalIssuers` (an AuthKit domain whose tokens are exchanged but never accepted as bearer tokens), `Actors` (`Id`, `Authentication`, `SubjectAudiences`, `AllowedClientIds`, `Scopes`, `AllowSecrets`, `Restrictions`), `TokenLifetimeMinutes` (10), `MaxExchangesPerSubjectTokenPerMinute` (10), `MaxActorTokenAgeMinutes` (1440). A token whose `aud` is in an actor's `SubjectAudiences` is refused as a bearer token of the API's routes.
- A workload identity actor (`ActorWorkloadIdentityOptions`) names the `Issuer`, `Subject` and `Audience` of the actor's token; for a managed identity the issuer is `https://sts.windows.net/{tenant}/` (v1), the subject is the identity's object id, and the audience is an app registration that stands for the exchange only (mcp.md, "Credentials of the actor"; `api://AzureADTokenExchange` and Azure resource audiences are refused).

**The deployment** (`src/Platform/Deployment`):

- `main.bicep` (subscription scope) creates the resource group `prod-2s-eusec-rg` in Sweden Central and the modules `identity`, `monitoring` (Log Analytics `prod-2s-log` with a daily cap, Application Insights), `cosmos`, `storage`, `keyvault`, `functions`, `staticwebapp`, `access` (the deployment identity's narrow roles). Names come from `naming.bicep` (`{environment}-{product}-{name}-{type}`).
- `domains.bicep` binds the custom domains with free managed certificates, after the DNS records of `main.bicep`'s output `dnsRecords` exist.
- `scripts/`: `New-ByokApplication.ps1`, `New-DeploymentApplication.ps1` (on `EntraApplications.psm1`), `Set-GitHubEnvironment.ps1`.
- GitHub: the environment `prod.42.Platform` (required reviewer, `main` only) holds the deployment identity (`AZURE_CLIENT_ID`, OIDC), and `Platform.UI.Admin.yml` deploys the admin UI from it. Workflow names follow [the naming convention](../../github/naming-convention.md): `<dotted path>[-<suffix>]`.
- The repository is public, so a public GitHub Container Registry package needs no pull credential.

## Proposed Changes

### 1. Hosting: Azure Container Apps, consumption, scale to zero

| Resource | Name | Settings |
| --- | --- | --- |
| Container Apps environment | `prod-2s-cae` | Consumption only (no workload profiles), Sweden Central, no virtual network, zone redundancy off; logs to the existing Log Analytics workspace `prod-2s-log` (`appLogsConfiguration.destination: 'log-analytics'`), so the existing daily cap covers them |
| Container App | `prod-2s-mcp-ca` | One container `mcp`, 0.25 vCPU and 0.5 GiB; `minReplicas: 0`, `maxReplicas: 3`; HTTP scale rule at 20 concurrent requests; single revision mode; the user-assigned identity of section 2 |
| User-assigned identity | `prod-2s-mcp-mi` | The server's own identity: its actor credential at the API, nothing else |

- **Ingress:** external, `targetPort: 8080` (the port of .NET's container images), `transport: 'auto'`, `allowInsecure: false` (HTTP redirects to HTTPS), no IP restrictions (MCP clients run anywhere; an unauthenticated call only gets `401` with the resource metadata).
- **Probes:** TCP liveness and readiness on 8080. The health endpoints of `Aspire.ServiceDefaults` exist only in Development, and opening them in production is not needed for scale to zero.
- **Cold start:** with `minReplicas: 0` the first call after idle starts a replica (a few seconds for a .NET container). MCP clients wait for the response; the server is stateless, so nothing is lost. An always-on replica (`minReplicas: 1`, billed at the idle rate between calls) would cost a few USD a month after the free grant and is not planned.
- **Cost:** the consumption environment has no base fee. Container Apps' monthly free grant (180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription) covers an assistant's use many times over; logs stay within the workspace cap. The managed certificate is free. Expected: **0 USD a month**.
- **Names:** `naming.bicep` gets `containerAppsEnvironment: '${environment}-${product}-cae'`, `mcpApp: '${environment}-${product}-mcp-ca'` and `mcpIdentity: '${environment}-${product}-mcp-mi'` in `resourceNamesType` and `resourceNames`.

Why not the Function App or a second Flex app: the server is an ASP.NET Core app with its own host and long-lived HTTP streams, not a set of functions; Container Apps runs the same image locally and in Azure and scales to zero at the same cost.

### 2. The actor's credential: a managed identity, no secret

The server proves itself to the API with the token of its user-assigned managed identity (`Mcp:Actor:WorkloadIdentity`, `Kind` `ManagedIdentity`). No secret or certificate exists anywhere.

- **A separate identity** `prod-2s-mcp-mi`, not the API's `prod-2s-api-mi`: the API's identity reads every secret of the vault and the storage, and the server needs none of it. The new identity gets **no Azure role at all**.
- **An exchange app registration** `2S Platform Token Exchange` (single tenant, no credential, no redirect URI, App ID URI `api://<its app id>`, access tokens v1): the audience the identity requests a token for. It stands for the Storyteller exchange only, as mcp.md requires (an audience that is a credential elsewhere, such as `api://AzureADTokenExchange` or an Azure resource, is refused at start on both sides). The identity's token then has `iss` `https://sts.windows.net/{tenant}/`, `aud` `api://<app id>` and `sub` the identity's object id.
- **Script** `scripts/New-TokenExchangeApplication.ps1` (on `EntraApplications.psm1`, like the other two): finds or creates the application and its service principal, sets the App ID URI, leaves `requestedAccessTokenVersion` unset (v1), and prints the app id for `parameters/prod.bicepparam` (`mcpExchangeAppId`). Safe to run again. Optional hardening, not needed for correctness: an app role assigned to `prod-2s-mcp-mi` with `appRoleAssignmentRequired`, so no other identity of the tenant can get a token for the audience at all; the API already pins the subject.
- **Token age:** Azure.Identity caches the identity's token (up to about 24 hours); `MaxActorTokenAgeMinutes` defaults to 1440, so the default fits. Keep it.

### 3. The container image

- **Build without a Dockerfile**, with the .NET SDK's container support, in `Mcp.csproj`:

  ```xml
  <PropertyGroup>
    <ContainerRepository>akobr/2s-platform-mcp</ContainerRepository>
    <ContainerFamily>noble-chiseled</ContainerFamily>
  </PropertyGroup>
  ```

  `dotnet publish src/Platform/Storyteller/Mcp/src -c Release -t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerImageTags='"<sha>;latest"'`. The chiseled Ubuntu base runs as the non-root `app` user on port 8080, with no shell; `InvariantGlobalization` is already on, so no ICU is needed.
- **Registry:** `ghcr.io/akobr/2s-platform-mcp`, a **public** package (the repository is public, so the image holds nothing secret: settings come from the Container App). The Container App pulls it without credentials. An Azure Container Registry (Basic, about 5 USD a month) is not needed.
- **Tags:** the commit SHA for every deployment, `latest` only as a convenience; the Container App always runs a SHA tag, so a deployment is reproducible and a rollback is `az containerapp update --image …:<older sha>`.

### 4. Templates: `modules/mcp.bicep`, `main.bicep`, `domains.bicep`

**`modules/identity.bicep`** stays as it is; `main.bicep` creates the second identity with the same module (a second call with `names.mcpIdentity`), or `modules/mcp.bicep` creates it, whichever keeps the modules simpler at implementation.

**`modules/mcp.bicep`** (new, resource group scope) creates the environment, the identity and the Container App. Parameters: `names`, `tags`, `location`, `logAnalyticsWorkspaceName`, `image`, `mcpHostName`, `apiHostName`, `authKitDomain`, `exchangeAppId`, `scope`, `minReplicas`, `maxReplicas`. The container's environment variables:

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Mcp__Provider` | `AuthKit` |
| `Mcp__ResourceUrl` | `https://mcp.2s.42for.net/mcp` |
| `Mcp__Authentication__Issuer` | `https://palatial-omen-84.authkit.app` (the AuthKit domain, `authKitDomain`; with the custom domain later `https://auth.42for.net`) |
| `Mcp__Authentication__Scope` | `openid` (section 6) |
| `Mcp__Actor__Id` | `mcp` |
| `Mcp__Actor__WorkloadIdentity__Kind` | `ManagedIdentity` |
| `Mcp__Actor__WorkloadIdentity__Audience` | `api://<mcpExchangeAppId>` |
| `Mcp__Actor__WorkloadIdentity__ClientId` | the client id of `prod-2s-mcp-mi` |
| `AZURE_CLIENT_ID` | the same client id (Azure.Identity's default for the user-assigned identity) |
| `Mcp__SecretTools__Enabled` | `false` |
| `Storyteller__BaseUrl` | `https://api.2s.42for.net/api` |

No secret is set, so the app has no `secrets` block.

**Keeping the deployed image.** The workflow (section 7) changes the image; a later run of `main.bicep` must not set it back. `modules/mcp-image.bicep` (new, the pattern of the Azure Developer CLI) reads the running image when the app exists:

```bicep
param appName string
param exists bool
resource app 'Microsoft.App/containerApps@2025-01-01' existing = if (exists) { name: appName }
output image string = exists ? app!.properties.template.containers[0].image : ''
```

`main.bicep` runs it before `modules/mcp.bicep` and passes `!empty(existingImage) ? existingImage : mcpInitialImage`. `parameters/prod.bicepparam` gets `mcpAppExists = false` for the first run, committed as `true` once the app exists (like the ids committed later in the existing runbook).

**`main.bicep` parameters** (all with defaults that keep the MCP server off, so a run without them changes nothing):

| Parameter | Default | Production |
| --- | --- | --- |
| `mcpEnabled` | `false` | `true` |
| `mcpHostName` | `''` | `mcp.2s.42for.net` |
| `mcpExchangeAppId` | `''` | the app id of section 2 |
| `mcpInitialImage` | `''` | `ghcr.io/akobr/2s-platform-mcp:<sha of the first build>` |
| `mcpAppExists` | `false` | `true` after the first run |
| `mcpScope` | `'openid'` | `openid` |
| `mcpMaxReplicas` | `3` | `3` |

The modules run only when `mcpEnabled` (`if (mcpEnabled)`). New outputs: `mcpAppName`, `mcpDefaultHostName` (`<app>.<environment default domain>`), `mcpIdentityPrincipalId`, `mcpIdentityClientId`; `dnsRecords` gets, when `mcpEnabled`:

| Name | Type | Value |
| --- | --- | --- |
| `mcp.2s` | CNAME | the app's default host name (`prod-2s-mcp-ca.<unique>.swedencentral.azurecontainerapps.io`) |
| `asuid.mcp.2s` | TXT | the environment's `customDomainConfiguration.customDomainVerificationId` |

**`domains.bicep`** binds `mcp.2s.42for.net` with a free managed certificate (`Microsoft.App/managedEnvironments/managedCertificates`, `domainControlValidation: 'CNAME'`), the same two steps as for the API: the host name on the app's ingress with `bindingType: 'Disabled'`, the certificate, then `SniEnabled` with the certificate's id. Like the API's binding, a later run reads the current binding and keeps an existing SNI binding. New parameter `mcpHostName` (empty skips it) in `parameters/prod.domains.bicepparam`.

**`modules/access.bicep`**: the deployment identity (`deploymentPrincipalId`) gets **Contributor on the Container App only**, enough for `az containerapp update --image`. It cannot touch the environment, the identity (assigning one needs Managed Identity Operator on it) or anything else in the group. If a narrower built-in role that allows changing the image exists at implementation time, use it.

### 5. The API: turn the exchange on and trust the actor

`modules/functions.bicep` gets the parameters `tokenExchangeEnabled` (from `mcpEnabled`), `mcpResourceUrl`, `mcpIdentityPrincipalId`, `mcpExchangeAppId` and `authKitDomain` (already there), and replaces `Auth__TokenExchange__Enabled: 'false'` by, when enabled:

| Setting | Value |
| --- | --- |
| `Auth__TokenExchange__Enabled` | `true` |
| `Auth__TokenExchange__AdditionalIssuers__0__Issuer` | `https://palatial-omen-84.authkit.app` (the AuthKit domain) |
| `Auth__TokenExchange__AdditionalIssuers__0__JwksUri` | `https://palatial-omen-84.authkit.app/oauth2/jwks` (the `jwks_uri` of its discovery document) |
| `Auth__TokenExchange__Actors__0__Id` | `mcp` |
| `Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Issuer` | `https://sts.windows.net/<tenant id>/` (`tenant().tenantId`) |
| `Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Subject` | the object id of `prod-2s-mcp-mi` |
| `Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Audience` | `api://<mcpExchangeAppId>` |
| `Auth__TokenExchange__Actors__0__SubjectAudiences__0` | `https://mcp.2s.42for.net/mcp` |

- **`AdditionalIssuers` is required** while the API's `Auth:AuthKit:Issuer` is the WorkOS API's per-client issuer: MCP sign-ins come from the AuthKit domain, a different issuer. The API accepts tokens of an additional issuer for the exchange only, never as bearer tokens. With the custom domain later ([424-247](https://linear.app/424fun/issue/424-247/add-the-custom-authkit-domain-auth42fornet-before-the-first-customer)) both may become `https://auth.42for.net`; the API then refuses to start with the domain also listed here, so the entry goes away in the same change (a Bicep condition: list it only when it differs from `Auth__AuthKit__Issuer`).
- **Defaults kept on purpose:** `Scopes` `User.Impersonation`; `AllowSecrets` off, so every token carries `NoSecretReveal` and the server's secret tools stay off (`Mcp__SecretTools__Enabled=false`); `AllowedClientIds` empty, because MCP clients register dynamically (the API warns once at start; section 6 explains the gate); `TokenLifetimeMinutes` 10; the replay limit 10.
- **No circular dependency:** the API needs the identity's object id and the MCP host name, the server needs only the API's URL. `main.bicep` creates the identity before `functions`.

### 6. WorkOS production and the audiences of 424-239

In the WorkOS dashboard, environment **Production**:

1. **Dynamic client registration** on (MCP clients register themselves at the AuthKit domain; mcp.md, "AuthKit").
2. **Resource indicator** `https://mcp.2s.42for.net/mcp`. MCP clients send it as `resource`, and the token's `aud` is then exactly the resource URL. mcp.md recommends making it the **default** resource indicator for clients that omit `resource`; with Multiple Applications on (424-239), check first in staging that a default resource indicator does not change the `aud` of the 2S admin UI's or `sform`'s tokens (they come from the WorkOS API with their application's `aud`). If it does, leave it non-default and accept that a client without `resource` is refused (all current MCP clients send it).
3. **The scope:** `openid`, not a WorkOS permission. Dynamically registered clients get only the OpenID Connect scopes plus environment-wide default scopes, and a permission in the default scopes would be in every token of every 42for.net product, so it gates nothing. The real gate is the API: the exchange refuses a person without a Storyteller account, and their roles decide every call. A WorkOS permission (`mcp:access`) can replace `openid` later if WorkOS adds per-client scopes.
4. **Keep the MCP audience out of the API's bearer audiences.** The 2S audiences of 424-239 (`Auth:AuthKit:Audiences`) list the 2S applications only; `https://mcp.2s.42for.net/mcp` is only in the actor's `SubjectAudiences`. The API refuses a token with that audience on its routes anyway (token passthrough check), so a leaked MCP token cannot call the API directly.
5. **The JWT template and Multiple Applications:** 424-239's staging findings say how the template's `aud` combines with an application's. Check in staging that a token issued at the AuthKit domain with `resource` carries the resource URL as `aud`, not the template's or an application's value. If the template overrides it, the MCP sign-in cannot work at the AuthKit domain and needs a WorkOS answer before rollout (stop condition of the runbook).

**Verification before rollout** (mcp.md: "Check one token of a test sign-in for these three claims and the scope"): one sign-in through the deployed server's metadata with an MCP client, decoded locally: `iss` the AuthKit domain, `aud` `https://mcp.2s.42for.net/mcp`, `sub` a `user_…` id, `scope` containing `openid`. Then `Mcp/testint` against production with that token and the managed identity is not possible from a workstation; the end-to-end check is a real tool call (`get_my_account`) from an MCP client.

### 7. Workflow `Platform.Storyteller.Mcp.yml`

Per the naming convention (`src/Platform/Storyteller/Mcp` → `Platform.Storyteller.Mcp`):

- **Triggers:** pull requests to `main` and pushes to `main` that touch `src/Platform/Storyteller/Mcp/**`, `src/Platform/Storyteller/Sdk.NSwag/**`, `src/Platform/Storyteller/Abstractions.Annotations/**`, `src/Platform/Aspire.ServiceDefaults/**`, `Directory.Packages.props` or the workflow; `workflow_dispatch` for a redeploy.
- **`test` job** (every trigger): `dotnet test --project src/Platform/Storyteller/Mcp/test`.
- **`image` job** (push to `main` and dispatch): `permissions: packages: write`; logs in to `ghcr.io` with `GITHUB_TOKEN`; `dotnet publish … -t:PublishContainer -p:ContainerRegistry=ghcr.io` with the tags `${{ github.sha }}` and `latest`. The first publish creates the package as private: set it **public** once in the package settings (runbook).
- **`deploy` job** (after `image`): `environment: prod.42.Platform` (required reviewer, `main` only), `permissions: id-token: write`; `azure/login` with the deployment identity (OIDC, the existing `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`); `az containerapp update -g ${{ vars.AZURE_RESOURCE_GROUP }} -n ${{ vars.AZURE_MCP_APP_NAME }} --image ghcr.io/akobr/2s-platform-mcp:${{ github.sha }}`; then waits for the new revision to be running and checks `https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp` answers `200`.
- **New variable** of `prod.42.Platform`: `AZURE_MCP_APP_NAME` = `prod-2s-mcp-ca`, set by `Set-GitHubEnvironment.ps1` (new parameter `-McpAppName`, default from the naming convention).
- `docs/github/workflows.md` gets the workflow.

### 8. Documentation

- `docs/Platform/Deployment/runbook-mcp-server.md` (new): the deployment, in order, with every what-if (delivered with this spec).
- `docs/Platform/Deployment/cookbook-azure-production.md`: a step for the MCP server after step 17 (what it creates and why each setting), and the open point of step 19 replaced by a link.
- `docs/Platform/Deployment/decisions.md`: the decisions of this spec (Container Apps, separate identity, `openid` as the gate, public image).
- `src/Platform/Deployment/README.md`: the new parameters, module and script.
- `docs/Platform/Storyteller/mcp.md`: a section "Running in Azure" linking the runbook.
- `CLAUDE.md`: the `Deployment/` row and the CI list (the new workflow).

### 9. Security review

| Risk | Mitigation |
| --- | --- |
| The server is reachable from the internet | Every call needs a valid AuthKit token for the MCP audience; the server holds no data and no secret; the API decides every call by the person's roles |
| A stolen MCP token | It is valid only for the exchange (the API refuses it as a bearer token); the exchange needs the actor's managed identity token as well, which exists only inside the Container App |
| A compromised server | It can act only for people who sign in to it, within their roles, with `NoSecretReveal`; it has no Azure role, no vault access and no storage access |
| Another product's token (shared WorkOS environment) | 424-239: the API's bearer audiences are the 2S applications only; the exchange accepts only the MCP audience from the AuthKit domain |
| Dynamic client registration lets any client register | Intended for MCP; registration alone grants nothing: a person still signs in and needs a Storyteller account |
| The deployment identity | Contributor on the Container App only; protected by the `prod.42.Platform` environment (reviewer, `main` only) like the API's deployment |
| The image | Public, built only by the workflow from `main`; chiseled, non-root base; SHA tags |

### 10. Out of scope

- An always-on replica, a second region, a virtual network.
- Application Insights for the server (an OTLP collector or the Azure Monitor exporter in `Aspire.ServiceDefaults`); console logs in Log Analytics are enough at first.
- The custom AuthKit domain (424-247): when it comes, `authKitDomain` changes, the server's issuer follows, and the API's additional issuer goes away (section 5).
- The server's secret tools (`AllowSecrets`).
- A WorkOS permission as the MCP scope (section 6, point 3).

### 11. Acceptance criteria

- `az bicep build` of `main.bicep` and `domains.bicep` pass; a run with the default parameters (`mcpEnabled = false`) shows no change in what-if.
- `https://mcp.2s.42for.net/mcp` answers `401` with `WWW-Authenticate` naming the resource metadata; the metadata names the AuthKit domain and the scope.
- An MCP client (e.g. Claude Code or Claude Desktop) signs in with a production account and `get_my_account` returns that account; a person without a Storyteller account gets the API's "No Storyteller account exists for this sign-in" text.
- The API's log shows the exchange with the actor `mcp`; the audit of a change made through the server names the person with `Via` `mcp`.
- A token with the MCP audience sent directly to `GET v1/access/account` gets `401`.
- With no traffic the app has zero replicas; the month's cost of the new resources is 0 USD.
- A push to `main` that changes the server builds the image and, after approval, runs the new revision.
