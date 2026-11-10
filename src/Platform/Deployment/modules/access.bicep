// The roles of people and pipelines (the API's identity gets its roles in storage.bicep and keyvault.bicep):
// - the owner (cookbook, steps 6 and 7): Key Vault Administrator, and Storage Blob Data Contributor to look at blobs
//   with an Entra sign-in (shared keys are off);
// - the deployment identity of GitHub Actions (cookbook, step 11.2), once it exists, with the least it needs:
//   Website Contributor on the Function App (deploy the code), Key Vault Secrets User on the secret cosmos-connection
//   only (DbCreator), and Storage Blob Data Contributor on the container deployments only (the Flex package). It gets
//   nothing on the resource group, so it can't list the Cosmos DB keys or read the vault's other secrets.

@description('The object id of the owner (an Entra user).')
param ownerObjectId string

@description('The object id of the service principal of "2S Platform Deployment"; empty until step 11.2.')
param deploymentPrincipalId string

param storageAccountName string
param deploymentContainerName string
param keyVaultName string
param cosmosSecretName string
param functionAppName string

var roles = {
  keyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  websiteContributor: 'de139f84-1756-47ae-9be6-808fbbe84772'
}

var hasDeploymentPrincipal = !empty(deploymentPrincipalId)

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageAccountName

  resource blobService 'blobServices' existing = {
    name: 'default'

    resource deploymentContainer 'containers' existing = {
      name: deploymentContainerName
    }
  }
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName

  resource cosmosSecret 'secrets' existing = {
    name: cosmosSecretName
  }
}

resource functionApp 'Microsoft.Web/sites@2025-03-01' existing = {
  name: functionAppName
}

resource ownerVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, ownerObjectId, roles.keyVaultAdministrator)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultAdministrator)
    principalId: ownerObjectId
    principalType: 'User'
  }
}

resource ownerStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, ownerObjectId, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: ownerObjectId
    principalType: 'User'
  }
}

resource deploymentFunctionApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(functionApp.id, deploymentPrincipalId, roles.websiteContributor)
  scope: functionApp
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.websiteContributor)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource deploymentSecret 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(vault::cosmosSecret.id, deploymentPrincipalId, roles.keyVaultSecretsUser)
  scope: vault::cosmosSecret
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource deploymentPackages 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(storage::blobService::deploymentContainer.id, deploymentPrincipalId, roles.storageBlobDataContributor)
  scope: storage::blobService::deploymentContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}
