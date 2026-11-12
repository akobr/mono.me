# Azure Production Environment of the 2S Platform

## Problem

The 2S Platform runs only on developer machines (the Aspire host with the Cosmos DB, Azurite and Lowkey Vault emulators). There is no environment in Azure, no infrastructure as code and no pipeline that deploys the API, the admin UI or the database schema, so the platform cannot be offered to its first companies.

The deployment has to be as cheap as possible (an Azure free account, the lifetime Cosmos DB free tier) and simple: **one environment** until the MVP is ready. That environment is created as **production from the first day** and is never promoted from something else. A development subscription is added later from the same templates (section 11).

The decisions taken in the conversation of 2026-11-08 and 2026-11-09:

- Region **Sweden Central** for every Azure resource.
- One subscription per environment, each with its **own free-tier Cosmos DB account**. Only production exists now.
- The API on the **Flex Consumption** plan of Azure Functions.
- **No mTLS at launch.** The certificate machine kinds are turned off by a deployment setting (spec [Turn Off Machine Credential Kinds per Deployment](../../Storyteller/specs/2026-11-09%20Turn%20Off%20Machine%20Credential%20Kinds%20per%20Deployment.md)); mTLS comes in a later phase.
- **HYOK** (client-side encryption) is the customer-held key option; no Managed HSM is provisioned.
- WorkOS **production** for AuthKit, Sentry and PostHog in their EU regions.

What has to be fixed before the first deployment, because it cannot be changed later, is listed in [Decisions that cannot be changed later](../decisions.md).

## Current State

**Compute and hosting**

