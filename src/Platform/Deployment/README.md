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
- Azure CLI 2.71 or newer with Bicep (`az bicep install`, `az bicep upgrade`), PowerShell 7.4 or newer (a failing `az` or `gh` call stops the scripts), for step 15 the GitHub CLI signed in as an admin of the repository, and, only to store the WorkOS API key from the shell instead of the portal, the Az PowerShell modules `Az.Accounts` and `Az.KeyVault` (`Install-Module Az.Accounts, Az.KeyVault -Scope CurrentUser`).
- Run everything from this folder: `cd src/Platform/Deployment`.
- Read **Never do** in the cookbook (step 0). The templates create two things that can't be undone: the Cosmos DB free tier and the vault's purge protection.

### Parameters

`prod.bicepparam` holds every value of the environment as a literal, including those that exist only after a later step. **Commit each of them as soon as it is known** (none is a secret): `main.bicep` owns the whole set of the API's app settings, so a run from a checkout where one is still empty removes its setting (an empty client id leaves its setting out, as an empty BYOK client id would be taken as one).

| Parameter | What | Known after |
| --- | --- | --- |
| `byokClientId` | `KeyValues__Protection__Byok__ClientId` | step 11.1 |
| `deploymentPrincipalId` | the deployment identity's roles | step 11.2 |
| `authKitClientId` | `Auth__AuthKit__ClientId` | step 13 |
| `authKitApiKeyEnabled` | `Auth__AuthKit__ApiKey` as a Key Vault reference | `true` once the secret `authkit-api-key` is in the vault |
| `budgetStartDate` | the budget's first month, never moved afterwards | the month of the first run |

Only the owner's personal values come from environment variables, so the repository doesn't hold them:

| Variable | Parameter |
| --- | --- |
| `PLATFORM_OWNER_EMAIL` | `ownerEmail`: the budget's alerts (required) |
| `PLATFORM_OWNER_OBJECT_ID` | `ownerObjectId`: Key Vault Administrator, Storage Blob Data Contributor (required): `az ad signed-in-user show --query id -o tsv` |

## First run

The complete first deployment, with every `what-if` and what it must show, the manual steps in between and the deployment of the code, is the [runbook](../../../docs/Platform/Deployment/runbook-azure-production.md). The sections below are the short version.

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

The Function App may log `403` errors from storage or the vault for a few minutes after its first start: new role assignments take a while to reach the data planes. If they don't stop, restart it (`az functionapp restart -g prod-2s-eusec-rg -n prod-2s-api-fa`).

Then run the hierarchical partition key probe against the account (cookbook, **step 5a**), before anything else touches the database. The probe's database (1000 RU/s) must be gone (it deletes it) before DbCreator creates `42.Platform.2S`, or the account leaves the free tier. The account-wide limit (`cosmosTotalThroughputLimit`, `capacity.totalThroughputLimit`) is `-1` (none) until DbCreator has created its containers: a capped account refuses to create a container without explicit throughput, and the containers share the database's throughput. Set it to `1000` right after DbCreator (runbook, phase 11.2); a later new container needs it lifted for that run.

The cookbook's steps 5a and 16 are bash, run from the **repository root** (their paths, such as `src/Platform/Storyteller/...`, are relative to it), with the variable block of the cookbook's step 0 pasted first: `RG`, `COSMOS`, `KV`, `FUNC`, `SWA` and the host names hold the same names as `naming.bicep`. On Windows use Git Bash, or translate each `X=…` to `$X = …` in PowerShell. Come back to this folder (`cd src/Platform/Deployment`) for the next step.

### 3. The Entra applications (step 11)

```powershell
./scripts/New-ByokApplication.ps1              # prints the application id: byokClientId
$deployment = ./scripts/New-DeploymentApplication.ps1
$deployment.PrincipalId                        # deploymentPrincipalId
```

Put both ids into `parameters/prod.bicepparam`, commit them, and run `main.bicep` again (the `create` command of step 2). The run adds `KeyValues__Protection__Byok__ClientId` and the deployment identity's roles, scoped to what it deploys: Website Contributor on the Function App, Key Vault Secrets User on the secret `cosmos-connection` only, and Storage Blob Data Contributor on the container `deployments` only. It gets nothing on the resource group, so it can't list the Cosmos DB keys or read other secrets directly. It is still trusted with every secret of the API: Website Contributor on the Function App lets it deploy code, and that code runs as the app's managed identity, which reads every secret of the vault. Start the publisher verification of `2S Platform Customer Keys` before the first BYOK customer.

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

By hand, as in the cookbook. Then put the WorkOS client id into `authKitClientId` of `parameters/prod.bicepparam`, commit it, and run `main.bicep` again.

For invitation emails, put the WorkOS management key into the vault, set `authKitApiKeyEnabled = true` in the parameters file, commit it, and run `main.bicep` again:

Store the key so that the key never goes on a command line (it would stay in the shell history and the process list). Either in the portal: **prod-2s-42fornet-kv → Objects → Secrets → Generate/Import**, name `authkit-api-key`, paste the `sk_…` value (you are Key Vault Administrator); or with Az PowerShell, which prompts for it:

```powershell
Connect-AzAccount -Subscription prod-42fornet-sub   # once, if Az PowerShell isn't signed in
Set-AzKeyVaultSecret -VaultName prod-2s-42fornet-kv -Name authkit-api-key `
  -SecretValue (Read-Host -AsSecureString -Prompt 'WorkOS API key (sk_…)') | Out-Null
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

Run `what-if`, then `create`, from an up-to-date checkout with the two owner variables. Every run is idempotent: names, role assignments (`guid(scope, principal, role)`), locks and the budget's start date are stable, and the `cosmos-connection` secret is written from the account's current key.

- **The app settings belong to the template.** A setting added by hand in the portal is removed by the next run; add it to `modules/functions.bicep`, and its value to the parameters file.
- **The managed identity is never re-created**: its principal id is the subject of the BYOK federation.
- **Rotating the Cosmos DB keys** (cookbook, step 7): switch `cosmosConnectionKey` to the other key, run `main.bicep`, restart the Function App (the app setting references the secret without a version, which the app refreshes only about once a day), then regenerate the key no longer in use.
- `domains.bicep` is not part of a normal run. A second run writes the binding without TLS, then puts the certificate again (an existing managed certificate is kept, not reissued) and binds it: the API's domain is without TLS for a few seconds.
- `MachineAuth__DisabledCredentialKinds__*` is read by the API only once [424-232](https://linear.app/424fun/issue/424-232/turn-off-machine-credential-kinds-per-deployment) is merged. Until then the setting has no effect. That is safe: with `IsCertificateHeaderFromClientAllowed=false` a certificate machine can't sign in anyway, but projects can still enable the kinds.

## Checks

```powershell
az bicep build --file main.bicep --stdout > $null
az bicep build --file domains.bicep --stdout > $null
az bicep build-params --file parameters/prod.bicepparam --stdout > $null   # needs the two owner variables

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
