# Runbook: Deploy the MCP Server to Production

The first deployment of the MCP server to Azure Container Apps, in order: what you run, what you do by hand in between, every `what-if` with what it must show, and the checks. It follows the spec [Deploy the MCP Server to Azure Container Apps](specs/2026-11-17%20Deploy%20the%20MCP%20Server%20to%20Azure%20Container%20Apps.md) (sections in brackets, e.g. **[S5]**) and continues the [production runbook](runbook-azure-production.md), whose phases 0–12 are done.

Everything is PowerShell 7.4+ on Windows, run from `src/Platform/Deployment` with the two `PLATFORM_OWNER_*` variables of the production runbook's phase 0. Lines starting with `#` are comments; replace `<…>` with your values.

| Phase | What | Mostly | Waits |
| --- | --- | --- | --- |
| 0 | Prerequisites | checks | — |
| 1 | WorkOS staging check | dashboard + an MCP client | — |
| 2 | The exchange app registration | script | — |
| 3 | The first image | workflow | a few minutes |
| 4 | First run of `main.bicep` with the MCP server | template | about 5 minutes |
| 5 | DNS records | your DNS provider | propagation |
| 6 | Custom domain (`domains.bicep`) | template | certificate, up to 10 minutes |
| 7 | WorkOS production | dashboard | — |
| 8 | GitHub environment, `mcpAppExists` | script + pull request | — |
| 9 | Verify | an MCP client + checks | — |

Stop and fix before going on whenever a check fails. Rolling back is phase 10.

## 0. Prerequisites

- [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api) is done and in production: the API reads the list of 2S audiences, Multiple Applications is on in WorkOS production, the admin UI and `sform` sign in.
- The implementation of the spec is merged to `main`: `modules/mcp.bicep`, `modules/mcp-image.bicep`, the API settings in `modules/functions.bicep`, `domains.bicep`, `scripts/New-TokenExchangeApplication.ps1`, the container settings in `Mcp.csproj`, the workflow `Platform.Storyteller.Mcp.yml`.
- Your Azure sign-in is the subscription's Owner and an Application Administrator of the tenant:

```powershell
az account show --query "{name:name, user:user.name, tenant:tenantId}" -o table   # prod-42fornet-sub
cd src/Platform/Deployment
$env:PLATFORM_OWNER_EMAIL = '<your email for the budget alerts>'
$env:PLATFORM_OWNER_OBJECT_ID = az ad signed-in-user show --query id -o tsv
```

- Container Apps' resource provider is registered:

```powershell
az provider register --namespace Microsoft.App --wait
az provider show --namespace Microsoft.App --query registrationState -o tsv   # Registered
```

## 1. WorkOS staging check [S6]

Before production, check how WorkOS issues MCP tokens with Multiple Applications on. In the WorkOS dashboard, environment **Staging**:

1. Turn on **dynamic client registration** and add the resource indicator `https://localhost:7180/mcp` (the local server).
2. Run the server locally against the local API (`dotnet run --project src/Platform/Aspire.Host/src`, start `mcp`; mcp.md, "Local development") with `Mcp:Authentication:Scope` `openid`.
3. Connect an MCP client to `https://localhost:7180/mcp`, sign in, and decode the token the client sends (the server's debug log, or the client's), locally only. **Expect:** `iss` `https://<staging subdomain>.authkit.app`, `aud` `https://localhost:7180/mcp`, `sub` `user_…`, `scope` containing `openid`.
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
az ad app show --id $exchange.ClientId --query "{name:displayName, audience:signInAudience, uris:identifierUris, version:api.requestedAccessTokenVersion, secrets:length(passwordCredentials)}" -o json
```

Expect `2S Platform Token Exchange`, `AzureADMyOrg`, `["api://<id>"]`, version `null` (v1), `0` secrets.

## 3. The first image [S3, S7]

1. Run the workflow **Platform.Storyteller.Mcp** by hand on `main` (**Actions → Platform.Storyteller.Mcp → Run workflow**). The `test` and `image` jobs run; the `deploy` job fails or waits, because the Container App doesn't exist yet: **reject** it in the approval.
2. Make the package public once: **github.com/akobr → Packages → 2s-platform-mcp → Package settings → Change visibility → Public**.
3. Check that it can be pulled without signing in, and note the tag:

```powershell
docker logout ghcr.io
docker pull ghcr.io/akobr/2s-platform-mcp:<sha of the run>
docker run --rm -e ASPNETCORE_ENVIRONMENT=Production ghcr.io/akobr/2s-platform-mcp:<sha>   # stops at once: settings missing, which shows the image starts
```

## 4. First run of `main.bicep` with the MCP server [S1, S4, S5]

In `parameters/prod.bicepparam`:

```bicep
param mcpEnabled = true
param mcpHostName = 'mcp.2s.42for.net'
param mcpExchangeAppId = '<$exchange.ClientId>'
param mcpInitialImage = 'ghcr.io/akobr/2s-platform-mcp:<sha of phase 3>'
param mcpAppExists = false
```

Commit them to `main` through a pull request, pull `main`, then:

```powershell
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect** (besides the usual noise of the production runbook):

