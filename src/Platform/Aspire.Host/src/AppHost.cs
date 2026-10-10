var builder = DistributedApplication.CreateBuilder(args);

// Local Azure Storage via Azurite
//var storage = builder.AddAzureStorage("storage").RunAsEmulator();
//var queues = storage.AddQueues("queues");
//var blobs = storage.AddBlobs("blobs");

// Local Cosmos DB emulator
/*var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator();*/
#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder
    .AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator =>
    {
        emulator.WithDataExplorer();
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithImageRegistry("mcr.microsoft.com");
        emulator.WithImage("cosmosdb/linux/azure-cosmos-emulator");
        emulator.WithImageTag("latest");
    });

var dbCreator = builder.AddProject<Projects.DbCreator>("db-creator")
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithExplicitStart();

var apiFunctions = builder.AddProject<Projects.Api_Functions>("api-functions")
    .WithReference(cosmos)
    .WaitFor(cosmos);

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

builder.Build().Run();
