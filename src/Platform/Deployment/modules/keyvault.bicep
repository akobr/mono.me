// Cookbook, step 7 (once, irreversible): one Premium vault for the projects' Standard (software) and HSM keys, and the
// secrets of the app settings. Purge protection can never be turned off, and every project's secrets depend on its key
// in this vault: never delete or purge the vault or one of its keys.

param name string
param location string
param tags object

@description('The principal id of the API\'s managed identity.')
param identityPrincipalId string

@description('The Cosmos DB account whose key becomes the secret cosmos-connection.')
param cosmosAccountName string

@description('The key of the account written to cosmos-connection; switched to rotate the other one without downtime.')
@allowed([
  'primary'
  'secondary'
])
param cosmosConnectionKey string = 'primary'

var cosmosSecretName = 'cosmos-connection'

var roles = {
  // create the projects' keys and rotation policies
  keyVaultCryptoOfficer: '14b46e9e-c2b7-41b4-b07b-48a6ebf60603'
  // wrap and unwrap the projects' data keys
  keyVaultCryptoServiceEncryptionUser: 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
  // the Key Vault references of the app settings and the AzureKeyVaults binding source
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'premium'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource lock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'do-not-delete'
  scope: vault
  properties: {
    level: 'CanNotDelete'
    notes: 'Every project\'s secrets are encrypted with its key in this vault; losing the key loses the secrets.'
  }
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2025-04-15' existing = {
  name: cosmosAccountName
}

// The API connects to Cosmos DB with the account key for now (Entra RBAC for Cosmos DB is a later hardening). The key
// goes from the account to the vault here and is never a parameter or an output.
resource cosmosSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: vault
  name: cosmosSecretName
  properties: {
    value: 'AccountEndpoint=${cosmos.properties.documentEndpoint};AccountKey=${cosmosConnectionKey == 'secondary' ? cosmos.listKeys().secondaryMasterKey : cosmos.listKeys().primaryMasterKey};'
    contentType: 'Cosmos DB connection string'
  }
}

resource identityRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in items(roles): {
    name: guid(vault.id, identityPrincipalId, roleId.value)
    scope: vault
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId.value)
      principalId: identityPrincipalId
      principalType: 'ServicePrincipal'
    }
  }
]

output id string = vault.id
output name string = vault.name
output uri string = vault.properties.vaultUri
output cosmosSecretName string = cosmosSecret.name
