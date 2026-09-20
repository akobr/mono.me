using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Polly;
using Testcontainers.CosmosDb;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

public class CosmosFixture : IAsyncLifetime
{
    private static readonly bool UseContainer =
        !string.Equals(
            Environment.GetEnvironmentVariable("STORYTELLER_TESTS_USE_LOCAL_EMULATOR"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public IServiceProvider Services { get; private set; } = null!;

    public IConfiguration Configuration { get; private set; } = null!;

    public CosmosDbContainer DbContainer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DbContainer = new CosmosDbBuilder()
            .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
            .WithReuse(true)
            .Build();

        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var containerTask = UseContainer ? DbContainer.StartAsync(cancellation.Token) : Task.CompletedTask;
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();

        await containerTask;

        var connectionString = UseContainer
            ? DbContainer.GetConnectionString()
            : "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        configBuilder.Add(new MemoryConfigurationSource
        {
            InitialData =
            [
                new("CosmosDb:Connection", connectionString),
                new("CosmosDb:ShouldAcceptAnyCertificate", "True"),
                new("MachineAuth:DefaultCredentialKind", "ApiKey"),
                new("MachineAuth:TrustDomain", "test.platform"),
                new("MachineAuth:CertificateLifetimeDays", "365"),
                new("MachineAuth:MinCertificateLifetimeDays", "1"),
                new("MachineAuth:MaxCertificateLifetimeDays", "3650"),
                new("MachineAuth:CaLifetimeYears", "20"),
                new("MachineAuth:RenewalWindowDays", "30"),
                new("MachineAuth:RenewalOverlapDays", "7"),
                new("MachineAuth:Authority:Kind", "Cosmos"),
                new("MachineAuth:Authority:IsAutoBootstrapEnabled", "True"),
            ],
        });

        Configuration = configBuilder.Build();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Register Cosmos DB infrastructure + all access services.
        services.AddCosmosDbAnnotations(Configuration);

        // Register certificate machine access (includes local CA, validator, policy-aware service).
        services.AddApiKeyMachineAccess();
        services.AddCertificateMachineAccess(Configuration);

        services.AddSingleton<CoreDbStructureBuilder>();

        Services = services.BuildServiceProvider();

        // Clean and recreate databases.
        await CleanupDatabaseAsync();
        var dbBuilder = Services.GetRequiredService<CoreDbStructureBuilder>();
        await dbBuilder.BuildAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private async Task CleanupDatabaseAsync()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new() { MaxRetryAttempts = 3, Delay = TimeSpan.FromSeconds(1) })
            .Build();

        await pipeline.ExecuteAsync(async _ =>
        {
            var clientProvider = Services.GetRequiredService<ICosmosClientProvider>();
            var client = clientProvider.Client;
            var iterator = client.GetDatabaseQueryIterator<DatabaseProperties>();

            while (iterator.HasMoreResults)
            {
                foreach (var database in await iterator.ReadNextAsync())
                {
                    await client.GetDatabase(database.Id).DeleteAsync();
                }
            }
        });
    }
}
