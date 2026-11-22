using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// What the operator hears once at start, per actor, while the exchange is on: an actor without AllowedClientIds accepts
// a token of any client for its SubjectAudiences, which is a warning unless UntrustedClientRestrictions narrows the
// clients outside TrustedClientIds; lists without effect and denied clients an actor lists warn too, and the denied
// clients are counted.
public class TokenExchangeActorNoticeTests
{
    private const string AnyClientWarning = "The actor {0} of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences ({1}) is accepted for the exchange. List the clients to narrow it, or set UntrustedClientRestrictions (for example ReadOnly and NoSecretReveal) for the clients outside TrustedClientIds.";

    [Fact]
    public async Task StartAsync_WarnsOncePerActorWithoutAllowedClientIdsOrUntrustedClientRestrictions()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example/mcp", "client-guid"] },
                new TokenExchangeActorOptions { Id = "reviewer", SubjectAudiences = ["https://reviewer.example"], AllowedClientIds = ["vscode"] },
                new TokenExchangeActorOptions { Id = "assistant", SubjectAudiences = ["https://assistant.example"] },
            ],
        });

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Warning, LogLevel.Warning]);
        logger.Entries.Select(entry => entry.Message).ShouldBe(
        [
            string.Format(AnyClientWarning, "mcp", "https://mcp.example/mcp, client-guid"),
            string.Format(AnyClientWarning, "assistant", "https://assistant.example"),
        ]);
    }

    // NoSecretReveal is the actor's own restriction without AllowSecrets, so UntrustedClientRestrictions that only repeat it
    // leave every client with the actor's whole policy: still the exposure the warning is about, and trust has no effect.
    [Fact]
    public async Task StartAsync_WithUntrustedClientRestrictionsThatAddNothing_StillWarns()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions
                {
                    Id = "mcp",
                    SubjectAudiences = ["https://mcp.example/mcp"],
                    TrustedClientIds = ["https://claude.ai/oauth/mcp-oauth-client-metadata"],
                    UntrustedClientRestrictions = [ActorRestrictions.NoSecretReveal],
                },
            ],
        });

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Warning, LogLevel.Warning]);
        logger.Entries.Select(entry => entry.Message).ShouldBe(
        [
            string.Format(AnyClientWarning, "mcp", "https://mcp.example/mcp"),
            "The actor mcp of Auth:TokenExchange:Actors lists 1 TrustedClientIds: TrustedClientIds has no effect without UntrustedClientRestrictions that add to the actor's own restrictions.",
        ]);
    }

    // The production default: every client reads only and reveals no secret unless it is trusted.
    [Fact]
    public async Task StartAsync_WithUntrustedClientRestrictions_InformsWhatTheUntrustedClientsGet()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions
                {
                    Id = "mcp",
                    SubjectAudiences = ["https://mcp.example/mcp"],
                    TrustedClientIds = ["https://claude.ai/oauth/mcp-oauth-client-metadata", " https://claude.ai/oauth/mcp-oauth-client-metadata "],
                    UntrustedClientRestrictions = [ActorRestrictions.ReadOnly, ActorRestrictions.NoSecretReveal],
                },
                new TokenExchangeActorOptions
                {
                    Id = "assistant",
                    SubjectAudiences = ["https://assistant.example"],
                    UntrustedClientRestrictions = [ActorRestrictions.ReadOnly],
                },
            ],
        });

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Information, LogLevel.Information]);
        logger.Entries.Select(entry => entry.Message).ShouldBe(
        [
            "The actor mcp of Auth:TokenExchange:Actors accepts a token of any client for https://mcp.example/mcp; the clients outside its 1 TrustedClientIds get ReadOnly, NoSecretReveal.",
            "The actor assistant of Auth:TokenExchange:Actors accepts a token of any client for https://assistant.example; the clients outside its 0 TrustedClientIds get ReadOnly.",
        ]);
    }

    // AllowSecrets takes NoSecretReveal from the actor's own policy: without AllowedClientIds, only the restrictions of
    // the untrusted clients can keep the person's secrets from a client nobody vetted.
    [Fact]
    public async Task StartAsync_AllowSecretsForAnyClient_WarnsUnlessTheUntrustedClientsRevealNoSecret()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions
                {
                    Id = "mcp",
                    SubjectAudiences = ["https://mcp.example/mcp"],
                    AllowSecrets = true,
                    TrustedClientIds = ["vscode"],
                    UntrustedClientRestrictions = [ActorRestrictions.ReadOnly],
                },
                new TokenExchangeActorOptions
                {
                    Id = "safe",
                    SubjectAudiences = ["https://safe.example"],
                    AllowSecrets = true,
                    UntrustedClientRestrictions = [ActorRestrictions.ReadOnly, ActorRestrictions.NoSecretReveal],
                },
                new TokenExchangeActorOptions
                {
                    Id = "listed",
                    SubjectAudiences = ["https://listed.example"],
                    AllowSecrets = true,
                    AllowedClientIds = ["vscode"],
                },
            ],
        });

        logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ShouldBe(
        [
            "The actor mcp of Auth:TokenExchange:Actors has AllowSecrets and no AllowedClientIds: every client may read the person's secrets through it; add NoSecretReveal to UntrustedClientRestrictions.",
        ]);
    }

    // Trusting a client changes nothing while every client gets the actor's whole policy anyway.
    [Fact]
    public async Task StartAsync_TrustedClientIdsWithoutUntrustedClientRestrictions_Warns()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            Actors =
            [
                new TokenExchangeActorOptions
                {
                    Id = "mcp",
                    SubjectAudiences = ["https://mcp.example/mcp"],
                    AllowedClientIds = ["vscode", "cursor"],
                    TrustedClientIds = ["vscode"],
                },
            ],
        });

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("The actor mcp of Auth:TokenExchange:Actors lists 1 TrustedClientIds: TrustedClientIds has no effect without UntrustedClientRestrictions that add to the actor's own restrictions.");
    }

    // Deny wins, so an id an actor also allows or trusts is a mistake worth a warning, matched as the deny list matches;
    // the denied clients are counted, and their ids stay in the settings.
    [Fact]
    public async Task StartAsync_DeniedClients_WarnWhereAnActorListsThem_AndAreCounted()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            Enabled = true,
            DeniedClientIds = ["https://chatgpt.com/oauth/client.json", " VSCODE ", "cursor", "Cursor"],
            Actors =
            [
                new TokenExchangeActorOptions
                {
                    Id = "mcp",
                    SubjectAudiences = ["https://mcp.example/mcp"],
                    TrustedClientIds = ["vscode"],
                    UntrustedClientRestrictions = [ActorRestrictions.ReadOnly],
                },
                new TokenExchangeActorOptions
                {
                    Id = "reviewer",
                    SubjectAudiences = ["https://reviewer.example"],
                    AllowedClientIds = ["https://ChatGPT.com/oauth/client.json"],
                },
            ],
        });

        logger.Entries.Select(entry => (entry.Level, entry.Message)).Skip(1).ShouldBe(
        [
            (LogLevel.Warning, "The client https://chatgpt.com/oauth/client.json of Auth:TokenExchange:DeniedClientIds is also in the TrustedClientIds or AllowedClientIds of the actor reviewer; it is denied."),
            (LogLevel.Warning, "The client VSCODE of Auth:TokenExchange:DeniedClientIds is also in the TrustedClientIds or AllowedClientIds of the actor mcp; it is denied."),
            (LogLevel.Information, "Auth:TokenExchange:DeniedClientIds blocks 3 clients for every actor of the exchange."),
        ]);
    }

    // A disabled exchange accepts no token, so there is nothing to tell.
    [Fact]
    public async Task StartAsync_WhileTheExchangeIsOff_LogsNothing()
    {
        var logger = await StartAsync(new TokenExchangeOptions
        {
            DeniedClientIds = ["cursor"],
            Actors = [new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example/mcp"], TrustedClientIds = ["cursor"] }],
        });

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

    private static async Task<RecordingLogger<TokenExchangeActorNotice>> StartAsync(TokenExchangeOptions options)
    {
        var logger = new RecordingLogger<TokenExchangeActorNotice>();
        var notice = new TokenExchangeActorNotice(Options.Create(options), logger);

        await notice.StartAsync(CancellationToken.None);

        return logger;
    }
}
