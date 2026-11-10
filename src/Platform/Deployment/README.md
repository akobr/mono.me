# Azure Environment of the 2S Platform (Bicep)

Steps 1–15 of the [cookbook](../../../docs/Platform/Deployment/cookbook-azure-production.md) as Bicep templates and small PowerShell scripts: the production environment in Sweden Central, as cheap as the free tiers allow. The names, settings and decisions are those of the [spec](../../../docs/Platform/Deployment/specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md) and the [decisions](../../../docs/Platform/Deployment/decisions.md); why the templates look the way they do is in [Bicep Templates for the Production Environment](../../../docs/Platform/Deployment/specs/2026-11-09%20Bicep%20Templates%20for%20the%20Production%20Environment.md).

Steps 16–17 (the code, DbCreator, the checks) stay in the cookbook.

## What is where

| File | Cookbook step | What |
| --- | --- | --- |
| `scripts/Initialize-Subscription.ps1` | 1 | providers, Flex Consumption in the region, the Cosmos DB free tier still free |
| `main.bicep` | 2–9, 11 (roles) | subscription scope: the resource group, the budget and every resource, through `modules/` |
| `modules/identity.bicep` | 3 | the user-assigned managed identity `prod-2s-api-mi` |
| `modules/monitoring.bicep` | 4 | Log Analytics `prod-2s-log` (0.15 GB a day, 30 days), Application Insights `prod-2s-api-appi` |
| `modules/cosmos.bicep` | 5 | `prod-2s-42fornet-cdb`: free tier, one region, session, continuous 7-day backup, delete lock, **no database** |
| `modules/storage.bicep` | 6 | `prod2s42fornetsa`: LRS, **shared keys off**, containers `deployments` and `configuration-transfers`, the identity's roles |
| `modules/keyvault.bicep` | 7 | `prod-2s-42fornet-kv`: Premium, RBAC, **purge protection**, delete lock, the secret `cosmos-connection`, the identity's roles |
| `modules/functions.bicep` | 8 | `prod-2s-api-asp` and `prod-2s-api-fa`: Flex Consumption, .NET 10 isolated, 512 MB, at most 10 instances, the app settings |
| `modules/staticwebapp.bicep` | 9 | `prod-2s-app-swa`: Free, `westeurope` (metadata only) |
| `modules/access.bicep` | 6, 7, 11.2 | the owner's roles, and the deployment identity's roles once it exists |
| `domains.bicep`, `modules/hostname-binding.bicep` | 10 | `api.2s.42for.net` with a managed certificate, `app.2s.42for.net` |
| `scripts/New-ByokApplication.ps1` | 11.1 | the Entra app `2S Platform Customer Keys` and its federation with the identity |
| `scripts/New-DeploymentApplication.ps1` | 11.2 | the Entra app `2S Platform Deployment` and its federation with GitHub |
| — (outputs of `main.bicep`) | 12 | the DNS records |
| — (by hand, cookbook) | 13, 14 | WorkOS production, Sentry, PostHog |
| `scripts/Set-GitHubEnvironment.ps1` | 15 | the GitHub environment `production`, its variables and its one secret |
| `naming.bicep` | — | the naming convention, imported by both entry points |
| `parameters/prod.bicepparam`, `parameters/prod.domains.bicepparam` | — | production |

## Before you start

- **Owner** of the subscription and **Application Administrator** of its tenant (the first runs create role assignments, locks and Entra applications).
- Azure CLI 2.71 or newer with Bicep (`az bicep install`, `az bicep upgrade`), PowerShell 7.4 or newer (a failing `az` or `gh` call stops the scripts), and for step 15 the GitHub CLI signed in as an admin of the repository.
- Run everything from this folder: `cd src/Platform/Deployment`.
- Read **Never do** in the cookbook (step 0). The templates create two things that can't be undone: the Cosmos DB free tier and the vault's purge protection.

### Parameters from variables

`prod.bicepparam` holds the decided values. The values that are personal, or exist only after a later step, come from environment variables, so the repository holds none of them:

| Variable | Parameter | When |
| --- | --- | --- |
| `PLATFORM_OWNER_EMAIL` | `ownerEmail`: the budget's alerts | always (required) |
| `PLATFORM_OWNER_OBJECT_ID` | `ownerObjectId`: Key Vault Administrator, Storage Blob Data Contributor | always (required): `az ad signed-in-user show --query id -o tsv` |
| `PLATFORM_BYOK_CLIENT_ID` | `byokClientId`: `KeyValues__Protection__Byok__ClientId` | after step 11.1 |
| `PLATFORM_DEPLOYMENT_PRINCIPAL_ID` | `deploymentPrincipalId`: the deployment identity's roles | after step 11.2 |
| `PLATFORM_AUTHKIT_CLIENT_ID` | `authKitClientId`: `Auth__AuthKit__ClientId` | after step 13 |
| `PLATFORM_AUTHKIT_API_KEY_ENABLED` | `authKitApiKeyEnabled`: `Auth__AuthKit__ApiKey` as a Key Vault reference | `true` once the secret `authkit-api-key` is in the vault |

An empty client id leaves its app setting out (an empty BYOK client id would be taken as one). Keep the variables of the later steps set for every later run: `main.bicep` owns the whole set of app settings, and a run without them removes them.

## First run

PowerShell; the `az` commands are the same in bash.

### 1. The subscription (step 1)

Upgrade the free account to pay-as-you-go and rename it to `prod-42fornet-sub` in the portal (cookbook, step 1.1–1.2). Then:

```powershell
az login
./scripts/Initialize-Subscription.ps1 -Subscription prod-42fornet-sub
```

