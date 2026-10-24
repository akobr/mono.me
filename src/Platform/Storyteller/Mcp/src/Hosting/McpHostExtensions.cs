using System.Reflection;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Tools;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;

namespace _42.Platform.Storyteller.Mcp.Hosting;

public static class McpHostExtensions
{
    public const string McpPath = "/mcp";

    public static readonly TimeSpan ApiCallTimeout = TimeSpan.FromMinutes(5);

    public static WebApplicationBuilder AddStorytellerMcp(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        var services = builder.Services;
        services.AddOptions<McpHostOptions>().Bind(builder.Configuration.GetSection(McpHostOptions.SectionName)).ValidateOnStart();
        services.AddOptions<StorytellerApiOptions>().Bind(builder.Configuration.GetSection(StorytellerApiOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<McpHostOptions>, McpHostOptionsValidator>();
        services.AddSingleton<IValidateOptions<StorytellerApiOptions>, StorytellerApiOptionsValidator>();

        if (!builder.Environment.IsDevelopment())
        {
            services.PostConfigure<LoggerFilterOptions>(LoggingSafety.Clamp);
        }

        services.AddMcpAuthentication();
        services.AddSingleton<IStorytellerTokenProvider, EntraOnBehalfOfTokenProvider>();
        services.AddStorytellerClients();

        var secretTools = builder.Configuration.GetValue<bool>($"{McpHostOptions.SectionName}:SecretTools:Enabled");
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "2s-platform", Title = "2S Platform", Version = GetVersion() };
                options.ServerInstructions = McpInstructions.Text;
            })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithRequestFilters(filters => filters.AddCallToolFilter(StorytellerCallFilter.Create))
            .WithStorytellerTools(secretTools);

        return builder;
    }

    public static WebApplication UseStorytellerMcp(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOAuthMetadata();
        app.MapMcp(McpPath).RequireAuthorization(McpPolicies.User);
        app.MapDefaultEndpoints();
        return app;
    }

    // Explicit registration: the catalog is exactly these classes, and the secret tools only when the setting asks.
    public static IMcpServerBuilder WithStorytellerTools(this IMcpServerBuilder builder, bool secretTools)
    {
        builder
            .WithTools<DiscoveryTools>()
            .WithTools<AnnotationTools>()
            .WithTools<ConfigurationTools>()
            .WithTools<SchemaTools>()
            .WithTools<TemplateTools>()
            .WithTools<MachineTools>()
            .WithTools<KeyValueTools>()
            .WithTools<UnderstandingTools>();

        if (secretTools)
        {
            builder
                .WithTools<SecretTools>()
                .WithTools<BindingCheckTools>();
        }

        return builder;
    }

    // The scoped ISdkConfiguration wins over the SDK's own singleton (TryAdd): every request's clients send the token of
    // that request. Unsafe methods are never retried: a retried machine creation would create a second machine.
    //
    // A call may take as long as ApiCallTimeout. The standard handler's 10-second attempt (and HttpClient's 100-second)
    // timeout would drop the answer of a write the API still completes, the one-time credentials of a new machine
    // among them. The circuit breaker samples at least two attempts, which its options validator requires.
    public static IServiceCollection AddStorytellerClients(this IServiceCollection services)
    {
        services.AddScoped<StorytellerAccessToken>();
        services.AddScoped<Sdk.ISdkConfiguration>(provider =>
        {
            var token = provider.GetRequiredService<StorytellerAccessToken>();
            return new Sdk.SdkConfiguration
            {
                BaseUrl = provider.GetRequiredService<IOptions<StorytellerApiOptions>>().Value.BaseUrl,
                AccessTokenFactory = () => token.Value ?? string.Empty,
            };
        });

        Sdk.ServicesCollectionExtensions.AddStorytellerSdk(services, null, client =>
        {
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental; it drops the defaults' handler that retries writes.
            client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            client.ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
            client.AddStandardResilienceHandler(options =>
            {
                options.Retry.DisableForUnsafeHttpMethods();
                options.AttemptTimeout.Timeout = ApiCallTimeout;
                options.TotalRequestTimeout.Timeout = ApiCallTimeout;
                options.CircuitBreaker.SamplingDuration = 2 * ApiCallTimeout;
            });
        });

        return services;
    }

    private static string GetVersion()
    {
        var assembly = typeof(McpHostExtensions).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
