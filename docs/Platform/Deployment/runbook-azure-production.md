# Runbook: Deploy the Production Environment of the 2S Platform

The whole first deployment of production, in order: what you run, what you do by hand in between, every `what-if` with what it must show, and the checks after each step. It runs the Bicep templates and scripts of [`src/Platform/Deployment`](../../../src/Platform/Deployment/README.md). For the reasons behind a setting, see the [cookbook](cookbook-azure-production.md): its step numbers are given in brackets (e.g. **[C5a]**).

Everything is PowerShell 7.4+ on Windows. Lines starting with `#` are comments; replace `<…>` with your values.

| Phase | What | Mostly | Waits |
| --- | --- | --- | --- |
| 0 | Prerequisites | you | — |
| 1 | The subscription | portal + script | provider registration, a few minutes |
| 2 | First run of `main.bicep` | template | about 10 minutes |
| 3 | The partition key probe | script | — |
| 4 | Entra applications, second run | scripts + template | — |
| 5 | WorkOS production | dashboard | — |
| 6 | DNS records | your DNS provider | propagation, minutes to an hour |
| 7 | Custom domains (`domains.bicep`) | template | certificate, up to 10 minutes |
| 8 | AuthKit client id, third run | template | — |
| 9 | Sentry and PostHog | dashboards | — |
| 10 | GitHub environment | script | — |
| 11 | **After the blockers are merged**: API, database, admin UI | build + deploy | — |
| 12 | Verify | checks | Cost Management, a day |

