using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;

using Azure.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The exchange client as the host registers it (AddStorytellerTokenExchange), next to the resilience handler that
// Aspire.ServiceDefaults adds to every client, and in front of its own token endpoint instead of the recording API of
// McpTestHost. Endpoint: false keeps the registered primary handler; exchangeTimeout replaces ExchangeTimeout.
internal sealed class ExchangeFixture : IDisposable
{
    public const string BaseUrl = "https://api.test/api";
    public const string TokenEndpoint = "https://api.test/api/v1/access/token";
    public const string ActorId = "mcp";
    public const string TokenFilePath = "/run/secrets/actor-token";
    public const string CertificatePath = "/etc/mcp/actor.pfx";

    public ExchangeFixture(McpActorOptions? actor = null, TokenCredential? managedIdentity = null, bool endpoint = true, TimeSpan? exchangeTimeout = null)
    {
        Actor = actor ?? TokenFileActor();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs));
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddSingleton(Options.Create(new McpHostOptions { Actor = Actor }));
        services.AddSingleton(Options.Create(new StorytellerApiOptions { BaseUrl = BaseUrl }));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IFileSystem>(Files);

        if (managedIdentity is not null)
        {
            services.AddSingleton(provider => new McpActorCredential(
                provider.GetRequiredService<IOptions<McpHostOptions>>(),
                provider.GetRequiredService<McpActorKey>(),
                Files,
                Clock,
                provider.GetRequiredService<ILogger<McpActorCredential>>(),
                managedIdentity));
        }

        services.AddStorytellerTokenExchange();

        // The last registration wins.
        if (exchangeTimeout is { } timeout)
        {
            services.AddSingleton<IStorytellerTokenProvider>(provider => new StorytellerTokenExchangeProvider(
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<McpActorCredential>(),
                provider.GetRequiredService<IOptions<StorytellerApiOptions>>(),
                Clock,
                provider.GetRequiredService<ILogger<StorytellerTokenExchangeProvider>>(),
                timeout,
                StorytellerTokenExchangeProvider.CacheSizeLimit));
        }

        if (endpoint)
        {
            services.AddHttpClient(StorytellerTokenExchangeProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Endpoint);
        }

        Services = services.BuildServiceProvider();
    }

    public McpActorOptions Actor { get; }

    public TokenEndpointHandler Endpoint { get; } = new();

    public TestClock Clock { get; } = new();

    public MockFileSystem Files { get; } = new();

    public RecordingLoggerProvider Logs { get; } = new();

    public ServiceProvider Services { get; }

    public IStorytellerTokenProvider Provider => Services.GetRequiredService<IStorytellerTokenProvider>();

    public static McpActorOptions TokenFileActor()
    {
        return new McpActorOptions
        {
            Id = ActorId,
            WorkloadIdentity = new McpWorkloadIdentityOptions { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = TokenFilePath },
        };
    }

    public static McpActorOptions ManagedIdentityActor(string audience = "api://storyteller-token-exchange")
    {
        return new McpActorOptions
        {
            Id = ActorId,
            WorkloadIdentity = new McpWorkloadIdentityOptions { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = audience },
        };
    }

    public static McpActorOptions ClientAssertionActor(string? password = TestCertificates.Password, string? audience = null)
    {
        return new McpActorOptions
        {
            Id = ActorId,
            ClientAssertionKey = new McpClientAssertionKeyOptions { CertificatePath = CertificatePath, CertificatePassword = password, Audience = audience },
        };
    }

    public static McpActorOptions ClientSecretActor(string secret)
    {
        return new McpActorOptions { Id = ActorId, ClientSecret = secret };
    }

    public void Dispose()
    {
        Services.Dispose();
    }
}