It stops if Flex Consumption isn't offered in Sweden Central, or if another Cosmos DB account of the subscription already has the free tier.

### 2. The resources (steps 2–9)

```powershell
$env:PLATFORM_OWNER_EMAIL = '<owner email>'
$env:PLATFORM_OWNER_OBJECT_ID = az ad signed-in-user show --query id -o tsv

az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

Then run the hierarchical partition key probe against the account (cookbook, **step 5a**), before anything else touches the database.

The cookbook's steps 5a and 16 are bash, run from the **repository root** (their paths, such as `src/Platform/Storyteller/...`, are relative to it), with the variable block of the cookbook's step 0 pasted first: `RG`, `COSMOS`, `KV`, `FUNC`, `SWA` and the host names hold the same names as `naming.bicep`. On Windows use Git Bash, or translate each `X=…` to `$X = …` in PowerShell. Come back to this folder (`cd src/Platform/Deployment`) for the next step.

### 3. The Entra applications (step 11)

```powershell
$env:PLATFORM_BYOK_CLIENT_ID = ./scripts/New-ByokApplication.ps1
$deployment = ./scripts/New-DeploymentApplication.ps1
$env:PLATFORM_DEPLOYMENT_PRINCIPAL_ID = $deployment.PrincipalId

az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

The second run adds `KeyValues__Protection__Byok__ClientId` and the deployment identity's roles (Contributor on the resource group, Key Vault Secrets User, Storage Blob Data Contributor). Start the publisher verification of `2S Platform Customer Keys` before the first BYOK customer.

### 4. DNS and the custom domains (steps 12 and 10)

```powershell
az deployment sub show --name prod-2s-main --query properties.outputs.dnsRecords.value -o table
```

Create those records in the zone `42for.net` (TTL 300 s until everything works): `api.2s` CNAME, `asuid.api.2s` TXT, `app.2s` CNAME. The `auth` records come from WorkOS (step 13). When they resolve:

```powershell
az deployment group create --name prod-2s-domains --resource-group prod-2s-eusec-rg `
  --template-file domains.bicep --parameters parameters/prod.domains.bicepparam
```

It binds `api.2s.42for.net` to the Function App, issues its free managed certificate (site-scoped, as Flex Consumption has it) and binds it with SNI, and adds `app.2s.42for.net` to the Static Web App. If ARM refuses the managed certificate of the Function App, add the domain in the portal as the cookbook says (**Function App → Custom domains → Add custom domain**, App Service Managed Certificate). Run `domains.bicep` again only to rebind: its first binding switches TLS off for a few seconds.

### 5. WorkOS, Sentry and PostHog (steps 13 and 14)

By hand, as in the cookbook. Then add the WorkOS client id to the app settings:

```powershell
$env:PLATFORM_AUTHKIT_CLIENT_ID = '<client_…>'
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

For invitation emails, put the WorkOS management key into the vault and run `main.bicep` again with `PLATFORM_AUTHKIT_API_KEY_ENABLED=true`:

```powershell
az keyvault secret set --vault-name prod-2s-42fornet-kv --name authkit-api-key --value '<sk_…>' --output none
```

### 6. The GitHub environment (step 15)

```powershell
gh auth login
./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId $deployment.ClientId -BaseAccountId '<user_01…>' `
  -SentryDsn '<dsn>' -PostHogProjectToken '<phc_…>'
```

It creates `production` (required reviewer you, deployment branch `main`), sets the variables of the cookbook's step 15 from the outputs of `prod-2s-main`, and pipes the Static Web App's deployment token into the secret `AZURE_STATIC_WEB_APPS_API_TOKEN` without printing it.

Continue with the cookbook's step 16 once its blockers are merged.

## Later runs

Run `what-if`, then `create`, with the same variables. Every run is idempotent: names, role assignments (`guid(scope, principal, role)`) and locks are stable, the budget's start date moves to the current month, and the `cosmos-connection` secret is written from the account's current key.

- **The app settings belong to the template.** A setting added by hand in the portal is removed by the next run; add it to `modules/functions.bicep`.
- **The managed identity is never re-created**: its principal id is the subject of the BYOK federation.
- `domains.bicep` is not part of a normal run.

## Checks

```powershell
az bicep build --file main.bicep --stdout > $null
az bicep build --file domains.bicep --stdout > $null
az bicep build-params --file parameters/prod.bicepparam --stdout > $null   # needs the two required variables

az cosmosdb show -g prod-2s-eusec-rg -n prod-2s-42fornet-cdb --query "{freeTier:enableFreeTier, backup:backupPolicy.type}" -o table
az keyvault show -n prod-2s-42fornet-kv --query "{purge:properties.enablePurgeProtection, rbac:properties.enableRbacAuthorization, sku:properties.sku.name}" -o table
az storage account show -n prod2s42fornetsa --query allowSharedKeyAccess
az lock list -g prod-2s-eusec-rg -o table
az functionapp config appsettings list -g prod-2s-eusec-rg -n prod-2s-api-fa --query "[].name" -o tsv
```

The rest of the checks are in the cookbook (steps 5, 7, 8.4 and 17).

## Not here yet

- The workflows `platform-infrastructure.yml` and `platform-deploy.yml` (spec, section 10). The deployment identity can deploy code, but running `main.bicep` from a pipeline needs more: the resource group and the budget at subscription scope, the locks and the role assignments.
- A `parameters/dev.bicepparam` for the development subscription (spec, section 11): another file with `environment = 'dev'`, its own domains and trust domain, and WorkOS staging.
