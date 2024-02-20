using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.AzureAd;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // worker.UseNewtonsoftJson(); // TODO: [P2] not sure if this is needed for OpenAPI
        worker.UseMiddleware<ExceptionHandlingMiddleware>();
    })
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.Configure<JsonSerializerOptions>(options =>
        {
            options.AllowTrailingCommas = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
        });

        services.Configure<LoggerFilterOptions>(options =>
        {
            // The Application Insights SDK adds a default logging filter that instructs ILogger to capture only Warning and more severe logs. Application Insights requires an explicit override.
            // Log levels can also be configured using appsettings.json. For more information, see https://learn.microsoft.com/en-us/azure/azure-monitor/app/worker-service#ilogger-logs
            var toRemove = options.Rules.FirstOrDefault(
                rule => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

            if (toRemove is not null)
            {
                options.Rules.Remove(toRemove);
            }
        });

        services.AddSingleton<ICosmosClientProvider, CosmosClientProvider>();
        services.AddSingleton<IContainerFactory, ContainerFactory>();
        services.AddSingleton<IContainerRepositoryProvider, ContainerRepositoryProvider>();
        services.AddSingleton<IMachineAccessService, AzureAdMachineAccessService>();

        services.AddSingleton<IAccessService, CosmosAccessService>();
        services.AddSingleton<IAnnotationService, CosmosAnnotationService>();
        services.AddSingleton<IConfigurationService, CosmosConfigurationService>();
    })
    .Build();

await host.RunAsync();