- `+ Microsoft.ManagedIdentity/userAssignedIdentities/prod-2s-mcp-mi`
- `+ Microsoft.App/managedEnvironments/prod-2s-cae`
- `+ Microsoft.App/containerApps/prod-2s-mcp-ca` with the image of phase 3, `minReplicas` 0, the identity, the environment variables of spec section 4 and **no** `secrets`
- `+` a role assignment: Contributor for the deployment identity on `prod-2s-mcp-ca`
- `~ Microsoft.Web/sites/prod-2s-api-fa/config/appsettings`: `Auth__TokenExchange__Enabled` `true`, the `AdditionalIssuers__0__*` pair (`https://palatial-omen-84.authkit.app`, `…/oauth2/jwks`) and the `Actors__0__*` settings (spec section 5); every other setting unchanged.

**Stop** on any `Delete`, any change of the Cosmos DB account or the vault, or an `Auth__AuthKit__Issuer` / audience change.

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

**Check:**

```powershell
az containerapp show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query "{state:properties.runningStatus, fqdn:properties.configuration.ingress.fqdn, image:properties.template.containers[0].image, min:properties.template.scale.minReplicas}" -o json
az containerapp logs show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --type console --tail 50
```

The log shows the server's start notice with the provider `AuthKit`, the issuer and "The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: https://mcp.2s.42for.net/mcp". A configuration error stops the container and names the setting.

```powershell
$fqdn = az containerapp show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query properties.configuration.ingress.fqdn -o tsv
curl.exe -s -o NUL -w "%{http_code}`n" -X POST "https://$fqdn/mcp"   # 401
```

The API restarted with the new settings; check it still answers and still signs in the admin UI:

```powershell
curl.exe -s https://api.2s.42for.net/api/v1/auth/configuration
```

Application Insights (`traces`, last 15 minutes) shows the API's warning that the actor `mcp` accepts any client's token for its audiences (`AllowedClientIds` empty, intended) and no start failure.

## 5. DNS records [S4]

```powershell
az deployment sub show --name prod-2s-main --query properties.outputs.dnsRecords.value -o table
```

In the zone `42for.net`, add the two new records (TTL 300):

| Name | Type | Value |
| --- | --- | --- |
| `mcp.2s` | CNAME | the `mcp.2s` value of the output (`prod-2s-mcp-ca.<unique>.swedencentral.azurecontainerapps.io`) |
| `asuid.mcp.2s` | TXT | the `asuid.mcp.2s` value of the output |

**Check:**

```powershell
Resolve-DnsName mcp.2s.42for.net -Type CNAME
Resolve-DnsName asuid.mcp.2s.42for.net -Type TXT
```

## 6. Custom domain with `domains.bicep` [S4]

`parameters/prod.domains.bicepparam` gets `param mcpHostName = 'mcp.2s.42for.net'` (committed with phase 4's pull request or its own).

```powershell
az deployment group what-if --name prod-2s-domains --resource-group prod-2s-eusec-rg `
  --template-file domains.bicep --parameters parameters/prod.domains.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect:** `+ Microsoft.App/managedEnvironments/prod-2s-cae/managedCertificates/…` for `mcp.2s.42for.net`, and `~ prod-2s-mcp-ca` with the custom domain on its ingress; nothing for the API's and the admin UI's bindings beyond the noise known from the production runbook.

```powershell
az deployment group create --name prod-2s-domains --resource-group prod-2s-eusec-rg `
  --template-file domains.bicep --parameters parameters/prod.domains.bicepparam
```

