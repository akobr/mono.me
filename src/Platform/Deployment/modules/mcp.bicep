// The MCP server (docs/Platform/Deployment/runbook-mcp-server.md): a Container Apps environment (workload profiles, the
// Consumption profile only) and the server's Container App, scaled to zero between calls (0.25 vCPU, 0.5 GiB, at most
// maxReplicas replicas), inside the monthly free grant of Container Apps. The environment has no base fee, no virtual
// network and no zone redundancy; its console and system logs go to the platform's Log Analytics workspace, under its
// daily cap.
//
// The server holds no data and no secret. It signs people in at the AuthKit domain and proves itself to the API with a
// token of its own managed identity (the actor mcp), which has no Azure role. Every setting is an environment variable
// here, validated by the server at start (src/Platform/Storyteller/Mcp/src/Hosting/McpHostOptionsValidator.cs): a wrong
// one stops the container and names the setting.
//
// The custom domain is here, not in domains.bicep: a Container App has no binding resource of its own, so every write of
// the app must carry its domains, or it drops them. With customDomainEnabled (once the DNS records exist) the app gets
// the host name with the binding type Auto and the environment issues a free managed certificate for it, which the
// platform then binds by itself (SNI). A later run writes the binding back as it is (existingCustomDomains).

param environmentName string
param appName string
param location string
param tags object

@description('The resource id of the platform\'s Log Analytics workspace (monitoring.bicep).')
param logAnalyticsWorkspaceId string

@description('The image, e.g. ghcr.io/akobr/2s-platform-mcp:<commit>; main.bicep passes the one the app runs, once it exists.')
param image string

@description('The host name of the server, e.g. mcp.2s.42for.net.')
param hostName string

@description('Binds hostName with a managed certificate; true only once its CNAME and TXT records exist.')
param customDomainEnabled bool

@description('The custom domains of the app as it runs (mcp-existing.bicep); empty before it exists.')
param existingCustomDomains array

@description('The resource id of the server\'s user-assigned identity.')
param identityId string

@description('The client id of the server\'s user-assigned identity.')
param identityClientId string

@description('Mcp:ResourceUrl: the public URL of the MCP endpoint, e.g. https://mcp.2s.42for.net/mcp.')
param resourceUrl string

@description('Mcp:Authentication:Issuer: the AuthKit domain people sign in at, e.g. https://palatial-omen-84.authkit.app.')
param authKitIssuer string

@description('Mcp:Authentication:Scope: what MCP clients request; openid, because dynamically registered clients get no WorkOS permission.')
param scope string

@description('Mcp:Actor:Id: the actor of the API\'s Auth:TokenExchange:Actors.')
param actorId string

@description('Mcp:Actor:WorkloadIdentity:Audience: the App ID URI of "2S Platform Token Exchange", api://<app id>.')
param exchangeAudience string

@description('Storyteller:BaseUrl: the API with /api, e.g. https://api.2s.42for.net/api.')
param apiBaseUrl string

@description('0 scales to zero without calls; 1 or more keeps that many replicas, billed at the idle rate between calls.')
@minValue(0)
@maxValue(10)
param minReplicas int

@minValue(1)
@maxValue(10)
param maxReplicas int

resource environment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    // Logs through the diagnostic setting below, into the resource-specific tables: no shared key of the workspace in
    // the environment, and not the legacy Data Collector API of the destination log-analytics.
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    // The Consumption profile only: no plan management fee, scale to zero, the monthly free grant. A Consumption-only
    // environment (no profiles) is a legacy type.
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

// The newest version of diagnostic settings, and the first with logAnalyticsDestinationType; there is no newer GA one.
#disable-next-line use-recent-api-versions
resource logs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'log-analytics'
  scope: environment
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    // The resource-specific tables ContainerAppConsoleLogs and ContainerAppSystemLogs; not the HTTP logs.
    logAnalyticsDestinationType: 'Dedicated'
    logs: [
      {
        category: 'ContainerAppConsoleLogs'
        enabled: true
      }
      {
        category: 'ContainerAppSystemLogs'
        enabled: true
      }
    ]
  }
}

