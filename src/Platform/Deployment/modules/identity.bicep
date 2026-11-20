// Cookbook, step 3: the API's only identity. It reads the vault and the storage, and is the federated credential of
// the BYOK application: its principal id is the subject there, so the identity must never be re-created.
// main.bicep also creates the MCP server's own identity with this module (step 17a), with no role: its object id is the
// subject the API's actor mcp pins, so a re-created identity needs a run of main.bicep before the server works again,
// and, once the exchange application requires an assignment, New-TokenExchangeApplication.ps1 -McpIdentityName again.

param name string
param location string
param tags object

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: name
  location: location
  tags: tags
}

output id string = identity.id
output name string = identity.name
output clientId string = identity.properties.clientId
output principalId string = identity.properties.principalId
