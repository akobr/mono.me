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

builder.Build().Run();
