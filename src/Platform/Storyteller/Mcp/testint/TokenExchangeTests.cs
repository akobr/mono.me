using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Xunit.Abstractions;

namespace _42.Platform.Storyteller.Mcp.IntegrationTests;

// The token exchange end to end, against a configured environment (ExchangeTestSettings), with the server's own
// StorytellerTokenExchangeProvider and SDK clients: the person's MCP token and the actor's credential give a Storyteller
// user token with which the API finds the person's account, and a sign-in without an account is refused with the fixed
// text.
public class TokenExchangeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Exchange_GivesATokenWithWhichTheApiFindsThePersonsAccount()
    {
        var settings = ExchangeTestSettings.TryRead();

        if (settings is null)
        {
            output.WriteLine($"Skipped: set {ExchangeTestSettings.Variables} to run against an environment.");
            return;
        }

        if (settings.ExpectNoAccount)
        {
            output.WriteLine($"Skipped: {ExchangeTestSettings.ExpectNoAccountVariable} says the person has no account.");
            return;
        }

        await using var services = CreateServices(settings);

        var token = await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(settings.UserToken, CancellationToken.None);

        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(new JsonWebToken(token.AccessToken).EncodedPayload));
        var claims = payload.RootElement;
        var subject = claims.GetProperty("sub").GetString();
        output.WriteLine($"sub={subject} scope={token.Scope} restrictions=[{string.Join(", ", token.Restrictions)}] expires={token.ExpiresAt:O}");

        claims.GetProperty("token_use").GetString().ShouldBe("user");
        claims.GetProperty("act").GetProperty("sub").GetString().ShouldBe(settings.Actor.Id);
        token.Scope.Split(' ').ShouldContain("User.Impersonation");
        token.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // The SDK clients of one request read the token the call filter puts into StorytellerAccessToken.
        await using var request = services.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<StorytellerAccessToken>().Value = token.AccessToken;
        var account = await request.ServiceProvider.GetRequiredService<Sdk.IAccessApiClient>().GetAccountAsync(CancellationToken.None);

        output.WriteLine($"account={account.Id} userName={account.UserName}");
        account.Id.ShouldBe(subject, "The API found another account than the one the exchanged token names.");
    }

    [Fact]
    public async Task Exchange_ForAPersonWithoutAnAccount_IsRefusedWithTheFixedText()
    {
        var settings = ExchangeTestSettings.TryRead();

        if (settings is null)
        {
            output.WriteLine($"Skipped: set {ExchangeTestSettings.Variables} to run against an environment.");
            return;
        }

        if (!settings.ExpectNoAccount)
        {
            output.WriteLine($"Skipped: set {ExchangeTestSettings.ExpectNoAccountVariable}=true with the token of a person without an account.");
            return;
        }

        await using var services = CreateServices(settings);
        var provider = services.GetRequiredService<IStorytellerTokenProvider>();

        var exception = await Should.ThrowAsync<StorytellerSignInRefusedException>(() => provider.GetTokenAsync(settings.UserToken, CancellationToken.None));

        // The API's error_description, which the server shows as it is; it must stay the text the server knows.
        exception.Message.ShouldBe(StorytellerSignInRefusedException.NoAccountMessage);
    }

    // The host's exchange and SDK registrations, with the actor and the API base URL of the environment.
    private static ServiceProvider CreateServices(ExchangeTestSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new McpHostOptions { Actor = settings.Actor }));
        services.AddSingleton(Options.Create(new StorytellerApiOptions { BaseUrl = settings.BaseUrl }));
        services.AddStorytellerTokenExchange();
        services.AddStorytellerClients();
        return services.BuildServiceProvider();
    }
}
