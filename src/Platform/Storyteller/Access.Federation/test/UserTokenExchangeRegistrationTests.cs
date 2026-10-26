using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public class UserTokenExchangeRegistrationTests
{
    private const string Secret = "a local development secret";

    [Fact]
    public async Task AddUserTokenExchange_RegistersTheExchangeOnce_WithoutAnotherMachineTokenValidator()
    {
        using var host = BuildHost(Environments.Production, [], registerTwice: true);

        await host.StartAsync();

        host.Services.GetRequiredService<IUserTokenExchange>().ShouldBeOfType<UserTokenExchangeService>();
        host.Services.GetServices<IUserTokenExchange>().Count().ShouldBe(1);
        host.Services.GetServices<ActorAuthenticator>().Count().ShouldBe(1);
        host.Services.GetServices<IMachineTokenValidator>().Single().ShouldBeOfType<StorytellerTokenValidator>();
        await host.StopAsync();
    }

    [Fact]
    public void AddUserTokenExchange_BeforeTheFederation_Throws()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<InvalidOperationException>(() => services.AddUserTokenExchange(new ConfigurationBuilder().Build()));

        exception.Message.ShouldContain(nameof(EntryPoint.AddFederatedMachineAccess));
    }

    [Fact]
    public async Task InvalidExchangeOptions_FailWhenTheHostStarts()
    {
        using var host = BuildHost(Environments.Production, new Dictionary<string, string?> { ["Auth:TokenExchange:TokenLifetimeMinutes"] = "30" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("TokenLifetimeMinutes");
    }

    // The aud of a client assertion may be the token endpoint the metadata publishes, which only PublicBaseUrl names.
    [Fact]
    public async Task ClientAssertionActor_WithoutPublicBaseUrl_FailsWhenTheHostStarts()
    {
        using var host = BuildHost(Environments.Production, ActorSettings("ClientAssertion:JwksUri", "https://mcp.example/.well-known/jwks.json"));

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("Auth:TokenExchange:Actors:0:Authentication:ClientAssertion needs MachineAuth:Federation:PublicBaseUrl");
    }

    [Fact]
    public async Task ClientAssertionActor_WithPublicBaseUrl_Starts()
    {
        var settings = ActorSettings("ClientAssertion:JwksUri", "https://mcp.example/.well-known/jwks.json");
        settings["MachineAuth:Federation:PublicBaseUrl"] = "https://storyteller.example/api";
        using var host = BuildHost(Environments.Production, settings);

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task WorkloadIdentityActor_WithoutPublicBaseUrl_Starts()
    {
        var settings = ActorSettings("WorkloadIdentity:Issuer", "https://sts.windows.net/9c8b7a6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d/");
        settings["Auth:TokenExchange:Actors:0:Authentication:WorkloadIdentity:Subject"] = "managed-identity-oid";
        settings["Auth:TokenExchange:Actors:0:Authentication:WorkloadIdentity:Audience"] = "api://storyteller-token-exchange";
        using var host = BuildHost(Environments.Production, settings);

        await host.StartAsync();
        await host.StopAsync();
    }

    // The whole graph from the host: the actor of the settings, the provider's subject validator and the account.
    [Fact]
    public async Task Exchange_ResolvedFromTheHost_IssuesAUserToken()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)));
        var subjects = new FakeUserSubjectTokenValidator
        {
            Result = UserSubjectTokenResult.Accepted("user_01", "client-1", DateTimeOffset.UtcNow.AddHours(1), null, "jti-1", [new Claim("sub", "user_01")]),
        };
        var access = new Mock<IAccessService>();
        access.Setup(service => service.GetAccountAsync("user_01")).ReturnsAsync(new Account
        {
            Id = "user_01",
            UserName = "ada@example.com",
            Name = "Ada",
            AccessMap = new Dictionary<string, AccountRole>(),
        });
        using var host = BuildHost(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["Auth:TokenExchange:Enabled"] = "true",
                ["Auth:TokenExchange:Actors:0:Id"] = "mcp",
                ["Auth:TokenExchange:Actors:0:Authentication:ClientSecretHash"] = hash,
                ["Auth:TokenExchange:Actors:0:SubjectAudiences:0"] = "https://mcp.example/mcp",
            },
            services => services.AddSingleton<IUserSubjectTokenValidator>(subjects),
            access.Object);

        await host.StartAsync();

        var result = await host.Services.GetRequiredService<IUserTokenExchange>().ExchangeAsync(new UserTokenExchangeRequest
        {
            GrantType = OAuthTokenExchange.GrantType,
            ClientId = "mcp",
            ClientSecret = Secret,
            SubjectToken = "subject.token.value",
            SubjectTokenType = OAuthTokenExchange.TokenTypes.AccessToken,
        });

        result.Succeeded.ShouldBeTrue();
        result.Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
        subjects.Calls.ShouldHaveSingleItem().Audiences.ShouldBe(["https://mcp.example/mcp"]);
        await host.StopAsync();
    }

    private static Dictionary<string, string?> ActorSettings(string proofKey, string proofValue)
    {
        return new Dictionary<string, string?>
        {
            ["Auth:TokenExchange:Enabled"] = "true",
            ["Auth:TokenExchange:Actors:0:Id"] = "mcp",
            [$"Auth:TokenExchange:Actors:0:Authentication:{proofKey}"] = proofValue,
            ["Auth:TokenExchange:Actors:0:SubjectAudiences:0"] = "https://mcp.example/mcp",
        };
    }

    private static IHost BuildHost(
        string environment,
        Dictionary<string, string?> values,
        Action<IServiceCollection>? configure = null,
        IAccessService? accessService = null,
        bool registerTwice = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = environment;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:ClientId"] = "4a1f7d2e-8b3c-4e5f-9a6b-1c2d3e4f5a6b",
            ["Auth:TenantId"] = "9c8b7a6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
        });
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.Configure<MachineAuthenticationOptions>(builder.Configuration.GetSection(MachineAuthenticationOptions.SectionName));
        builder.Services.AddSingleton(accessService ?? Mock.Of<IAccessService>());
        builder.Services.AddSingleton(Mock.Of<IUserSubjectTokenValidator>());
        configure?.Invoke(builder.Services);
        builder.Services.AddFederatedMachineAccess(builder.Configuration);
        builder.Services.AddUserTokenExchange(builder.Configuration);

        if (registerTwice)
        {
            builder.Services.AddUserTokenExchange(builder.Configuration);
        }

        return builder.Build();
    }
}
