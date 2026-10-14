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

builder.Build().Run();
