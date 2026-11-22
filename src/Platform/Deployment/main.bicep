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

@description('The aud of the WorkOS JWT template (step 13): Auth__AuthKit__Audience, the single audience. Ignored by the API while authKitAudiences lists any; it stays for a rollback to a build that reads only it.')
param authKitAudience string = 'https://${trustDomain}'

@description('The audiences of the 2S WorkOS applications (Auth__AuthKit__Audiences__<n>, 424-239): user tokens must carry one of them, and authKitAudience no longer counts. Empty until Multiple Applications is enabled in WorkOS production (runbook, "Enabling Multiple Applications in WorkOS").')
param authKitAudiences string[] = []

@description('Adds Auth__AuthKit__ApiKey as a reference to the secret authkit-api-key, set by hand in the vault.')
param authKitApiKeyEnabled bool = false

@description('The application id of "2S Platform Customer Keys", known after step 11.1.')
param byokClientId string = ''

@description('The DSN of the Sentry project of the API (2s-api), known after step 14; the setting is left out while it is empty.')
param sentryDsn string = ''

@description('The organization of the exhibition, the demo project that the admin UI offers to a new user; set with exhibitionProject and exhibitionOwner, after its seed (runbook, "Turning the exhibition on"). The three Exhibition__ settings are left out unless all three are set.')
param exhibitionOrganization string = ''

@description('The exhibition project.')
param exhibitionProject string = ''

@description('The id of the account that owns the exhibition project: the --owner of its seed.')
param exhibitionOwner string = ''

@description('Auth__StepUp__Enabled: whether secrets, roles and machine credentials need a recent sign-in of users. Empty leaves the setting out, and the API decides (on under AuthKit); \'false\' is the escape hatch when a client cannot satisfy the step-up.')
@allowed([
  ''
  'true'
  'false'
])
param stepUpEnabled string = ''

@description('Auth__StepUp__MaxAge in seconds: the oldest sign-in that still counts, from 60 (1 minute) to 86400 (24 hours), the range the API accepts at start; the template writes it as a TimeSpan. Null leaves the setting out (10 minutes).')
@minValue(60)
@maxValue(86400)
param stepUpMaxAgeSeconds int?

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

@description('The MCP server on Container Apps (docs/Platform/Deployment/runbook-mcp-server.md): its identity, environment and app, and the API\'s user token exchange with the server as its actor. false (the default) creates none of it and keeps the exchange off.')
param mcpEnabled bool = false

@description('The host name of the MCP server, e.g. mcp.2s.42for.net; its resource URL is https://<host>/mcp. Required with mcpEnabled.')
param mcpHostName string = ''

@description('The application id of "2S Platform Token Exchange" (scripts/New-TokenExchangeApplication.ps1): the server\'s actor tokens are issued for api://<this id>. Required with mcpEnabled.')
param mcpExchangeAppId string = ''

@description('The image of the run that creates the Container App, e.g. ghcr.io/akobr/2s-platform-mcp:<commit>. Once the app exists (mcpAppExists), the template keeps the image it runs and this is ignored.')
param mcpInitialImage string = ''

@description('true once the Container App exists: the template then keeps the image that Platform.Storyteller.Mcp.yml deployed, and the binding of its custom domain. false only for the run that creates it.')
param mcpAppExists bool = false

@description('Binds mcpHostName to the MCP server with a free managed certificate; true only once its CNAME and TXT records (the output dnsRecords) exist. false (the default) leaves the app on its default host name, and removes a bound domain.')
param mcpCustomDomain bool = false

@description('The scope MCP clients request (Mcp:Authentication:Scope): openid, since dynamically registered clients get no WorkOS permission; the API is the gate.')
param mcpScope string = 'openid'

@description('The hard gate of the actor mcp (Auth:TokenExchange:Actors:0:AllowedClientIds): with entries, the exchange refuses every other client, and a sign-in that names no client. Empty (the default) accepts any client, which dynamic registration needs; set to mcpTrustedClientIds, it refuses every dynamically registered client (runbook, "Cut off every unlisted client").')
param mcpAllowedClientIds string[] = []

@description('The MCP clients that act with the actor\'s own policy only (Auth:TokenExchange:Actors:0:TrustedClientIds), compared exactly: the ids the API recorded for clients with a Client ID Metadata Document of web redirects only, never a dynamically registered id (runbook, "Trust a client").')
param mcpTrustedClientIds string[] = []

