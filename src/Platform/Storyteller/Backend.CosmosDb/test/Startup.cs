using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.CosmosDb;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class Startup : IAsyncLifetime, ITestContext
{
    public IConfiguration Configuration { get; private set; }

    public IServiceProvider Services { get; private set; }

    public CosmosDbContainer DbContainer { get; private set; }

    public async Task InitializeAsync()
    {
        DbContainer = new CosmosDbBuilder()
            .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
            .Build();

        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var containerTask = DbContainer.StartAsync(cancellation.Token);
        var services = new ServiceCollection();
        var configurationBuilder = new ConfigurationBuilder();

        await containerTask;
        Configure(configurationBuilder);
        Configuration = configurationBuilder.Build();
        ConfigureServices(services, Configuration);
        Services = services.BuildServiceProvider();

        var dbBuilder = Services.GetRequiredService<CoreDbStructureBuilder>();
        await dbBuilder.BuildAsync();
    }

    public Task DisposeAsync()
    {
        return DbContainer.StopAsync();
    }

    private void Configure(IConfigurationBuilder builder)
    {
        var connectionString = DbContainer.GetConnectionString();

        builder.Add(new MemoryConfigurationSource
        {
            InitialData = [
                new KeyValuePair<string, string>("cosmosDb:connection", connectionString),
                new KeyValuePair<string, string>("cosmosDb:shouldAcceptAnyCertificate", "True")
            ]
        });
    }

    private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCosmosDbAnnotations(configuration);
        services.AddSingleton<CoreDbStructureBuilder>();
    }
}
