// Production of the 2S Platform, deployed to the subscription prod-42fornet-sub (decision 14).
// The decided values are literals; personal values and values known only after a later step come from variables, so
// the repository holds none of them and the file needs no edit between the runs (src/Platform/Deployment/README.md).

using '../main.bicep'

param environment = 'prod'
param location = 'swedencentral'
param regionCode = 'eusec'

param dnsZone = '42for.net'
param apiHostName = 'api.2s.42for.net'
param appHostName = 'app.2s.42for.net'
param authKitDomain = 'auth.42for.net'
param trustDomain = '2s.42for.net'

// The aud of the WorkOS JWT template (cookbook, step 13); an open point until confirmed.
param authKitAudience = 'https://2s.42for.net'
param authKitClientId = readEnvironmentVariable('PLATFORM_AUTHKIT_CLIENT_ID', '')
param authKitApiKeyEnabled = bool(readEnvironmentVariable('PLATFORM_AUTHKIT_API_KEY_ENABLED', 'false'))

param byokClientId = readEnvironmentVariable('PLATFORM_BYOK_CLIENT_ID', '')
param deploymentPrincipalId = readEnvironmentVariable('PLATFORM_DEPLOYMENT_PRINCIPAL_ID', '')

param ownerObjectId = readEnvironmentVariable('PLATFORM_OWNER_OBJECT_ID')
param ownerEmail = readEnvironmentVariable('PLATFORM_OWNER_EMAIL')
param budgetAmount = 5
