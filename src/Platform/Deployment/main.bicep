// The Azure environment of the 2S Platform: steps 2-9 of docs/Platform/Deployment/cookbook-azure-production.md and the
// Azure roles of step 11. The custom domains (step 10) are domains.bicep, run once the DNS records exist.
//
//   az deployment sub what-if --location swedencentral --template-file main.bicep --parameters parameters/prod.bicepparam
//   az deployment sub create  --location swedencentral --template-file main.bicep --parameters parameters/prod.bicepparam
//
// See README.md for the order of a first run.

targetScope = 'subscription'

import { environmentName, resourceNames, resourceTags } from 'naming.bicep'

@description('The category of the environment.')
param environment environmentName

@description('The region of every resource but the Static Web App; final (decision 5).')
param location string = 'swedencentral'

@description('The region code in the name of the resource group (eusec: Europe, Sweden Central).')
param regionCode string = 'eusec'

@description('The product part of the names.')
param product string = '2s'

@description('The company part of the globally unique names.')
param company string = '42fornet'

@description('The location of the Static Web App\'s metadata only (Static Web Apps have no Sweden Central location, and West Europe refuses new tenants).')
param staticWebAppLocation string = 'eastus2'

@description('The DNS zone of the host names, for the DNS records of the outputs.')
param dnsZone string

@description('The host name of the API; final (decision 1).')
param apiHostName string

@description('The host name of the admin UI (decision 13).')
param appHostName string

@description('The AuthKit domain of WorkOS production: the custom domain (auth.42for.net) or, until it exists, the environment\'s <subdomain>.authkit.app; empty leaves Auth__AuthKit__AuthKitDomain out (decision 4).')
param authKitDomain string

@description('true when authKitDomain is the custom AuthKit domain: it is then the admin UI\'s AuthKit host. false: the admin UI keeps its session in localStorage, a stopgap until the custom domain exists.')
param authKitCustomDomain bool = true

@description('The trust domain of Storyteller tokens; final (decision 2).')
param trustDomain string

@description('The WorkOS production client id (client_…), known after step 13; the setting is left out while it is empty.')
param authKitClientId string = ''

@description('The aud of the WorkOS JWT template (step 13).')
param authKitAudience string = 'https://${trustDomain}'

@description('Adds Auth__AuthKit__ApiKey as a reference to the secret authkit-api-key, set by hand in the vault.')
param authKitApiKeyEnabled bool = false

@description('The application id of "2S Platform Customer Keys", known after step 11.1.')
param byokClientId string = ''

@description('The object id of the owner: Key Vault Administrator, Storage Blob Data Contributor.')
@minLength(36)
@maxLength(36)
param ownerObjectId string

@description('The email address of the budget\'s alerts.')
@minLength(3)
param ownerEmail string

@description('The object id of the service principal of "2S Platform Deployment", known after step 11.2.')
param deploymentPrincipalId string = ''

@description('The monthly budget of the subscription in its billing currency.')
@minValue(1)
param budgetAmount int = 5

@description('The first day of the budget\'s first month (yyyy-MM-01). Fixed once the budget exists: a budget can be created with the current or a future month, and its start date is not moved by later runs.')
param budgetStartDate string

@description('The Cosmos DB key the API uses (the secret cosmos-connection): primary or secondary, switched to rotate the other key (cookbook, step 7).')
@allowed([
  'primary'
  'secondary'
])
param cosmosConnectionKey string = 'primary'

@description('The account-wide throughput limit of Cosmos DB in RU/s, or -1 for none. A capped account refuses containers without explicit throughput, so it stays -1 until DbCreator has created the shared-throughput containers, then 1000 (the free tier).')
@minValue(-1)
param cosmosTotalThroughputLimit int = 1000

@description('The daily cap of Log Analytics in GB.')
param logDailyCapGb string = '0.15'

@description('The maximum number of instances of the API (a cap on cost).')
param functionMaximumInstanceCount int = 10

@description('The memory of an instance of the API in MB.')
param functionInstanceMemoryMB int = 512

@description('Always-ready instances of the API\'s HTTP functions: 0 (none, nothing billed when idle) or more (no cold starts, billed all the time, no free grants).')
@minValue(0)
param functionAlwaysReadyInstances int = 0

@description('The machine credential kinds turned off until mTLS exists (MachineAuth:DisabledCredentialKinds).')
param disabledCredentialKinds string[] = [
  'Certificate'
  'CertificateAndApiKey'
]

@description('AZURE_FUNCTIONS_ENVIRONMENT and DOTNET_ENVIRONMENT of the API.')
param runtimeEnvironment string = 'Production'

var names = resourceNames(environment, product, company, regionCode)
var tags = resourceTags(environment)

func relativeName(hostName string, zone string) string =>
  endsWith(hostName, '.${zone}') ? substring(hostName, 0, length(hostName) - length(zone) - 1) : hostName

// Step 2: the resource group and the budget.
resource group 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: names.resourceGroup
  location: location
  tags: tags
}