Phases 1–10 can be done now. Phase 11 waits for [424-230](https://linear.app/424fun/issue/424-230/store-all-organizations-in-one-shared-cosmos-db-container-remove-the) (shared `organizations` container) and [424-232](https://linear.app/424fun/issue/424-232/turn-off-machine-credential-kinds-per-deployment) (`MachineAuth:DisabledCredentialKinds`) on `main`.

## Reading a what-if

Every template run below is preceded by its `what-if`. Run it, compare with the **Expect** list of the step, and only then run `create`.

- **Stop** on any `Delete`, and on any `Modify` of the Cosmos DB account (`enableFreeTier`, `backupPolicy`, `locations`) or of the vault (`enablePurgeProtection`, `enableRbacAuthorization`, `sku`) after the first run.
- **Ignore** noise: what-if often lists properties of `Microsoft.Web/sites` and `serverfarms` that ARM fills in itself (`siteConfig.*`, `kind`, `reserved`) as changes, and values computed during the deployment (`listKeys()`, ids of new resources) as unknown.
- `--exclude-change-types NoChange Ignore` below keeps the output to what changes.

## 0. Prerequisites

**Accounts and rights**

- **Owner** of the subscription that becomes `prod-42fornet-sub`, and **Application Administrator** (or Global Administrator) of its Entra tenant.
- Access to the DNS zone of `42for.net`.
- The WorkOS dashboard (environment **Production**), Sentry (EU, `de.sentry.io`) and PostHog (EU cloud).
- Admin of the GitHub repository `akobr/mono.me`.

**Tools**

```powershell
$PSVersionTable.PSVersion          # 7.4 or newer
az version                         # azure-cli 2.71 or newer
az bicep install; az bicep upgrade
gh --version
# Only to store the WorkOS API key from the shell (phase 8; the portal needs nothing):
Install-Module Az.Accounts, Az.KeyVault -Scope CurrentUser   # Connect-AzAccount, Set-AzKeyVaultSecret
dotnet --version                   # 10.0.401 or newer (phase 3 and 11)
node --version                     # v24 (phase 11, the admin UI)
```

**The checkout.** Until [PR #183](https://github.com/akobr/mono.me/pull/183) is merged, work on its branch; afterwards on a short branch from `main` (the parameters file gets commits in phases 4 and 8):

```powershell
git fetch origin
git switch feat/424-231; git pull                                   # while PR #183 is open
# or, after it is merged: git switch --no-track -c chore/prod-environment-ids origin/main   (no pull: origin/main is fresh)
cd src/Platform/Deployment
```

**Every new terminal** needs these two variables (the only personal values; the repository doesn't hold them):

```powershell
az login
az account set --subscription prod-42fornet-sub     # after phase 1; az login may pick another subscription
$env:PLATFORM_OWNER_EMAIL = '<your email for the budget alerts>'
$env:PLATFORM_OWNER_OBJECT_ID = az ad signed-in-user show --query id -o tsv
```

Check that the parameters compile:

```powershell
az bicep build-params --file parameters/prod.bicepparam --stdout > $null; "ok"
```

**Before the first run, check `budgetStartDate`** in `parameters/prod.bicepparam`: it must be the first day of the current month (or a later one). Fix and commit it if the month has changed since `2026-11-01`; afterwards never change it.

## 1. The subscription [C1]

**By hand, in the portal:**

1. **Subscriptions → your free subscription → Upgrade** to pay-as-you-go (keeps the credit and the 12-month free services; lifts the Flex Consumption quota).
2. **Rename** it to `prod-42fornet-sub`.
3. Make sure **no Cosmos DB account exists** in it (the free tier goes to the first one).

**Run:**

```powershell
./scripts/Initialize-Subscription.ps1 -Subscription prod-42fornet-sub
```

**Expect:** the providers registered, `Flex Consumption is offered in swedencentral.`, and `No Cosmos DB account has the free tier yet`. If it stops, don't continue: a missing Flex region means the region decision is revisited; a taken free tier means another account has to go first.

## 2. First run of `main.bicep` [C2–C9]

Creates the resource group, the budget, the identity, Log Analytics and Application Insights, Cosmos DB (**free tier, irreversible**), the storage account, the Key Vault (**purge protection, irreversible**), the Function App and the Static Web App.

**What-if:**

```powershell
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect: only `Create`**, about 25 resources:

- `prod-2s-eusec-rg`, the budget `prod-2s-budget` (5, alerts to your email);
- `prod-2s-api-mi`, `prod-2s-log`, `prod-2s-api-appi`;
- `prod-2s-42fornet-cdb` with **`enableFreeTier: true`**, `backupPolicy.type: Continuous`, `capacity.totalThroughputLimit: -1` (no limit until phase 11.2), and the lock `do-not-delete`;
- `prod2s42fornetsa` with `allowSharedKeyAccess: false`, containers `deployments` and `configuration-transfers`, three role assignments;
- `prod-2s-42fornet-kv` with `sku.name: premium`, `enablePurgeProtection: true`, `enableRbacAuthorization: true`, the lock, the secret `cosmos-connection`, three role assignments;
- `prod-2s-api-asp` (`FC1`), `prod-2s-api-fa` with `siteScopedCertificatesEnabled: true` and its `appsettings` (**no** `Auth__AuthKit__ClientId` and **no** `KeyValues__Protection__Byok__ClientId` yet), two `basicPublishingCredentialsPolicies`;
- `prod-2s-app-swa` (Free, `eastus2`: metadata only, West Europe, the only European one, refuses new tenants);
- two role assignments for you (Key Vault Administrator, Storage Blob Data Contributor).

**Run:**

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

If it fails part-way, read the error, fix the cause and run the same command again: every step is idempotent. Never delete the resource group (the locks refuse it anyway).

**Check:**

```powershell
az cosmosdb show -g prod-2s-eusec-rg -n prod-2s-42fornet-cdb `
  --query "{freeTier:enableFreeTier, backup:backupPolicy.type, limit:capacity.totalThroughputLimit}" -o table   # True Continuous -1
az keyvault show -n prod-2s-42fornet-kv `
  --query "{purge:properties.enablePurgeProtection, rbac:properties.enableRbacAuthorization, sku:properties.sku.name}" -o table   # True True premium
az storage account show -n prod2s42fornetsa --query allowSharedKeyAccess    # false
az lock list -g prod-2s-eusec-rg -o table                                  # the account and the vault
az consumption budget show --budget-name prod-2s-budget --query "{amount:amount, start:timePeriod.startDate}" -o table
```

In the portal (**Cost Management → Budgets**) the budget shows four alert conditions. The Function App has no code yet; a few `403` logs right after its first start are role assignments still propagating.

## 3. The hierarchical partition key probe [C5a]

Once, **before DbCreator ever runs**, while the account has no database. The probe comes with 424-230; until it is merged, run it from a worktree of that branch (from the repository root):

```powershell
cd ../../..                                            # the repository root
git fetch origin feat/424-230
git worktree add ../mono.me-hpk-probe origin/feat/424-230
Push-Location ../mono.me-hpk-probe

$key = az cosmosdb keys list -g prod-2s-eusec-rg -n prod-2s-42fornet-cdb --query primaryMasterKey -o tsv
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- `
  --endpoint "https://prod-2s-42fornet-cdb.documents.azure.com:443/" --key $key --label "Azure, Sweden Central" |
  Tee-Object -FilePath ../hpk-probe-azure-swedencentral.txt
Remove-Variable key

Pop-Location
git worktree remove ../mono.me-hpk-probe
cd src/Platform/Deployment
```

**Expect:** `19 passed, 0 failed`. **Then:** attach `hpk-probe-azure-swedencentral.txt` to 424-230, and check that the probe removed its database (its 1000 RU/s and DbCreator's 1000 RU/s together would leave the free tier):

```powershell
az cosmosdb sql database list -g prod-2s-eusec-rg -a prod-2s-42fornet-cdb --query "[].name" -o tsv   # nothing
```

## 4. Entra applications and the second run [C11]

**Run:**

```powershell
./scripts/New-ByokApplication.ps1                       # prints the application id
$deployment = ./scripts/New-DeploymentApplication.ps1
$deployment                                             # ClientId, PrincipalId, TenantId, SubscriptionId
```

**By hand:**

1. In `parameters/prod.bicepparam` set `byokClientId = '<application id of 2S Platform Customer Keys>'` and `deploymentPrincipalId = '<$deployment.PrincipalId>'`.
2. Commit and push: `git commit -am "chore(platform): the Entra application ids of production (424-231)"`, `git push -u origin HEAD` (the first push of a new branch sets its upstream).
3. Note `$deployment.ClientId` for phase 10 (running the script again prints it again).
4. In the Entra admin center, **App registrations → 2S Platform Customer Keys → Branding & properties**: start **publisher verification** (a Microsoft Partner Network id) before the first BYOK customer.

**What-if:**

```powershell
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect:**

- `Modify` of `prod-2s-api-fa/appsettings`: `+ KeyValues__Protection__Byok__ClientId`, nothing removed;
- `Create` of three role assignments for the deployment identity: Website Contributor on `prod-2s-api-fa`, Key Vault Secrets User on `…/secrets/cosmos-connection`, Storage Blob Data Contributor on `…/containers/deployments`;
- nothing else (a `Modify` of the budget or the site without a real value change is noise).

**Run:**

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

**Check:**

```powershell
az ad app federated-credential list --id <BYOK application id> --query "[].{name:name, subject:subject}" -o table
az role assignment list --assignee $deployment.PrincipalId --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

## 5. WorkOS production [C13]

**By hand, WorkOS dashboard, environment Production:**

1. **No custom domain yet** (about 99 USD a month; added before the first customer, [424-247](https://linear.app/424fun/issue/424-247/add-the-custom-authkit-domain-auth42fornet-before-the-first-customer)). Note the environment's **AuthKit domain** (`<subdomain>.authkit.app`, under **Domains**) for `authKitDomain` in phase 8. Until the custom domain exists, the issuer the API checks is `https://api.workos.com/` and the admin UI keeps its session in `localStorage` ([decisions](decisions.md#without-the-custom-authkit-domain-until-the-first-customer)).
2. **Applications**: name the default application `2S Platform` and note its **client id** (`client_…`): phase 8 and phase 11. Later 42for.net products get their own applications in this environment (one login, one account).
3. **JWT template**:

   ```json
   {
     "aud": "https://2s.42for.net",
     "email": {{ user.email }},
     "email_verified": {{ user.email_verified }},
     "name": "{{ user.first_name }} {{ user.last_name }}"
   }
   ```

   `aud` must equal the API's `Auth__AuthKit__Audience` (`https://2s.42for.net`, the default of `main.bicep`). The template is shared by every application of the environment; before a second product signs users in, the API accepts only the audiences of the 2S applications, and enabling Multiple Applications changes the `aud` ([decisions](decisions.md#one-workos-environment-for-the-products-of-42fornet)). Don't enable Multiple Applications in production before that change.
4. **Redirects**: redirect URI `https://app.2s.42for.net/callback`, sign-out URI `https://app.2s.42for.net/login`, allowed origin `https://app.2s.42for.net`.
5. **CLI Auth** on.
6. **Invitation link**: `https://app.2s.42for.net/invitations/accept`.
7. **Your own account**: sign up once in production; note its user id (`user_01…`): `baseAccount__id` in phase 11 and `-BaseAccountId` in phase 10.
8. Optional, for invitation emails: create an **API key** (`sk_…`) and keep it for phase 8.

## 6. DNS records [C12]

**Get the Azure records:**

```powershell
az deployment sub show --name prod-2s-main --query properties.outputs.dnsRecords.value -o table
```

**By hand, in the zone `42for.net`** (TTL 300 s until everything works):

| Name | Type | Value |
| --- | --- | --- |
| `api.2s` | CNAME | `prod-2s-api-fa.<…>.azurewebsites.net` (output) |
| `asuid.api.2s` | TXT | the `customDomainVerificationId` (output) |
| `app.2s` | CNAME | `<…>.azurestaticapps.net` (output) |
| | | any verification record WorkOS asks for |

**Wait until they resolve:**

```powershell
Resolve-DnsName api.2s.42for.net -Type CNAME -Server 1.1.1.1
Resolve-DnsName asuid.api.2s.42for.net -Type TXT -Server 1.1.1.1
Resolve-DnsName app.2s.42for.net -Type CNAME -Server 1.1.1.1
```

(The `auth` record comes with the custom AuthKit domain later, see "Adding the custom AuthKit domain".)

## 7. Custom domains with `domains.bicep` [C10]

**What-if:**

```powershell
az deployment group what-if --name prod-2s-domains --resource-group prod-2s-eusec-rg `
  --template-file domains.bicep --parameters parameters/prod.domains.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect: only `Create`**: `prod-2s-api-fa/api.2s.42for.net` (hostname binding), `prod-2s-api-fa/api.2s.42for.net` (certificate), `prod-2s-app-swa/app.2s.42for.net` (custom domain). Nested binding deployments may show as unknown.

**Run:**

```powershell
az deployment group create --name prod-2s-domains --resource-group prod-2s-eusec-rg `
  --template-file domains.bicep --parameters parameters/prod.domains.bicepparam
```

The certificate takes up to 10 minutes. **If ARM refuses the managed certificate** of the Function App, add the domain in the portal instead: **prod-2s-api-fa → Custom domains → Add custom domain**, "App Service Managed Certificate"; the Static Web App's domain from the template stays.

**Check:**

```powershell
curl.exe -sI https://api.2s.42for.net/api/v1/auth/configuration   # any HTTP status (404/503 before phase 11), no TLS error
curl.exe -sI https://app.2s.42for.net                             # TLS works (content comes in phase 11)
az staticwebapp hostname list -g prod-2s-eusec-rg -n prod-2s-app-swa --query "[].{name:name, status:status}" -o table   # Ready
```

The first run binds the host name without TLS before the certificate exists. **If the certificate or the SNI binding fails, `api.2s.42for.net` stays without TLS** until you fix the cause and run `domains.bicep` again, or bind the certificate in the portal (above); check with the `curl.exe` below. A later run reads the current binding and keeps an existing SNI binding, so it doesn't switch TLS off.

## 8. AuthKit client id (and API key), third run

**By hand:**

1. In `parameters/prod.bicepparam` set `authKitClientId = '<client_…>'` and `authKitDomain = '<subdomain>.authkit.app'` (phase 5; `authKitCustomDomain` stays `false`).
2. Optional, invitation emails: store the WorkOS API key as the secret `authkit-api-key` and set `authKitApiKeyEnabled = true`. the key never goes on a command line (it would stay in the shell history and the process list). Either in the portal: **prod-2s-42fornet-kv → Objects → Secrets → Generate/Import**, name `authkit-api-key`, paste the `sk_…` value (you are Key Vault Administrator); or with Az PowerShell, which prompts for it:

   ```powershell
   Connect-AzAccount -Subscription prod-42fornet-sub   # once, if Az PowerShell isn't signed in
   Set-AzKeyVaultSecret -VaultName prod-2s-42fornet-kv -Name authkit-api-key `
     -SecretValue (Read-Host -AsSecureString -Prompt 'WorkOS API key (sk_…)') | Out-Null
   ```

3. Commit and push: `git commit -am "chore(platform): the WorkOS production client id (424-231)"`, `git push -u origin HEAD`.

**What-if:**

```powershell
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore
```

**Expect:** only a `Modify` of `prod-2s-api-fa/appsettings`: `+ Auth__AuthKit__ClientId` (and `+ Auth__AuthKit__ApiKey` with the Key Vault reference if enabled). **`KeyValues__Protection__Byok__ClientId` must stay**; if the what-if removes it, your checkout is missing the commit of phase 4.

**Run:**

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
```

**Check:**

```powershell
az functionapp config appsettings list -g prod-2s-eusec-rg -n prod-2s-api-fa --query "[].name" -o tsv | Sort-Object
```

## 9. Sentry and PostHog [C14]

**By hand:**

- **Sentry** (EU): a project `2s-admin-ui`; note its **DSN**. Add an **uptime monitor** on `https://api.2s.42for.net/api/v1/auth/configuration` (expects 200; it turns green after phase 11).
- **PostHog** (EU cloud): the production project's **token** (`phc_…`); the host is `https://eu.i.posthog.com`.

## 10. GitHub environment `prod.42.Platform` [C15]

**Run:**

```powershell
gh auth login                                           # an admin of akobr/mono.me
./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId '<$deployment.ClientId>' -BaseAccountId '<user_01…>' `
  -SentryDsn '<dsn>' -PostHogProjectToken '<phc_…>'
```

**Check** in **Settings → Environments → prod.42.Platform**: required reviewer you, deployment branch `main` only, the variables of the cookbook's step 15 and the secret `AZURE_STATIC_WEB_APPS_API_TOKEN`.

The infrastructure is done. Steps 11–12 deploy the code.

**Merge the parameter commits into `main` now**: PR #183, or a pull request of the branch with the commits of phases 4 and 8. Phase 11 and every later run of `main.bicep` start from `main`; a `main` without those ids would remove the BYOK and AuthKit app settings at its next run (check with what-if: no `-` in `appsettings`).

## 11. The code (after 424-230 and 424-232 are merged) [C16]

From an up-to-date `main` (with the parameter commits) at the repository root:

```powershell
git switch main; git pull
```

Each build-and-deploy below is **one script block** (`& { … }`): paste it whole. In it, a failing command (`$PSNativeCommandUseErrorActionPreference`, PowerShell 7.4+: a nonzero exit of `dotnet`, `npm` or `az` throws) stops the whole block, so an old or half-built output is never deployed. Lines pasted one by one would not stop: the console runs the next line after an error. A cleanup that can't delete an old output stops the block too; only a missing path is skipped.

### 11.1 The API

```powershell
& {
  $ErrorActionPreference = 'Stop'
  $PSNativeCommandUseErrorActionPreference = $true
  foreach ($path in '.publish/api', '.publish/api.zip') {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
  }

  dotnet publish src/Platform/Storyteller/Api.Functions/src -c Release -o .publish/api
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory("$PWD/.publish/api", "$PWD/.publish/api.zip")
  az functionapp deployment source config-zip -g prod-2s-eusec-rg -n prod-2s-api-fa --src .publish/api.zip
}
```

**Check:** `curl.exe -s https://api.2s.42for.net/api/v1/auth/configuration` returns `200` with `"Provider":"AuthKit"`.

### 11.2 The database (DbCreator, once)

Creates `42.Platform.2S` (autoscale up to 1000 RU/s), the containers `core` and `organizations`, and the organization `42dotnet` with you as its owner.

```powershell
& {
  $ErrorActionPreference = 'Stop'
  $PSNativeCommandUseErrorActionPreference = $true
  try {
    $env:CosmosDb__Connection = az keyvault secret show --vault-name prod-2s-42fornet-kv --name cosmos-connection --query value -o tsv
    $env:CosmosDb__ShouldAcceptAnyCertificate = 'false'    # app.config.json accepts any certificate (the local emulator's)
    $env:baseAccount__id = '<user_01…>'                    # your WorkOS production user id (phase 5)
    $env:stagingSeed__enabled = 'false'                    # never true here: it seeds the staging test accounts
    dotnet run --project src/Platform/Storyteller/DbCreator/src -c Release
  }
  finally {
    Remove-Item Env:CosmosDb__Connection, Env:CosmosDb__ShouldAcceptAnyCertificate, Env:baseAccount__id, Env:stagingSeed__enabled `
      -ErrorAction SilentlyContinue
  }
}
```

**Check:**

```powershell
az cosmosdb sql container list -g prod-2s-eusec-rg -a prod-2s-42fornet-cdb -d 42.Platform.2S --query "[].name" -o tsv   # core, organizations
```

**Then cap the account** at the free tier, now that the containers exist (a capped account refuses containers without explicit throughput, so the limit couldn't be on before): set `cosmosTotalThroughputLimit = 1000` in `src/Platform/Deployment/parameters/prod.bicepparam`, commit it to `main` through a pull request, and run `main.bicep` (with the two `PLATFORM_OWNER_*` variables of phase 0):

```powershell
Push-Location src/Platform/Deployment
az deployment sub what-if --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam `
  --exclude-change-types NoChange Ignore      # only: Modify prod-2s-42fornet-cdb, capacity.totalThroughputLimit -1 => 1000
```

When the what-if shows only that change:

```powershell
az deployment sub create --name prod-2s-main --location swedencentral `
  --template-file main.bicep --parameters parameters/prod.bicepparam
Pop-Location
az cosmosdb show -g prod-2s-eusec-rg -n prod-2s-42fornet-cdb --query capacity.totalThroughputLimit   # 1000
```

A later schema change that adds a container needs the limit back at `-1` for the run of DbCreator.

### 11.3 The admin UI

```powershell
& {
  $ErrorActionPreference = 'Stop'
  $PSNativeCommandUseErrorActionPreference = $true
  Push-Location src/Platform/Storyteller/sdk.typescript/src
  try { npm ci; npm run build } finally { Pop-Location }

  Push-Location src/Platform/ui.admin/src
  try {
    if (Test-Path dist) { Remove-Item dist -Recurse -Force }
    npm ci
    npm run build

    # Removed again by the finally below.
    $env:API_BASE_URL = 'https://api.2s.42for.net/api'
    Remove-Item Env:AUTH_API_HOSTNAME -ErrorAction SilentlyContinue   # a value left in the session would win over the switch
    $env:AUTH_LOCAL_STORAGE_SESSION = 'true'                # no custom AuthKit domain yet; with it: $env:AUTH_API_HOSTNAME = 'auth.42for.net'
    $env:ENVIRONMENT_NAME = 'production'
    $env:SENTRY_DSN = '<dsn>'
    $env:POSTHOG_PROJECT_TOKEN = '<phc_…>'
    $env:POSTHOG_HOST = 'https://eu.i.posthog.com'
    $env:RELEASE = git rev-parse HEAD
    node build/write-environment-config.ts
    if (Select-String -Path dist/staticwebapp.config.json -Pattern '__(API_ORIGIN|AUTH_HOSTNAME|SENTRY_ORIGIN|POSTHOG_ORIGIN)__') {
      throw 'dist/staticwebapp.config.json still has CSP placeholders.'
    }

    # The SWA CLI reads the token from SWA_CLI_DEPLOYMENT_TOKEN, so it is never a command-line argument.
    $env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -g prod-2s-eusec-rg -n prod-2s-app-swa --query properties.apiKey -o tsv
    if (-not $env:SWA_CLI_DEPLOYMENT_TOKEN) { throw 'The Static Web App returned no deployment token.' }
    npx @azure/static-web-apps-cli deploy ./dist --env production
  }
  finally {
    Remove-Item Env:API_BASE_URL, Env:AUTH_API_HOSTNAME, Env:AUTH_LOCAL_STORAGE_SESSION, Env:ENVIRONMENT_NAME, Env:SENTRY_DSN, Env:POSTHOG_PROJECT_TOKEN, `
      Env:POSTHOG_HOST, Env:RELEASE, Env:SWA_CLI_DEPLOYMENT_TOKEN -ErrorAction SilentlyContinue
    Pop-Location
  }
}
```

## 12. Verify [C17]

```powershell
curl.exe -s https://api.2s.42for.net/api/v1/auth/configuration          # 200, "Provider":"AuthKit", "AuthKitDomain":"https://<subdomain>.authkit.app"
curl.exe -sI http://api.2s.42for.net/api/v1/auth/configuration         # a redirect to https
curl.exe -sI -X OPTIONS -H "Origin: https://app.2s.42for.net" -H "Access-Control-Request-Method: GET" `
  https://api.2s.42for.net/api/v1/auth/configuration                    # Access-Control-Allow-Origin: https://app.2s.42for.net
az storage account show -n prod2s42fornetsa --query allowSharedKeyAccess # false
az lock list -g prod-2s-eusec-rg -o table                               # the account and the vault
```

**By hand:**

- Open `https://app.2s.42for.net` and sign in with your production account: the organization `42dotnet` is listed.
- Create a **test** organization and project (never a customer's), and a Standard secret: **Settings → Secrets** shows the project's key, and `az keyvault key list --vault-name prod-2s-42fornet-kv --query "[].name" -o tsv` lists a `kek-…` key.
- **Settings → Machines**: enabling `Certificate` answers `400` "… is turned off in this deployment."
- Application Insights (`prod-2s-api-appi`), **Logs → traces**: `KeyValueProtectionTimer` runs hourly without errors.
- Sentry: the uptime monitor is green.
- **A day later**, Cost Management: Cosmos DB at 0.

Then tick the acceptance criteria of [424-231](https://linear.app/424fun/issue/424-231/create-the-production-environment-of-the-2s-platform-in-azure).

## The credit period (until 7 December 2026)

While the free account's credit lasts, `parameters/prod.bicepparam` raises three settings, to use the credit and to see what the platform costs: `budgetAmount = 50`, `functionAlwaysReadyInstances = 1` (one always-ready instance of the API's HTTP functions: no cold starts, billed all the time, and no free grants of Flex Consumption while it is on) and `logDailyCapGb = '1'`. Each goes back without a rebuild.

**On 7 December**: set `budgetAmount = 5`, `functionAlwaysReadyInstances = 0`, `logDailyCapGb = '0.15'`; commit through a pull request; what-if (expect `Modify` of the budget's amount, the site's `alwaysReady`, and the workspace's `dailyQuotaGb`, nothing else); create. Also remove what was added by hand for the period (the Application Insights availability test, the Azure Load Testing resource, the Defender for Cloud plans) and check Cost Management for the month.

## Adding the custom AuthKit domain (before the first customer)

Tracked in [424-247](https://linear.app/424fun/issue/424-247/add-the-custom-authkit-domain-auth42fornet-before-the-first-customer). Everyone signs in again once afterwards; no data changes.

1. WorkOS (production): **Domains → Configure AuthKit domain** `auth.42for.net`; create the records it shows in the zone `42for.net` (an `auth` CNAME and any verification record) and wait for **Verified**.
2. `parameters/prod.bicepparam`: `authKitDomain = 'auth.42for.net'`, `authKitCustomDomain = true`; commit through a pull request; what-if (expect `Auth__AuthKit__Issuer` and `Auth__AuthKit__AuthKitDomain` → `https://auth.42for.net`), then create.
3. The admin UI: `AUTH_API_HOSTNAME=auth.42for.net` and no `AUTH_LOCAL_STORAGE_SESSION` (phase 11.3, or the GitHub environment), then build and deploy it again.
4. Check: the discovery answers `"AuthKitDomain":"https://auth.42for.net"`, the sign-in goes through `auth.42for.net`, the browser's `localStorage` holds no AuthKit refresh token, and `sform` signs in.

## Later

- Every later infrastructure change: what-if, then create, from an up-to-date checkout, with the two `PLATFORM_OWNER_*` variables. The template owns the app settings: add a setting to `modules/functions.bicep`, never in the portal.
- Rotating the Cosmos DB key: cookbook, step 7.
- The workflows `platform-infrastructure.yml` and `platform-deploy.yml` replace phases 2–8 and 11 later (spec, section 10).
