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

@description('The aud of the WorkOS JWT template: the single audience, ignored by the API while authKitAudiences lists any.')
param authKitAudience string

@description('The audiences of the 2S WorkOS applications; empty leaves Auth__AuthKit__Audiences out.')
param authKitAudiences string[]

@description('Adds Auth__AuthKit__ApiKey as a reference to the secret authkit-api-key, set by hand in the vault.')
param authKitApiKeyEnabled bool

@description('The application id of the BYOK application; the setting is left out while it is empty (an empty value would be used as a client id).')
param byokClientId string

@description('The DSN of the Sentry project of the API (2s-api); the setting is left out while it is empty, and the API reports nothing.')
param sentryDsn string

@description('The organization of the exhibition project; the three Exhibition__ settings are left out unless all three parameters are set, and the API then has no exhibition.')
param exhibitionOrganization string

@description('The exhibition project.')
param exhibitionProject string

@description('The id of the account that owns the exhibition project: the --owner of its seed.')
param exhibitionOwner string

@description('Auth__StepUp__Enabled, \'true\' or \'false\'; the setting is left out while it is empty, and the API decides (on under AuthKit).')
@allowed([
  ''
  'true'
  'false'
])
param stepUpEnabled string

@description('Auth__StepUp__MaxAge in seconds, 60 to 86400; the setting is left out while it is null (10 minutes).')
@minValue(60)
@maxValue(86400)
param stepUpMaxAgeSeconds int?

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

@description('true: authKitDomain is the custom AuthKit domain and issues the user tokens. false: the WorkOS API issues them.')
param authKitCustomDomain bool

@description('The user token exchange for the MCP server (Auth:TokenExchange); false keeps it off with no actor.')
param tokenExchangeEnabled bool

@description('The id of the MCP server\'s actor, Mcp:Actor:Id of the server.')
param mcpActorId string

@description('The MCP resource URL, e.g. https://mcp.2s.42for.net/mcp: the only aud of the person\'s tokens the actor may exchange.')
param mcpResourceUrl string

@description('The object id of the MCP server\'s managed identity: the sub of its actor tokens.')
param mcpIdentityPrincipalId string

@description('The App ID URI of "2S Platform Token Exchange", api://<app id>: the aud of the actor tokens.')
param mcpExchangeAudience string

// Without a custom domain, the user tokens the admin UI and sform get from the WorkOS API (user_management/authenticate)
// carry iss https://api.workos.com/user_management/<client id> (checked on a production token, 2026-11-11).
var authKitIssuer = authKitCustomDomain ? 'https://${authKitDomain}' : 'https://api.workos.com/user_management/${authKitClientId}'

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

// The list wins over Auth__AuthKit__Audience in the API; the single value stays, so that a rollback to a build that reads
// only it still checks an audience.
var authKitAudienceSettings = toObject(
  range(0, length(authKitAudiences)),
  i => 'Auth__AuthKit__Audiences__${i}',
  i => authKitAudiences[i]
)

var authKitClientIdSetting = empty(authKitClientId) ? {} : { Auth__AuthKit__ClientId: authKitClientId }

var authKitDomainSetting = empty(authKitDomain) ? {} : { Auth__AuthKit__AuthKitDomain: 'https://${authKitDomain}' }

var authKitApiKeySetting = authKitApiKeyEnabled
  ? { Auth__AuthKit__ApiKey: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=authkit-api-key)' }
  : {}

// The user token exchange of the MCP server (docs/Platform/Storyteller/mcp.md, "Registering the actor in the API"). The
// server's sign-ins come from the AuthKit domain; while the API's issuer is another one (the WorkOS API, before the custom
// domain), the domain is an additional issuer, accepted for the exchange only. With the custom domain both are the same,
// and listing it would stop the API at start. The actor proves itself with a v1 token of its managed identity for the
// exchange application: issuer the tenant's v1 issuer, sub the identity's object id, aud the App ID URI; the API finds
// its keys through the issuer's discovery document (https://sts.windows.net/<tenant>/.well-known/openid-configuration).
// The defaults stay: Scopes User.Impersonation, NoSecretReveal (AllowSecrets off), any MCP client (dynamic
// registration), 10-minute tokens, actor tokens up to 24 hours old (Azure.Identity caches them).
var authKitDomainIssuer = 'https://${authKitDomain}'
var tokenExchangeIssuerSettings = !tokenExchangeEnabled || empty(authKitDomain) || authKitDomainIssuer == authKitIssuer
  ? {}
  : {
      Auth__TokenExchange__AdditionalIssuers__0__Issuer: authKitDomainIssuer
      Auth__TokenExchange__AdditionalIssuers__0__JwksUri: '${authKitDomainIssuer}/oauth2/jwks'
    }

var tokenExchangeSettings = tokenExchangeEnabled
  ? {
      Auth__TokenExchange__Enabled: 'true'
      Auth__TokenExchange__Actors__0__Id: mcpActorId
      Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Issuer: 'https://sts.windows.net/${tenant().tenantId}/'
      Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Subject: mcpIdentityPrincipalId
      Auth__TokenExchange__Actors__0__Authentication__WorkloadIdentity__Audience: mcpExchangeAudience
      Auth__TokenExchange__Actors__0__SubjectAudiences__0: mcpResourceUrl
    }
  : {
      // No MCP server: the exchange answers unsupported_grant_type.
      Auth__TokenExchange__Enabled: 'false'
    }

var byokSetting = empty(byokClientId) ? {} : { KeyValues__Protection__Byok__ClientId: byokClientId }

var sentryDsnSetting = empty(sentryDsn) ? {} : { Sentry__Dsn: sentryDsn }

// All three or none: the API stops at its start with one or two of them.
var exhibitionSettings = empty(exhibitionOrganization) || empty(exhibitionProject) || empty(exhibitionOwner)
  ? {}
  : {
      Exhibition__Organization: exhibitionOrganization
      Exhibition__Project: exhibitionProject
      Exhibition__Owner: exhibitionOwner
    }

// The step-up of users (Auth:StepUp): only what is set, so the API's defaults apply otherwise.
var stepUpEnabledSetting = empty(stepUpEnabled) ? {} : { Auth__StepUp__Enabled: stepUpEnabled }

// Written as the TimeSpan the API reads: hh:mm:ss, and 1.00:00:00 for 24 hours (24:00:00 would be 24 days).
var stepUpMaxAge = stepUpMaxAgeSeconds ?? 0
var stepUpMaxAgeTimeSpan = stepUpMaxAge == 86400
  ? '1.00:00:00'
  : '${padLeft(string(stepUpMaxAge / 3600), 2, '0')}:${padLeft(string((stepUpMaxAge % 3600) / 60), 2, '0')}:${padLeft(string(stepUpMaxAge % 60), 2, '0')}'

var stepUpMaxAgeSetting = stepUpMaxAgeSeconds == null ? {} : { Auth__StepUp__MaxAge: stepUpMaxAgeTimeSpan }

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
  properties: union(baseSettings, disabledKindSettings, authKitAudienceSettings, authKitClientIdSetting, authKitDomainSetting, authKitApiKeySetting, tokenExchangeSettings, tokenExchangeIssuerSettings, byokSetting, sentryDsnSetting, exhibitionSettings, stepUpEnabledSetting, stepUpMaxAgeSetting)
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
