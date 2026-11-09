// Cookbook, step 5 (once, irreversible): the subscription's free-tier Cosmos DB account.
//
// The free tier is chosen when the account is created and can never be added later, and a subscription has one
// free-tier account. Provisioned throughput (the free tier does not apply to serverless), one region without zone
// redundancy (charged), session consistency, continuous backup with the free 7-day tier.
//
// No database: DbCreator creates 42.Platform.2S with the containers core and organizations (step 16, after 424-230).
// Between this account and DbCreator, run the hierarchical partition key probe (step 5a).

param name string
param location string
param tags object

@description('The free tier; only the first Cosmos DB account of a subscription can have it, and only at creation.')
param enableFreeTier bool = true

resource account 'Microsoft.DocumentDB/databaseAccounts@2025-04-15' = {
  name: name
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: enableFreeTier
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    backupPolicy: {
      type: 'Continuous'
      continuousModeProperties: {
        tier: 'Continuous7Days'
      }
    }
    minimalTlsVersion: 'Tls12'
    publicNetworkAccess: 'Enabled'
    // The API connects with the account key (CosmosDb:Connection); Entra RBAC for the data plane is a later hardening.
    disableLocalAuth: false
    enableAutomaticFailover: false
    enableMultipleWriteLocations: false
  }
}

resource lock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'do-not-delete'
  scope: account
  properties: {
    level: 'CanNotDelete'
    notes: 'The free tier can only be chosen when an account is created, and there is one per subscription.'
  }
}

output id string = account.id
output name string = account.name
output endpoint string = account.properties.documentEndpoint
