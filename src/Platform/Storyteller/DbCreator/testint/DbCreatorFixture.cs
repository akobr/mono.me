using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.KeyValues;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Testcontainers.LowkeyVault;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The tool's own registrations over an emulator: a new platform database, built by the root command's
// CoreDbStructureBuilder.BuildAsync (the containers, the access of the base owner and the set core). Services has no
// key-encryption provider, as DbCreator without a vault; GetVaultServicesAsync gives the same registrations with the
// Standard vault of the Startup (AddKeyVaultKeyProtection) on Lowkey Vault, over the same database.
public class DbCreatorFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _vaultLock = new(1, 1);
    private LowkeyVaultContainer? _vault;
    private ServiceProvider? _vaultServices;

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

        Services = BuildServices(Configuration, withVault: false);

        await CleanupDatabaseAsync();
        CoreRun = await Services.GetRequiredService<CoreDbStructureBuilder>().BuildAsync();
    }

    // The services with the Standard vault: Lowkey Vault in Docker, started on the first call with a reuse label of its
    // own, or a running instance named by LOWKEY_VAULT_URL.
    public async Task<IServiceProvider> GetVaultServicesAsync(CancellationToken cancellationToken = default)
    {
        await _vaultLock.WaitAsync(cancellationToken);

        try
        {
            if (_vaultServices is not null)
            {
                return _vaultServices;
            }

            var vaultUri = await StartVaultAsync(cancellationToken);
            var configuration = new ConfigurationBuilder()
                .AddConfiguration(Configuration)
                .Add(new MemoryConfigurationSource
                {
                    InitialData =
                    [
                        new("AzureKeyVaults:default", vaultUri),
                        new($"{KeyValueOptions.SectionName}:Protection:Emulator", "true"),
                    ],
                })
                .Build();

            _vaultServices = BuildServices(configuration, withVault: true);
            return _vaultServices;
        }
        finally
        {
            _vaultLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_vaultServices is not null)
        {
            await _vaultServices.DisposeAsync();
        }

        if (_vault is not null)
        {
            await _vault.DisposeAsync();
        }

        _vaultLock.Dispose();
    }

    private static ServiceProvider BuildServices(IConfiguration configuration, bool withVault)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCosmosDbAnnotations(configuration);

        if (withVault)
        {
            // as Startup registers it; no host environment, so not Production, and the emulator is allowed
            services.AddKeyVaultKeyProtection(configuration);
        }

        // every query of the seed over the container organizations must filter on the organization
        services.AddSingleton<RequestHandler, OrganizationQueryGuard>();
        services.AddDbCreator(configuration);

        // the bindings of the API (Api.Functions' Program), not of the tool: the tests read what a seed wrote as a
        // resolved configuration, with the native pairs as the default, public and secret sources
        services.AddSingleton<ConfigBindingFunction>();
        services.AddSingleton<AnnotationBindingFunction>(provider =>
            new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => provider.GetRequiredService<IAnnotationService>())));
        services
            .AddConfigurationBindings(options => options
                .AddFunction<ConfigBindingFunction>("config")
                .AddFunction<AnnotationBindingFunction>("annotation"))
            .AddKeyValueBindings();

        return services.BuildServiceProvider();
    }

    private async Task<string> StartVaultAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("LOWKEY_VAULT_URL");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // its own label, so its own reused vault: disposing a reused container stops it, which would cut off the other
        // suites that run Lowkey Vault at the same time
        _vault = LowkeyVaultTestContainer.Build("dbcreator-integrationtests");

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMinutes(3));
        await _vault.StartAsync(cancellation.Token);
        return _vault.GetBaseAddress();
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
