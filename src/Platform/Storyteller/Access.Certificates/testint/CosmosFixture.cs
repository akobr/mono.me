using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.DbCreator.Logic;
using DotNet.Testcontainers.Builders;
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
        string.Equals(
            Environment.GetEnvironmentVariable("STORYTELLER_TESTS_USE_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public IServiceProvider Services { get; private set; } = null!;

    public IConfiguration Configuration { get; private set; } = null!;

    public CosmosDbContainer DbContainer { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        DbContainer = new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
            .WithReuse(true)
            // Its own label, so its own reused emulator: the cleanup deletes every database, and Testcontainers' reuse
            // is not safe when two test modules start the same container at once (two containers, one hash).
            .WithLabel("net.42for.tests.cosmos", "access-certificates-integrationtests")
            // 8 partitions instead of the default 10 start faster and need less memory; 5 are too few for the containers the tests create (503).
            .WithEnvironment("AZURE_COSMOS_EMULATOR_PARTITION_COUNT", "8")
            // The port opens before the partitions start: wait for the emulator's "Started" line, or the first
            // requests fail with "The SSL connection could not be established".
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(@"(?m)^Started\s*$"))
            .Build();

        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var containerTask = UseContainer ? DbContainer.StartAsync(cancellation.Token) : Task.CompletedTask;
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();

        await containerTask;

        // The module's connection string names http, but the pinned image serves HTTPS.
        var connectionString = UseContainer
            ? DbContainer.GetConnectionString().Replace("http://", "https://")
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

        // Stands in for AuthKit M2M applications, so ClientCredentials routing runs against real storage.
        services.AddSingleton<RecordingIdentityProviderMachineAccessService>();
        services.AddSingleton<IIdentityProviderMachineAccessService>(p => p.GetRequiredService<RecordingIdentityProviderMachineAccessService>());

        // Federated machines next to it, with the Cosmos DB issuer and signing key stores.
        services.AddFederatedMachineAccess(Configuration);

        services.AddSingleton<CoreDbStructureBuilder>();

        Services = services.BuildServiceProvider();

        // Clean and recreate databases.
        await CleanupDatabaseAsync();
        var dbBuilder = Services.GetRequiredService<CoreDbStructureBuilder>();
        await dbBuilder.BuildAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
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
