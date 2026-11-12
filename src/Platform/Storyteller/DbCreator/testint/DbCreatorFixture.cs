using _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The tool's own registrations over an emulator: a new platform database, built by the root command's
// CoreDbStructureBuilder.BuildAsync (the containers, the access of the base owner and the set core).
public class DbCreatorFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = null!;

    public IConfiguration Configuration { get; private set; } = null!;

    // The local emulator, or a reused container of the vNext (default) or classic emulator: see CosmosTestEmulator.
    public CosmosTestEmulator Emulator { get; } = CosmosTestEmulator.FromEnvironment("dbcreator-integrationtests");

    // What BuildAsync seeded into the new database.
    public DataSetRunResult CoreRun { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Emulator.StartAsync(cancellation.Token);

        Configuration = new ConfigurationBuilder()
            .Add(new MemoryConfigurationSource
            {
                InitialData =
                [
                    new("CosmosDb:Connection", Emulator.GetConnectionString()),
                    new("CosmosDb:ShouldAcceptAnyCertificate", "True"),
                ],
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCosmosDbAnnotations(Configuration);

        // every query of the seed over the container organizations must filter on the organization
        services.AddSingleton<RequestHandler, OrganizationQueryGuard>();
        services.AddDbCreator(Configuration);

        Services = services.BuildServiceProvider();

        await CleanupDatabaseAsync();
        CoreRun = await Services.GetRequiredService<CoreDbStructureBuilder>().BuildAsync();
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
        await pipeline.ExecuteAsync(async token =>
        {
            var client = Services.GetRequiredService<ICosmosClientProvider>().Client;

            try
            {
                await client.GetDatabase(CosmosConstants.DatabaseName).DeleteAsync(cancellationToken: token);
            }
            catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // nothing to delete
            }
        });
    }
}
