# Bicep Templates for Steps 1–15

## Overview

Steps 1–15 of the [cookbook](../../cookbook-azure-production.md) are now Bicep templates and PowerShell scripts in `src/Platform/Deployment/`, as planned in [Bicep Templates for the Production Environment](../2026-11-09%20Bicep%20Templates%20for%20the%20Production%20Environment.md). `main.bicep` builds every Azure resource of production and its roles, `domains.bicep` the custom domains, and four scripts the parts Bicep cannot do (the providers, the two Entra applications, the GitHub environment). The documents of the issue (the cookbook, the environment's spec and the decisions) were not in the repository yet and are committed with this change. Nothing was deployed: the templates build and lint clean with Bicep 0.49.2, the parameter files compile, and the scripts parse in PowerShell 7.6.

## What Was Done

### 1. Folder and entry points

- `src/Platform/Deployment/` as planned: `main.bicep`, `domains.bicep`, `naming.bicep`, `bicepconfig.json`, `modules/` (8 modules), `parameters/prod.bicepparam`, `parameters/prod.domains.bicepparam`, `scripts/`, `README.md`.
- The folder has no project file, so `src/Platform/Directory.Build.proj` (`**\*.*?proj`) ignores it, like `GitHubAction/`.
- `bicepconfig.json` makes unused parameters and variables, secrets in outputs and insecure secret parameters errors, and keeps `use-recent-api-versions` as a warning. Every resource uses an API version the linter accepts (`Microsoft.Web/*` 2025-03-01, `Microsoft.KeyVault/*` 2025-05-01, `Microsoft.Storage/*` 2025-01-01, `Microsoft.DocumentDB` 2025-04-15, `Microsoft.Consumption/budgets` 2026-06-01, `Microsoft.Resources/resourceGroups` 2025-04-01).

### 2. Naming and parameters

- `naming.bicep` exports `resourceNames(environment, product, company, regionCode)`, `resourceTags(environment)` and the types `environmentName` and `resourceNamesType`; both entry points import them (`import { … } from 'naming.bicep'`).
- `prod.bicepparam` holds the decided values as literals (domains, trust domain, region, the `aud` `https://2s.42for.net`) and reads `PLATFORM_OWNER_EMAIL`, `PLATFORM_OWNER_OBJECT_ID` (required, no default: compiling without them fails with BCP427), `PLATFORM_BYOK_CLIENT_ID`, `PLATFORM_DEPLOYMENT_PRINCIPAL_ID`, `PLATFORM_AUTHKIT_CLIENT_ID` and `PLATFORM_AUTHKIT_API_KEY_ENABLED` with `readEnvironmentVariable`.
- **Added to the plan:** `PLATFORM_AUTHKIT_API_KEY_ENABLED` for `authKitApiKeyEnabled`, so even that switch needs no edit of the file.

### 3. `main.bicep`

- The resource group, the budget (`prod-2s-budget`, 5 a month, 50/80/100 % actual and 100 % forecast to `ownerEmail`, start date `utcNow('yyyy-MM-01')`), and the eight modules scoped to the resource group, chained by their outputs.
- Outputs as planned, plus `apiBaseUrl` and `authKitDomain` for `Set-GitHubEnvironment.ps1`. `dnsRecords` computes the names relative to `dnsZone` with a local function (`relativeName`): `api.2s` CNAME, `asuid.api.2s` TXT, `app.2s` CNAME.

### 4. Modules

1. **identity.bicep**: as planned.
2. **monitoring.bicep**: as planned; the cap is a string parameter passed through `json()` (Bicep has no decimal literals).
3. **cosmos.bicep**: as planned, with `enableFreeTier` as a parameter (default `true`). The lock note says why it exists.
4. **storage.bicep**: as planned. Shared keys are off from the first write: the Function App is created with identity-based storage and deployment storage, so the cookbook's switch of step 8.4 has no counterpart.
5. **keyvault.bicep**: as planned. `cosmos-connection` is built from `listKeys().primaryMasterKey` and the account's `documentEndpoint`; the key is never a parameter or an output.
6. **functions.bicep**: as planned. `siteScopedCertificatesEnabled: true` is documented for Flex Consumption but missing from Bicep's type definitions of `Microsoft.Web/sites`, so its BCP037 warning is suppressed on that line with a comment. The disabled credential kinds become `MachineAuth__DisabledCredentialKinds__<i>` through `toObject(range(…))`. `Auth__AuthKit__ClientId` and `KeyValues__Protection__Byok__ClientId` are left out while empty: `CustomerKeyProvider` takes any non-null client id (`ClientId ?? throw`), so an empty string would be used as one.
7. **staticwebapp.bicep**: as planned, with `allowConfigFileUpdates`.
8. **access.bicep**: as planned; the deployment identity's three role assignments are conditional on `deploymentPrincipalId`.
9. **hostname-binding.bicep**: as planned.

### 5. `domains.bicep`

As planned: the binding without TLS, the site-scoped managed certificate `Microsoft.Web/sites/certificates` (`canonicalName` only), the binding with `SniEnabled` and the certificate's thumbprint, and the Static Web App's custom domain with `cname-delegation`. Outputs: the thumbprint and the domain's status. Whether ARM issues a site-scoped managed certificate from `canonicalName` alone could not be checked without a deployment (Microsoft Learn shows only the portal for managed certificates and Bicep for uploaded and Key Vault ones); the README keeps the portal fallback of the cookbook.

### 6. Scripts

- **Initialize-Subscription.ps1**: as planned, and it also stops when another Cosmos DB account of the subscription already has the free tier (the irreversible part of step 5).
- **EntraApplications.psm1** (added): the shared find-or-create of an application (exact display name through a Graph `$filter`; it refuses duplicates and a wrong sign-in audience and warns about any secret or certificate), its service principal, and a federated credential (created, or updated when its issuer or subject changed). The module sets its own preference variables, as a module does not see those of the calling script.
- **New-ByokApplication.ps1** and **New-DeploymentApplication.ps1**: as planned; the second returns an object with `ClientId`, `PrincipalId`, `TenantId` and `SubscriptionId`.
- **Set-GitHubEnvironment.ps1**: as planned. It reads the outputs of the subscription deployment `prod-2s-main` (the name the README uses), leaves variables with empty values as they are, and writes the Static Web App token to the standard input of `gh secret set` through a process, so the token is neither an argument nor followed by a new line.

### 7. Order of a first run

`README.md` gives the six runs of the plan with their commands (PowerShell), the variables to set before each, and the checks. The deployment names are `prod-2s-main` and `prod-2s-domains`.

### 8. Documentation

- `src/Platform/Deployment/README.md` as planned.
- The cookbook's introduction points to the templates and says not to mix them with the manual steps for a new environment (the CLI names the Flex plan itself and can't turn on site-scoped certificates later).
- `CLAUDE.md` lists `Deployment/` in the Platform table.
- The cookbook, the spec of the environment and the decisions are committed unchanged apart from that introduction.

### 9. Validation

- `bicep build` of `main.bicep` and `domains.bicep`, and `bicep build-params` of both parameter files: no errors, no warnings (Bicep CLI 0.49.2, installed with `az bicep install`).
- The compiled template was checked for the site's properties (including `siteScopedCertificatesEnabled`), the app settings expression and the outputs.
- The scripts parse with the PowerShell 7.6 parser; PSScriptAnalyzer is not installed and was not run.
- Not done: `what-if` and a deployment (no subscription was touched).

### 10. Open points

- The managed certificate of the API through `domains.bicep` is unproven until the first run (section 5).
- The `aud` of the WorkOS JWT template is still the proposal `https://2s.42for.net` (cookbook, open points).
- The pipelines and the rights a pipeline needs to run `main.bicep` (spec, section 10), and `parameters/dev.bicepparam`, are follow-ups.