**Check:**

```powershell
az containerapp hostname list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table   # mcp.2s.42for.net, SniEnabled
curl.exe -s -D - -o NUL -X POST https://mcp.2s.42for.net/mcp
```

Expect `401` over TLS with `WWW-Authenticate: Bearer resource_metadata="https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp"`. Then:

```powershell
curl.exe -s https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp
```

Expect `resource` `https://mcp.2s.42for.net/mcp`, `authorization_servers` `["https://palatial-omen-84.authkit.app"]`, `scopes_supported` `["openid"]`.

If the certificate fails, the host name stays without TLS: fix the DNS records and run `domains.bicep` again.

## 7. WorkOS production [S6]

In the WorkOS dashboard, environment **Production**:

1. **Dynamic client registration:** on.
2. **Resource indicator:** add `https://mcp.2s.42for.net/mcp`. Make it the default only if phase 1 showed the default doesn't change the admin UI's `aud`.
3. Nothing else: no permission, no change of the JWT template, no new application.

**Check:** sign out of the admin UI, sign in again, and check that it loads your organizations (its `aud` is unchanged).

## 8. GitHub environment and `mcpAppExists` [S4, S7]

```powershell
./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId '<the deployment app id>' -BaseAccountId '<user_01…>' `
  -McpAppName prod-2s-mcp-ca
gh variable list --env prod.42.Platform | Select-String AZURE_MCP_APP_NAME
```

Then set `param mcpAppExists = true` in `parameters/prod.bicepparam` and commit it through a pull request, so later runs of `main.bicep` keep the image the workflow deployed. Run the what-if once more: **expect** no change of `prod-2s-mcp-ca`'s image.

Finally, run the workflow **Platform.Storyteller.Mcp** by hand and **approve** the `deploy` job: it runs `az containerapp update --image …:<sha>` and checks the metadata URL. **Check** that the job is green and the revision runs the new SHA:

```powershell
az containerapp revision list -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query "[].{name:name, active:properties.active, image:properties.template.containers[0].image}" -o table
```

## 9. Verify [S11]

| Check | How | Expected |
| --- | --- | --- |
| Sign-in | Add `https://mcp.2s.42for.net/mcp` as a remote MCP server in an MCP client (e.g. `claude mcp add --transport http 2s https://mcp.2s.42for.net/mcp`), sign in with your production account | the WorkOS sign-in page of `palatial-omen-84.authkit.app`, then the client lists the tools |
| The token | the client's token, decoded locally | `iss` the AuthKit domain, `aud` `https://mcp.2s.42for.net/mcp`, `sub` your `user_…`, `scope` with `openid` |
| The exchange | the tool `get_my_account` | your account (`user_01M75…`, `ales@42for.software`) |
| Your data | list your organizations and a project's annotations | the same as in the admin UI |
| No account | sign in with a WorkOS user that has no Storyteller account | "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." |
| Audit | change one annotation through the assistant, then look at its audit in the admin UI | your account with `Via` `mcp` |
| Secrets | ask for a secret value | refused (`NoSecretReveal`), the secret tools are not listed |
| No passthrough | send the MCP token to `GET https://api.2s.42for.net/api/v1/access/account` | `401` |
| Logs | `az containerapp logs show … --type console` and the API's Application Insights | the exchange logged with actor `mcp`, no token or body in any log |
| Scale to zero | after 10 minutes without calls: `az containerapp replica list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table` | no replica |
| Cost | Cost Management after a few days, the resource group filtered by the new resources | 0 USD |

## 10. Rolling back

- **A bad image:** `az containerapp update -g prod-2s-eusec-rg -n prod-2s-mcp-ca --image ghcr.io/akobr/2s-platform-mcp:<previous sha>`.
- **Turn the server off, keep everything:** `az containerapp ingress disable -g prod-2s-eusec-rg -n prod-2s-mcp-ca`.
- **Turn the exchange off in the API:** set `mcpEnabled = false` in `parameters/prod.bicepparam` and run `main.bicep`: `Auth__TokenExchange__Enabled` goes back to `false` and every MCP call answers that the exchange is off. The Container App and its environment stay (a template run doesn't delete them); delete them by hand if wanted (`az containerapp delete`, `az containerapp env delete`), then the identity and the DNS records.
