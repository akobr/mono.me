// The custom domains of production (cookbook, step 10), once the DNS records of step 12 exist.

using '../domains.bicep'

param environment = 'prod'
param regionCode = 'eusec'
param apiHostName = 'api.2s.42for.net'
param appHostName = 'app.2s.42for.net'