// The settings of the server (docs/Platform/Storyteller/mcp.md, "Settings of the server"); no secret, so no secrets block.
var settings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  Mcp__Provider: 'AuthKit'
  Mcp__ResourceUrl: resourceUrl
  Mcp__Authentication__Issuer: authKitIssuer
  Mcp__Authentication__Scope: scope
  Mcp__Actor__Id: actorId
  Mcp__Actor__WorkloadIdentity__Kind: 'ManagedIdentity'
  Mcp__Actor__WorkloadIdentity__Audience: exchangeAudience
  Mcp__Actor__WorkloadIdentity__ClientId: identityClientId
  // Azure.Identity's default user-assigned identity, should anything else ask for a token.
  AZURE_CLIENT_ID: identityClientId
  // The tools that return secrets stay off: the API's actor withholds secrets anyway (NoSecretReveal).
  Mcp__SecretTools__Enabled: 'false'
  Storyteller__BaseUrl: apiBaseUrl
}

var settingVariables = [for setting in items(settings): { name: setting.key, value: setting.value }]

// The binding as it runs once the certificate is bound (with its certificate), written back unchanged; Auto until then,
// which binds the certificate below as soon as it is issued.
var boundDomains = filter(
  existingCustomDomains,
  domain => domain.name =~ hostName && domain.?bindingType != 'Disabled' && !empty(domain.?certificateId ?? '')
)
var customDomains = !customDomainEnabled
  ? []
  : empty(boundDomains)
      ? [
          {
            name: hostName
            bindingType: 'Auto'
          }
        ]
      : [boundDomains[0]]

resource app 'Microsoft.App/containerApps@2026-07-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      // One revision serves at a time; a new image moves all traffic to its revision once it is ready.
      activeRevisionsMode: 'Single'
      maxInactiveRevisions: 10
      ingress: {
        // MCP clients run anywhere, so no IP restriction: without a valid token a call only gets 401. The managed
        // certificate needs the app reachable from the certificate authority too. A request may take 240 seconds.
        external: true
        // The port of .NET's container images (ASPNETCORE_HTTP_PORTS).
        targetPort: 8080
        transport: 'auto'
        // HTTP is redirected to HTTPS; TLS ends at the ingress.
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        customDomains: customDomains
      }
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: settingVariables
          // The health endpoints of Aspire.ServiceDefaults exist only in Development, so the probes check the port. In the
          // order Azure stores them (Liveness, Readiness, Startup), so a run that changes nothing writes the same template:
          // no what-if noise and no new revision.
          probes: [
            {
              type: 'Liveness'
              tcpSocket: {
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              tcpSocket: {
                port: 8080
              }
              periodSeconds: 5
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              // Up to 30 seconds to start listening; a configuration error stops the process before that.
              type: 'Startup'
              tcpSocket: {
                port: 8080
              }
              initialDelaySeconds: 1
              periodSeconds: 3
              timeoutSeconds: 3
              failureThreshold: 10
            }
          ]
        }
      ]
      // With minReplicas 0, zero replicas without calls (about 5 minutes after the last one), and a call starts one, a
      // cold start of a few seconds; with 1, one replica always runs. Every 20 concurrent calls one more.
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

// The free managed certificate, validated by the CNAME of the host name to the app; it needs the host name on the app
// first. A later run puts it again unchanged, which the platform accepts (it refuses only a changed subject or
// validation, ManagedCertNoUpdate). It renews itself.
resource certificate 'Microsoft.App/managedEnvironments/managedCertificates@2026-07-01' = if (customDomainEnabled) {
  parent: environment
  name: replace(hostName, '.', '-')
  location: location
  tags: tags
  properties: {
    subjectName: hostName
    domainControlValidation: 'CNAME'
  }
  dependsOn: [
    app
  ]
}

output environmentName string = environment.name
output appName string = app.name
output defaultHostName string = app.properties.configuration.ingress.fqdn
@description('The value of the TXT record asuid.<host> that validates the custom domain.')
output customDomainVerificationId string = app.properties.customDomainVerificationId
