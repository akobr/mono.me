// The roles of people and pipelines (the API's identity gets its roles in storage.bicep and keyvault.bicep):
// - the owner (cookbook, steps 6 and 7): Key Vault Administrator, and Storage Blob Data Contributor to look at blobs
//   with an Entra sign-in (shared keys are off);
// - the deployment identity of GitHub Actions (cookbook, step 11.2), once it exists: Contributor on the resource group
//   to deploy the code, Key Vault Secrets User for DbCreator's cosmos-connection, Storage Blob Data Contributor for the
//   Flex deployment package.

@description('The object id of the owner (an Entra user).')
param ownerObjectId string

@description('The object id of the service principal of "2S Platform Deployment"; empty until step 11.2.')
param deploymentPrincipalId string

param storageAccountName string
param keyVaultName string

var roles = {
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  keyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

var hasDeploymentPrincipal = !empty(deploymentPrincipalId)

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageAccountName
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
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

resource deploymentGroup 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(resourceGroup().id, deploymentPrincipalId, roles.contributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource deploymentVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(vault.id, deploymentPrincipalId, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource deploymentStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (hasDeploymentPrincipal) {
  name: guid(storage.id, deploymentPrincipalId, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: deploymentPrincipalId
    principalType: 'ServicePrincipal'
  }
}
