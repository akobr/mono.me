using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// An actor without AllowedClientIds accepts a token of any client for its SubjectAudiences: the operator hears it once at
// start, per such actor, while the exchange is on.
public class TokenExchangeActorNoticeTests
{
    [Fact]
    public async Task StartAsync_WarnsOncePerActorWithoutAllowedClientIds()
    {
        var logger = new RecordingLogger<TokenExchangeActorNotice>();
        var notice = new TokenExchangeActorNotice(Options.Create(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example/mcp", "client-guid"] },
                new TokenExchangeActorOptions { Id = "reviewer", SubjectAudiences = ["https://reviewer.example"], AllowedClientIds = ["vscode"] },
                new TokenExchangeActorOptions { Id = "assistant", SubjectAudiences = ["https://assistant.example"] },
            ],
        }), logger);

        await notice.StartAsync(CancellationToken.None);

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Warning, LogLevel.Warning]);
        logger.Entries.Select(entry => entry.Message).ShouldBe(
        [
            "The actor mcp of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences (https://mcp.example/mcp, client-guid) is accepted for the exchange. List the clients to narrow it.",
            "The actor assistant of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences (https://assistant.example) is accepted for the exchange. List the clients to narrow it.",
        ]);
    }

    // A disabled exchange accepts no token, so there is nothing to warn about.
    [Fact]
    public async Task StartAsync_WhileTheExchangeIsOff_LogsNothing()
    {
        var logger = new RecordingLogger<TokenExchangeActorNotice>();
        var notice = new TokenExchangeActorNotice(Options.Create(new TokenExchangeOptions
        {
            Actors = [new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example/mcp"] }],
        }), logger);

        await notice.StartAsync(CancellationToken.None);

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void AddUserTokenExchange_RegistersTheNoticeOnce()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddFederatedMachineAccess(configuration);

        services.AddUserTokenExchange(configuration);
        services.AddUserTokenExchange(configuration);

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(TokenExchangeActorNotice)).ShouldBe(1);
    }
}
