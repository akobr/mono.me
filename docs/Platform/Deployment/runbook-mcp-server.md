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
| 11 | The cost of the MCP server: its budget, and what to do when it warns | template + metrics | — |

Later, for the MCP clients ([424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow)): [Trust a client](#trust-a-client), [Cut off one client](#cut-off-one-client) and [Cut off every unlisted client](#cut-off-every-unlisted-client). A client that isn't trusted only reads and reveals no secret.

Stop and fix before going on whenever a check fails. Each change of `parameters/prod.bicepparam` goes to `main` through a pull request before its run, as in the production runbook: `main.bicep` owns the API's app settings, so a run from an old checkout would set them back.

## 0. Prerequisites

- [424-239](https://linear.app/424fun/issue/424-239/accept-only-the-2s-authkit-applications-audiences-in-the-api) is done and in production: the API reads the list of 2S audiences (`authKitAudiences`), Multiple Applications is on in WorkOS production, the admin UI and `sform` sign in ([production runbook](runbook-azure-production.md#enabling-multiple-applications-in-workos)).
- The implementation of [424-287](https://linear.app/424fun/issue/424-287) is merged to `main`: `modules/mcp.bicep`, `modules/mcp-existing.bicep`, the API's settings in `modules/functions.bicep`, the role in `modules/access.bicep`, `scripts/New-TokenExchangeApplication.ps1`, the container settings in `Mcp.csproj` and the workflow `Platform.Storyteller.Mcp.yml`.
- The API in production is a build with [424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow): it reads the client lists and knows the restriction `ReadOnly`. An older build ignores `mcpUntrustedClientRestrictions`, and every MCP client could change anything the person may change.
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
2. Run the server locally against the local API with `Mcp:Authentication:Scope` `openid` (mcp.md, "Local development"): from the repository root, `dotnet run --project src/Platform/Aspire.Host/src`, then start `mcp` in the dashboard. Come back to `src/Platform/Deployment` for the rest of the runbook.
3. Connect an MCP client to `https://localhost:7180/mcp`, sign in, and decode the token the client sends, locally only (`Read-JwtClaims` of the production runbook, "Enabling Multiple Applications in WorkOS"). **Expect:** `iss` `https://<staging subdomain>.authkit.app`, `aud` `https://localhost:7180/mcp`, `sub` `user_…`, `scope` containing `openid`.
4. Sign in to the local admin UI and decode its token: its `aud` is still the 2S application's (the value 424-239 put into the API's audiences), not the resource URL.
5. Make the resource indicator the **default** one and repeat 4. If the admin UI's `aud` changed, put it back to non-default: production keeps it non-default too (phase 7).

**Stop** if the MCP token's `aud` is not the resource URL (the JWT template or the application overrides it): the API's exchange would refuse every MCP sign-in. Ask WorkOS before going on.

**The clients** ([424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow)): which client id the API records, and what WorkOS lets a client do.

6. Turn on the **Client ID Metadata Document** (CIMD) under **Connect → Configuration**, and check the AuthKit metadata:

```powershell
$authkit = 'https://<staging subdomain>.authkit.app'
$metadata = Invoke-RestMethod "$authkit/.well-known/oauth-authorization-server"
$metadata | Select-Object client_id_metadata_document_supported, token_endpoint_auth_methods_supported
```

**Expect** `True` and `none` among the methods. Claude uses CIMD only with both; otherwise it registers a new client on every fresh connection.

7. Sign in once through a client that registers dynamically (DCR; Cursor has no CIMD) and once through one that uses CIMD (Claude Code 2.1.81 or newer and VS Code have a metadata document, but a client may still prefer DCR while it is on: the recorded id tells), and call `get_my_account` with each. Record the client id the API stored for each: the local API's request line `A request … via mcp (client <id>) answered …`, or `via mcp (client <id>)` in the local admin UI's access audit trail after an access change through the client. The exchange's line `… exchanged a user token of the account … (client <id>) …` names it too, but cuts it after 64 characters. **Expect** `client_01…` for the DCR client and the metadata URL for the CIMD one (e.g. `https://claude.ai/oauth/claude-code-client-metadata`). If a CIMD client's id is not its URL, no client can be trusted ([Trust a client](#trust-a-client)) and every client stays read only: note it and go on.

8. A negative check of CIMD: ask for a code for Claude's metadata URL with a redirect that is not in its document.

```powershell
$query = 'response_type=code&scope=openid&code_challenge_method=S256&code_challenge=' + ('a' * 43) +
  '&client_id=' + [uri]::EscapeDataString('https://claude.ai/oauth/mcp-oauth-client-metadata') +
  '&redirect_uri=' + [uri]::EscapeDataString('https://example.com/callback') +
  '&resource=' + [uri]::EscapeDataString('https://localhost:7180/mcp')
Start-Process "$($metadata.authorization_endpoint)?$query"
```

**Expect** an error from WorkOS, no sign-in page and no redirect to `example.com`. **Stop** and don't trust any client in production if WorkOS signs you in: anyone could then take tokens in a trusted client's name.

9. The precondition: a token of a self-registered client must never be an API bearer token. Once the custom AuthKit domain makes both issuers the same, only its `aud` keeps it out; the local API already has the AuthKit domain as its issuer, so the check holds for that case. Run it against a Release build of the API with the staging settings (`dotnet run -c Release`): a Debug build decodes bearer tokens without checking their audience (`DEV_AUTH`), so it could answer `200` where Release refuses. Register a client, get a token without `resource`, call a plain API route, then repeat with `$resource` set to the 2S application's audience (the `aud` of step 4):

```powershell
$redirect = 'http://127.0.0.1:8765/callback'
$client = Invoke-RestMethod -Method Post -Uri $metadata.registration_endpoint -ContentType 'application/json' -Body (@{
    client_name = '2S precondition check'; redirect_uris = @($redirect); token_endpoint_auth_method = 'none'
    grant_types = @('authorization_code'); response_types = @('code') } | ConvertTo-Json)
$verifier = -join (1..64 | ForEach-Object { [char](Get-Random -Minimum 97 -Maximum 123) })
$challenge = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$resource = ''   # the second time, from this line on: the aud of step 4
$query = "response_type=code&scope=openid&code_challenge_method=S256&code_challenge=$challenge&client_id=$($client.client_id)" +
  '&redirect_uri=' + [uri]::EscapeDataString($redirect) + $(if ($resource) { '&resource=' + [uri]::EscapeDataString($resource) })
Start-Process "$($metadata.authorization_endpoint)?$query"
# Sign in. The browser then fails to open 127.0.0.1:8765; copy the code from its address bar (?code=…).
$code = Read-Host 'code'
$token = Invoke-RestMethod -Method Post -Uri $metadata.token_endpoint -Body @{
  grant_type = 'authorization_code'; code = $code; redirect_uri = $redirect; client_id = $client.client_id; code_verifier = $verifier }
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $($token.access_token)" http://localhost:7071/api/v1/access/account
```

**Expect** `401` both times. A `200` means the gap is open: with the custom AuthKit domain, such a client would call the API directly with the person's full rights, with no `Via`, no `ReadOnly` and no deny list. Production is safe from it while the WorkOS API issues its user tokens (`authKitCustomDomain = false`), so go on, but record it on the follow-up of 424-302 (a client allow-list for AuthKit bearer tokens) and don't add the custom AuthKit domain ([later](#later-the-custom-authkit-domain)) until the API checks the client of its bearer tokens.

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

1. The merge of the implementation to `main` already ran the workflow **Platform.Storyteller.Mcp** (it changes the server's project and adds the workflow): its `test` and `image` jobs pushed `ghcr.io/akobr/2s-platform-mcp:<merge commit>`, and its `deploy` job waits for the approval of `prod.42.Platform`. **Reject** that deployment, because the Container App doesn't exist yet, and take `<commit>` from the run (the step summary of the `image` job names the image and its digest). Run the workflow by hand on `main` (**Actions → Platform.Storyteller.Mcp → Run workflow**) only if there is no such run or it failed; reject its deployment too.
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
param mcpBudgetStartDate = '<the first day of this month, e.g. 2026-12-01>'
```

`mcpBudgetStartDate` is the first month of the server's budget (phase 11): the month of this run, or a later one if the run may slip into it; never change it while the budget exists. Production ran this phase before the budget existed: phase 11, "Adding the budget", is its run.

The client lists stay as committed: `mcpUntrustedClientRestrictions = ['ReadOnly', 'NoSecretReveal']` and the three id lists empty, so every client reads, changes nothing and reveals no secret until one is trusted ([Trust a client](#trust-a-client)).

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
- `+ Microsoft.App/containerApps/prod-2s-mcp-ca`: the image of phase 3, `minReplicas` from `mcpMinReplicas` (0 by default), `maxReplicas` 3, `cooldownPeriod` 300, the identity, the 12 environment variables of the cookbook's step 17a, the three TCP probes, **no** `secrets` and no `customDomains`
- `+ Microsoft.Consumption/budgets/prod-2s-mcp-budget`: amount `mcpBudgetAmount` (1, or 12 with `mcpMinReplicas = 1`), `timeGrain` `Monthly`, the start date you set, the filter `MeterCategory` `In` `Azure Container Apps`, the notifications `actual50`, `actual100` and `forecasted100` to your email
- `+ Microsoft.Authorization/roleAssignments/<guid>`: Container Apps Contributor (`358470bc-…`) for the deployment identity, scoped to `prod-2s-mcp-ca`
- `~ Microsoft.Web/sites/prod-2s-api-fa/config/appsettings`: `Auth__TokenExchange__Enabled` `false` → `true`, `+ Auth__TokenExchange__AdditionalIssuers__0__Issuer` (`https://palatial-omen-84.authkit.app`) and `__JwksUri` (`…/oauth2/jwks`), `+ Auth__TokenExchange__Actors__0__Id` (`mcp`), `+ …__Authentication__WorkloadIdentity__Issuer` (`https://sts.windows.net/<tenant>/`), `__Subject` (the identity's object id: what-if may show it as an expression, since the identity doesn't exist yet), `__Audience` (`api://<mcpExchangeAppId>`), `+ …__SubjectAudiences__0` (`https://mcp.2s.42for.net/mcp`), `+ …__UntrustedClientRestrictions__0` (`ReadOnly`) and `__1` (`NoSecretReveal`); no `…__AllowedClientIds__*`, `…__TrustedClientIds__*` or `Auth__TokenExchange__DeniedClientIds__*` while their lists are empty; every other setting unchanged.

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

The `POST` answers `401` with `WWW-Authenticate: Bearer resource_metadata="https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp"` (the first call after an idle period waits 10 to 30 seconds for a replica). The log, streamed while a replica runs, shows `The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: https://mcp.2s.42for.net/mcp.`, `Now listening on: http://[::]:8080` and `Hosting environment: Production`. Two warnings of ASP.NET Core's data protection about keys stored in the container are expected: the server protects nothing with them. A configuration error stops the container and names the setting (`az containerapp logs show … --type system` shows the restarts).

**Check the API:** it restarted with the new settings.

```powershell
curl.exe -s https://api.2s.42for.net/api/v1/auth/configuration
```

It answers as before, and the admin UI still signs in. Application Insights (`prod-2s-api-appi`, **Logs → traces**, the last 15 minutes) shows the API's information line that the actor `mcp` accepts a token of any client for `https://mcp.2s.42for.net/mcp` and that the clients outside its 0 `TrustedClientIds` get `ReadOnly` and `NoSecretReveal`, no warning about the actor, and no start failure. A warning that the actor `mcp` has no `AllowedClientIds` means the untrusted restrictions are missing or the API's build is older than 424-302: **stop**, and turn the exchange off ([10. Rolling back](#10-rolling-back)) until they are set.

**Restrict the exchange application to the server's identity** (recommended): only `prod-2s-mcp-mi` then gets tokens for `api://<id>` from Entra; the API pins its object id anyway.

```powershell
$exchange = ./scripts/New-TokenExchangeApplication.ps1 -McpIdentityName prod-2s-mcp-mi
az ad sp show --id $exchange.ClientId --query appRoleAssignmentRequired   # true
```

From then on, a new or re-created `prod-2s-mcp-mi` (a new object id) gets no token for the application until this command runs again for it; the script warns about it when it runs without `-McpIdentityName`.

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
# Resolve-DnsName has no CAA type: ask a public resolver over HTTPS (an empty answer means no CAA record), or look in the zone.
foreach ($name in 'mcp.2s.42for.net', '2s.42for.net', '42for.net') { $name; (Invoke-RestMethod "https://dns.google/resolve?name=$name&type=CAA").Answer.data }
```

A certificate authority uses the CAA records of the closest of the three names that has any. If there are such records, one must allow `digicert.com` (`0 issue "digicert.com"`), the authority of the managed certificates; no CAA record at all allows every authority. The API's managed certificate (`api.2s.42for.net`) comes from the same authority, so it is likely fine already.

## 6. The custom domain [S4]

In `parameters/prod.bicepparam`:

```bicep
param mcpAppExists = true
param mcpCustomDomain = true
```

`mcpAppExists` makes every later run keep the image that the workflow deployed (and the bound certificate); the template refuses `mcpCustomDomain` without it. `mcpCustomDomain` puts `mcp.2s.42for.net` on the app with the binding type `Auto` and creates the environment's free managed certificate, which the platform binds as soon as it is issued. Commit through a pull request, pull `main`, then the what-if of phase 4.

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
2. **Resource indicator:** add `https://mcp.2s.42for.net/mcp`. Make it the default only if phase 1 showed that a default doesn't change the admin UI's `aud`. Without the default, only MCP clients that send the `resource` parameter (RFC 8707) can sign in; a client that omits it gets the environment's client id as `aud`, which the server refuses. The MCP authorization spec requires `resource`, and Claude Code sends it.
3. Nothing else: no permission, no change of the JWT template, no new application.

**Check:** sign out of the admin UI, sign in again, and check that it loads your organizations (its `aud` is unchanged).

## 8. GitHub environment and the first deployment by the workflow [S7]

```powershell
./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId '<the deployment app id>' -BaseAccountId '<user_01…>'
gh variable list --repo akobr/mono.me --env prod.42.Platform | Select-String AZURE_MCP_APP_NAME   # prod-2s-mcp-ca
```

The script takes `AZURE_MCP_APP_NAME` from the output `mcpAppName` of `prod-2s-main` (or `-McpAppName`) and leaves the other variables as they are when their parameter is empty.

Run the workflow **Platform.Storyteller.Mcp** by hand on `main` and **approve** the `deploy` job. The run reuses the image of its commit if one exists (a commit is built once, so its tag never moves), and the deployment runs it by its digest: `az containerapp update --image ghcr.io/akobr/2s-platform-mcp@sha256:… --revision-suffix gh<run>-<attempt> --no-wait`. It waits until that revision is provisioned and the latest ready one, and reads the resource metadata from `https://mcp.2s.42for.net`. **Check** that the job is green and the revision runs the digest of the step summary:

```powershell
az containerapp revision list -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query "[].{name:name, active:properties.active, image:properties.template.containers[0].image}" -o table
```

From now on every merge to `main` that changes the server builds an image and waits for your approval to run it. If the check after the update fails, the job tries to put back the image of the revision that served before (a new revision) and stays red either way. That rollback revision may also not become ready within its 10 minutes, and then the previous image is not confirmed: the step **Roll back to the previous image** of the job says which, and the revision list above shows what runs.

**Don't run `main.bicep` while a deployment of the workflow runs or waits for approval.** The template reads the running image and writes it back, so a run that overlaps an update can put the earlier image back. Let the `deploy` job finish (or reject it) first; after a run that may have overlapped, check the image with the revision list above and run the workflow again if needed. Deployments queue one at a time, and a newer one that waits replaces an older waiting one: approve the newest and reject older ones, or an older image could follow a newer one.

If the job fails with `AuthorizationFailed` on `Microsoft.App/containerApps/write`, the role assignment of phase 4 hasn't reached the identity yet (wait a few minutes and re-run the job), or Container Apps Contributor doesn't cover the write: then change `containerAppsContributor` in `modules/access.bicep` to Contributor (`b24988ac-6180-42a0-ab88-20f7382dd24c`), still scoped to the app, and run `main.bicep`.

## 9. Verify [S11]

| Check | How | Expected |
| --- | --- | --- |
| Sign-in | Add `https://mcp.2s.42for.net/mcp` as a remote MCP server in an MCP client (e.g. `claude mcp add --transport http 2s https://mcp.2s.42for.net/mcp`), sign in with your production account | the WorkOS sign-in page of `palatial-omen-84.authkit.app`, then the client lists the tools: the read tools only, since no client is trusted yet |
| The token | the client's token, decoded locally | `iss` the AuthKit domain, `aud` `https://mcp.2s.42for.net/mcp`, `sub` your `user_…`, `scope` with `openid` |
| The exchange | the tool `get_my_account` | your account |
| Your data | list your organizations and a project's annotations | the same as in the admin UI |
| No account | sign in with a WorkOS user that has no Storyteller account | "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." |
| Read only | ask the assistant to change one annotation | nothing changes: the assistant has no write tool, or the call answers "The 2S API lets this client app only read for you, so the change was not made. …" (`ActorReadOnly`) |
| The tool list | connect the client while the API is idle (no call for a while) | the list comes within about 5 seconds: `tools/list` waits that long for the person's token, then lists the write tools anyway and logs "tools/list: the Storyteller token for the person took longer than 5 seconds; …" (the API refuses a write of a read-only client in any case). It is one exchange per sign-in token, cached for the calls that follow |
| Read only at the API | the same change sent to the API itself, past the MCP server: take a read-only Storyteller token from the exchange (staging, the actor's credential) and `PUT` one annotation with it | `403` with `ErrorCode` `ActorReadOnly`; hiding a tool in the MCP server proves nothing about the API |
| The client | the API's Application Insights (`prod-2s-api-appi`, **Logs**): `traces \| where message has "via mcp (client" \| project timestamp, message \| take 20` | every request of the assistant: `A request … via mcp (client <id>) answered …`, with `client_01…` for a dynamically registered client or the metadata URL for one with CIMD |
| Audit | once a client is trusted ([Trust a client](#trust-a-client)): an access change through it, e.g. `create_machine` and then `delete_machine` of a test machine, then the access audit trail in the admin UI | your account `via mcp (client <id>)` |
| Secrets | ask for a secret value | refused (`NoSecretReveal`), and the secret tools are not listed |
| No passthrough | send the MCP token to `GET https://api.2s.42for.net/api/v1/access/account` | `401` |
| Logs | Log Analytics `prod-2s-log`: `ContainerAppConsoleLogs \| where ContainerAppName == "prod-2s-mcp-ca" \| take 50`, and the API's Application Insights | the exchange logged with the actor `mcp` and the client, no token or body in any log |
| Scale to zero (only with `mcpMinReplicas = 0`) | after 10 minutes without calls: `az containerapp replica list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table` | no replica |
| The budget | `az rest --url "https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Consumption/budgets/prod-2s-mcp-budget?api-version=2026-06-01" --query "properties.{amount:amount, start:timePeriod.startDate, spent:currentSpend.amount, filter:filter.dimensions}" -o json` (`az rest` fills in the subscription) | amount `mcpBudgetAmount` (12 in production), your start date, spent a few USD at most by mid-month with the replica always on (`0` with scale to zero; a few cents: see the Cost row), the filter `MeterCategory` `In` `Azure Container Apps` |
| Cost | after a few days: Cost Management → Cost analysis, scope the subscription, group by **Service name** (the meter category), the server's resources only | `Azure Container Apps`: with the replica always on, a cost that grows by about 0.15–0.20 USD a day once the grant is used up; with scale to zero, 0 USD. If its usage shows under another meter category, the budget's filter in `main.bicep` must name that one. No row yet (free usage may not be listed) leaves the filter unconfirmed: phase 11 checks it again at the end of the month. A few cents while the server is far inside the grant (phase 11, step 1) mean Cost Management prices the free usage during the month: no misconfiguration, see phase 11, "A cost although the replica-minutes are inside the grant" |

A request through the ingress ends after 240 seconds: a tool call that waits longer for the API is cut off, and the assistant checks a write with the matching read.

## 10. Rolling back

- **A bad image:** `az containerapp update -g prod-2s-eusec-rg -n prod-2s-mcp-ca --image <the image of an earlier revision>` (the revision list of phase 8 has their digests; `ghcr.io/akobr/2s-platform-mcp:<previous commit>` is the same image, since a commit's tag never moves). A later run of `main.bicep` keeps that image.
- **The server unreachable at once, everything kept:** `az containerapp ingress disable -g prod-2s-eusec-rg -n prod-2s-mcp-ca`. The next run of `main.bicep` turns the ingress on again, so for longer use the next point.
- **The exchange off in the API:** set `mcpEnabled = false` in `parameters/prod.bicepparam`, pull request, what-if (only `prod-2s-api-fa/appsettings`: `Auth__TokenExchange__Enabled` → `false`, the `AdditionalIssuers__0__*`, `Actors__0__*` and `DeniedClientIds__*` settings removed), then create: every MCP call answers that the exchange is off. The identity, the environment, the app, its certificate, the role assignment, the budget and the exchange application stay (a template run doesn't delete them). Delete them by hand if wanted: `az containerapp delete`, `az containerapp env delete`, `az identity delete -n prod-2s-mcp-mi`, `az consumption budget delete --budget-name prod-2s-mcp-budget`, the role assignment (`az role assignment list --all --assignee <deployment principal id>`), `az ad app delete --id <mcpExchangeAppId>`, the DNS records and the GitHub variable `AZURE_MCP_APP_NAME`; then set `mcpAppExists = false`, `mcpCustomDomain = false` and, if the budget was deleted, `mcpBudgetStartDate = ''`: a new budget can't start in a past month, so the run that turns the server on again sets the month of that run (phase 4). If the exchange application stays while the identity goes, a later identity needs phase 4's `New-TokenExchangeApplication.ps1 -McpIdentityName prod-2s-mcp-mi` again, because the application requires an assignment.
- **The custom domain off:** `mcpCustomDomain = false` and a run of `main.bicep` removes the binding; the certificate stays in the environment until it is deleted (`az containerapp env certificate delete`). This takes the server out of service: its resource URL and the API's subject audience stay `https://mcp.2s.42for.net/mcp`, so clients can't sign in, and the workflow's last check fails until the domain is bound again. To stop the server, use one of the two points above.
- **The API rolled back past 424-302:** an API build from before it ignores the client lists, so every MCP client could change anything again and a denied client could sign in again, and it refuses the tokens that carry `ReadOnly`. Turn the exchange off first (`mcpEnabled = false`, the point above), then roll the API back; turn the exchange on again only with a build that knows `ReadOnly`.

## Trust a client

Every MCP client outside `mcpTrustedClientIds` only reads and reveals no secret (`mcpUntrustedClientRestrictions`, [decisions](decisions.md#the-mcp-server)). A trusted client changes whatever the person's roles allow, and still reads no secret while the actor withholds them (`NoSecretReveal`, the default). Trust a client only when all of these hold:

- It has a Client ID Metadata Document (CIMD) whose redirects are all web addresses (`https://…`), such as Claude's `https://claude.ai/oauth/mcp-oauth-client-metadata`. Never one whose document lists a loopback redirect (`http://localhost…`, `http://127.0.0.1…`), such as Claude Code's or VS Code's: any local process can present its URL with that redirect.
- Its id is never a dynamically registered one (`client_01…`): that id is random per registration, and anyone can register a client with the same name.
- CIMD is on in WorkOS production (**Connect → Configuration**), and the checks 6 and 8 of [phase 1](#1-workos-staging-check-s6) pass against production (`$authkit = 'https://palatial-omen-84.authkit.app'`, the `resource` `https://mcp.2s.42for.net/mcp`).

1. Sign in through the client and call a read tool, e.g. `get_my_account`.
2. Copy the id the API recorded, never one read from a decoded token: the API reads `azp`, then `appid`, then `client_id`, so a token with more than one of them is matched on the first. Run the query of the check **The client** of [phase 9](#9-verify-s11); the request lines end with `via mcp (client <id>) answered <status>`. Check the document behind the id:

```powershell
Invoke-RestMethod '<id>' | Select-Object client_id, redirect_uris   # the same client_id, only https:// redirects
```

3. In `parameters/prod.bicepparam`, exactly as recorded (the trust list compares case):

```bicep
param mcpTrustedClientIds = ['<id>']
```

Pull request, what-if (only `prod-2s-api-fa/appsettings`: `+ Auth__TokenExchange__Actors__0__TrustedClientIds__0`), then create. The Function App restarts with the new settings. The client's tokens of before keep `ReadOnly` until they expire, so it may take up to 10 minutes before it can change anything; then run the check **Audit** of phase 9.

To trust it no longer, remove the entry and run the template the same way: once the API runs with the new settings, the client's live tokens get `ReadOnly` again.

## Cut off one client

For a client with a stable id: a CIMD metadata URL. Not for a dynamically registered client: it registers again under a new id, so use [Cut off every unlisted client](#cut-off-every-unlisted-client). While the list has an entry, a sign-in that names no client, and a Storyteller token issued before the API recorded clients, is refused too: it cannot show that it is not the blocked client.

1. Find the id the API recorded (step 2 of [Trust a client](#trust-a-client)).
2. In `parameters/prod.bicepparam` (compared ignoring case; it wins over `mcpAllowedClientIds` and `mcpTrustedClientIds`):

```bicep
param mcpDeniedClientIds = ['<id>']
```

Pull request, what-if (only `prod-2s-api-fa/appsettings`: `+ Auth__TokenExchange__DeniedClientIds__0`), then create. The Function App restarts, and the client's live tokens end with it: the API answers them `401` and refuses its exchanges with "The client app the person signed in with is blocked for this deployment. Use another app, sform or the admin UI." The MCP server forgets a token the API refuses, so the person sees that text from the next exchange on.

**In an emergency**, set the app setting by hand first, then commit the same entry before the next run of `main.bicep`: the template removes settings set by hand, and a run without the entry lets the client in again.

```powershell
# <n>: the next index, 0 while mcpDeniedClientIds is empty.
az functionapp config appsettings set -g prod-2s-eusec-rg -n prod-2s-api-fa --settings "Auth__TokenExchange__DeniedClientIds__<n>=<id>" --output none
```

## Cut off every unlisted client

The switch for dynamically registered clients, whose ids are random: list the clients that may sign in, and the exchange refuses every other one, also a sign-in that names no client. In `parameters/prod.bicepparam`, set the list to the trusted clients:

```bicep
param mcpAllowedClientIds = ['<each id of mcpTrustedClientIds>']
```

Pull request, what-if (only `prod-2s-api-fa/appsettings`: `+ Auth__TokenExchange__Actors__0__AllowedClientIds__<n>`), then create. From the restart on, the API refuses the exchange of every other client; the API checks this list at the exchange only, so the tokens such a client already has run out within 10 minutes (`TokenLifetimeMinutes`) plus the clock skew (`MachineAuth:Federation:ClockSkewSeconds`, 30 seconds). With no trusted client there is nothing to list: turn the exchange off instead ([10. Rolling back](#10-rolling-back)).

Turning dynamic client registration off in WorkOS production does the same for the whole environment, and also locks out Cursor and every other client without CIMD. Access tokens WorkOS issued before stay valid until they expire.

## 11. The cost of the MCP server

What the server costs depends on `mcpMinReplicas` (the [decisions](decisions.md#the-mcp-server) have the whole range; the spec is [Cost Range and Budget of the MCP Server](specs/2026-11-22%20Cost%20Range%20and%20Budget%20of%20the%20MCP%20Server.md)):

- **1, as in production:** one replica always runs, at the idle rate between calls: about 4–6 USD a month, up to about 14 USD if it were busy all the time. Scanners cost almost nothing more; the replica is up anyway.
- **0:** nothing while it is warm for less than about 200 hours a month, the monthly free grant of Container Apps at 0.25 vCPU and 0.5 GiB, per subscription. Every request wakes a replica for about 5.5 minutes, a `401` or a scanner's request too, and each warm hour past the grant costs about 0.027 USD.

Either way, three replicas busy all month cost about 53 USD of compute, plus 0.40 USD per million requests past the grant's 2 million, which no setting caps.

**The budget** `prod-2s-mcp-budget` holds the subscription's Container Apps cost. It emails you at 50 % and 100 % of `mcpBudgetAmount` (in the billing currency) and when the month's forecast reaches it. Production sets 12, so an email means more than the always-on replica's idle cost (6 USD at 50 %); with scale to zero the default 1 fits, and the first email means the free grant is used up. Cost data of a pay-as-you-go subscription can come up to three days late, and budgets are checked once a day: an email tells about the past few days, not the last hour.

**Adding the budget** to the running server (production, after 424-305): `mcpBudgetStartDate` (the first day of the month of the run, or a later month) and `mcpBudgetAmount = 12` are in `parameters/prod.bicepparam`; if the run happens in a later month than the committed date, set that month first. Pull `main` and run the what-if of phase 4. **Expect** `+ Microsoft.Consumption/budgets/prod-2s-mcp-budget` as in phase 4, and possibly `~ Microsoft.App/containerApps/prod-2s-mcp-ca` with `template.scale.cooldownPeriod` `300` (the platform's default written out; it makes a new revision with the same image); nothing else beyond the usual noise. Don't run it while a deployment of the workflow runs or waits for approval (phase 8). Then the create, and the checks **The budget** and **Cost** of phase 9 after a few days: with the replica always on, the server's cost shows in Cost analysis within the month, which confirms the budget's filter.

**Without an email**: with scale to zero, at the end of the first month or two, run step 1 below. More than 12,000 replica-minutes in a month without an email means the budget's filter doesn't match: check the server's meter category in Cost analysis (phase 9, the Cost row) and fix the filter in `main.bicep`. With the replica always on, a Container Apps cost in Cost analysis while the budget's `spent` (phase 9) stays 0 means the same.

When an email comes:

1. **How warm the server was this month.** The platform metric `Replicas`, read per minute, needs no setting of the app (Azure Monitor keeps platform metrics for 93 days at no cost). A minute without data had no replica, so the count is right whether the metric reports zeros or nothing. One call per day of the month:

   ```powershell
   $app = az containerapp show -g prod-2s-eusec-rg -n prod-2s-mcp-ca --query id -o tsv
   $now = (Get-Date).ToUniversalTime()
   $minutes = 0; $peak = 0; $multi = 0
   for ($day = [datetime]::new($now.Year, $now.Month, 1, 0, 0, 0, [DateTimeKind]::Utc); $day -lt $now; $day = $day.AddDays(1)) {
     $maxima = az monitor metrics list --resource $app --metric Replicas --aggregation Maximum --interval PT1M `
       --start-time $day.ToString('o') --end-time $day.AddDays(1).ToString('o') `
       --query 'value[0].timeseries[0].data[].maximum' -o json | ConvertFrom-Json
     $minutes += ($maxima | Where-Object { $_ } | Measure-Object -Sum).Sum
     $peak = [math]::Max($peak, [double](($maxima | Where-Object { $_ } | Measure-Object -Maximum).Maximum))
     $multi += @($maxima | Where-Object { $_ -ge 2 }).Count
   }
   '{0:N0} replica-minutes this month (the free grant: 12,000 a month, about 400 a day; one replica always on: 1,440 a day); at most {1} replicas, {2:N0} minutes with 2 or more' -f $minutes, $peak, $multi
   ```

2. **Who wakes it.** The requests of the last 24 hours by status class, and in how many of those hours each came:

   ```powershell
   $series = az monitor metrics list --resource $app --metric Requests --aggregation Total --interval PT1H `
     --start-time $now.AddDays(-1).ToString('o') --end-time $now.ToString('o') --filter "statusCodeCategory eq '*'" `
     --query 'value[0].timeseries' -o json | ConvertFrom-Json
   $series | ForEach-Object {
     [pscustomobject]@{ Status = $_.metadatavalues[0].value; Requests = ($_.data.total | Measure-Object -Sum).Sum; Hours = @($_.data | Where-Object total).Count }
   }
   ```

   With the replica always on, the requests tell how busy it was. With scale to zero, `4xx` in most hours of the day is scanner traffic (`401` without a token, `404` for other paths); `2xx` is people using the server.

3. **What to change**, in `parameters/prod.bicepparam` through a pull request, then the what-if and the create of phase 4 (the what-if shows the changed value on `prod-2s-mcp-ca` or on the budget):
   - **Busy with the replica always on** (many `2xx` in most hours, or minutes with 2 or more replicas): the server doing its work; raise `mcpBudgetAmount` to what a month may cost, or lower `mcpMaxReplicas`. Scanners alone don't make it busy, and `mcpCooldownSeconds` does nothing with `mcpMinReplicas = 1`.
   - **With scale to zero: scanners, and more than about 400 replica-minutes a day:** `param mcpCooldownSeconds = 120` (about 2.5 minutes warm per request instead of 5.5) or `60` (a request every 5 minutes then still fits the grant). The price is a cold start of 10 to 30 seconds after every pause of a session that is longer than the cool-down. Check it once after the run: one `curl.exe -s -o NUL -X POST https://mcp.2s.42for.net/mcp`, then `az containerapp replica list -g prod-2s-eusec-rg -n prod-2s-mcp-ca -o table` every half minute until no replica is left: about the cool-down plus up to half a minute.
   - **With scale to zero: people's use past the grant:** the server doing its work. Raise `mcpBudgetAmount` to what a month may cost.
   - **With scale to zero: a cost although the replica-minutes are inside the grant** (and no other Container App runs in the subscription): Cost Management prices the free usage during the month, and only the invoice subtracts the grant. Set `mcpBudgetAmount = 11`: the grant is worth about 5.40 USD at Sweden Central's prices, so the first email, at 50 % (5.50 USD), then comes just past it.
   - **Two or three replicas for long** (step 1 shows at most 2 or 3 replicas and many minutes with 2 or more): more than 20 concurrent requests at a time, which the cool-down doesn't govern; lower `mcpMaxReplicas`.
   - **At once:** `az containerapp ingress disable` (phase 10) stops every request, and the server with it.

If scanners stay the cost even with a short cool-down, the next steps are rule-based routing of the environment (a template change), or `mcpMinReplicas = 1`, as production runs (decisions, "The MCP server").

## Later: the custom AuthKit domain

When [424-247](https://linear.app/424fun/issue/424-247/add-the-custom-authkit-domain-auth42fornet-before-the-first-customer) sets `authKitDomain = 'auth.42for.net'` and `authKitCustomDomain = true`, the same run of `main.bicep` moves the server's issuer (`Mcp__Authentication__Issuer`) to `https://auth.42for.net` and removes the API's additional issuer, which would then equal `Auth__AuthKit__Issuer` (the API refuses to start with the same domain in both). Its what-if shows both changes. MCP clients find the new authorization server in the resource metadata and register again; people sign in once more. Dynamic client registration and the resource indicator are settings of the WorkOS environment and stay.

With the custom domain both issuers are the same, and only `aud` keeps the token of a self-registered client off the API's routes ([424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow)). Switch only if the check 9 of [phase 1](#1-workos-staging-check-s6) answered `401` both times, and run it again against production right after the switch (`$authkit = 'https://auth.42for.net'`, the route `https://api.2s.42for.net/api/v1/access/account`). A `200` there means any self-registered client can call the API with the person's full rights: set `authKitCustomDomain = false` again until the API checks the client of its bearer tokens.

The API then checks MCP tokens with the keys of its own issuer, `https://api.workos.com/sso/jwks/<client id>`, not with the AuthKit domain's `…/oauth2/jwks`, which signs them: there is no setting for a second key set of the same issuer. On 2026-11-20 both URLs of production published the same single key (`sso_oidc_key_pair_01M3MMVAKJ4P2WW5CJ2B4D1T4E`). Compare the `kid` values of both again right before the switch, and after it run the check **The exchange** of phase 9 (`get_my_account` through the MCP server). If the keys differ, the switch needs a change of the API first.
