// The naming convention of the 2S Platform's Azure resources (docs/Platform/Deployment/decisions.md):
// {environment}-{product}-{name}-{type}; the resource group carries the region in place of the name, the globally
// unique names carry the company, and the storage account is the same parts without hyphens.

@export()
@description('The category of an environment: production now, development later.')
type environmentName = 'prod' | 'dev'

@export()
@description('Every resource name of one environment.')
type resourceNamesType = {
  resourceGroup: string
  budget: string
  identity: string
  logAnalytics: string
  appInsights: string
  cosmos: string
  storage: string
  keyVault: string
  functionPlan: string
  functionApp: string
  staticWebApp: string
}

@export()
@description('The names of one environment, e.g. resourceNames(\'prod\', \'2s\', \'42fornet\', \'eusec\').')
func resourceNames(environment environmentName, product string, company string, regionCode string) resourceNamesType => {
  resourceGroup: '${environment}-${product}-${regionCode}-rg'
  budget: '${environment}-${product}-budget'
  identity: '${environment}-${product}-api-mi'
  logAnalytics: '${environment}-${product}-log'
  appInsights: '${environment}-${product}-api-appi'
  cosmos: '${environment}-${product}-${company}-cdb'
  storage: toLower('${environment}${product}${company}sa')
  keyVault: '${environment}-${product}-${company}-kv'
  functionPlan: '${environment}-${product}-api-asp'
  functionApp: '${environment}-${product}-api-fa'
  staticWebApp: '${environment}-${product}-app-swa'
}

@export()
@description('The tags of every resource of an environment.')
func resourceTags(environment environmentName) object => {
  product: '2s-platform'
  environment: environment
  managedBy: 'bicep'
}
