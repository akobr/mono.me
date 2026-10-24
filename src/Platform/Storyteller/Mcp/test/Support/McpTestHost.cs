using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using ModelContextProtocol.Client;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The MCP host in memory: test signing keys instead of Entra's, a stub on-behalf-of exchange and a recording API.
internal sealed class McpTestHost : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Mcp:ResourceUrl"] = TestTokens.ResourceUrl,
        ["Mcp:EntraId:TenantId"] = TestTokens.TenantId,
        ["Mcp:EntraId:ClientId"] = TestTokens.McpClientId,
        ["Mcp:EntraId:AllowedClientIds:0"] = TestTokens.ClientAppId,
        ["Mcp:EntraId:ClientSecret"] = "test-secret",
        ["Storyteller:BaseUrl"] = "https://api.test/api",
        ["Storyteller:Scopes:0"] = "api://storyteller/User.Impersonation",
    };

    private readonly string _environment;

    public McpTestHost(IDictionary<string, string?>? settings = null, string environment = "Production")
    {
        _environment = environment;

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public RecordingApiHandler Api { get; } = new();

    public StubTokenProvider Tokens { get; } = new();

    public RecordingLoggerProvider Logs { get; } = new();

    public async Task<McpClient> ConnectAsync(string? token = null)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token ?? TestTokens.Create()}" },
            },
            CreateClient(),
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(TestTokens.CreateConfiguration()));

            services.RemoveAll<IStorytellerTokenProvider>();
            services.AddSingleton<IStorytellerTokenProvider>(Tokens);

            // Every SDK client reaches the recording API; the resilience handler stays in front of it.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = Api));

            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
