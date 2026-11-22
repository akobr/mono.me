// Production of the 2S Platform, deployed to the subscription prod-42fornet-sub (decision 14).
// Every value of the environment is a literal here, including the ids that exist only after a later step: commit them
// as soon as they are known (they are not secrets). main.bicep owns the whole set of the API's app settings, so a run
// with an id left empty removes its setting. Only the owner's personal values come from variables
// (src/Platform/Deployment/README.md).

using '../main.bicep'

param environment = 'prod'
param location = 'swedencentral'
// Static Web Apps offer Central US, East US 2, West US 2, West Europe and East Asia; West Europe refuses new tenants
// (https://aka.ms/locationineligible). Only the app's metadata is there; the admin UI is served from the global edge.
param staticWebAppLocation = 'eastus2'
param regionCode = 'eusec'

param dnsZone = '42for.net'
param apiHostName = 'api.2s.42for.net'
param appHostName = 'app.2s.42for.net'
// No custom AuthKit domain yet (USD 99 a month): the WorkOS API issues the user tokens (per client id) and the admin
// UI keeps its session in localStorage. authKitDomain is the environment's AuthKit domain (WorkOS dashboard, phase 5), committed once known.
// With the custom domain later: authKitDomain = 'auth.42for.net', authKitCustomDomain = true (runbook, "Adding the
// custom AuthKit domain").
param authKitDomain = 'palatial-omen-84.authkit.app'
param authKitCustomDomain = false
param trustDomain = '2s.42for.net'

// The aud of the WorkOS JWT template (cookbook, step 13) is the default https://<trustDomain>; an open point until
// confirmed.
// authKitAudiences (the audiences of the 2S applications) stays at its default [] until the release that enables
// Multiple Applications in WorkOS production. Its values are production's, of the kind the staging check finds (for
// example production's client id, never a staging one); 424-239, runbook "Enabling Multiple Applications in WorkOS".

// Step 13: the WorkOS production client id (client_…).
param authKitClientId = 'client_01M3MMVAP9HG798GFH9WS2NB8J'
// true once the secret authkit-api-key is in the vault (invitation emails).
param authKitApiKeyEnabled = true

// Step 11.1: the application id of "2S Platform Customer Keys" (New-ByokApplication.ps1).
param byokClientId = 'cb2da9e3-9316-4fd8-b428-7c7161bdd685'
// Step 11.2: the object id of the service principal of "2S Platform Deployment" (New-DeploymentApplication.ps1).
param deploymentPrincipalId = '2a9df1b7-89a4-42e5-b007-5fe1763b8898'

// Step 14: the DSN of the API's Sentry project 2s-api; empty would leave Sentry__Dsn out
// and the API would report nothing. Not a secret: it only lets a sender add events to the project.
param sentryDsn = 'https://d5046cb672ec2f66209c56807d4611e8@o4512431543549952.ingest.de.sentry.io/4512431550890064'

// The step-up of users (Auth:StepUp): empty and null leave both settings out, so the API's defaults apply (on under
// AuthKit, a sign-in counts for 10 minutes). stepUpEnabled = 'false' is the escape hatch when a client cannot satisfy
// it; stepUpMaxAgeSeconds takes 60 to 86400 (900 for 15 minutes), and the build refuses anything else. Both can change
// any time (decisions).
param stepUpEnabled = ''
param stepUpMaxAgeSeconds = null

param ownerObjectId = readEnvironmentVariable('PLATFORM_OWNER_OBJECT_ID')
param ownerEmail = readEnvironmentVariable('PLATFORM_OWNER_EMAIL')
// No account-wide throughput limit until DbCreator has created core and organizations (a capped account refuses
// containers without explicit throughput); then 1000, the free tier (runbook, phase 11.2).
param cosmosTotalThroughputLimit = 1000

// The Cosmos DB key in the secret cosmos-connection; switch it only to rotate the other key (cookbook, step 7).
param cosmosConnectionKey = 'primary'

