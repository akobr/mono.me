using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Polly;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

public class CosmosFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = null!;

    public IConfiguration Configuration { get; private set; } = null!;

    // The local emulator, or a reused container of the vNext (default) or classic emulator: see CosmosTestEmulator.
    public CosmosTestEmulator Emulator { get; } = CosmosTestEmulator.FromEnvironment("access-certificates-integrationtests");

    public async ValueTask InitializeAsync()
    {
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var containerTask = Emulator.StartAsync(cancellation.Token);
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();

        await containerTask;

        var connectionString = Emulator.GetConnectionString();

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

        // Only the platform database: the run starts from an empty one, and other databases of the emulator stay.
        await pipeline.ExecuteAsync(async _ =>
        {
            var client = Services.GetRequiredService<ICosmosClientProvider>().Client;

            try
            {
                await client.GetDatabase(CosmosConstants.DatabaseName).DeleteAsync();
            }
            catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // nothing to delete
            }
        });
    }
}
