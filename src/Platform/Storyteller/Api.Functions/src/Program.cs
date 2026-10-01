using System.Text.Json.Serialization;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Json;

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
        worker.UseMiddleware<MachineAuthenticationMiddleware>();
        worker.UseMiddleware<BearerAuthenticationMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // This is required to make the default JSON serializer in Azure Functions to use the same settings as the one in ASP.NET Core
        // Needed if you want to use IActionResult
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = new NoChangeNamingPolicy();
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); // JsonNamingPolicy.CamelCase
            options.JsonSerializerOptions.Converters.Add(new JObjectConverter());
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

        // Add persistant and logic layer
        services.AddCosmosDbAnnotations(context.Configuration, "cosmos");

        // Add authentication by API keys
        services.AddApiKeyMachineAccess();
        // Add certificate-based machine authentication (must be after AddApiKeyMachineAccess)
        services.AddCertificateMachineAccess(context.Configuration);
        // Override to Key Vault CA in production (comment out for local dev)
        //services.AddKeyVaultCertificateAuthority(context.Configuration);
        // Add authentication by Azure Entra
        //services.AddAzureAdMachineAccess();

        // AuthKit user authentication is a later phase. Naming it here fails startup
        // instead of validating those tokens with the Entra ID rules.
        var authProvider = context.Configuration.GetSection(UserAuthenticationOptions.SectionName)["Provider"];

        if (string.IsNullOrWhiteSpace(authProvider)
            || authProvider.Equals(nameof(IdentityProviderKind.EntraId), StringComparison.OrdinalIgnoreCase))
        {
            services.AddEntraIdUserAuthentication(context.Configuration);
        }
        else if (authProvider.Equals(nameof(IdentityProviderKind.AuthKit), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Auth:Provider AuthKit is not available yet. Entra ID remains the user identity provider in this build.");
        }
        else
        {
            throw new InvalidOperationException($"Auth:Provider '{authProvider}' is not a known identity provider.");
        }

        // Add data-bindings for configurations
        services.AddSingleton<ConfigBindingFunction>();
        services.AddSingleton<AnnotationBindingFunction>(sp =>
            new AnnotationBindingFunction(
                new Lazy<IAnnotationService>(() => sp.GetRequiredService<IAnnotationService>())));
        services
            .AddConfigurationBindings(options => options
                .AddFunction<ConfigBindingFunction>("config")
                .AddFunction<AnnotationBindingFunction>("annotation"))
            .AddAzureKeyVaultBindings(
                context.Configuration,
                context.HostingEnvironment);
    })
    .Build();

await host.RunAsync();
