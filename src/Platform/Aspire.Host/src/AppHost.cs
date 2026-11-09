using _42.Platform.Aspire.Host;

var builder = DistributedApplication.CreateBuilder(args);

// Local Azure Storage via Azurite, on the default ports, so AzureWebJobsStorage=UseDevelopmentStorage=true of the
// Functions host reaches it: the queue and the archives of configuration exports and imports live there.
var storage = builder
    .AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithBlobPort(10000);
        emulator.WithQueuePort(10001);
        emulator.WithTablePort(10002);
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

// Local Cosmos DB emulator, picked by Cosmos:Mode. "container" (default) runs the Linux vNext emulator in a container
// (light, quick to start, x64 and Arm, with the Data Explorer); "local" uses the Windows emulator installed on the machine
// (ConnectionStrings:cosmos), the engine of the service with real request charges and indexes. The launch profiles
// *-local-cosmos set the local mode.
var cosmosMode = builder.Configuration["Cosmos:Mode"] ?? "container";
IResourceBuilder<IResourceWithConnectionString> cosmos = cosmosMode.ToLowerInvariant() switch
{
    "container" => builder
        .AddAzureCosmosDB("cosmos")
        .RunAsEmulator(emulator =>
        {
            emulator.WithLifetime(ContainerLifetime.Persistent);
            // its own volume: the classic emulator's volume platform-cosmos-data holds the layout before the shared
            // container organizations, which DbCreator refuses, and a format the vNext emulator does not read
            emulator.WithDataVolume("platform-cosmos-vnext-data");
            emulator.WithDataExplorer();
        }),
    "local" => builder.AddConnectionString("cosmos"),
    _ => throw new InvalidOperationException($"Unknown Cosmos:Mode '{cosmosMode}', use 'container' or 'local'."),
};

