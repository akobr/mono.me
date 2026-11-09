// Cookbook, step 6: the storage of the Functions host (timers, leases, the deployment package) and of the export and
// import of views. Shared keys are off from the start: the Function App is created with identity-based storage and
// deployment storage, so the two-phase switch of cookbook step 8.4 is not needed.

param name string
param location string
param tags object

@description('The principal id of the API\'s managed identity.')
param identityPrincipalId string

var deploymentContainerName = 'deployments'

// The code creates the container (and the queue) itself with the identity too; creating it here only makes it visible.
var transfersContainerName = 'configuration-transfers'

var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
}

resource account 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    allowCrossTenantReplication: false
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: account
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource transfersContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: transfersContainerName
  properties: {
    publicAccess: 'None'
  }
}

// Blobs and tables: the Functions host's timer leases and keys; queues: the view transfers.
resource identityRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in items(roles): {
    name: guid(account.id, identityPrincipalId, roleId.value)
    scope: account
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId.value)
      principalId: identityPrincipalId
      principalType: 'ServicePrincipal'
    }
  }
]

output id string = account.id
output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
output deploymentContainerName string = deploymentContainer.name
