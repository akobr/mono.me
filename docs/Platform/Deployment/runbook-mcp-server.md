# Runbook: Deploy the MCP Server to Production

The first deployment of the MCP server to Azure Container Apps, in order: what you run, what you do by hand in between, every `what-if` with what it must show, and the checks. It follows the spec [Deploy the MCP Server to Azure Container Apps](specs/2026-11-17%20Deploy%20the%20MCP%20Server%20to%20Azure%20Container%20Apps.md) (sections in brackets, e.g. **[S5]**) as implemented ([review](specs/reviews/2026-11-20%20Deploy%20the%20MCP%20Server%20to%20Azure%20Container%20Apps.md)), and continues the [production runbook](runbook-azure-production.md), whose phases 0–12 are done. The cookbook's [step 17a](cookbook-azure-production.md#17a-the-mcp-server-container-apps-later) explains every setting.

Everything is PowerShell 7.4+ on Windows, run from `src/Platform/Deployment` of an up-to-date checkout of `main`, with the two `PLATFORM_OWNER_*` variables of the production runbook's phase 0. Lines starting with `#` are comments; replace `<…>` with your values.

| Phase | What | Mostly | Waits |
| --- | --- | --- | --- |
| 0 | Prerequisites | checks | — |
| 1 | WorkOS staging check | dashboard + an MCP client | — |
| 2 | The exchange app registration | script | — |
| 3 | The first image | workflow | a few minutes |
| 4 | First run of `main.bicep` with the MCP server | template + script | about 5 minutes |
| 5 | DNS records | your DNS provider | propagation |
| 6 | The custom domain (`mcpCustomDomain`, `mcpAppExists`) | template | the certificate, up to 20 minutes |
| 7 | WorkOS production | dashboard | — |
| 8 | GitHub environment and the first deployment by the workflow | script + workflow | a few minutes |
| 9 | Verify | an MCP client + checks | — |
| 10 | Rolling back | — | — |

Stop and fix before going on whenever a check fails. Each change of `parameters/prod.bicepparam` goes to `main` through a pull request before its run, as in the production runbook: `main.bicep` owns the API's app settings, so a run from an old checkout would set them back.

## 0. Prerequisites