- `src/Platform/Storyteller/Api.Functions/src` is an isolated-worker Azure Functions app on .NET 10 (`Program.cs`). It registers Cosmos DB (`AddCosmosDbAnnotations`), conditional reads, Key Vault key protection (`AddKeyVaultKeyProtection`), view transfers over Azure Storage (`AddAzureStorageViewTransfers`), API-key, certificate and federated machine access, the user provider chosen by `Auth:Provider` (AuthKit here), the user token exchange, AuthKit machine access, the bindings (Key Vault, key/values) and Application Insights (`AddApplicationInsightsTelemetryWorkerService`). The API has no Sentry SDK.
- Timer functions (`KeyValueProtectionTimer` hourly, `KeyValueUsageSnapshotTimer` daily) and the queue trigger of view transfers need `AzureWebJobsStorage` ([key-values.md](../../Storyteller/key-values.md#rotation-and-re-wrap)).
- The Linux Consumption plan does not run .NET 10, and Microsoft ends Functions support for .NET 8 and 9 on 2026-11-10. Flex Consumption runs .NET 10 isolated and is the recommended serverless plan.
- `Api.Functions/src/Dockerfile` copies project paths that no longer exist (`Abstraction`, `AzureAd`, `CosmosDb`); it is not used by this spec.
- `src/Platform/Storyteller/Mcp/src` is an ASP.NET Core app (`Microsoft.NET.Sdk.Web`). It is not part of the first deployment.
- `src/Platform/Scheduler` and `src/Platform/Supervisor` are not part of the first deployment.

**Data**

- `Backend.CosmosDb/src/CosmosConstants.cs`: the database is `42.Platform.2S`, with the container `core` and one container `org.{organization}` per organization. `ContainerFactory.BuildDatabase` creates the database with shared autoscale throughput (`CosmosDb:AutoscaleMaxThroughput`, 1000 by default) or manual throughput (`CosmosDb:ManualThroughput`).
- A database with shared throughput holds at most 25 containers. With `core`, that is 24 organizations in one environment.
- The Cosmos client authenticates with a connection string only (`CosmosDb:Connection` or `ConnectionStrings:cosmos`, `Backend.CosmosDb/src/EntryPoint.cs`), so the account key has to be stored as a secret.
- `DbCreator` (`src/Platform/Storyteller/DbCreator`) creates the database and containers (`2sInitDb`), applies the container settings (TTL on, indexing exclusions) on every start, and seeds the base account (`baseAccount:id`). It also seeds AuthKit staging accounts when `stagingSeed:enabled` is set (Aspire only).
- `Backend.AzureStorage/src/AzureStorageConnection.cs` accepts a connection string or an identity-based section (`accountName`, or `blobServiceUri` and `queueServiceUri`, plus `clientId`), so storage needs no key.

**Keys and secrets** ([key-values.md](../../Storyteller/key-values.md))

- Standard secrets are envelope-encrypted with a per-project RSA software key in `AzureKeyVaults:{KeyValues:Protection:Standard:VaultName}` (`default`), the HSM level with an RSA-HSM key in `AzureKeyVaults:{KeyValues:Protection:Hsm:VaultName}` (`premium`). The identity needs **Key Vault Crypto Officer** and **Key Vault Crypto Service Encryption User** on the vault.
- BYOK reaches the customer's vault as a multi-tenant Entra application (`KeyValues:Protection:Byok:ClientId`) whose federated credential is the API's user-assigned managed identity (`ManagedIdentityClientId`). A customer whose vault has a firewall needs a static egress IP of the platform.
- HYOK needs no infrastructure: values are encrypted on the client.
- Production uses `ManagedIdentityCredential`, then `EnvironmentCredential`; emulator mode fails the startup in Production.

**Machines and tokens** ([authentication.md](../../Storyteller/authentication.md))

- The API reads a client certificate only from the header `MachineAuth:CertificateHeaderName` (`X-ARR-ClientCert`), and only when `MachineAuth:IsCertificateHeaderFromClientAllowed` is true (`Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`). Locally `local.settings.json` turns it on.
- The machine certificate authority and the signing key of Storyteller tokens (`tsk.1`) are stored in the `core` container (`MachineAuth:Authority:Kind` `Cosmos`). Whoever reads that container can mint Storyteller tokens.
- `MachineAuth:TrustDomain` (`2s.platform` by default) derives `MachineAuth:Federation:Issuer` and `Audience`; changing them rejects every token already issued. `MachineAuth:Federation:PublicBaseUrl` builds the `TokenEndpoint` that federation rules pin as their audience.
- Every project's key/value policy defaults to `RequireMtlsForSecretDelivery = true`, so without mTLS no machine receives secret values.

**Admin UI** (`src/Platform/ui.admin/src`)

- A Vite SPA configured at build time: `VITE_API_BASE_URL`, `VITE_AUTH_API_HOSTNAME` (the custom AuthKit domain, required on every host except localhost), `VITE_ENVIRONMENT_NAME`, `VITE_SENTRY_DSN`, `VITE_POSTHOG_PROJECT_TOKEN`, `VITE_POSTHOG_HOST` (`.env.example`).
- The API's CORS must allow the admin origin ([authentication.md](../../Storyteller/authentication.md#cors-for-browser-clients)).

**CI**

- `.github/workflows/` has the docs deployment to a Static Web App, the conformance suite, the SDK release and the UI automation tests (`ui-admin-automation.yml`, which can target a deployed URL). There is no infrastructure or application deployment.

## Proposed Changes

### 1. Subscription, region and naming

- The existing free-account subscription is upgraded to pay-as-you-go and renamed **`prod-42fornet-sub`**. The upgrade keeps the remaining credit and the 12-month free services, and lifts the free-trial Flex Consumption quota (15 cores) to the standard one (250 cores).
- Everything lives in **Sweden Central** (`swedencentral`). Before the first deployment, `az functionapp list-flexconsumption-locations` confirms that Flex Consumption is offered there; the Static Web App has no region choice for static content.
- One resource group **`prod-2s-eusec-rg`**. Names follow `{environment}-{product}-{name}-{type}`; the resource group carries the region instead of a name (`eusec`, Europe: Sweden Central), and the globally unique names carry `42fornet`:

| Resource | Name |
| --- | --- |
| Resource group | `prod-2s-eusec-rg` |
| Cosmos DB account | `prod-2s-42fornet-cdb` |
| Function App and its Flex plan | `prod-2s-api-fa`, `prod-2s-api-asp` |
| Storage account | `prod2s42fornetsa` (Azure allows only lower-case letters and digits here) |
| Key Vault (Premium) | `prod-2s-42fornet-kv` (at most 24 characters) |
| User-assigned managed identity | `prod-2s-api-mi` |
| Log Analytics, Application Insights | `prod-2s-log`, `prod-2s-api-appi` |
| Static Web App | `prod-2s-app-swa` |

- Tags on every resource: `product=2s-platform`, `environment=prod`, `managedBy=bicep`.
- A budget on the subscription (5 USD a month, alerts at 50, 80 and 100 % to the owner's email), created by the templates.

### 2. Infrastructure as code (Bicep)

A new folder `src/Platform/Deployment/` (no .NET project, like `GitHubAction/`):

```
src/Platform/Deployment/
├── README.md
├── main.bicep                 # subscription scope: resource group, budget, then modules
├── modules/
│   ├── identity.bicep         # user-assigned managed identity
│   ├── monitoring.bicep       # Log Analytics (daily cap), Application Insights
│   ├── cosmos.bicep           # free-tier account, no database (DbCreator creates it)
│   ├── storage.bicep          # Storage account, deployment container, RBAC
│   ├── keyvault.bicep         # Premium vault, RBAC mode, purge protection, lock, secrets
│   ├── functions.bicep        # Flex plan, Function App, app settings, CORS, custom domain
│   └── staticwebapp.bicep     # Free Static Web App, custom domain
└── parameters/
    └── prod.bicepparam        # the only environment for now
```

- `main.bicep` takes `environment` (`prod`, later `dev`), `location` (`swedencentral`), the domain names, the AuthKit settings, `trustDomain`, the Sentry and PostHog public values, and the owner's object id and email for the budget and the Key Vault administrator role. Nothing environment-specific is hard-coded in a module, so adding development is one more `.bicepparam` (section 11).
- Secrets never go into parameter files. The Cosmos key is written to the vault by the template itself (`listKeys` of the account into a Key Vault secret); the AuthKit management key, if the deployment needs one, is set once by hand with `az keyvault secret set`.
- The templates are deployed with `az deployment sub create --location swedencentral --template-file main.bicep --parameters parameters/prod.bicepparam`; `what-if` runs first in the pipeline (section 9).

### 3. Cosmos DB

- One account `prod-2s-42fornet-cdb`, API for NoSQL, **free tier on** (`enableFreeTier: true`, chosen at creation and impossible to add later), capacity mode provisioned (the free tier does not apply to serverless), one region (Sweden Central, no availability zones: zone redundancy is charged), session consistency, **continuous backup with the 7-day tier** (free), public network access with the key (no private endpoint: Flex virtual network integration is not part of this phase).
- `disableLocalAuth` stays false, because the API authenticates with the key; Entra RBAC on the data plane is a later hardening step (a change of `Backend.CosmosDb` to accept an endpoint and a `TokenCredential`).
- The database `42.Platform.2S` is created by `DbCreator` with **shared autoscale 100–1000 RU/s** (`CosmosDb:AutoscaleMaxThroughput=1000`, the default): within the free tier, so it costs nothing. The 400 RU/s and 25 GB of the free account's first 12 months are not planned on.
- An Azure resource lock (`CanNotDelete`) on the account.
- **The container per organization is replaced before the first deployment** by one shared container `organizations` with the hierarchical partition key `/Organization`, `/PartitionKey` (decided on 2026-11-09; its own Storyteller spec follows the emulator probe, see [Hierarchical Partition Key Probe](../../Storyteller/cookbooks/hierarchical-partition-key-probe.md)). The database then holds two containers, `core` and `organizations`, whatever the number of organizations, so the limit of 25 containers of a shared database and the 500 containers of an account no longer apply.

### 4. Storage account

- `prod2s42fornetsa`: StorageV2, Standard LRS, TLS 1.2, no public blob access, **shared key access off** (`allowSharedKeyAccess: false`).
- Containers: `deployments` (the Flex deployment package) and `configuration-transfers` (archives); the queue `configuration-transfers` is created by the code (`ViewTransferNames`).
- The managed identity gets **Storage Blob Data Owner**, **Storage Queue Data Contributor** and **Storage Table Data Contributor** (the Functions host's timer leases and secrets use blobs and tables).
- The app uses identity-based connections, so no storage key exists in any setting:
  - `AzureWebJobsStorage__accountName=prod2s42fornetsa`, `AzureWebJobsStorage__credential=managedidentity`, `AzureWebJobsStorage__clientId=<identity client id>`;
  - `ConnectionStrings` is not used for storage; `AzureStorageConnection` reads the same `AzureWebJobsStorage` section (`accountName`, `clientId`).

### 5. Key Vault

- One vault `prod-2s-42fornet-kv`, SKU **Premium** (no base fee; software keys cost the same as in Standard; HSM keys cost only once a project allows the HSM level), **RBAC authorization**, soft delete 90 days, **purge protection on**, a `CanNotDelete` lock. Losing a project's key-encryption key loses every secret of that project, so the vault is never deleted or purged.
- Both vault names of the API point at it: `AzureKeyVaults__default` and `AzureKeyVaults__premium` = `https://prod-2s-42fornet-kv.vault.azure.net/`.
- Roles of the managed identity on the vault: **Key Vault Crypto Officer** (create keys and rotation policies), **Key Vault Crypto Service Encryption User** (wrap, unwrap), **Key Vault Secrets User** (the Key Vault references of the app settings and the `AzureKeyVaults` binding source). The owner gets **Key Vault Administrator**.
- Secrets in the vault: `cosmos-connection` (written by the template), and nothing else at the start.
- No Managed HSM (`KeyValues:Protection:OrganizationManagedHsms` stays empty).

### 6. Identity and BYOK

- The user-assigned managed identity `prod-2s-api-mi` is the only identity of the Function App (`keyVaultReferenceIdentity` set to it, `AZURE_CLIENT_ID` set to its client id).
- A multi-tenant Entra app registration **`2S Platform Customer Keys`** in the owner's tenant, created once by a script `src/Platform/Deployment/scripts/New-ByokApplication.ps1` (Bicep does not manage Entra applications): sign-in audience `AzureADMultipleOrgs`, no secret, no redirect URI, a federated identity credential with issuer `https://login.microsoftonline.com/<tenant>/v2.0`, subject the identity's object id and audience `api://AzureADTokenExchange`. Its application id goes to `KeyValues__Protection__Byok__ClientId`, the identity's client id to `KeyValues__Protection__Byok__ManagedIdentityClientId`.
- The app's display name and its publisher are what customers see when they consent, so publisher verification (a Microsoft Partner Network id) is started before the first BYOK customer.
- **No static egress IP** in this phase. A customer vault behind a firewall needs a virtual network, Flex virtual network integration and a NAT gateway with a public IP (about 35 USD a month); added with the first such customer.
- For Azure managed identity federation of machines, a second app registration **`2S Platform API`** (`requestedAccessTokenVersion: 2`) is created by the same script when the first Azure workload needs it ([authentication.md](../../Storyteller/authentication.md#azure-managed-identity)).

### 7. Function App (API)

- A Flex Consumption plan and app `prod-2s-api-fa`: Linux, runtime `dotnet-isolated` `10.0`, instance memory **512 MB**, maximum instances **10** (a cap on cost), **no always-ready instance** (no cost when idle; cold starts of a few seconds are accepted for the MVP), deployment storage the `deployments` container with the managed identity, HTTPS only, minimum TLS 1.2, FTP and basic publishing credentials off.
- **Incoming client certificates off** (`clientCertEnabled: false`).
- Custom domain **`api.2s.42for.net`** with a free managed certificate (site-scoped on Flex). The API's public base is `https://api.2s.42for.net/api`.
- CORS: `https://app.2s.42for.net` only, no credentials.
- Application Insights through `APPLICATIONINSIGHTS_CONNECTION_STRING`, with sampling of the worker (`host.json` keeps its settings).
- App settings (production):

| Setting | Value |
| --- | --- |
| `AZURE_FUNCTIONS_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | `Production` |
| `AZURE_CLIENT_ID` | the identity's client id |
| `AzureWebJobsStorage__accountName`, `__credential`, `__clientId` | section 4 |
| `CosmosDb__Connection` | `@Microsoft.KeyVault(VaultName=kv-…;SecretName=cosmos-connection)` |
| `Auth__Provider` | `AuthKit` |
| `Auth__AuthKit__ClientId`, `Auth__AuthKit__Issuer` | the WorkOS **production** client id and the custom AuthKit domain (`https://auth.42for.net`) |
| `Auth__TokenExchange__Enabled` | `false` (no MCP server yet) |
| `MachineAuth__DefaultCredentialKind` | `ApiKey` |
| `MachineAuth__DisabledCredentialKinds__0`, `__1` | `Certificate`, `CertificateAndApiKey` |
| `MachineAuth__IsCertificateHeaderFromClientAllowed` | `false` (the default, set explicitly) |
| `MachineAuth__TrustDomain` | the production trust domain `2s.42for.net` ([decisions](../decisions.md)) |
| `MachineAuth__Authority__Kind` | `Cosmos` |
| `MachineAuth__Federation__PublicBaseUrl` | `https://api.2s.42for.net/api` |
| `AzureKeyVaults__default`, `AzureKeyVaults__premium` | the vault URI |
| `KeyValues__Protection__Byok__ClientId`, `__ManagedIdentityClientId` | section 6 |
| `KeyValues__Protection__Emulator` | `false` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | from `monitoring.bicep` |

  `local.settings.json` and the Aspire host keep their local values; nothing of them is copied.
- The key/value pricing (`KeyValues:Pricing`) stays unset until prices are decided.

### 8. Monitoring

- Log Analytics `prod-2s-log` with a **daily cap of 0.15 GB** (within the 5 GB a month free) and 30-day retention; workspace-based Application Insights `prod-2s-api-appi`.
- Sentry (EU region, `de.sentry.io`): the project `2s-admin-ui` for the admin UI with `environment=production`. The API has no Sentry SDK; adding one is out of scope (its errors are in Application Insights). (Superseded on 2026-11-12: [Report the API Errors to Sentry](../../Storyteller/specs/2026-11-12%20Report%20the%20API%20Errors%20to%20Sentry.md) adds it, with the project `2s-api`.)
- PostHog (EU cloud, `https://eu.i.posthog.com`): the production project's token goes to the admin UI build. When development is added, it gets its own PostHog project, so production analytics never contain test traffic.
- An availability test is not created (charged); Sentry's uptime monitor on `GET https://api.2s.42for.net/api/v1/auth/configuration` is used instead.

### 9. Admin UI

- A Static Web App `prod-2s-app-swa`, **Free** plan, custom domain **`app.2s.42for.net`** (free certificate), no managed functions, with the UI's existing `public/staticwebapp.config.json` (its fallback and headers are reviewed for the production domains).
- The build in the pipeline sets `VITE_API_BASE_URL=https://api.2s.42for.net/api`, `VITE_AUTH_API_HOSTNAME=auth.42for.net`, `VITE_ENVIRONMENT_NAME=production`, `VITE_SENTRY_DSN`, `VITE_POSTHOG_PROJECT_TOKEN`, `VITE_POSTHOG_HOST`; these are public values, kept as GitHub environment variables.
- WorkOS production: the custom AuthKit domain `auth.42for.net` (a CNAME), the redirect URI `https://app.2s.42for.net/callback` (`app/boot/auth.ts`), the allowed origin `https://app.2s.42for.net`, and the sign-out redirect.

### 10. Pipelines

- A GitHub environment **`production`** with required reviewers (the owner) and the branch rule `main`.
- Deployment identity: an Entra app registration **`2S Platform Deployment`** with a federated credential for `repo:<owner>/<repo>:environment:production`, **Owner** on `prod-2s-eusec-rg` only while the templates assign roles (afterwards **Contributor** plus **Role Based Access Control Administrator** limited to the roles of sections 4–5), and **Contributor** on the subscription for the budget. No deployment secret exists; the workflows use `azure/login` with OIDC.
- `.github/workflows/platform-infrastructure.yml` (manual, `workflow_dispatch`, input `environment`): `az bicep build`, `what-if`, then `az deployment sub create`.
- `.github/workflows/platform-deploy.yml` (manual first; on a release tag later):
  1. build and test the API (`dotnet test --project` of the Storyteller projects touched, or the conformance workflow as a gate);
  2. `dotnet publish src/Platform/Storyteller/Api.Functions/src -c Release`, zip, deploy with `Azure/functions-action` (Flex one-deploy);
  3. run `DbCreator` (`2sInitDb`) from the runner with `CosmosDb__Connection` read from the vault by the deployment identity (**Key Vault Secrets User**) and `baseAccount__id` = the owner's AuthKit production user id; `stagingSeed__enabled=false`;
  4. build the admin UI with the variables of section 9 and deploy it with `Azure/static-web-apps-deploy` (the SWA deployment token as the only secret of the environment);
  5. smoke test: `GET /api/v1/auth/configuration` answers 200 with `Provider=AuthKit`, and the admin UI's `index.html` answers 200.
- The UI automation workflow (`ui-admin-automation.yml`) can target `https://app.2s.42for.net` with the Elementary set once a test organization and the user pool exist in WorkOS production; until development exists, those tests run in a dedicated test organization of production, which is deleted and re-created by the seeding, never in a customer organization.

### 11. Adding the development environment later

- A new subscription `dev-42fornet-sub` under the same billing account and tenant, with its own free-tier Cosmos account (one per subscription), created by the same templates with `parameters/dev.bicepparam`: `environment=dev`, the domains `api.dev.2s.42for.net` and `app.dev.2s.42for.net`, **WorkOS staging**, its own `TrustDomain`, its own BYOK app registration, its own PostHog project, `stagingSeed__enabled` allowed.
- Nothing of production is copied: development gets its own signing key and certificate authority (created by the first exchange and the first certificate in its own Cosmos account).
- The GitHub environment `development` without required reviewers, deployed on every push to `main`; production stays manual or tag-based.

### 12. Order of the first deployment

1. Fix the values of [Decisions that cannot be changed later](../decisions.md): domain, trust domain, WorkOS production setup, names.
2. Upgrade the subscription, register the providers (`Microsoft.Web`, `Microsoft.App`, `Microsoft.DocumentDB`, `Microsoft.KeyVault`, `Microsoft.Storage`, `Microsoft.Insights`, `Microsoft.OperationalInsights`, `Microsoft.Consumption`), check Flex availability in Sweden Central.
3. Create the deployment app registration and the GitHub environment.
4. Run `New-ByokApplication.ps1` (needs the identity, so after a first run of the templates with the identity module, or with the identity created by the script).
5. Run `platform-infrastructure.yml`.
6. Create the DNS records (`api`, `admin`, `auth` CNAMEs and the TXT validations); bind the custom domains (second run of the templates, which adds the bindings once the records validate).
7. Configure WorkOS production (section 9).
8. Run `platform-deploy.yml`.
9. Sign in to the admin UI as the base account, create the first organization, check that a Standard secret creates the project's key in the vault (**Settings → Secrets** shows it), and that creating a `Certificate` machine answers 400.

### 13. Out of scope (later phases)

- mTLS machines: a test Flex app with incoming client certificates (`clientCertEnabled=true`, `clientCertMode=Optional`) to check that the front end passes `X-ARR-ClientCert`; then `IsCertificateHeaderFromClientAllowed=true`, `DisabledCredentialKinds` emptied, and the projects' `RequireMtlsForSecretDelivery` working as designed. Fallbacks if Flex does not pass the header: Windows Consumption, or Container Apps with a parser of `X-Forwarded-Client-Cert`.
- The MCP server on Container Apps (consumption, scale to zero, image from GHCR) with `Auth:TokenExchange:Enabled=true` and its actor.
- Cosmos DB with Entra RBAC instead of the key; private endpoints; NAT gateway for BYOK firewalls.
- A Sentry SDK in the API; an always-ready instance; zone redundancy; a second region. (Superseded on 2026-11-12 for the Sentry SDK: [Report the API Errors to Sentry](../../Storyteller/specs/2026-11-12%20Report%20the%20API%20Errors%20to%20Sentry.md).)
- The admin UI hiding the turned-off kinds (it shows the API's 400 message today).

### 14. Expected cost

| Item | Monthly |
| --- | --- |
| Cosmos DB (free tier, autoscale up to 1000 RU/s, < 25 GB) | 0 |
| Flex Consumption (within 100,000 GB-s and 250,000 executions) | 0 |
| Static Web App Free | 0 |
| Storage account (LRS, a few MB, transactions) | < 1 USD |
| Key Vault (keys and operations; HSM keys only when a project allows them) | < 1 USD |
| Log Analytics and Application Insights (capped below the free 5 GB) | 0 |
| Total | about 0–2 USD |

What raises it: Cosmos throughput above 1000 RU/s or a second region, an always-ready instance, HSM keys (about 1 USD per key and month), a NAT gateway (about 35 USD), a Managed HSM (thousands of USD).
