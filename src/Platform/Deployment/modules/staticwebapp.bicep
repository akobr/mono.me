// Cookbook, step 9: the admin UI on the Free plan. Static Web Apps have no Sweden Central location (and West Europe refuses new tenants): the location only
// holds the app's metadata, the content is served from the global edge. No repository link: the deployment uses the
// app's deployment token (the one secret of the GitHub environment, step 15).

param name string
param location string
param tags object

resource app 'Microsoft.Web/staticSites@2025-03-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
  }
}

output id string = app.id
output name string = app.name
output defaultHostName string = app.properties.defaultHostname