// The credit period of the free account, until 2026-12-07: switch these back to 5, 0 and '0.15' on 7 December
// (runbook, "The credit period"). Only settings that can go back without a rebuild.
param budgetAmount = 50
param functionAlwaysReadyInstances = 1
param logDailyCapGb = '1'

// The month of the first run; if that is a later month, set its first day before the run, then never change it.
param budgetStartDate = '2026-11-01'

// The exhibition, the demo project that the onboarding of the admin UI offers to a new user (runbook, "Turning the
// exhibition on"): the set exhibit of DbCreator, seeded with its own names and with the operator's production account
// as its --owner. All three or none; the API offers it only while that account is the only Administrator or Owner of
// the project. Removing the three turns it off again.
param exhibitionOrganization = 'fourth-wall'
param exhibitionProject = 'toons-and-tyrants'
param exhibitionOwner = 'user_01M75BRGA307NA3QWSH3JYZ8G3'

// The MCP server on Container Apps (docs/Platform/Deployment/runbook-mcp-server.md), off until its phase 4. Then:
// mcpEnabled = true, mcpExchangeAppId the application id that New-TokenExchangeApplication.ps1 prints (phase 2) and
// mcpInitialImage the image of the first build (phase 3). Once the app exists and its DNS records too (phase 6):
// mcpAppExists = true, so later runs keep the image that Platform.Storyteller.Mcp.yml deployed, and mcpCustomDomain =
// true, which binds mcp.2s.42for.net with a managed certificate. mcpEnabled = false turns the API's exchange off again
// and leaves the Container App as it is.
param mcpEnabled = true
param mcpHostName = 'mcp.2s.42for.net'
param mcpExchangeAppId = '2399e89b-0b03-4d80-9f02-cf25a146509d'
param mcpInitialImage = 'ghcr.io/akobr/2s-platform-mcp:20ef5b5a2e55db09753cf8ca5cadfef28b0e2b22'
param mcpAppExists = true
param mcpCustomDomain = true
// One replica always runs: no cold start on the first call after an idle period, about 4-5 USD a month at the idle rate
// beyond the free grant (decisions, "The MCP server"). 0 scales it to zero again, for 0 USD.
param mcpMinReplicas = 1
// The MCP server's budget prod-2s-mcp-budget (the subscription's Container Apps cost): its first month, never changed
// while the budget exists (a re-created budget needs the month of its run); the what-if refuses mcpEnabled without it.
// With one replica always on the cost is about 4-6 USD a month at the idle rate, so the amount is 12: the 50 % alert
// (6 USD) means more than the idle baseline, 100 % about a replica busy all month (runbook, "The cost of the MCP
// server"). mcpCooldownSeconds (300) matters only with mcpMinReplicas = 0.
param mcpBudgetStartDate = '2026-12-01'
param mcpBudgetAmount = 12

// The MCP clients (runbook-mcp-server.md, "Trust a client"): every client outside mcpTrustedClientIds only reads and
// reveals no secret. The trusted list stays empty until a sign-in of a client with a Client ID Metadata Document has been
// recorded: copy the id the API recorded (its request log line "… via mcp (client <id>) …", or ViaClient in the access
// audit trail), never one read from a decoded token, and only of a client whose document lists only web redirects,
// none on loopback (any local process can present such a client's URL). Never a dynamically registered id (client_01…): it is random per registration.
// mcpDeniedClientIds cuts off one client with a stable id; mcpAllowedClientIds set to the trusted list cuts off every
// other client, dynamically registered ones too. That needs at least one trusted client: an empty list accepts every
// client, so with none turn the exchange off instead (mcpEnabled = false).
param mcpAllowedClientIds = []
param mcpTrustedClientIds = []
param mcpUntrustedClientRestrictions = [
  'ReadOnly'
  'NoSecretReveal'
]
param mcpDeniedClientIds = []
