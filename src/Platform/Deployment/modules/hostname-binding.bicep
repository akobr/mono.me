// One hostname binding of the Function App. A module, so that domains.bicep can write the same binding twice in one
// deployment: first without TLS (the managed certificate needs the bound host name), then with the certificate.

param siteName string
param hostName string

@allowed([
  'Disabled'
  'SniEnabled'
])
param sslState string = 'Disabled'

@description('The thumbprint of the certificate when sslState is SniEnabled.')
param thumbprint string = ''

resource site 'Microsoft.Web/sites@2025-03-01' existing = {
  name: siteName
}

resource binding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  parent: site
  name: hostName
  properties: {
    siteName: siteName
    hostNameType: 'Verified'
    customHostNameDnsRecordType: 'CName'
    sslState: sslState
    thumbprint: empty(thumbprint) ? null : thumbprint
  }
}
