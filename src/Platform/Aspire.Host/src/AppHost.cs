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

// Local Cosmos DB emulator, picked by Cosmos:Mode. "container" (default) runs the Linux vNext emulator in a container;
// "local" uses the Windows emulator installed on the machine (ConnectionStrings:cosmos), which also supports stored
// procedures, triggers and UDFs. The launch profiles *-local-cosmos set the local mode.
var cosmosMode = builder.Configuration["Cosmos:Mode"] ?? "container";
#pragma warning disable ASPIRECOSMOSDB001
IResourceBuilder<IResourceWithConnectionString> cosmos = cosmosMode.ToLowerInvariant() switch
{
    "container" => builder
        .AddAzureCosmosDB("cosmos")
        .RunAsPreviewEmulator(emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
            emulator.WithImageRegistry("mcr.microsoft.com");
            emulator.WithImage("cosmosdb/linux/azure-cosmos-emulator");
            emulator.WithImageTag("latest");
        }),
    "local" => builder.AddConnectionString("cosmos"),
    _ => throw new InvalidOperationException($"Unknown Cosmos:Mode '{cosmosMode}', use 'container' or 'local'."),
};
#pragma warning restore ASPIRECOSMOSDB001

var dbCreator = builder.AddProject<Projects.DbCreator>("db-creator")
    .WithReference(cosmos)
    .WaitFor(cosmos)
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
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithEnvironment("AzureKeyVaults__default", keyVault.GetEndpoint("https"))
    .WithEnvironment("KeyValues__Protection__Emulator", "true")
    .WaitFor(keyVault)
    .WithEnvironment("AzureWebJobsStorage", "UseDevelopmentStorage=true")
    .WaitFor(storage);

// Administration UI (src/Platform/ui.admin): the Vite dev server on the fixed port 5173, which is the redirect URI
// registered in the WorkOS staging environment. Run `npm ci` in ui.admin/src (and build the TypeScript SDK) first.
builder.AddViteApp("ui-admin", "../../ui.admin/src", "dev:aspire")
    .WithNpm(install: false)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5173;
        endpoint.TargetPort = 5173;
        endpoint.IsProxied = false;
    })
    .WithEnvironment("VITE_API_BASE_URL", ReferenceExpression.Create($"{apiFunctions.GetEndpoint("http")}/api"))
    .WithReference(apiFunctions);

// MCP server (Storyteller/Mcp): needs its sign-in settings in its user secrets (Mcp:Provider, Mcp:Authentication:*)
// and its actor for the API's token exchange (Mcp:Actor:Id and, in Development, Mcp:Actor:ClientSecret), which the
// API's Auth:TokenExchange:Actors must list, so it starts from the dashboard. The API accepts HTTP in Development, as
// the admin UI uses it.
builder.AddProject<Projects.Mcp>("mcp")
    .WithReference(apiFunctions)
    .WaitFor(apiFunctions)
    .WithEnvironment("Storyteller__BaseUrl", ReferenceExpression.Create($"{apiFunctions.GetEndpoint("http")}/api"))
    .WithExplicitStart();

builder.Build().Run();