@description('The restrictions of every MCP client outside mcpTrustedClientIds, also of a sign-in that names no client (Auth:TokenExchange:Actors:0:UntrustedClientRestrictions): ReadOnly refuses every change, NoSecretReveal every secret value.')
@allowed([
  'ReadOnly'
  'NoSecretReveal'
])
param mcpUntrustedClientRestrictions string[] = [
  'ReadOnly'
  'NoSecretReveal'
]

@description('The clients the exchange refuses for every actor, even an allowed or trusted one (Auth:TokenExchange:DeniedClientIds), compared ignoring case: the switch for one client with a stable id, not for a dynamically registered one, which registers again (runbook, "Cut off one client").')
param mcpDeniedClientIds string[] = []

@description('The fewest replicas of the MCP server: 0 (the default) scales it to zero without calls, inside the free grant, with a cold start of a few seconds on the next call; 1 keeps one replica, billed at the idle rate between calls (about 4-5 USD a month).')
@minValue(0)
@maxValue(10)
param mcpMinReplicas int = 0

@description('The most replicas of the MCP server (a cap on cost).')
@minValue(1)
@maxValue(10)
param mcpMaxReplicas int = 3

var names = resourceNames(environment, product, company, regionCode)
var tags = resourceTags(environment)

// The MCP server's actor at the API: Mcp:Actor:Id of the server, Auth:TokenExchange:Actors:0:Id of the API.
var mcpActorId = 'mcp'

// Checked when the server is on, before anything is written: a missing value would give the API an actor that can never
// prove itself, or the server a setting that stops it.
var mcpResourceUrl = !mcpEnabled
  ? ''
  : empty(mcpHostName) ? fail('mcpHostName is required with mcpEnabled, e.g. mcp.2s.42for.net.') : 'https://${mcpHostName}/mcp'
var mcpExchangeAudience = !mcpEnabled
  ? ''
  : length(mcpExchangeAppId) != 36 || length(split(mcpExchangeAppId, '-')) != 5
      ? fail('mcpExchangeAppId must be the application id (a GUID) of "2S Platform Token Exchange" with mcpEnabled: New-TokenExchangeApplication.ps1 prints it.')
      : 'api://${mcpExchangeAppId}'
var mcpAuthKitIssuer = !mcpEnabled
  ? ''
  : empty(authKitDomain) ? fail('authKitDomain is required with mcpEnabled: people sign in to the MCP server there.') : 'https://${authKitDomain}'
var mcpCustomDomainChecked = mcpEnabled && mcpCustomDomain && !mcpAppExists
  ? fail('mcpCustomDomain needs mcpAppExists = true: bind the domain on a later run, once the app and its DNS records exist.')
  : mcpCustomDomain
var mcpInitialImageChecked = mcpEnabled && !mcpAppExists && empty(mcpInitialImage)
  ? fail('mcpInitialImage is required for the run that creates the MCP server\'s Container App (mcpAppExists = false).')
  : mcpInitialImage
// A blank client id would stop the API at start, and with it the admin UI and sform: the deployment fails instead.
var mcpAllowedClientIdsChecked = mcpEnabled && hasBlankEntry(mcpAllowedClientIds)
  ? fail('mcpAllowedClientIds entries must not be blank with mcpEnabled: the API would not start.')
  : mcpAllowedClientIds
var mcpTrustedClientIdsChecked = mcpEnabled && hasBlankEntry(mcpTrustedClientIds)
  ? fail('mcpTrustedClientIds entries must not be blank with mcpEnabled: the API would not start.')
  : mcpTrustedClientIds
var mcpDeniedClientIdsChecked = mcpEnabled && hasBlankEntry(mcpDeniedClientIds)
  ? fail('mcpDeniedClientIds entries must not be blank with mcpEnabled: the API would not start.')
  : mcpDeniedClientIds

func hasBlankEntry(values string[]) bool => !empty(filter(values, value => empty(trim(value))))

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

// The MCP server's own identity, separate from the API's: its only use is to prove the server to the API as the actor
// mcp. It has no Azure role. Created before the API's settings, which name its object id.
module mcpIdentity 'modules/identity.bicep' = if (mcpEnabled) {
  name: 'platform-mcp-identity'
  scope: group
  params: {
    name: names.mcpIdentity
    location: location
    tags: tags
  }
}

