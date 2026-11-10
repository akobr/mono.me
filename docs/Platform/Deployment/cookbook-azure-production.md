# Cookbook: Create the Production Environment of the 2S Platform in Azure

This cookbook builds the production environment of the 2S Platform by hand, step by step, with the Azure CLI. It follows the spec [Azure Production Environment of the 2S Platform](specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md) and the answers in [Decisions That Cannot Be Changed Later](decisions.md). Steps 1–15 are also Bicep templates and scripts in [`src/Platform/Deployment`](../../../src/Platform/Deployment/README.md) (spec, section 2); this cookbook stays the explanation of every setting and the checklist, and steps 16–17 are done by hand either way. Build a new environment with one of the two, not a mix: the templates name the Flex plan `prod-2s-api-asp` and turn on site-scoped certificates when they create the Function App, which an app created by the CLI can't get later.

Work through the steps in order. Each step says what it creates, the commands, and how to check it. Steps marked **once** are never repeated; steps marked **irreversible** can't be undone without losing data or breaking clients.

## 0. Before you start

### What you need

| Need | Why |
| --- | --- |
| **Owner** of the subscription `prod-42fornet-sub`, and **Application Administrator** (or Global Administrator) in its Entra tenant | resources, role assignments, app registrations |
| Azure CLI 2.71 or newer (`az version`), logged in with `az login` | every command below; Flex Consumption options are in preview in the CLI |
| .NET 10 SDK (10.0.401+), Node.js 24 (the admin UI's `.nvmrc`), `npm` | building the API, DbCreator and the admin UI |
| Access to the DNS zone of `42for.net` | the records of step 12 |
| The WorkOS dashboard, **production** environment | step 13 |
| Sentry (EU, `de.sentry.io`) and PostHog (EU cloud) | step 14 |
| Admin of the GitHub repository `akobr/mono.me` | step 15 |

### Blockers in the code

These must be merged to `main` before step 16 (the first deployment of the code). The infrastructure (steps 1–15) can be built before.

- **[424-230](https://linear.app/424fun/issue/424-230/store-all-organizations-in-one-shared-cosmos-db-container-remove-the)**: one shared container `organizations` instead of a container per organization. **DbCreator must not run against the production account before it is merged**: the old code would create `org.*` containers, which the new code refuses.
- **The setting `MachineAuth:DisabledCredentialKinds`** (branch `feat/claude/azure-deployment`, spec [Turn Off Machine Credential Kinds per Deployment](../Storyteller/specs/2026-11-09%20Turn%20Off%20Machine%20Credential%20Kinds%20per%20Deployment.md)): production turns off the certificate machine kinds until mTLS exists.

### Names and values

Every name below was decided on 2026-11-09. Paste this block into the shell you run the steps in (bash; in PowerShell use `$NAME = "…"`):

```sh
SUBSCRIPTION="prod-42fornet-sub"
LOCATION="swedencentral"
RG="prod-2s-eusec-rg"

COSMOS="prod-2s-42fornet-cdb"
KV="prod-2s-42fornet-kv"
SA="prod2s42fornetsa"              # storage: lower-case letters and digits only
MI="prod-2s-api-mi"
FUNC="prod-2s-api-fa"
LAW="prod-2s-log"
APPI="prod-2s-api-appi"
SWA="prod-2s-app-swa"

API_HOST="api.2s.42for.net"
APP_HOST="app.2s.42for.net"
AUTH_HOST="auth.42for.net"
TRUST_DOMAIN="2s.42for.net"

OWNER_EMAIL="<owner email for the budget alerts>"
GITHUB_REPO="akobr/mono.me"
TAGS="product=2s-platform environment=prod managedBy=manual"
```

### Never do

- **Never delete or purge the Key Vault or one of its keys.** Every secret of a project is encrypted with that project's key in this vault; losing the key loses the secrets. Purge protection and a lock prevent it (step 7).
- **Never delete the Cosmos DB account.** The free tier can only be chosen when an account is created, and there is one per subscription (step 5).
- **Never run the Cosmos DB test suites against the production account**: they delete every database of the account they use.
- **Never copy production data to a development machine** (decision 15): the `core` container holds the signing key of Storyteller tokens and the machine certificate authority.
- **Never change `MachineAuth:TrustDomain` or the API domain after the first machine is created**: it rejects every token and breaks every federated machine.

## 1. Subscription (once)

1. Upgrade the free account to pay-as-you-go in the portal (**Subscriptions → the subscription → Upgrade**). The remaining credit and the 12-month free services stay. The upgrade lifts the Flex Consumption quota of free trials (15 cores) to the standard 250 cores.
2. Rename it to `prod-42fornet-sub` (**Subscriptions → Rename**).
3. Select it and register the resource providers:

```sh
az account set --subscription "$SUBSCRIPTION"
for p in Microsoft.Web Microsoft.App Microsoft.DocumentDB Microsoft.KeyVault Microsoft.Storage \
         Microsoft.Insights Microsoft.OperationalInsights Microsoft.ManagedIdentity Microsoft.Consumption; do
  az provider register --namespace "$p"
done
az provider list --query "[?registrationState!='Registered' && contains(namespace,'Microsoft.')].namespace" -o tsv | grep -E "Web|App|DocumentDB|KeyVault|Storage|Insights|OperationalInsights|ManagedIdentity|Consumption" || echo "all registered"
```

4. Check that Flex Consumption is offered in Sweden Central:

```sh
az functionapp list-flexconsumption-locations --query "[?name=='swedencentral'].name" -o tsv
```

It must print `swedencentral`. If it doesn't, stop: the region decision has to be revisited before anything else is created.

**Note:** 2S takes the subscription's only free-tier Cosmos DB account and its Flex Consumption free grant. Another 42for.net product in this subscription gets a paid Cosmos DB account, or a database in this one (decisions, open points).

## 2. Resource group and budget

```sh
az group create --name "$RG" --location "$LOCATION" --tags $TAGS
```

The budget (5 USD a month, alerts at 50, 80 and 100 % of the actual cost) is easiest in the portal: **Cost Management → Budgets → Add**, scope the subscription, amount 5, monthly, three alert conditions to `$OWNER_EMAIL`. Add one more alert at 100 % of the *forecasted* cost.

Check: `az group show --name "$RG" --query properties.provisioningState -o tsv` prints `Succeeded`.

## 3. User-assigned managed identity

The API's only identity. It reads the vault, the storage and (for BYOK) signs in to customers' tenants through a federated credential.

```sh
az identity create --resource-group "$RG" --name "$MI" --location "$LOCATION" --tags $TAGS
MI_ID=$(az identity show -g "$RG" -n "$MI" --query id -o tsv)
MI_CLIENT_ID=$(az identity show -g "$RG" -n "$MI" --query clientId -o tsv)
MI_PRINCIPAL_ID=$(az identity show -g "$RG" -n "$MI" --query principalId -o tsv)
echo "$MI_CLIENT_ID $MI_PRINCIPAL_ID"
```

Keep the three values; later steps use them.

## 4. Log Analytics and Application Insights

```sh
az monitor log-analytics workspace create --resource-group "$RG" --workspace-name "$LAW" \
  --location "$LOCATION" --retention-time 30 --quota 0.15 --tags $TAGS
LAW_ID=$(az monitor log-analytics workspace show -g "$RG" -n "$LAW" --query id -o tsv)

az monitor app-insights component create --app "$APPI" --resource-group "$RG" --location "$LOCATION" \
  --workspace "$LAW_ID" --kind web --application-type web --tags $TAGS
APPI_CS=$(az monitor app-insights component show -g "$RG" -a "$APPI" --query connectionString -o tsv)
```

`--quota 0.15` is the daily cap in GB, inside the 5 GB a month that Log Analytics gives for free. An availability test is **not** created (it is charged); Sentry's uptime monitor watches the API instead (step 14).

## 5. Cosmos DB (once, irreversible)

The free tier is chosen here and only here: **an account created without it can never get it**, and the subscription allows one free-tier account. Create no other Cosmos DB account in this subscription before this one.

```sh
az cosmosdb create --resource-group "$RG" --name "$COSMOS" \
  --kind GlobalDocumentDB \
  --enable-free-tier true \
  --locations regionName="$LOCATION" failoverPriority=0 isZoneRedundant=false \
  --default-consistency-level Session \
  --backup-policy-type Continuous --continuous-tier Continuous7Days \
  --minimal-tls-version Tls12 \
  --tags $TAGS

az lock create --name "do-not-delete" --lock-type CanNotDelete --resource-group "$RG" \
  --resource-name "$COSMOS" --resource-type Microsoft.DocumentDB/databaseAccounts
```

- Provisioned throughput (the default capacity mode): the free tier does not apply to serverless.
- One region, no availability zones (zone redundancy is charged).
- Continuous backup with the 7-day tier is free.
- **No database is created here.** DbCreator creates the database `42.Platform.2S` with shared autoscale up to 1000 RU/s, and the containers `core` and `organizations` (step 16).

Check the free tier:

```sh
az cosmosdb show -g "$RG" -n "$COSMOS" --query "{freeTier:enableFreeTier, backup:backupPolicy.type, region:locations[0].locationName}" -o table
```

### 5a. Run the hierarchical partition key probe (once, before DbCreator)

While the account has no database yet, run the probe of [the cookbook of the probe](../Storyteller/cookbooks/hierarchical-partition-key-probe.md) (run 4). The probe (`src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs`) and its cookbook come with [424-230](https://linear.app/424fun/issue/424-230/store-all-organizations-in-one-shared-cosmos-db-container-remove-the): until it is merged, run the commands below from a checkout of the branch `feat/424-230`, e.g. a worktree next to the repository:

```sh
git fetch origin feat/424-230
git worktree add ../mono.me-hpk-probe origin/feat/424-230
cd ../mono.me-hpk-probe        # afterwards: cd - && git worktree remove ../mono.me-hpk-probe
```

 Its database is then the only one, so its 1000 RU/s stay inside the free tier, and it deletes it at the end.

```sh
KEY=$(az cosmosdb keys list -g "$RG" -n "$COSMOS" --query primaryMasterKey -o tsv)
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- \
  --endpoint "https://$COSMOS.documents.azure.com:443/" --key "$KEY" --label "Azure, Sweden Central"
unset KEY
```

Expect `19 passed, 0 failed`. Save the output and attach it to [424-230](https://linear.app/424fun/issue/424-230/store-all-organizations-in-one-shared-cosmos-db-container-remove-the): it gives the real request charges.

## 6. Storage account

The Functions host (timers, leases, the deployment package) and the export and import of views (blob container and queue `configuration-transfers`) use it. The app reaches it with the managed identity only.

```sh
az storage account create --resource-group "$RG" --name "$SA" --location "$LOCATION" \
  --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 \
  --allow-blob-public-access false --https-only true --tags $TAGS
SA_ID=$(az storage account show -g "$RG" -n "$SA" --query id -o tsv)

# the identity of the API, and you (to create the container with your Entra sign-in)
for role in "Storage Blob Data Owner" "Storage Queue Data Contributor" "Storage Table Data Contributor"; do
  az role assignment create --assignee-object-id "$MI_PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
    --role "$role" --scope "$SA_ID"
done
az role assignment create --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --role "Storage Blob Data Contributor" --scope "$SA_ID"

az storage container create --account-name "$SA" --name deployments --auth-mode login
```

Shared key access stays **on** until step 8 is finished: `az functionapp create` writes a connection string with the key while it creates the app. Step 8.4 switches the app to the identity and turns the key off.

## 7. Key Vault (once, irreversible)

One Premium vault for both the Standard keys (software) and the HSM keys of the projects. A Premium vault has no base fee; software keys cost the same as in Standard, and HSM keys cost only when a project allows the HSM level.

```sh
az keyvault create --resource-group "$RG" --name "$KV" --location "$LOCATION" \
  --sku premium --enable-rbac-authorization true \
  --enable-purge-protection true --retention-days 90 --tags $TAGS
KV_ID=$(az keyvault show -g "$RG" -n "$KV" --query id -o tsv)
KV_URI=$(az keyvault show -g "$RG" -n "$KV" --query properties.vaultUri -o tsv)

az lock create --name "do-not-delete" --lock-type CanNotDelete --resource-group "$RG" \
  --resource-name "$KV" --resource-type Microsoft.KeyVault/vaults

# the API: create keys and rotation policies, wrap and unwrap, read the secrets of its settings
for role in "Key Vault Crypto Officer" "Key Vault Crypto Service Encryption User" "Key Vault Secrets User"; do
  az role assignment create --assignee-object-id "$MI_PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
    --role "$role" --scope "$KV_ID"
done
# you
az role assignment create --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --role "Key Vault Administrator" --scope "$KV_ID"
```

**Purge protection can never be turned off** once it is on. That is the point.

Store the Cosmos DB connection string as a secret (the API connects with the account key for now; Entra RBAC for Cosmos DB is a later hardening):

```sh
COSMOS_CS=$(az cosmosdb keys list -g "$RG" -n "$COSMOS" --type connection-strings \
  --query "connectionStrings[?description=='Primary SQL Connection String'].connectionString | [0]" -o tsv)
az keyvault secret set --vault-name "$KV" --name cosmos-connection --value "$COSMOS_CS" --output none
unset COSMOS_CS
```

Check: `az keyvault show -n "$KV" --query "{purge:properties.enablePurgeProtection, rbac:properties.enableRbacAuthorization, sku:properties.sku.name}" -o table` prints `True True premium`.

**Rotating the Cosmos DB key.** The secret holds the account's primary key and has no expiry: an expired secret would break the API's Key Vault reference. To rotate, regenerate the primary key (`az cosmosdb keys regenerate -g "$RG" -n "$COSMOS" --key-kind primary`), write the new connection string to `cosmos-connection` at once (the commands above, or a run of `main.bicep`, which reads it from `listKeys()`), then restart the Function App (`az functionapp restart -g "$RG" -n "$FUNC"`), as Key Vault references are cached for up to a day. The API's Cosmos DB calls fail between the regeneration and the restart, so rotate in a quiet moment.

## 8. Function App (the API)

### 8.1 Create it on Flex Consumption

```sh
az functionapp create --resource-group "$RG" --name "$FUNC" \
  --flexconsumption-location "$LOCATION" \
  --runtime dotnet-isolated --runtime-version 10.0 \
  --instance-memory 512 --maximum-instance-count 10 \
  --storage-account "$SA" \
  --deployment-storage-name "$SA" --deployment-storage-container-name deployments \
  --deployment-storage-auth-type UserAssignedIdentity --deployment-storage-auth-value "$MI_ID" \
  --assign-identity "$MI_ID" \
  --app-insights "$APPI" \
  --https-only true --tags $TAGS
FUNC_ID=$(az functionapp show -g "$RG" -n "$FUNC" --query id -o tsv)
FUNC_HOST=$(az functionapp show -g "$RG" -n "$FUNC" --query defaultHostName -o tsv)
echo "$FUNC_HOST"
```

- 512 MB instances, at most 10 (a cap on cost), **no always-ready instance** (nothing is billed when idle; cold starts of a few seconds are accepted for the MVP).
- The CLI names the Flex plan itself. The Bicep templates later name it `prod-2s-api-asp`.
- Incoming client certificates stay **off** (the default): mTLS comes in a later phase.

### 8.2 Platform settings

```sh
az functionapp config set -g "$RG" -n "$FUNC" --min-tls-version 1.2 --ftps-state Disabled
az resource update --ids "$FUNC_ID/basicPublishingCredentialsPolicies/scm" --set properties.allow=false
az resource update --ids "$FUNC_ID/basicPublishingCredentialsPolicies/ftp" --set properties.allow=false
# Key Vault references resolve with the user-assigned identity
az resource update --ids "$FUNC_ID" --set properties.keyVaultReferenceIdentity="$MI_ID"
az functionapp cors add -g "$RG" -n "$FUNC" --allowed-origins "https://$APP_HOST"
```

### 8.3 Application settings

The values of step 13 (WorkOS) are needed for the `Auth__AuthKit__*` settings; fill them in once step 13 is done, or come back to this command.

```sh
AUTHKIT_CLIENT_ID="<WorkOS production client id, client_…>"
AUTHKIT_AUDIENCE="https://$TRUST_DOMAIN"        # the aud of the JWT template of step 13
BYOK_CLIENT_ID="<application id of '2S Platform Customer Keys', step 11>"

az functionapp config appsettings set -g "$RG" -n "$FUNC" --settings \
  AZURE_FUNCTIONS_ENVIRONMENT=Production \
  DOTNET_ENVIRONMENT=Production \
  AZURE_CLIENT_ID="$MI_CLIENT_ID" \
  "CosmosDb__Connection=@Microsoft.KeyVault(VaultName=$KV;SecretName=cosmos-connection)" \
  Auth__Provider=AuthKit \
  Auth__AuthKit__ClientId="$AUTHKIT_CLIENT_ID" \
  Auth__AuthKit__Issuer="https://$AUTH_HOST" \
  Auth__AuthKit__Audience="$AUTHKIT_AUDIENCE" \
  Auth__AuthKit__AuthKitDomain="https://$AUTH_HOST" \
  Auth__AuthKit__DefaultUserScopes__0=User.Impersonation \
  Auth__TokenExchange__Enabled=false \
  MachineAuth__DefaultCredentialKind=ApiKey \
  MachineAuth__DisabledCredentialKinds__0=Certificate \
  MachineAuth__DisabledCredentialKinds__1=CertificateAndApiKey \
  MachineAuth__IsCertificateHeaderFromClientAllowed=false \
  MachineAuth__TrustDomain="$TRUST_DOMAIN" \
  MachineAuth__Authority__Kind=Cosmos \
  MachineAuth__Federation__PublicBaseUrl="https://$API_HOST/api" \
  AzureKeyVaults__default="$KV_URI" \
  AzureKeyVaults__premium="$KV_URI" \
  KeyValues__Protection__Emulator=false \
  KeyValues__Protection__Byok__ClientId="$BYOK_CLIENT_ID" \
  KeyValues__Protection__Byok__ManagedIdentityClientId="$MI_CLIENT_ID" \
  APPLICATIONINSIGHTS_CONNECTION_STRING="$APPI_CS" \
  --output none
```

| Setting | Why |
| --- | --- |
| `Auth__AuthKit__Issuer` | must equal the `iss` of WorkOS tokens: the custom domain `https://auth.42for.net` |
| `Auth__AuthKit__Audience` | tokens must carry this `aud` (the JWT template, step 13). `https://2s.42for.net` is the proposal; confirm it before the first sign-in, see "Open points" |
| `Auth__AuthKit__DefaultUserScopes__0` | every signed-in user gets `User.Impersonation`, and their roles decide the rest; without it (and without a permission map) every user gets `401` |
| `Auth__TokenExchange__Enabled=false` | no MCP server in this phase |
| `MachineAuth__DisabledCredentialKinds__*` | no mTLS yet: projects can't enable certificate machines or issue shared certificates |
| `MachineAuth__IsCertificateHeaderFromClientAllowed=false` | the API never reads `X-ARR-ClientCert`, so nobody can fake a certificate with it |
| `MachineAuth__TrustDomain`, `__Federation__PublicBaseUrl` | the issuer and audience of Storyteller tokens, and the token endpoint that federation rules pin; **final** (decisions 1 and 2) |
| `AzureKeyVaults__default`, `__premium` | both vault names of the API point at the one Premium vault |

Invitation emails need the WorkOS management key. When you want them, store it and add the setting:

```sh
az keyvault secret set --vault-name "$KV" --name authkit-api-key --value "<sk_…>" --output none
az functionapp config appsettings set -g "$RG" -n "$FUNC" --output none \
  --settings "Auth__AuthKit__ApiKey=@Microsoft.KeyVault(VaultName=$KV;SecretName=authkit-api-key)"
```

### 8.4 Storage by identity, then the key off

```sh
az functionapp config appsettings set -g "$RG" -n "$FUNC" --output none --settings \
  AzureWebJobsStorage__accountName="$SA" \
  AzureWebJobsStorage__credential=managedidentity \
  AzureWebJobsStorage__clientId="$MI_CLIENT_ID"
az functionapp config appsettings delete -g "$RG" -n "$FUNC" --setting-names AzureWebJobsStorage
az storage account update -g "$RG" -n "$SA" --allow-shared-key-access false
```

`Backend.AzureStorage` reads the same `AzureWebJobsStorage` section (`accountName`, `clientId`), so the view exports use the identity too.

Check: `az functionapp config appsettings list -g "$RG" -n "$FUNC" --query "[?starts_with(name,'AzureWebJobsStorage')].name" -o tsv` prints the three `__` settings and no plain `AzureWebJobsStorage`.

## 9. Static Web App (the admin UI)

```sh
az staticwebapp create --resource-group "$RG" --name "$SWA" --location westeurope --sku Free --tags $TAGS
SWA_HOST=$(az staticwebapp show -g "$RG" -n "$SWA" --query defaultHostname -o tsv)
SWA_TOKEN=$(az staticwebapp secrets list -g "$RG" -n "$SWA" --query properties.apiKey -o tsv)
echo "$SWA_HOST"
```

The location only holds the app's metadata (Static Web Apps offer a few regions, Sweden Central isn't one); the content is served from the global edge. The deployment token `SWA_TOKEN` goes to GitHub in step 15; don't keep it elsewhere.

## 10. Custom domains

Create the DNS records of step 12 first, then bind:

```sh
# API
az functionapp config hostname add -g "$RG" --webapp-name "$FUNC" --hostname "$API_HOST"
az functionapp config ssl create -g "$RG" -n "$FUNC" --hostname "$API_HOST"
THUMB=$(az functionapp config ssl show -g "$RG" --certificate-name "$API_HOST" --query thumbprint -o tsv)
az functionapp config ssl bind -g "$RG" -n "$FUNC" --certificate-thumbprint "$THUMB" --ssl-type SNI

# admin UI
az staticwebapp hostname set -g "$RG" -n "$SWA" --hostname "$APP_HOST"
```

Flex Consumption uses site-scoped certificates. When a CLI command above refuses on Flex, do the same in the portal: **Function App → Custom domains → Add custom domain**, certificate type "App Service Managed Certificate". The certificate is free and renews itself.

Check: `curl -sI "https://$API_HOST/api/v1/auth/configuration"` answers over TLS (a `404` or `503` is fine before step 16; a TLS error is not).

## 11. Entra app registrations (once)

### 11.1 `2S Platform Customer Keys` (BYOK)

A multi-tenant application with **no secret**: its credential is a federation with the API's managed identity. Customers consent to it and grant its service principal **Key Vault Crypto Service Encryption User** on their key.

```sh
TENANT_ID=$(az account show --query tenantId -o tsv)
BYOK_CLIENT_ID=$(az ad app create --display-name "2S Platform Customer Keys" \
  --sign-in-audience AzureADMultipleOrgs --query appId -o tsv)
az ad sp create --id "$BYOK_CLIENT_ID" --output none

cat > byok-fic.json <<EOF
{
  "name": "prod-2s-api-mi",
  "issuer": "https://login.microsoftonline.com/$TENANT_ID/v2.0",
  "subject": "$MI_PRINCIPAL_ID",
  "audiences": ["api://AzureADTokenExchange"],
  "description": "The managed identity of the 2S API (production)"
}
EOF
az ad app federated-credential create --id "$BYOK_CLIENT_ID" --parameters byok-fic.json
rm byok-fic.json
echo "$BYOK_CLIENT_ID"
```

Put `$BYOK_CLIENT_ID` into `KeyValues__Protection__Byok__ClientId` (step 8.3). Customers see the application's name and publisher when they consent: start **publisher verification** (a Microsoft Partner Network id) before the first BYOK customer, or they see an "unverified" warning.

A customer whose vault has a firewall needs a static egress IP of the platform. There is none in this phase (it needs a virtual network, Flex virtual network integration and a NAT gateway, about 35 USD a month): add it with the first such customer.

### 11.2 `2S Platform Deployment` (GitHub Actions)

The identity of the deployment workflows, with a federated credential for the GitHub environment `production`. No secret.

```sh
DEPLOY_CLIENT_ID=$(az ad app create --display-name "2S Platform Deployment" --query appId -o tsv)
DEPLOY_SP_ID=$(az ad sp create --id "$DEPLOY_CLIENT_ID" --query id -o tsv)

cat > deploy-fic.json <<EOF
{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:$GITHUB_REPO:environment:production",
  "audiences": ["api://AzureADTokenExchange"],
  "description": "GitHub Actions, environment production"
}
EOF
az ad app federated-credential create --id "$DEPLOY_CLIENT_ID" --parameters deploy-fic.json
rm deploy-fic.json

RG_ID=$(az group show -n "$RG" --query id -o tsv)
az role assignment create --assignee-object-id "$DEPLOY_SP_ID" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "$RG_ID"
az role assignment create --assignee-object-id "$DEPLOY_SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Key Vault Secrets User" --scope "$KV_ID"          # DbCreator reads cosmos-connection
az role assignment create --assignee-object-id "$DEPLOY_SP_ID" --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" --scope "$SA_ID"   # the Flex deployment package
echo "client $DEPLOY_CLIENT_ID tenant $TENANT_ID subscription $(az account show --query id -o tsv)"
```

`Contributor` on the resource group is enough for deploying code. The role assignments of steps 6 and 7 are done by you here; when the Bicep templates take over, the identity also needs **Role Based Access Control Administrator** limited to those roles (spec, section 10).

## 12. DNS records (zone `42for.net`)

| Name | Type | Value | For |
| --- | --- | --- | --- |
| `api.2s` | CNAME | `$FUNC_HOST` (step 8.1) | the API |
| `asuid.api.2s` | TXT | `az functionapp show -g "$RG" -n "$FUNC" --query customDomainVerificationId -o tsv` | domain ownership for the Function App |
| `app.2s` | CNAME | `$SWA_HOST` (step 9) | the admin UI |
| `auth` | CNAME | the target WorkOS shows for the custom AuthKit domain (step 13) | AuthKit sign-in |
| | | any TXT/CNAME records WorkOS asks for | WorkOS domain verification |

Keep the TTL low (300 s) until everything works.

## 13. WorkOS production

In the WorkOS dashboard, environment **Production**:

1. **Custom domain** for AuthKit: `auth.42for.net`, with the DNS records WorkOS shows (step 12). This is the issuer the API checks; it is final (decision 4).
2. Note the **client id** (`client_…`) for `Auth__AuthKit__ClientId` and `VITE_*` of step 15.
3. **JWT template**, so tokens carry the audience and the profile fields the platform stores:

   ```json
   {
     "aud": "https://2s.42for.net",
     "email": {{ user.email }},
     "email_verified": {{ user.email_verified }},
     "name": "{{ user.first_name }} {{ user.last_name }}"
   }
   ```

   `aud` must equal `Auth__AuthKit__Audience`.
4. **Redirects** of the admin UI: redirect URI `https://app.2s.42for.net/callback`, sign-out URI `https://app.2s.42for.net/login`, allowed origin (CORS) `https://app.2s.42for.net`.
5. **CLI Auth** on, so `sform` can sign in with the device flow.
6. **Invitation link**: `https://app.2s.42for.net/invitations/accept` (and the management key of step 8.3 for the emails).
7. **Your own account**: sign up once in production. Its user id (`user_01…`) is `baseAccount__id` of DbCreator (step 16) and becomes the owner of the platform's first organization (decision 11).

The checklist of the admin UI ([README](../../../src/Platform/ui.admin/README.md#workos-dashboard-checklist)) has the same items in more detail.

## 14. Sentry and PostHog

- **Sentry** (EU, `de.sentry.io`): a project `2s-admin-ui`. Its DSN goes to `VITE_SENTRY_DSN`; the environment is `production`. Add an **uptime monitor** on `https://api.2s.42for.net/api/v1/auth/configuration` (expects 200).
- **PostHog** (EU cloud): the production project's token goes to `VITE_POSTHOG_PROJECT_TOKEN`, the host is `https://eu.i.posthog.com`.

The API has no Sentry SDK; its errors are in Application Insights.

## 15. GitHub environment `production`

In the repository settings, **Environments → New environment → `production`**: required reviewer the owner, deployment branch `main` only.

| Kind | Name | Value |
| --- | --- | --- |
| variable | `AZURE_CLIENT_ID` | `$DEPLOY_CLIENT_ID` (step 11.2) |
| variable | `AZURE_TENANT_ID` | `$TENANT_ID` |
| variable | `AZURE_SUBSCRIPTION_ID` | the id of `prod-42fornet-sub` |
| variable | `AZURE_RESOURCE_GROUP` | `prod-2s-eusec-rg` |
| variable | `AZURE_FUNCTIONAPP_NAME` | `prod-2s-api-fa` |
| variable | `AZURE_KEYVAULT_NAME` | `prod-2s-42fornet-kv` |
| variable | `STORYTELLER_BASE_ACCOUNT_ID` | your WorkOS production user id |
| variable | `VITE_API_BASE_URL` | `https://api.2s.42for.net/api` |
| variable | `VITE_AUTH_API_HOSTNAME` | `auth.42for.net` |
| variable | `VITE_ENVIRONMENT_NAME` | `production` |
| variable | `VITE_SENTRY_DSN` | step 14 |
| variable | `VITE_POSTHOG_PROJECT_TOKEN`, `VITE_POSTHOG_HOST` | step 14 |
| **secret** | `AZURE_STATIC_WEB_APPS_API_TOKEN` | `$SWA_TOKEN` (step 9) |

The workflows (`platform-infrastructure.yml`, `platform-deploy.yml`, spec section 10) are written separately; until they exist, step 16 is done by hand.

## 16. First deployment (after the blockers are merged)

Run from a clean checkout of `main`, with 424-230 and the setting of the certificate kinds merged.

### 16.1 The API

```sh
dotnet publish src/Platform/Storyteller/Api.Functions/src -c Release -o .publish/api
(cd .publish/api && zip -r ../api.zip .)
az functionapp deployment source config-zip -g "$RG" -n "$FUNC" --src .publish/api.zip
```

### 16.2 The database (DbCreator, once per schema change)

DbCreator creates the database `42.Platform.2S` (shared autoscale up to 1000 RU/s), the containers `core` and `organizations`, and seeds the organization `42dotnet` with you as its owner.

```sh
export CosmosDb__Connection="$(az keyvault secret show --vault-name "$KV" --name cosmos-connection --query value -o tsv)"
export baseAccount__id="<your WorkOS production user id, user_01…>"
export CosmosDb__ShouldAcceptAnyCertificate=false   # app.config.json accepts any certificate (the local emulator's)
export stagingSeed__enabled=false
dotnet run --project src/Platform/Storyteller/DbCreator/src -c Release
unset CosmosDb__Connection CosmosDb__ShouldAcceptAnyCertificate
```

`app.config.json` of DbCreator points at the local emulator and sets `shouldAcceptAnyCertificate: true` for its self-signed certificate; the environment variables override both, so the production run checks the certificate of Cosmos DB. Never set `stagingSeed__enabled=true` here: it seeds the AuthKit staging test accounts.

### 16.3 The admin UI

The UI installs the TypeScript SDK from its folder (`file:../../Storyteller/sdk.typescript/src`), so the SDK is built first, as in `.github/workflows/ui-admin.yml`. One build serves every environment: `build/write-environment-config.ts` writes `dist/app-config.json` and resolves the CSP placeholders (`__API_ORIGIN__`, `__AUTH_HOSTNAME__`, `__SENTRY_ORIGIN__`, `__POSTHOG_ORIGIN__`) of `dist/staticwebapp.config.json` for this deployment.

```sh
(cd src/Platform/Storyteller/sdk.typescript/src && npm ci && npm run build)
cd src/Platform/ui.admin/src
npm ci
npm run build
API_BASE_URL="https://$API_HOST/api" AUTH_API_HOSTNAME="$AUTH_HOST" ENVIRONMENT_NAME=production \
SENTRY_DSN="<dsn>" POSTHOG_PROJECT_TOKEN="<token>" POSTHOG_HOST="https://eu.i.posthog.com" RELEASE="$(git rev-parse HEAD)" \
  node build/write-environment-config.ts
grep -cE "__(API_ORIGIN|AUTH_HOSTNAME|SENTRY_ORIGIN|POSTHOG_ORIGIN)__" dist/staticwebapp.config.json   # must print 0
npx @azure/static-web-apps-cli deploy ./dist --deployment-token "$SWA_TOKEN" --env production
cd -
```

The UI's `staticwebapp.config.json` gives the SPA fallback and the headers, with the CSP origins written by the step above.

## 17. Verify

| Check | How | Expected |
| --- | --- | --- |
| Discovery | `curl -s https://api.2s.42for.net/api/v1/auth/configuration` | `200`, `"Provider":"AuthKit"`, `"AuthKitDomain":"https://auth.42for.net"` |
| HTTPS only | `curl -sI http://api.2s.42for.net/api/v1/auth/configuration` | a redirect to HTTPS |
| CORS | `curl -sI -X OPTIONS -H "Origin: https://app.2s.42for.net" -H "Access-Control-Request-Method: GET" https://api.2s.42for.net/api/v1/auth/configuration` | `Access-Control-Allow-Origin: https://app.2s.42for.net` |
| Sign-in | open `https://app.2s.42for.net`, sign in with your production account | the organization `42dotnet` is listed |
| Containers | `az cosmosdb sql container list -g "$RG" -a "$COSMOS" -d 42.Platform.2S --query "[].name" -o tsv` | exactly `core` and `organizations` |
| Free tier | Cost Management after a day | Cosmos DB at 0 |
| Secrets | in the UI, create a project and a Standard secret | **Settings → Secrets** shows the project's key; `az keyvault key list --vault-name "$KV"` lists a `kek-…` key |
| Certificate kinds off | in **Settings → Machines**, enable `Certificate` | `400` "… is turned off in this deployment." |
| Timers | Application Insights, `traces` of the last hours | `KeyValueProtectionTimer` runs hourly without errors |
| Storage key off | `az storage account show -n "$SA" --query allowSharedKeyAccess` | `false` |
| Locks | `az lock list -g "$RG" -o table` | the Cosmos DB account and the vault |
| Uptime | Sentry | the monitor is green |

## 18. Costs to expect

| Item | Monthly |
| --- | --- |
| Cosmos DB (free tier, autoscale up to 1000 RU/s, under 25 GB) | 0 |
| Flex Consumption (within 100,000 GB-s and 250,000 executions) | 0 |
| Static Web App Free | 0 |
| Storage account | under 1 USD |
| Key Vault (keys and operations; HSM keys only when a project allows them) | under 1 USD |
| Log Analytics, Application Insights (capped) | 0 |
| **Total** | **about 0–2 USD** |

What raises it: Cosmos DB throughput above 1000 RU/s, a second region or zone redundancy, an always-ready instance, HSM keys (about 1 USD per key a month), a NAT gateway (about 35 USD), a Managed HSM (thousands of USD). The budget of step 2 warns at 2.50 USD.

## 19. Open points (decide before or during the work)

- **The `aud` of the JWT template** (step 13): `https://2s.42for.net` is proposed. `auth.42for.net` is meant to be shared with the other products of 42for.net, which means one WorkOS environment, one JWT template and one `aud`: a token issued to another 42for.net product would then also be accepted by the 2S API. Check whether WorkOS lets each application have its own audience before the second product signs users in.
- **The invitation link** is one per WorkOS environment, so it points to the 2S admin UI while the environment is shared.
- **Publisher verification** of `2S Platform Customer Keys` (step 11.1), before the first BYOK customer.
- **The development environment** comes later as a new subscription (`dev-42fornet-sub`) from the same Bicep templates (spec, section 11), with WorkOS staging, its own trust domain and its own BYOK application.
