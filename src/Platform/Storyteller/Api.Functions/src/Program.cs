using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.AzureAd;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // worker.UseNewtonsoftJson(new JsonSerializerSettings
        // {
        //     NullValueHandling = NullValueHandling.Ignore,
        //     ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },
        //     Converters = new List<JsonConverter> { new StringEnumConverter(new DefaultNamingStrategy()) },
        // });
        worker.UseMiddleware<ExceptionHandlingMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.Configure<JsonSerializerOptions>(options =>
        {
            options.PropertyNamingPolicy = new NoChangeNamingPolicy();
            options.Converters.Add(new JsonStringEnumConverter()); // JsonNamingPolicy.CamelCase
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.PropertyNameCaseInsensitive = true;
            options.AllowTrailingCommas = true;
        });

        // This is required to make the default JSON serializer in Azure Functions to use the same settings as the one in ASP.NET Core
        // Needed is you want to use IActionResult
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = new NoChangeNamingPolicy();
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); // JsonNamingPolicy.CamelCase
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.AllowTrailingCommas = true;
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

        if (context.HostingEnvironment.IsDevelopment())
        {
            services.AddLogging(builder =>
            {
                builder.AddDebug();
            });
        }

        services.AddCosmosDbAnnotations(context.Configuration);
        services.AddAzureAdMachineAccess();
    })
    .Build();

await host.RunAsync();
