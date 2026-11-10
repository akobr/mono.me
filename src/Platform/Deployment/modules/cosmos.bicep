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

// With a limit, Cosmos DB refuses to create a container without explicit throughput, so the shared-throughput
// containers of DbCreator (and of the probe) can't be created under it: the limit is turned on after them.
@description('The most throughput the account accepts in RU/s (1000: the free tier), or -1 for no limit. -1 until DbCreator has created the containers.')
@minValue(-1)
param totalThroughputLimit int = 1000

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
    // The sum of every database's and container's throughput (autoscale counts its maximum) can't exceed it; -1 is
    // no limit.
    capacity: {
      totalThroughputLimit: totalThroughputLimit
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
