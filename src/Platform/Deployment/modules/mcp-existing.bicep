// The MCP server's Container App as it runs now, read before main.bicep writes it again (the pattern of the Azure
// Developer CLI). Its image is the one Platform.Storyteller.Mcp.yml deployed, and its custom domain carries the
// certificate the platform bound: a later run of main.bicep keeps both instead of setting them back. A module of its own,
// because a template that reads a resource it also deploys would read what it writes.

param appName string

@description('false before the first run that creates the app (mcpAppExists of the parameters): nothing is read.')
param exists bool

resource app 'Microsoft.App/containerApps@2026-07-01' existing = if (exists) {
  name: appName
}

output image string = exists ? app!.properties.template.containers[0].image : ''
output customDomains array = exists ? (app!.properties.configuration.ingress.?customDomains ?? []) : []