- [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api) is done and in production: the API reads the list of 2S audiences (`authKitAudiences`), Multiple Applications is on in WorkOS production, the admin UI and `sform` sign in ([production runbook](runbook-azure-production.md#enabling-multiple-applications-in-workos)).
- The implementation of [424-287](https://linear.app/424fun/issue/424-287) is merged to `main`: `modules/mcp.bicep`, `modules/mcp-existing.bicep`, the API's settings in `modules/functions.bicep`, the role in `modules/access.bicep`, `scripts/New-TokenExchangeApplication.ps1`, the container settings in `Mcp.csproj` and the workflow `Platform.Storyteller.Mcp.yml`.
- Your Azure sign-in is the subscription's Owner and an Application Administrator of the tenant:

```powershell
az account show --query "{name:name, user:user.name, tenant:tenantId}" -o table   # prod-42fornet-sub
cd src/Platform/Deployment
$env:PLATFORM_OWNER_EMAIL = '<your email for the budget alerts>'
$env:PLATFORM_OWNER_OBJECT_ID = az ad signed-in-user show --query id -o tsv
```

- The resource providers of Container Apps and of diagnostic settings are registered:

```powershell
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.Insights --wait
az provider show --namespace Microsoft.App --query registrationState -o tsv        # Registered
az provider show --namespace Microsoft.Insights --query registrationState -o tsv   # Registered
```

- Azure CLI 2.71 or newer with a recent Bicep (`az bicep upgrade`): the templates use `Microsoft.App` 2026-07-01, whose types an old Bicep doesn't know (warning `BCP081`).

## 1. WorkOS staging check [S6]

Before production, check how WorkOS issues MCP tokens with Multiple Applications on. In the WorkOS dashboard, environment **Staging**:

1. Turn on **dynamic client registration** and add the resource indicator `https://localhost:7180/mcp` (the local server).
2. Run the server locally against the local API (`dotnet run --project src/Platform/Aspire.Host/src`, start `mcp`; mcp.md, "Local development") with `Mcp:Authentication:Scope` `openid`.
3. Connect an MCP client to `https://localhost:7180/mcp`, sign in, and decode the token the client sends, locally only (`Read-JwtClaims` of the production runbook, "Enabling Multiple Applications in WorkOS"). **Expect:** `iss` `https://<staging subdomain>.authkit.app`, `aud` `https://localhost:7180/mcp`, `sub` `user_…`, `scope` containing `openid`.
4. Sign in to the local admin UI and decode its token: its `aud` is still the 2S application's (the value 424-239 put into the API's audiences), not the resource URL.
5. Make the resource indicator the **default** one and repeat 4. If the admin UI's `aud` changed, put it back to non-default: production keeps it non-default too (phase 7).

**Stop** if the MCP token's `aud` is not the resource URL (the JWT template or the application overrides it): the API's exchange would refuse every MCP sign-in. Ask WorkOS before going on.

## 2. The exchange app registration [S2]

```powershell
$exchange = ./scripts/New-TokenExchangeApplication.ps1
$exchange.ClientId          # mcpExchangeAppId
$exchange.IdentifierUri     # api://<the same id>
```

**Check:**

```powershell
az ad app show --id $exchange.ClientId --query "{name:displayName, audience:signInAudience, uris:identifierUris, version:api.requestedAccessTokenVersion, secrets:length(passwordCredentials), certificates:length(keyCredentials)}" -o json
```

Expect `2S Platform Token Exchange`, `AzureADMyOrg`, `["api://<id>"]`, version `null` (v1), `0` secrets and `0` certificates. The script also created its service principal: a managed identity can't get a token for an application without one.

## 3. The first image [S3, S7]

1. Run the workflow **Platform.Storyteller.Mcp** by hand on `main` (**Actions → Platform.Storyteller.Mcp → Run workflow**). The `test` and `image` jobs run and push `ghcr.io/akobr/2s-platform-mcp:<commit>` and `:latest`. The `deploy` job waits for the approval of `prod.42.Platform`: **reject** it, because the Container App doesn't exist yet.
2. Check the package's visibility: **github.com/akobr → Packages → 2s-platform-mcp**, or `gh api /users/akobr/packages/container/2s-platform-mcp --jq .visibility` (needs `read:packages`). If it is `private`: **Package settings → Danger Zone → Change visibility → Public** (it can't be made private again; the image holds nothing secret). It is linked to the repository, so later pushes of the workflow need no setting.
3. Check that it can be pulled without signing in, and note the tag (the commit of the run):

```powershell
docker logout ghcr.io
docker pull ghcr.io/akobr/2s-platform-mcp:<commit>
docker run --rm -e ASPNETCORE_ENVIRONMENT=Production ghcr.io/akobr/2s-platform-mcp:<commit>
```

The container stops at once with `OptionsValidationException` listing the missing settings (`Mcp:ResourceUrl`, `Mcp:Actor:Id`, …): the image starts, and the settings come from the Container App.

## 4. First run of `main.bicep` with the MCP server [S1, S2, S4, S5]

In `parameters/prod.bicepparam` (`mcpHostName = 'mcp.2s.42for.net'` is already there):

```bicep
param mcpEnabled = true
param mcpExchangeAppId = '<$exchange.ClientId>'
param mcpInitialImage = 'ghcr.io/akobr/2s-platform-mcp:<commit of phase 3>'
param mcpAppExists = false
param mcpCustomDomain = false
```

Commit them to `main` through a pull request, pull `main`, then:

```powershell
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect** (besides the usual noise of the production runbook):

- `+ Microsoft.ManagedIdentity/userAssignedIdentities/prod-2s-mcp-mi`
- `+ Microsoft.App/managedEnvironments/prod-2s-cae`: the workload profile `Consumption`, `appLogsConfiguration.destination` `azure-monitor`, `zoneRedundant` false
- `+ Microsoft.Insights/diagnosticSettings/log-analytics` on `prod-2s-cae`: `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` to `prod-2s-log`, `Dedicated`
- `+ Microsoft.App/containerApps/prod-2s-mcp-ca`: the image of phase 3, `minReplicas` 0, `maxReplicas` 3, the identity, the 12 environment variables of the cookbook's step 17a, the three TCP probes, **no** `secrets` and no `customDomains`
- `+ Microsoft.Authorization/roleAssignments/<guid>`: Container Apps Contributor (`358470bc-…`) for the deployment identity, scoped to `prod-2s-mcp-ca`
- `~ Microsoft.Web/sites/prod-2s-api-fa/config/appsettings`: `Auth__TokenExchange__Enabled` `false` → `true`, `+ Auth__TokenExchange__AdditionalIssuers__0__Issuer` (`https://palatial-omen-84.authkit.app`) and `__JwksUri` (`…/oauth2/jwks`), `+ Auth__TokenExchange__Actors__0__Id` (`mcp`), `+ …__Authentication__WorkloadIdentity__Issuer` (`https://sts.windows.net/<tenant>/`), `__Subject` (the identity's object id: what-if may show it as an expression, since the identity doesn't exist yet), `__Audience` (`api://<mcpExchangeAppId>`), `+ …__SubjectAudiences__0` (`https://mcp.2s.42for.net/mcp`); every other setting unchanged.

**Stop** on any `Delete`, any change of the Cosmos DB account or the vault, or a change of `Auth__AuthKit__Issuer` or the audiences. A missing parameter stops the what-if itself with its message (e.g. `mcpExchangeAppId must be the application id …`).

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

**Check the server:**

```powershell
az containerapp show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query "{state:properties.runningStatus, fqdn:properties.configuration.ingress.fqdn, image:properties.template.containers[0].image, min:properties.template.scale.minReplicas}" -o json
$fqdn = az containerapp show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query properties.configuration.ingress.fqdn -o tsv
curl.exe -s -D - -o NUL -X POST "https://$fqdn/mcp"
az containerapp logs show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --type console --tail 50
```

The `POST` answers `401` with `WWW-Authenticate: Bearer resource_metadata="https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp"` (the first call after an idle period waits a few seconds for a replica). The log, streamed while a replica runs, shows `The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: https://mcp.2s.42for.net/mcp.`, `Now listening on: http://[::]:8080` and `Hosting environment: Production`. Two warnings of ASP.NET Core's data protection about keys stored in the container are expected: the server protects nothing with them. A configuration error stops the container and names the setting (`az containerapp logs show … --type system` shows the restarts).

**Check the API:** it restarted with the new settings.

```powershell
curl.exe -s https://api.2s.42for.net/api/v1/auth/configuration
```

It answers as before, and the admin UI still signs in. Application Insights (`prod-2s-api-appi`, **Logs → traces**, the last 15 minutes) shows the API's warning that the actor `mcp` accepts any client's token for its audiences (`AllowedClientIds` empty, intended for dynamic registration) and no start failure.

**Restrict the exchange application to the server's identity** (recommended): only `prod-2s-mcp-mi` then gets tokens for `api://<id>` from Entra; the API pins its object id anyway.

```powershell
./scripts/New-TokenExchangeApplication.ps1 -McpIdentityName prod-2s-mcp-mi
az ad sp show --id $exchange.ClientId --query appRoleAssignmentRequired   # true
```

## 5. DNS records [S4]

```powershell
az deployment sub show --name prod-2s-main --query properties.outputs.dnsRecords.value -o table
```

In the zone `42for.net`, add the two new records (TTL 300), straight to the app (no proxy or other CNAME in between, or the certificate can't be issued or renewed):

| Name | Type | Value |
| --- | --- | --- |
| `mcp.2s` | CNAME | the `mcp.2s` value of the output (`prod-2s-mcp-ca.<unique>.swedencentral.azurecontainerapps.io`) |
| `asuid.mcp.2s` | TXT | the `asuid.mcp.2s` value of the output (the app's domain verification id) |

**Check:**

```powershell
Resolve-DnsName mcp.2s.42for.net -Type CNAME
Resolve-DnsName asuid.mcp.2s.42for.net -Type TXT
Resolve-DnsName 42for.net -Type CAA
```

If the zone has CAA records, one must allow `digicert.com` (`0 issue "digicert.com"`), the authority of the managed certificates; no CAA record allows every authority.

## 6. The custom domain [S4]

In `parameters/prod.bicepparam`:

```bicep
param mcpAppExists = true
param mcpCustomDomain = true
```

`mcpAppExists` makes every later run keep the image that the workflow deployed (and the bound certificate); `mcpCustomDomain` puts `mcp.2s.42for.net` on the app with the binding type `Auto` and creates the environment's free managed certificate, which the platform binds as soon as it is issued. Commit through a pull request, pull `main`, then the what-if of phase 4.

**Expect:**

- `~ Microsoft.App/containerApps/prod-2s-mcp-ca`: `+ configuration.ingress.customDomains` with `mcp.2s.42for.net` and `bindingType` `Auto`; the image unchanged
- `+ Microsoft.App/managedEnvironments/prod-2s-cae/managedCertificates/mcp-2s-42for-net`: `subjectName` `mcp.2s.42for.net`, `domainControlValidation` `CNAME`
- nothing for the API's app settings

Then the `create` command of phase 4. It waits for the certificate, which takes up to 20 minutes. If the run fails with `InvalidCustomHostNameValidation`, a DNS record of phase 5 is missing or not yet visible: fix it and run again.

**Check:**

```powershell
az containerapp hostname list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table          # mcp.2s.42for.net, SniEnabled
az containerapp env certificate list -g prod-2s-eusec-rg -n prod-2s-cae --managed-certificates-only -o table   # Succeeded
curl.exe -s -D - -o NUL -X POST https://mcp.2s.42for.net/mcp
curl.exe -s https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp
```

Expect `401` over TLS with `WWW-Authenticate: Bearer resource_metadata="https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp"`, and the document with `resource` `https://mcp.2s.42for.net/mcp`, `authorization_servers` `["https://palatial-omen-84.authkit.app"]` and `scopes_supported` `["openid"]`.

If the certificate fails (`Failed`), the host name stays without TLS: fix the DNS or CAA records and run `main.bicep` again, which asks for the certificate again. Run the what-if once more after the binding: it shows no change of `prod-2s-mcp-ca` (the bound certificate and the image are written back as they are). The certificate renews itself while the CNAME points at the app and the app is reachable.

## 7. WorkOS production [S6]

In the WorkOS dashboard, environment **Production**:

1. **Dynamic client registration:** on.
2. **Resource indicator:** add `https://mcp.2s.42for.net/mcp`. Make it the default only if phase 1 showed that a default doesn't change the admin UI's `aud`.
3. Nothing else: no permission, no change of the JWT template, no new application.

**Check:** sign out of the admin UI, sign in again, and check that it loads your organizations (its `aud` is unchanged).

## 8. GitHub environment and the first deployment by the workflow [S7]

```powershell
./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId '<the deployment app id>' -BaseAccountId '<user_01…>'
gh variable list --repo akobr/mono.me --env prod.42.Platform | Select-String AZURE_MCP_APP_NAME   # prod-2s-mcp-ca
```

The script takes `AZURE_MCP_APP_NAME` from the output `mcpAppName` of `prod-2s-main` (or `-McpAppName`) and leaves the other variables as they are when their parameter is empty.

Run the workflow **Platform.Storyteller.Mcp** by hand on `main` and **approve** the `deploy` job. It runs `az containerapp update --image ghcr.io/akobr/2s-platform-mcp:<commit> --revision-suffix gh<run>-<attempt> --no-wait`, waits until that revision is provisioned and the latest ready one, and reads the resource metadata from `https://mcp.2s.42for.net`. **Check** that the job is green and the revision runs the commit:

```powershell
az containerapp revision list -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query "[].{name:name, active:properties.active, image:properties.template.containers[0].image}" -o table
```

From now on every merge to `main` that changes the server builds an image and waits for your approval to run it.

If the job fails with `AuthorizationFailed` on `Microsoft.App/containerApps/write`, the role assignment of phase 4 hasn't reached the identity yet (wait a few minutes and re-run the job), or Container Apps Contributor doesn't cover the write: then change `containerAppsContributor` in `modules/access.bicep` to Contributor (`b24988ac-6180-42a0-ab88-20f7382dd24c`), still scoped to the app, and run `main.bicep`.

## 9. Verify [S11]

| Check | How | Expected |
| --- | --- | --- |
| Sign-in | Add `https://mcp.2s.42for.net/mcp` as a remote MCP server in an MCP client (e.g. `claude mcp add --transport http 2s https://mcp.2s.42for.net/mcp`), sign in with your production account | the WorkOS sign-in page of `palatial-omen-84.authkit.app`, then the client lists the tools |
| The token | the client's token, decoded locally | `iss` the AuthKit domain, `aud` `https://mcp.2s.42for.net/mcp`, `sub` your `user_…`, `scope` with `openid` |
| The exchange | the tool `get_my_account` | your account |
| Your data | list your organizations and a project's annotations | the same as in the admin UI |
| No account | sign in with a WorkOS user that has no Storyteller account | "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." |
| Audit | change one annotation through the assistant, then look at its audit in the admin UI | your account with `Via` `mcp` |
| Secrets | ask for a secret value | refused (`NoSecretReveal`), and the secret tools are not listed |
| No passthrough | send the MCP token to `GET https://api.2s.42for.net/api/v1/access/account` | `401` |
| Logs | Log Analytics `prod-2s-log`: `ContainerAppConsoleLogs \| where ContainerAppName == "prod-2s-mcp-ca" \| take 50`, and the API's Application Insights | the exchange logged with the actor `mcp`, no token or body in any log |
| Scale to zero | after 10 minutes without calls: `az containerapp replica list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table` | no replica |
| Cost | Cost Management after a few days, the resource group filtered by the new resources | 0 USD |

A request through the ingress ends after 240 seconds: a tool call that waits longer for the API is cut off, and the assistant checks a write with the matching read.

## 10. Rolling back

- **A bad image:** `az containerapp update -g prod-2s-eusec-rg -n prod-2s-mcp-ca --image ghcr.io/akobr/2s-platform-mcp:<previous commit>` (the revision list of phase 8 has the earlier ones). A later run of `main.bicep` keeps that image.
- **The server unreachable at once, everything kept:** `az containerapp ingress disable -g prod-2s-eusec-rg -n prod-2s-mcp-ca`. The next run of `main.bicep` turns the ingress on again, so for longer use the next point.
- **The exchange off in the API:** set `mcpEnabled = false` in `parameters/prod.bicepparam`, pull request, what-if (only `prod-2s-api-fa/appsettings`: `Auth__TokenExchange__Enabled` → `false`, the `AdditionalIssuers__0__*` and `Actors__0__*` settings removed), then create: every MCP call answers that the exchange is off. The identity, the environment, the app, its certificate, the role assignment and the exchange application stay (a template run doesn't delete them). Delete them by hand if wanted: `az containerapp delete`, `az containerapp env delete`, `az identity delete -n prod-2s-mcp-mi`, the role assignment (`az role assignment list --all --assignee <deployment principal id>`), `az ad app delete --id <mcpExchangeAppId>`, the DNS records and the GitHub variable `AZURE_MCP_APP_NAME`; then set `mcpAppExists = false` and `mcpCustomDomain = false`.
- **The custom domain off:** `mcpCustomDomain = false` and a run of `main.bicep` removes the binding; the certificate stays in the environment until it is deleted (`az containerapp env certificate delete`).

## Later: the custom AuthKit domain

When [424-247](https://linear.app/424fun/issue/424-247/add-the-custom-authkit-domain-auth42fornet-before-the-first-customer) sets `authKitDomain = 'auth.42for.net'` and `authKitCustomDomain = true`, the same run of `main.bicep` moves the server's issuer (`Mcp__Authentication__Issuer`) to `https://auth.42for.net` and removes the API's additional issuer, which would then equal `Auth__AuthKit__Issuer` (the API refuses to start with the same domain in both). Its what-if shows both changes. MCP clients find the new authorization server in the resource metadata and register again; people sign in once more. Dynamic client registration and the resource indicator are settings of the WorkOS environment and stay.