// The fixed AuthKit staging accounts DbCreator seeds: on by default for the container emulator; in the local mode,
// whose ConnectionStrings:cosmos can point anywhere, only with an explicit StagingSeed:Enabled (the *-local-cosmos profiles).
var seedStagingAccounts = cosmosMode.Equals("container", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["StagingSeed:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

var dbCreator = builder.AddProject<Projects.DbCreator>("db-creator")
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithEnvironment("stagingSeed__enabled", seedStagingAccounts ? "true" : "false")
    .WithExplicitStart();

// The owner account DbCreator seeds (BaseAccount:Id in the user secrets of this host): under Entra ID the owner's
// lower-case {tid}:{oid}, under AuthKit or Keycloak the sub. Without it DbCreator seeds its legacy Entra ID sub.
if (builder.Configuration["BaseAccount:Id"] is { Length: > 0 } baseAccountId)
{
    dbCreator.WithEnvironment("baseAccount__id", baseAccountId);
}

// Local Key Vault emulator (Lowkey Vault): the key-encryption keys of the Standard level of key/value secrets.
// Relaxed ports let the vault answer on the host port that Aspire maps to 8443.
var keyVault = builder.AddContainer("keyvault", "nagyesta/lowkey-vault", "7.3.112")
    .WithHttpsEndpoint(targetPort: 8443, name: "https")
    .WithEnvironment("LOWKEY_ARGS", "--LOWKEY_VAULT_RELAXED_PORTS=true")
    .WithLifetime(ContainerLifetime.Persistent);

var apiFunctions = builder.AddProject<Projects.Api_Functions>("api-functions")
    .WithHttpEndpoint(port: 7071, name: "http", isProxied: false)
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithEnvironment("AzureKeyVaults__default", keyVault.GetEndpoint("https"))
    .WithEnvironment("KeyValues__Protection__Emulator", "true")
    .WaitFor(keyVault)
    .WithEnvironment("AzureWebJobsStorage", "UseDevelopmentStorage=true")
    .WaitFor(storage)
    // The Functions host answers its landing page once it is up, so WaitFor(apiFunctions) means ready, not just started.
    .WithHttpHealthCheck("/", endpointName: "http");

// Administration UI (src/Platform/ui.admin): the Vite dev server on the fixed port 5173, which is the redirect URI
// registered in the WorkOS staging environment. It prepares itself: ui-admin-sdk installs and builds the TypeScript
// SDK, then ui-admin-installer installs the UI's packages (both `npm run prepare:local`, skipped when up to date). The
// "Clean reinstall" command redoes both from scratch. Node 22.18 or newer must be on PATH.
var uiAdmin = builder.AddViteApp("ui-admin", "../../ui.admin/src", "dev:aspire")
    .WithLocalPreparation(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../..")))
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5173;
        endpoint.TargetPort = 5173;
        endpoint.IsProxied = false;
    })
    .WithEnvironment("VITE_API_BASE_URL", ReferenceExpression.Create($"{apiFunctions.GetEndpoint("http")}/api"))
    .WithReference(apiFunctions)
    .WaitFor(apiFunctions)
    // Healthy once Vite serves the page, so the UI tests wait for the dev server, not just its process.
    .WithHttpHealthCheck("/", endpointName: "http");

// MCP server (Storyteller/Mcp), started from the dashboard. Its user secrets need Mcp:ResourceUrl (its own URL ending
// in /mcp, for example https://localhost:7180/mcp), Mcp:Provider and Mcp:Authentication:* (the same provider as the
// API's Auth:Provider), and its actor: Mcp:Actor:Id and, in Development, Mcp:Actor:ClientSecret. The API (its
// local.settings.json) must turn the exchange on and list that actor, or every tool answers that the exchange is off:
// Auth:TokenExchange:Enabled true, Auth:TokenExchange:Actors:0:Id = Mcp:Actor:Id,
// Actors:0:Authentication:ClientSecretHash = the hex SHA-256 of Mcp:Actor:ClientSecret, and Actors:0:SubjectAudiences
// = the MCP's audience (Mcp:Authentication:Audience, else Mcp:ResourceUrl; under Entra ID also
// Mcp:Authentication:ClientId). The MCP server logs the audiences to list at start. The API accepts HTTP in
// Development, as the admin UI uses it.
builder.AddProject<Projects.Mcp>("mcp")
    .WithReference(apiFunctions)
    .WaitFor(apiFunctions)
    .WithEnvironment("Storyteller__BaseUrl", ReferenceExpression.Create($"{apiFunctions.GetEndpoint("http")}/api"))
    .WithExplicitStart();

// UI automation tests of the admin UI (src/Platform/UI.Admin.AutomationTests), run by hand from the dashboard against
// ui-admin with the dev profile (the pool passwords in the test project's user secrets), grouped under ui-automation.
// ui-tests-elementary: the Elementary set, one test at a time, in a visible Chromium slowed down by 200 ms per action;
// ui-tests-all: every test, headless and in parallel. Each run exits when the tests end; start it again to rerun.
// Target:BaseUrl is passed as a parameter, so a run targets this ui-admin whatever the user secrets or UITESTS_* say.
var uiTestsTarget = ReferenceExpression.Create($"Target:BaseUrl={uiAdmin.GetEndpoint("http")}");
var uiAutomation = builder.AddResource(new UiAutomationGroupResource("ui-automation"))
    .WithInitialState(new()
    {
        ResourceType = "Group",
        State = KnownResourceStates.Running,
        Properties = [],
    })
    .ExcludeFromManifest();

builder.AddProject<Projects.UI_Admin_AutomationTests>("ui-tests-elementary")
    .WithEnvironment("UITESTS_PROFILE", "dev")
    .WithArgs("--test-parameter", uiTestsTarget)
    .WithArgs("--maximum-parallel-tests", "1", "--test-parameter", "Browser:Headless=false", "--test-parameter", "Browser:SlowMoMs=200", "--treenode-filter", "/**[Category=Elementary]")
    .WithParentRelationship(uiAutomation)
    .WaitFor(apiFunctions)
    .WaitFor(uiAdmin)
    .WithExplicitStart()
    .ExcludeFromManifest();

builder.AddProject<Projects.UI_Admin_AutomationTests>("ui-tests-all")
    .WithEnvironment("UITESTS_PROFILE", "dev")
    .WithArgs("--test-parameter", uiTestsTarget)
    .WithArgs("--test-parameter", "Browser:Headless=true")
    .WithParentRelationship(uiAutomation)
    .WaitFor(apiFunctions)
    .WaitFor(uiAdmin)
    .WithExplicitStart()
    .ExcludeFromManifest();

builder.Build().Run();

// The dashboard group of the UI automation tests: a resource of its own that runs nothing.
internal sealed class UiAutomationGroupResource(string name) : Resource(name);
