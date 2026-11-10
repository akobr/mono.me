// Cookbook, step 10: the custom domains of the API and the admin UI with free managed certificates. Run it once the DNS
// records of step 12 exist (the outputs dnsRecords of main.bicep):
//
//   az deployment group create --resource-group prod-2s-eusec-rg --template-file domains.bicep \
//     --parameters parameters/prod.domains.bicepparam
//
// The first run binds the host name without TLS (the managed certificate needs a bound, validated host name), issues
// the certificate and binds it with SNI. Once the domain is bound, set apiHostNameBound = true in the parameters file:
// later runs skip the step without TLS and write the binding with SNI and the certificate's current thumbprint
// directly, so HTTPS stays up. The certificates renew themselves.

import { environmentName, resourceNames, resourceTags } from 'naming.bicep'

param environment environmentName

@description('The region code in the name of the resource group.')
param regionCode string = 'eusec'

param product string = '2s'
param company string = '42fornet'

@description('The host name of the API, e.g. api.2s.42for.net.')
param apiHostName string

@description('The host name of the admin UI, e.g. app.2s.42for.net.')
param appHostName string

param location string = resourceGroup().location

@description('true once the API\'s host name is bound with its certificate: reruns then keep TLS on (no binding without TLS).')
param apiHostNameBound bool = false

var names = resourceNames(environment, product, company, regionCode)
var tags = resourceTags(environment)

resource functionApp 'Microsoft.Web/sites@2025-03-01' existing = {
  name: names.functionApp
}

resource staticWebApp 'Microsoft.Web/staticSites@2025-03-01' existing = {
  name: names.staticWebApp
}

// The API: bind the host name (validated by the TXT record asuid.<host>), issue the site-scoped managed certificate,
// then bind it with SNI.
module apiHostName_bind 'modules/hostname-binding.bicep' = if (!apiHostNameBound) {
  name: 'platform-api-hostname'
  params: {
    siteName: functionApp.name
    hostName: apiHostName
  }
}

resource apiCertificate 'Microsoft.Web/sites/certificates@2025-03-01' = {
  parent: functionApp
  name: apiHostName
  location: location
  tags: tags
  properties: {
    canonicalName: apiHostName
  }
  dependsOn: [
    apiHostName_bind
  ]
}

module apiHostName_tls 'modules/hostname-binding.bicep' = {
  name: 'platform-api-hostname-tls'
  params: {
    siteName: functionApp.name
    hostName: apiHostName
    sslState: 'SniEnabled'
    thumbprint: apiCertificate.properties.thumbprint
  }
}

// The admin UI: the CNAME app.<zone> -> the default host name validates the domain, and the free certificate follows.
resource appDomain 'Microsoft.Web/staticSites/customDomains@2025-03-01' = {
  parent: staticWebApp
  name: appHostName
  properties: {
    validationMethod: 'cname-delegation'
  }
}

output apiCertificateThumbprint string = apiCertificate.properties.thumbprint
output appDomainStatus string = appDomain.properties.status
