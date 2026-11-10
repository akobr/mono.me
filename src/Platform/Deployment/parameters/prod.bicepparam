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
param authKitDomain = 'auth.42for.net'
param trustDomain = '2s.42for.net'

// The aud of the WorkOS JWT template (cookbook, step 13) is the default https://<trustDomain>; an open point until
// confirmed.

// Step 13: the WorkOS production client id (client_…).
param authKitClientId = ''
// true once the secret authkit-api-key is in the vault (invitation emails).
param authKitApiKeyEnabled = false

// Step 11.1: the application id of "2S Platform Customer Keys" (New-ByokApplication.ps1).
param byokClientId = ''
// Step 11.2: the object id of the service principal of "2S Platform Deployment" (New-DeploymentApplication.ps1).
param deploymentPrincipalId = ''

param ownerObjectId = readEnvironmentVariable('PLATFORM_OWNER_OBJECT_ID')
param ownerEmail = readEnvironmentVariable('PLATFORM_OWNER_EMAIL')
// No account-wide throughput limit until DbCreator has created core and organizations (a capped account refuses
// containers without explicit throughput); then 1000, the free tier (runbook, phase 11.2).
param cosmosTotalThroughputLimit = -1

// The Cosmos DB key in the secret cosmos-connection; switch it only to rotate the other key (cookbook, step 7).
param cosmosConnectionKey = 'primary'

param budgetAmount = 5
// The month of the first run; if that is a later month, set its first day before the run, then never change it.
param budgetStartDate = '2026-11-01'
