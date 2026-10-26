using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;

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

// The MCP host in memory: test signing keys instead of the provider's, a recording API and an in-memory file system
// (for the actor's key and token file). The token exchange is stubbed (Tokens) unless exchange is true; then the host's
// own exchange reaches the recording API too. The actor proves itself with the token file TokenFilePath, read only by
// an exchange. The provider is Entra ID unless the settings name another (Mcp:Provider); each starts from its own
// sign-in settings.
internal sealed class McpTestHost : WebApplicationFactory<Program>
{
    public const string ActorId = "mcp";
    public const string TokenFilePath = "/var/run/secrets/tokens/storyteller";

    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private readonly bool _exchange;

    public McpTestHost(IDictionary<string, string?>? settings = null, string environment = "Production", bool exchange = false)
    {
        _environment = environment;
        _exchange = exchange;
        _settings = DefaultSettings(settings?.TryGetValue("Mcp:Provider", out var provider) == true ? provider : null);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public RecordingApiHandler Api { get; } = new();

    public StubTokenProvider Tokens { get; } = new();

    public RecordingLoggerProvider Logs { get; } = new();

    public MockFileSystem Files { get; } = new();

    // The client negotiates the newest protocol revision unless the test pins one.
    public async Task<McpClient> ConnectAsync(string? token = null, string? protocolVersion = null)
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

        return await McpClient.CreateAsync(transport, protocolVersion is null ? null : new McpClientOptions { ProtocolVersion = protocolVersion });
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
            // The metadata names the issuer of the provider, as its discovery document would.
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure<McpAuthSettings>((options, auth) => options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(TestTokens.CreateConfiguration(auth.Issuer)));

            if (!_exchange)
            {
                services.RemoveAll<IStorytellerTokenProvider>();
                services.AddSingleton<IStorytellerTokenProvider>(Tokens);
            }

            // Every client reaches the recording API, the exchange's too; the handlers the host adds stay in front of it.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = Api));

            services.RemoveAll<IFileSystem>();
            services.AddSingleton<IFileSystem>(Files);

            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    private static Dictionary<string, string?> DefaultSettings(string? provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mcp:ResourceUrl"] = TestTokens.ResourceUrl,
            ["Mcp:Actor:Id"] = ActorId,
            ["Mcp:Actor:WorkloadIdentity:Kind"] = "TokenFile",
            ["Mcp:Actor:WorkloadIdentity:TokenFilePath"] = TokenFilePath,
            ["Storyteller:BaseUrl"] = "https://api.test/api",
        };

        if (string.Equals(provider, nameof(McpIdentityProvider.AuthKit), StringComparison.OrdinalIgnoreCase))
        {
            settings["Mcp:Authentication:Issuer"] = TestTokens.AuthKitIssuer;
        }
        else if (string.Equals(provider, nameof(McpIdentityProvider.Keycloak), StringComparison.OrdinalIgnoreCase))
        {
            settings["Mcp:Authentication:Issuer"] = TestTokens.KeycloakIssuer;
        }
        else
        {
            settings["Mcp:Authentication:TenantId"] = TestTokens.TenantId;
            settings["Mcp:Authentication:ClientId"] = TestTokens.McpClientId;
            settings["Mcp:Authentication:AllowedClientIds:0"] = TestTokens.ClientAppId;
        }

        return settings;
    }
}
