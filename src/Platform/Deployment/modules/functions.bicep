// Cookbook, step 8: the API on a Flex Consumption plan, .NET 10 isolated, 512 MB instances, at most 10 of them and no
// always-ready instance (nothing is billed when idle). It runs as the user-assigned identity only: storage, deployment
// storage and Key Vault references all use it, so no key of the storage account exists in any setting.
//
// The custom domain and its certificate are bound by domains.bicep (step 10). A PUT of the site keeps its host names.

param planName string
param appName string
param location string
param tags object

@description('The resource id of the API\'s managed identity.')
param identityId string

@description('The client id of the API\'s managed identity.')
param identityClientId string

param storageAccountName string
param storageBlobEndpoint string
param deploymentContainerName string

param appInsightsConnectionString string

param keyVaultName string
param keyVaultUri string
param cosmosSecretName string

@description('The host name of the API, e.g. api.2s.42for.net; final (decision 1).')
param apiHostName string

@description('The host name of the admin UI, the only CORS origin, e.g. app.2s.42for.net.')
param appHostName string

@description('The AuthKit domain: the custom domain (e.g. auth.42for.net) or the environment\'s <subdomain>.authkit.app; empty leaves Auth__AuthKit__AuthKitDomain out.')
param authKitDomain string

@description('The trust domain of Storyteller tokens, e.g. 2s.42for.net; final (decision 2).')
param trustDomain string

@description('The WorkOS production client id (client_…); the setting is left out while it is empty.')
param authKitClientId string

@description('The aud of the WorkOS JWT template.')
param authKitAudience string

@description('Adds Auth__AuthKit__ApiKey as a reference to the secret authkit-api-key, set by hand in the vault.')
param authKitApiKeyEnabled bool

@description('The application id of the BYOK application; the setting is left out while it is empty (an empty value would be used as a client id).')
param byokClientId string

@description('The machine credential kinds turned off until mTLS exists.')
param disabledCredentialKinds string[]

@description('AZURE_FUNCTIONS_ENVIRONMENT and DOTNET_ENVIRONMENT.')
param runtimeEnvironment string

@minValue(1)
@maxValue(1000)
param maximumInstanceCount int

@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int

@description('Always-ready instances of the HTTP functions (the http group); 0 keeps none.')
@minValue(0)
param alwaysReadyInstances int

// WorkOS issues user tokens (the admin UI's and the CLI's device flow) as the environment's AuthKit domain, the
// <subdomain>.authkit.app one or the custom one: the iss of its /.well-known/openid-configuration.
var authKitIssuer = 'https://${authKitDomain}'

var baseSettings = {
  AZURE_FUNCTIONS_ENVIRONMENT: runtimeEnvironment
  DOTNET_ENVIRONMENT: runtimeEnvironment
  AZURE_CLIENT_ID: identityClientId
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString

  // Storage by identity; Backend.AzureStorage reads the same section (accountName, clientId).
  AzureWebJobsStorage__accountName: storageAccountName
  AzureWebJobsStorage__credential: 'managedidentity'
  AzureWebJobsStorage__clientId: identityClientId

  CosmosDb__Connection: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${cosmosSecretName})'

  Auth__Provider: 'AuthKit'
  Auth__AuthKit__Issuer: authKitIssuer
  Auth__AuthKit__Audience: authKitAudience
  // Every signed-in user gets User.Impersonation and their roles decide the rest; without it every user gets 401.
  Auth__AuthKit__DefaultUserScopes__0: 'User.Impersonation'
  // No MCP server in this phase.
  Auth__TokenExchange__Enabled: 'false'

  MachineAuth__DefaultCredentialKind: 'ApiKey'
  // The API never reads X-ARR-ClientCert, so nobody can fake a certificate with it.
  MachineAuth__IsCertificateHeaderFromClientAllowed: 'false'
  MachineAuth__TrustDomain: trustDomain
  MachineAuth__Authority__Kind: 'Cosmos'
  MachineAuth__Federation__PublicBaseUrl: 'https://${apiHostName}/api'

  // Both vault names of the API point at the one Premium vault.
  AzureKeyVaults__default: keyVaultUri
  AzureKeyVaults__premium: keyVaultUri

  KeyValues__Protection__Emulator: 'false'
  KeyValues__Protection__Byok__ManagedIdentityClientId: identityClientId
}

var disabledKindSettings = toObject(
  range(0, length(disabledCredentialKinds)),
  i => 'MachineAuth__DisabledCredentialKinds__${i}',
  i => disabledCredentialKinds[i]
)

var authKitClientIdSetting = empty(authKitClientId) ? {} : { Auth__AuthKit__ClientId: authKitClientId }

var authKitDomainSetting = empty(authKitDomain) ? {} : { Auth__AuthKit__AuthKitDomain: 'https://${authKitDomain}' }

var authKitApiKeySetting = authKitApiKeyEnabled
  ? { Auth__AuthKit__ApiKey: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=authkit-api-key)' }
  : {}

var byokSetting = empty(byokClientId) ? {} : { KeyValues__Protection__Byok__ClientId: byokClientId }

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2025-03-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // mTLS comes in a later phase.
    clientCertEnabled: false
    keyVaultReferenceIdentity: identityId
    // Site-scoped certificates (the managed certificate of the API's domain) can only be turned on at creation. The
    // property is documented for Flex Consumption (functions-infrastructure-as-code, "Site-scoped certificates") but
    // missing from Bicep's type definitions of Microsoft.Web/sites.
    #disable-next-line BCP037
    siteScopedCertificatesEnabled: true
    siteConfig: {
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      cors: {
        allowedOrigins: [
          'https://${appHostName}'
        ]
        supportCredentials: false
      }
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageBlobEndpoint}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identityId
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
        alwaysReady: alwaysReadyInstances == 0
          ? []
          : [
              {
                name: 'http'
                instanceCount: alwaysReadyInstances
              }
            ]
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
}

// The template owns the whole set of app settings: a setting added by hand is removed by the next run.
resource appSettings 'Microsoft.Web/sites/config@2025-03-01' = {
  parent: site
  name: 'appsettings'
  properties: union(baseSettings, disabledKindSettings, authKitClientIdSetting, authKitDomainSetting, authKitApiKeySetting, byokSetting)
}

resource scmCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: site
  name: 'scm'
  properties: {
    allow: false
  }
}

resource ftpCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: site
  name: 'ftp'
  properties: {
    allow: false
  }
}

output id string = site.id
output name string = site.name
output defaultHostName string = site.properties.defaultHostName
output customDomainVerificationId string = site.properties.customDomainVerificationId
