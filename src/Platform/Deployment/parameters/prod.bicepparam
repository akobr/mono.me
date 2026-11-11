// Production of the 2S Platform, deployed to the subscription prod-42fornet-sub (decision 14).
// Every value of the environment is a literal here, including the ids that exist only after a later step: commit them
// as soon as they are known (they are not secrets). main.bicep owns the whole set of the API's app settings, so a run
// with an id left empty removes its setting. Only the owner's personal values come from variables
// (src/Platform/Deployment/README.md).

using '../main.bicep'

param environment = 'prod'
param location = 'swedencentral'
// Static Web Apps offer Central US, East US 2, West US 2, West Europe and East Asia; West Europe refuses new tenants
// (https://aka.ms/locationineligible). Only the app's metadata is there; the admin UI is served from the global edge.
param staticWebAppLocation = 'eastus2'
param regionCode = 'eusec'

param dnsZone = '42for.net'
param apiHostName = 'api.2s.42for.net'
param appHostName = 'app.2s.42for.net'
// No custom AuthKit domain yet (USD 99 a month): the issuer is the environment's AuthKit domain and the admin UI keeps
// its session in localStorage. authKitDomain is the environment's AuthKit domain (WorkOS dashboard, phase 5), committed once known.
// With the custom domain later: authKitDomain = 'auth.42for.net', authKitCustomDomain = true (runbook, "Adding the
// custom AuthKit domain").
param authKitDomain = 'palatial-omen-84.authkit.app'
param authKitCustomDomain = false
param trustDomain = '2s.42for.net'

// The aud of the WorkOS JWT template (cookbook, step 13) is the default https://<trustDomain>; an open point until
// confirmed.

// Step 13: the WorkOS production client id (client_…).
param authKitClientId = 'client_01M3MMVAP9HG798GFH9WS2NB8J'
// true once the secret authkit-api-key is in the vault (invitation emails).
param authKitApiKeyEnabled = true

// Step 11.1: the application id of "2S Platform Customer Keys" (New-ByokApplication.ps1).
param byokClientId = 'cb2da9e3-9316-4fd8-b428-7c7161bdd685'
// Step 11.2: the object id of the service principal of "2S Platform Deployment" (New-DeploymentApplication.ps1).
param deploymentPrincipalId = '2a9df1b7-89a4-42e5-b007-5fe1763b8898'

param ownerObjectId = readEnvironmentVariable('PLATFORM_OWNER_OBJECT_ID')
param ownerEmail = readEnvironmentVariable('PLATFORM_OWNER_EMAIL')
// No account-wide throughput limit until DbCreator has created core and organizations (a capped account refuses
// containers without explicit throughput); then 1000, the free tier (runbook, phase 11.2).
param cosmosTotalThroughputLimit = -1

// The Cosmos DB key in the secret cosmos-connection; switch it only to rotate the other key (cookbook, step 7).
param cosmosConnectionKey = 'primary'

// The credit period of the free account, until 2026-12-07: switch these back to 5, 0 and '0.15' on 7 December
// (runbook, "The credit period"). Only settings that can go back without a rebuild.
param budgetAmount = 50
param functionAlwaysReadyInstances = 1
param logDailyCapGb = '1'

// The month of the first run; if that is a later month, set its first day before the run, then never change it.
param budgetStartDate = '2026-11-01'