// The app as it runs, read before it is written again.
module mcpExisting 'modules/mcp-existing.bicep' = if (mcpEnabled) {
  name: 'platform-mcp-existing'
  scope: group
  params: {
    appName: names.mcpApp
    exists: mcpAppExists
  }
}

module mcp 'modules/mcp.bicep' = if (mcpEnabled) {
  name: 'platform-mcp'
  scope: group
  params: {
    environmentName: names.containerAppsEnvironment
    appName: names.mcpApp
    location: location
    tags: tags
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    image: mcpAppExists ? mcpExisting!.outputs.image : mcpInitialImageChecked
    hostName: mcpHostName
    customDomainEnabled: mcpCustomDomainChecked
    existingCustomDomains: mcpExisting!.outputs.customDomains
    identityId: mcpIdentity!.outputs.id
    identityClientId: mcpIdentity!.outputs.clientId
    resourceUrl: mcpResourceUrl
    authKitIssuer: mcpAuthKitIssuer
    scope: mcpScope
    actorId: mcpActorId
    exchangeAudience: mcpExchangeAudience
    apiBaseUrl: 'https://${apiHostName}/api'
    minReplicas: mcpMinReplicas > mcpMaxReplicas ? fail('mcpMinReplicas must not exceed mcpMaxReplicas.') : mcpMinReplicas
    maxReplicas: mcpMaxReplicas
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
    authKitCustomDomain: authKitCustomDomain
    trustDomain: trustDomain
    authKitClientId: authKitClientId
    authKitAudience: authKitAudience
    authKitAudiences: authKitAudiences
    authKitApiKeyEnabled: authKitApiKeyEnabled
    byokClientId: byokClientId
    sentryDsn: sentryDsn
    exhibitionOrganization: exhibitionOrganization
    exhibitionProject: exhibitionProject
    exhibitionOwner: exhibitionOwner
    stepUpEnabled: stepUpEnabled
    stepUpMaxAgeSeconds: stepUpMaxAgeSeconds
    disabledCredentialKinds: disabledCredentialKinds
    runtimeEnvironment: runtimeEnvironment
    maximumInstanceCount: functionMaximumInstanceCount
    instanceMemoryMB: functionInstanceMemoryMB
    alwaysReadyInstances: functionAlwaysReadyInstances
    tokenExchangeEnabled: mcpEnabled
    mcpActorId: mcpActorId
    mcpResourceUrl: mcpResourceUrl
    mcpIdentityPrincipalId: mcpEnabled ? mcpIdentity!.outputs.principalId : ''
    mcpExchangeAudience: mcpExchangeAudience
    mcpAllowedClientIds: mcpAllowedClientIdsChecked
    mcpTrustedClientIds: mcpTrustedClientIdsChecked
    mcpUntrustedClientRestrictions: mcpUntrustedClientRestrictions
    mcpDeniedClientIds: mcpDeniedClientIdsChecked
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
    // The name always, so the skipped role assignment never has an empty scope; the module's output makes the role wait
    // for the app.
    mcpEnabled: mcpEnabled
    mcpAppName: mcpEnabled ? mcp!.outputs.appName : names.mcpApp
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

// The MCP server; empty while mcpEnabled is false. mcpAppName is AZURE_MCP_APP_NAME of the GitHub environment.
output mcpAppName string = mcpEnabled ? mcp!.outputs.appName : ''
output mcpDefaultHostName string = mcpEnabled ? mcp!.outputs.defaultHostName : ''
output mcpResourceUrl string = mcpResourceUrl
output mcpIdentityClientId string = mcpEnabled ? mcpIdentity!.outputs.clientId : ''
output mcpIdentityPrincipalId string = mcpEnabled ? mcpIdentity!.outputs.principalId : ''

// The MCP server's records, once it exists: the CNAME to the app and the TXT record that validates the custom domain.
var mcpDnsRecords = mcpEnabled
  ? [
      {
        name: relativeName(mcpHostName, dnsZone)
        type: 'CNAME'
        value: mcp!.outputs.defaultHostName
      }
      {
        name: 'asuid.${relativeName(mcpHostName, dnsZone)}'
        type: 'TXT'
        value: mcp!.outputs.customDomainVerificationId
      }
    ]
  : []

@description('The records of step 12 in the zone dnsZone, and the MCP server\'s with mcpEnabled. The auth record (and its verification) comes from WorkOS.')
output dnsRecords object[] = concat([
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
], mcpDnsRecords)
