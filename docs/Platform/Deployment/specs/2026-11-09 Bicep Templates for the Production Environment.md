# Bicep Templates for the Production Environment

## Problem

The production environment of the 2S Platform ([spec](2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md)) is described only by the [cookbook](../cookbook-azure-production.md): steps 1–15 build it by hand with the Azure CLI, in an exact order, with values copied between steps (the identity's ids, the vault URI, the connection string of Application Insights). A step done twice, out of order or with a typo gives an environment that differs from the decisions, and nothing describes the environment as it should be. The development subscription (spec, section 11) would repeat everything by hand.

Spec section 2 plans the templates in `src/Platform/Deployment/`. This spec turns steps 1–15 of the cookbook into those templates and into small scripts for the parts Bicep cannot do, so that the first run and every later run give the same environment.

## Current State

- No infrastructure as code exists in the repository. `.github/workflows/` has no infrastructure or application deployment.
- `src/Platform/GitHubAction/` is the precedent for a folder under `src/Platform/` with no .NET project: `src/Platform/Directory.Build.proj` only picks up `**\*.*?proj`, so other files are ignored by the build.
- The cookbook steps and what they create:

| Step | What | Azure resource? |
| --- | --- | --- |
| 1 | upgrade and rename the subscription, register providers, check Flex in Sweden Central | no (portal, CLI) |
| 2 | resource group, budget | yes |
| 3 | user-assigned managed identity | yes |
| 4 | Log Analytics (0.15 GB a day, 30 days), Application Insights | yes |
| 5 | Cosmos DB, free tier, continuous 7-day backup, delete lock | yes |
| 5a | the hierarchical partition key probe | no (a script against the account) |
| 6 | storage account, `deployments` container, roles | yes |
| 7 | Premium vault, RBAC, purge protection, lock, roles, the `cosmos-connection` secret | yes |
| 8 | Flex Consumption Function App, platform settings, app settings, storage by identity | yes |
| 9 | Static Web App (Free) | yes |
| 10 | custom domains and managed certificates | yes, after the DNS records |
| 11 | Entra apps `2S Platform Customer Keys` and `2S Platform Deployment`, the deployment identity's roles | Entra objects (no); roles (yes) |
| 12 | DNS records in the zone `42for.net` | no (outside Azure) |
| 13 | WorkOS production | no |
| 14 | Sentry and PostHog | no |
| 15 | GitHub environment `production` | no (GitHub API) |

- Facts of the code the templates rely on (checked):
  - `KeyValues:Protection:Byok:ClientId` is used when it is not null (`KeyValues.Azure.KeyVault/src/CustomerKeyProvider.cs:49`), so an empty value would be taken as a client id: the setting must be left out until the application exists.
  - `Backend.AzureStorage` creates its blob container and queue itself (`CreateIfNotExistsAsync`), with the identity of `AzureWebJobsStorage__clientId`.
  - `AzureKeyVaults:<name>` holds the vault URI (`Access.Certificates.Azure.KeyVault/src/EntryPoint.cs:20`).
- Flex Consumption ([Microsoft Learn](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan)): one app per plan; .NET 10 isolated supported; certificates are **site-scoped** (`Microsoft.Web/sites/certificates`, a child of the site), which must be turned on with `siteScopedCertificatesEnabled: true` **when the app is created** (no migration path for an existing app); managed certificates are supported; the Azure CLI cannot manage site-scoped certificates yet, ARM and Bicep can.

## Proposed Changes

### 1. Folder and entry points

```
src/Platform/Deployment/
├── README.md                     # how to run steps 1–15 with the templates and scripts
├── bicepconfig.json              # linter settings
├── main.bicep                    # subscription scope: steps 2–9 and the roles of step 11
├── domains.bicep                 # resource group scope: step 10, run once the DNS records exist
├── naming.bicep                  # exported naming function and types (compile-time import)
├── modules/
│   ├── identity.bicep            # step 3
│   ├── monitoring.bicep          # step 4
│   ├── cosmos.bicep              # step 5 (no database)
│   ├── storage.bicep             # step 6
│   ├── keyvault.bicep            # step 7
│   ├── functions.bicep           # step 8
│   ├── staticwebapp.bicep        # step 9
│   ├── access.bicep              # the owner's and the deployment identity's roles (steps 6, 7, 11.2)
│   └── hostname-binding.bicep    # a hostname binding of the Function App (step 10)
├── parameters/
│   ├── prod.bicepparam           # main.bicep, production
│   └── prod.domains.bicepparam   # domains.bicep, production
└── scripts/
    ├── Initialize-Subscription.ps1   # step 1: providers, Flex in the region
    ├── New-ByokApplication.ps1       # step 11.1
    ├── New-DeploymentApplication.ps1 # step 11.2 (the Entra objects)
    └── Set-GitHubEnvironment.ps1     # step 15
```

- `main.bicep` is deployed with `az deployment sub create --location swedencentral --template-file main.bicep --parameters parameters/prod.bicepparam`; `what-if` first.
- **The custom domains are a separate entry point** (`domains.bicep`, `az deployment group create`). The first binding of a managed certificate needs three writes of the hostname binding (bind without TLS, issue the certificate, bind with SNI). Done in `main.bicep`, every later run would switch TLS off for a few seconds. Kept apart, `main.bicep` never touches the bindings (a PUT of the site keeps its host names), and `domains.bicep` runs once after step 12, or again only to rebind.
- Steps 5a, 12, 13, 14 stay manual. `main.bicep` outputs the DNS records of step 12.

### 2. Naming and parameters

- `naming.bicep` exports `resourceNames(environment, product, company, regionCode)`, which returns every name of the [naming convention](../decisions.md): `{environment}-{product}-{name}-{type}`, the resource group with the region (`prod-2s-eusec-rg`), `42fornet` in the globally unique names, the storage account without hyphens. Both entry points import it, so a name is defined once. It also exports the type `environmentName` (`'prod' | 'dev'`).
- Names of production: `prod-2s-eusec-rg`, `prod-2s-42fornet-cdb`, `prod-2s-42fornet-kv`, `prod2s42fornetsa`, `prod-2s-api-mi`, `prod-2s-api-fa`, `prod-2s-api-asp`, `prod-2s-log`, `prod-2s-api-appi`, `prod-2s-app-swa`; the budget `prod-2s-budget`.
- Tags on every resource and the resource group: `product=2s-platform`, `environment=<environment>`, `managedBy=bicep`.
- Parameters of `main.bicep`: `environment`, `location` (`swedencentral`), `regionCode` (`eusec`), `staticWebAppLocation` (`westeurope`), `dnsZone` (`42for.net`), `apiHostName`, `appHostName`, `authKitDomain`, `trustDomain`, `authKitClientId`, `authKitAudience` (default `https://<trustDomain>`), `authKitApiKeyEnabled`, `byokClientId`, `ownerObjectId`, `ownerEmail`, `deploymentPrincipalId`, `budgetAmount` (5), `budgetStartDate` (`utcNow('yyyy-MM-01')`), `logDailyCapGb` (`'0.15'`), `functionMaximumInstanceCount` (10), `functionInstanceMemoryMB` (512), `disabledCredentialKinds` (`Certificate`, `CertificateAndApiKey`), `runtimeEnvironment` (`Production`).
- `prod.bicepparam` holds the decided values as literals. Values that are personal or exist only after a later step are read with `readEnvironmentVariable`, so the repository never holds the owner's email and the file needs no edit between runs:

| Parameter | Variable | Known after |
| --- | --- | --- |
| `ownerEmail` | `PLATFORM_OWNER_EMAIL` (required) | — |
| `ownerObjectId` | `PLATFORM_OWNER_OBJECT_ID` (required) | — |
| `authKitClientId` | `PLATFORM_AUTHKIT_CLIENT_ID` | step 13 |
| `byokClientId` | `PLATFORM_BYOK_CLIENT_ID` | step 11.1 |
| `deploymentPrincipalId` | `PLATFORM_DEPLOYMENT_PRINCIPAL_ID` | step 11.2 |

- No secret is a parameter. The Cosmos DB key goes from the account to the vault inside the template; the AuthKit management key is set by hand (`az keyvault secret set … authkit-api-key`), and `authKitApiKeyEnabled=true` adds its Key Vault reference.

### 3. `main.bicep` (subscription scope)

- The resource group, and the **budget** `Microsoft.Consumption/budgets` on the subscription: 5 USD a month, alerts at 50, 80 and 100 % of the actual cost and 100 % of the forecast, to `ownerEmail`. The start date is the first day of the current month on every run (a budget refuses a past start date outside its current period).
- The modules, scoped to the resource group, with outputs passed between them in place of the shell variables of the cookbook.
- Outputs: the names, the identity's client and principal ids, the vault URI, the default host names of the Function App and the Static Web App, the Function App's `customDomainVerificationId`, the tenant and subscription ids, and `dnsRecords` (name relative to `dnsZone`, type, value) for step 12: `api.2s` CNAME, `asuid.api.2s` TXT, `app.2s` CNAME. The `auth` record comes from WorkOS.

### 4. Modules

1. **identity.bicep**: `Microsoft.ManagedIdentity/userAssignedIdentities`. Its principal id is the subject of the BYOK federated credential, so the identity must never be re-created.
2. **monitoring.bicep**: Log Analytics `PerGB2018`, 30 days, `workspaceCapping.dailyQuotaGb` 0.15; workspace-based Application Insights (`kind: web`). No availability test.
3. **cosmos.bicep**: `GlobalDocumentDB`, `enableFreeTier: true`, one location (Sweden Central, `isZoneRedundant: false`), session consistency, `backupPolicy` continuous with the tier `Continuous7Days`, minimal TLS 1.2, public network access, `disableLocalAuth: false` (the API uses the key), no automatic failover, no multiple write locations. **No database** (DbCreator creates it after 424-230). A `CanNotDelete` lock `do-not-delete`.
4. **storage.bicep**: StorageV2, Standard LRS, hot, TLS 1.2, no public blob access, HTTPS only, **`allowSharedKeyAccess: false` from the start**, `defaultToOAuthAuthentication: true`. Containers `deployments` and `configuration-transfers` (the code creates the queue). Roles of the identity: Storage Blob Data Owner, Storage Queue Data Contributor, Storage Table Data Contributor. Unlike the cookbook, the key is never on: the Function App is created with identity-based storage and deployment storage from its first write, so the two-phase switch of step 8.4 is not needed.
5. **keyvault.bicep**: SKU Premium, RBAC authorization, soft delete 90 days, **purge protection**, public network access; a `CanNotDelete` lock. The secret `cosmos-connection` = `AccountEndpoint=<documentEndpoint>;AccountKey=<primary key>;` from the account's `listKeys()`. Roles of the identity: Key Vault Crypto Officer, Key Vault Crypto Service Encryption User, Key Vault Secrets User.
6. **functions.bicep**:
   - Plan `Microsoft.Web/serverfarms` `FC1` / `FlexConsumption`, `reserved: true`.
   - Site `functionapp,linux`, the user-assigned identity only, `keyVaultReferenceIdentity` = the identity, `httpsOnly`, `clientCertEnabled: false`, **`siteScopedCertificatesEnabled: true`** (only possible at creation), `siteConfig` with TLS 1.2, FTPS disabled, HTTP/2, CORS `https://<appHostName>` without credentials.
   - `functionAppConfig`: deployment storage the `deployments` container with the identity (`UserAssignedIdentity`), 512 MB, at most 10 instances, no always-ready instance, runtime `dotnet-isolated` `10.0`.
   - `basicPublishingCredentialsPolicies` `scm` and `ftp` off.
   - App settings (`sites/config` `appsettings`, so the template owns the whole set): those of cookbook step 8.3 and 8.4, with the disabled credential kinds generated from the parameter. `Auth__AuthKit__ClientId` and `KeyValues__Protection__Byok__ClientId` are left out while their parameters are empty (an empty BYOK client id would be used as one); `Auth__AuthKit__ApiKey` is a Key Vault reference when `authKitApiKeyEnabled`. No setting deprecated on Flex (`FUNCTIONS_WORKER_RUNTIME`, `FUNCTIONS_EXTENSION_VERSION`, `WEBSITE_RUN_FROM_PACKAGE`, `WEBSITE_CONTENT*`).
7. **staticwebapp.bicep**: `Microsoft.Web/staticSites`, `Free`, in `westeurope` (metadata only), no repository link (deployed with its token).
8. **access.bicep**: the owner (principal type `User`): Key Vault Administrator on the vault, Storage Blob Data Contributor on the storage account. The deployment identity, when `deploymentPrincipalId` is set: Contributor on the resource group, Key Vault Secrets User on the vault (DbCreator reads `cosmos-connection`), Storage Blob Data Contributor on the storage account (the Flex package).
9. **hostname-binding.bicep**: one `Microsoft.Web/sites/hostNameBindings` (CNAME, verified), with an SSL state and thumbprint as parameters, so `domains.bicep` can write the same binding twice.

Role assignment names are `guid(scope, principal, role)`, so a run never duplicates them.

### 5. `domains.bicep` (resource group scope, step 10)

- The API: `hostname-binding.bicep` without TLS → the managed certificate `Microsoft.Web/sites/certificates` (`canonicalName` = the API host) → `hostname-binding.bicep` with `SniEnabled` and the certificate's thumbprint.
- The admin UI: `Microsoft.Web/staticSites/customDomains` with `cname-delegation` (the free certificate comes with it).
- If ARM refuses the managed certificate on the site, the README falls back to the portal as the cookbook does (**Custom domains → Add custom domain**, App Service Managed Certificate).

### 6. Scripts (PowerShell 7 and the Azure CLI, idempotent)

- **Initialize-Subscription.ps1** (step 1.3–1.4): selects the subscription, registers the providers of the cookbook, waits for them, and stops unless `az functionapp list-flexconsumption-locations` lists the location.
- **New-ByokApplication.ps1** (step 11.1): finds or creates the multi-tenant app `2S Platform Customer Keys` (`AzureADMultipleOrgs`, no secret), its service principal, and the federated credential named after the identity (issuer `https://login.microsoftonline.com/<tenant>/v2.0`, subject the identity's principal id, audience `api://AzureADTokenExchange`). Prints the application id for `PLATFORM_BYOK_CLIENT_ID`.
- **New-DeploymentApplication.ps1** (step 11.2): finds or creates the app `2S Platform Deployment`, its service principal and the federated credential `github-<environment>` (`repo:<repo>:environment:<environment>`). Prints the application id and the service principal's object id for `PLATFORM_DEPLOYMENT_PRINCIPAL_ID`. Its roles are assigned by `access.bicep`.
- **Set-GitHubEnvironment.ps1** (step 15): creates the GitHub environment (required reviewer the owner, deployment branch `main`) with `gh api`, and sets its variables from the deployment outputs and the parameters (`AZURE_*`, `STORYTELLER_BASE_ACCOUNT_ID`, `VITE_*`). The one secret, `AZURE_STATIC_WEB_APPS_API_TOKEN`, is piped from `az staticwebapp secrets list` to `gh secret set` without being printed.

### 7. Order of a first run

1. Step 1 by hand (upgrade, rename), then `Initialize-Subscription.ps1`.
2. `main.bicep` with `PLATFORM_OWNER_EMAIL` and `PLATFORM_OWNER_OBJECT_ID` (steps 2–9). Then step 5a (the probe).
3. `New-ByokApplication.ps1` and `New-DeploymentApplication.ps1`; `main.bicep` again with `PLATFORM_BYOK_CLIENT_ID` and `PLATFORM_DEPLOYMENT_PRINCIPAL_ID` (step 11).
4. The DNS records from the outputs (step 12), then `domains.bicep` (step 10).
5. WorkOS, Sentry, PostHog by hand (steps 13, 14); `main.bicep` again with `PLATFORM_AUTHKIT_CLIENT_ID`.
6. `Set-GitHubEnvironment.ps1` (step 15).

The first runs are done by the owner (Owner of the subscription): the deployment identity of step 11.2 has Contributor on the resource group only, which cannot create the resource group, the budget, the locks or the role assignments. Running the templates from a pipeline needs more rights and is part of the workflows (spec, section 10), not of this change.

### 8. Documentation

- `src/Platform/Deployment/README.md`: the files, the variables, the order of section 7 with the commands, and checks.
- The cookbook says, at its start, that steps 1–15 can be run with the templates.
- `CLAUDE.md`: a row for `Deployment/` in the Platform table.

### 9. Validation

- `az bicep build` and `az bicep build-params` of both entry points with no errors and no linter warnings; the scripts parse in PowerShell 7.
- No deployment is part of this change: `what-if` against the subscription is the first step of the README.

### 10. Out of scope

- The workflows `platform-infrastructure.yml` and `platform-deploy.yml` (spec, section 10) and the rights a pipeline needs to run `main.bicep`.
- Steps 16–17 (the code, DbCreator, the checks), the development parameters file, and everything in spec section 13.