resource budget 'Microsoft.Consumption/budgets@2026-06-01' = {
  name: names.budget
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [ownerEmail]
      }
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ownerEmail]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [ownerEmail]
      }
      forecasted100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ownerEmail]
      }
    }
  }
}

// Step 3.
module identity 'modules/identity.bicep' = {
  name: 'platform-identity'
  scope: group
  params: {
    name: names.identity
    location: location
    tags: tags
  }
}

// Step 4.
module monitoring 'modules/monitoring.bicep' = {
  name: 'platform-monitoring'
  scope: group
  params: {
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
    location: location
    tags: tags
    dailyCapGb: logDailyCapGb
  }
}

// Step 5 (once, irreversible: the free tier).
module cosmos 'modules/cosmos.bicep' = {
  name: 'platform-cosmos'
  scope: group
  params: {
    name: names.cosmos
    location: location
    tags: tags
    totalThroughputLimit: cosmosTotalThroughputLimit
  }
}

// Step 6.
module storage 'modules/storage.bicep' = {
  name: 'platform-storage'
  scope: group
  params: {
    name: names.storage
    location: location
    tags: tags
    identityPrincipalId: identity.outputs.principalId
  }
}

// Step 7 (once, irreversible: purge protection).
module keyVault 'modules/keyvault.bicep' = {
  name: 'platform-keyvault'
  scope: group
  params: {
    name: names.keyVault
    location: location
    tags: tags
    identityPrincipalId: identity.outputs.principalId
    cosmosAccountName: cosmos.outputs.name
    cosmosConnectionKey: cosmosConnectionKey
  }
}

// Step 8.
module functions 'modules/functions.bicep' = {
  name: 'platform-functions'
  scope: group
  params: {
    planName: names.functionPlan
    appName: names.functionApp
    location: location
    tags: tags
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    storageAccountName: storage.outputs.name
    storageBlobEndpoint: storage.outputs.blobEndpoint
    deploymentContainerName: storage.outputs.deploymentContainerName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.name
    keyVaultUri: keyVault.outputs.uri
    cosmosSecretName: keyVault.outputs.cosmosSecretName
    apiHostName: apiHostName
    appHostName: appHostName
    authKitDomain: authKitDomain
    trustDomain: trustDomain
    authKitClientId: authKitClientId
    authKitAudience: authKitAudience
    authKitApiKeyEnabled: authKitApiKeyEnabled
    byokClientId: byokClientId
    disabledCredentialKinds: disabledCredentialKinds
    runtimeEnvironment: runtimeEnvironment
    maximumInstanceCount: functionMaximumInstanceCount
    instanceMemoryMB: functionInstanceMemoryMB
    alwaysReadyInstances: functionAlwaysReadyInstances
  }
}

// Step 9.
module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'platform-staticwebapp'
  scope: group
  params: {
    name: names.staticWebApp
    location: staticWebAppLocation
    tags: tags
  }
}

// Steps 6, 7 and 11.2: the owner's and the deployment identity's roles.
module access 'modules/access.bicep' = {
  name: 'platform-access'
  scope: group
  params: {
    ownerObjectId: ownerObjectId
    deploymentPrincipalId: deploymentPrincipalId
    storageAccountName: storage.outputs.name
    deploymentContainerName: storage.outputs.deploymentContainerName
    keyVaultName: keyVault.outputs.name
    cosmosSecretName: keyVault.outputs.cosmosSecretName
    functionAppName: functions.outputs.name
  }
}

output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
output resourceGroupName string = group.name
output names object = names

output identityId string = identity.outputs.id
output identityClientId string = identity.outputs.clientId
output identityPrincipalId string = identity.outputs.principalId

output keyVaultName string = keyVault.outputs.name
output keyVaultUri string = keyVault.outputs.uri
output cosmosAccountName string = cosmos.outputs.name
output storageAccountName string = storage.outputs.name

output functionAppName string = functions.outputs.name
output functionAppDefaultHostName string = functions.outputs.defaultHostName
output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppDefaultHostName string = staticWebApp.outputs.defaultHostName

// For the build of the admin UI (step 15).
output apiBaseUrl string = 'https://${apiHostName}/api'
output authKitDomain string = authKitDomain
// The admin UI's AuthKit host (AUTH_API_HOSTNAME): the custom domain, or empty without one.
output authApiHostname string = authKitCustomDomain ? authKitDomain : ''
// AUTH_LOCAL_STORAGE_SESSION of the admin UI: true only without a custom AuthKit domain.
output authLocalStorageSession bool = !authKitCustomDomain

@description('The records of step 12 in the zone dnsZone. The auth record (and its verification) comes from WorkOS.')
output dnsRecords object[] = [
  {
    name: relativeName(apiHostName, dnsZone)
    type: 'CNAME'
    value: functions.outputs.defaultHostName
  }
  {
    name: 'asuid.${relativeName(apiHostName, dnsZone)}'
    type: 'TXT'
    value: functions.outputs.customDomainVerificationId
  }
  {
    name: relativeName(appHostName, dnsZone)
    type: 'CNAME'
    value: staticWebApp.outputs.defaultHostName
  }
]
