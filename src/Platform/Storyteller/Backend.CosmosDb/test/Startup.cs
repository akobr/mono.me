using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.DbCreator;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Polly;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class Startup : IAsyncLifetime, ITestContext
{
    public IConfiguration Configuration { get; private set; }

    public IServiceProvider Services { get; private set; }

    // The local emulator, or a reused container of the vNext (default) or classic emulator: see CosmosTestEmulator.
    public CosmosTestEmulator Emulator { get; } = CosmosTestEmulator.FromEnvironment("backend-cosmosdb-unittests");

    public async ValueTask InitializeAsync()
    {
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var containerTask = Emulator.StartAsync(cancellation.Token);
        var services = new ServiceCollection();
        var configurationBuilder = new ConfigurationBuilder();

        await containerTask;
        Configure(configurationBuilder);
        Configuration = configurationBuilder.Build();
        ConfigureServices(services, Configuration);
        Services = services.BuildServiceProvider();

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
            .AddRetry(new()
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
            })
            .Build();

        await pipeline.ExecuteAsync(async _ =>
        {
            await DeleteDatabasesAsync();
        });
    }

    // Only the platform database: the run starts from an empty one, and other databases of the emulator stay.
    private async Task DeleteDatabasesAsync()
    {
        var clientProvider = Services.GetRequiredService<ICosmosClientProvider>();

        try
        {
            await clientProvider.Client.GetDatabase(CosmosConstants.DatabaseName).DeleteAsync();
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // nothing to delete
        }
    }

    private void Configure(IConfigurationBuilder builder)
    {
        var connectionString = Emulator.GetConnectionString();

        builder.Add(new MemoryConfigurationSource
        {
            InitialData = [
                new KeyValuePair<string, string>("cosmosDb:connection", connectionString),
                new KeyValuePair<string, string>("cosmosDb:shouldAcceptAnyCertificate", "True"),
                new KeyValuePair<string, string?>("cosmosDb:autoscaleMaxThroughput", null)
            ]
        });
    }

    private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // the key/value service logs the request charge of every Cosmos operation at Debug
        services.AddLogging(builder => builder
            .AddConsole()
            .AddFilter("_42.Platform.Storyteller.KeyValues", LogLevel.Debug));
        services.AddCosmosDbAnnotations(configuration);

        // every organization-wide query of the suite must filter on c.Organization (OrganizationQueryGuard)
        services.AddSingleton<RequestHandler, OrganizationQueryGuard>();

        // the structure builder with what it seeds (the set core), as the tool registers it
        services.AddDbCreator(configuration);

        services.AddSingleton<ConfigBindingFunction>();
        services.AddSingleton<AnnotationBindingFunction>(sp =>
            new AnnotationBindingFunction(
                new Lazy<IAnnotationService>(() => sp.GetRequiredService<IAnnotationService>())));
        services.AddSingleton<DecliningSecretSource>();
        services.AddConfigurationBindings(options => options
            .AddFunction<ConfigBindingFunction>("config")
            .AddFunction<AnnotationBindingFunction>("annotation")
            .AddSource<DecliningSecretSource>(DecliningSecretSource.VaultKey));

        // key/value pairs: in-memory key-encryption providers for every level, and a counting decorator for the binding tests
        var standardProvider = new FakeKeyEncryptionProvider();
        services.AddSingleton(standardProvider);
        services.AddSingleton<IKeyEncryptionProvider>(standardProvider);
        services.AddSingleton<IKeyEncryptionProvider>(new FakeKeyEncryptionProvider(KeyValueProtection.Hsm));
        services.AddSingleton<FakeCustomerKeyProvider>();
        services.AddSingleton<IKeyEncryptionProvider>(provider => provider.GetRequiredService<FakeCustomerKeyProvider>());
        services.AddSingleton<CosmosKeyValueService>();
        services.AddSingleton<CountingKeyValueService>();
        services.Replace(ServiceDescriptor.Singleton<IKeyValueService>(provider => provider.GetRequiredService<CountingKeyValueService>()));
        services.AddKeyValueBindings();
    }
}
