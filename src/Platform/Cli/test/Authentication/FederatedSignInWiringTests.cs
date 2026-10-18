using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// The registrations of Startup with a federated machine in a GitHub Actions job: one call of a typed client asks
// the runner for an ID token, exchanges it, and sends the Storyteller token to the API.
public class FederatedSignInWiringTests : IDisposable
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string GitHubRequestToken = "github-request-token-value";
    private const string TokenEndpoint = "https://storyteller.example.com/api/v1/acme/billing/access/token";
    private const string AnnotationsUrl = "https://storyteller.example.com/api/v1/acme/billing/default/annotations";
    private const string AnnotationsAnswer = """{"Annotations":[],"Count":0}""";

    // The primary handler of every client: the runner, the token endpoint and the API answer in turn.
    private readonly ScriptedHttpHandler _http = new();
    private readonly ServiceProvider _provider;

    public FederatedSignInWiringTests()
    {
        var environment = new Dictionary<string, string>
        {
            [StorytellerVariables.API_URL] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = GitHubRequestUrl,
            [GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = GitHubRequestToken,
        };
        var services = new ServiceCollection();

        // As Startup.ConfigureServices registers them; the token store and discovery are never used by a machine.
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.AddHttpClient();
        Startup.ConfigureMachineSignIn(services);

        // Startup reads the variables of the process.
        services.Replace(ServiceDescriptor.Singleton(MachineSignInSettings.FromEnvironment(name => environment.GetValueOrDefault(name))));
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddSingleton(Mock.Of<IAuthenticationConfigurationResolver>());
        services.AddSingleton<IAuthenticationService, AuthenticationServiceSelector>();
        services.AddSingleton<ICommandContext, CommandContext>();
        Startup.ConfigureStorytellerSdk(services);
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _http));

        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ACallOfATypedClient_AsksTheRunner_Exchanges_AndSendsTheStorytellerToken()
    {
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, AnnotationsAnswer);

        var annotations = await CallAsync();

        annotations.Count.ShouldBe(0);
        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization)).ShouldBe(
        [
            (HttpMethod.Get, $"{GitHubRequestUrl}&audience={Uri.EscapeDataString(TokenEndpoint)}", $"Bearer {GitHubRequestToken}"),
            (HttpMethod.Post, TokenEndpoint, null),
            (HttpMethod.Get, AnnotationsUrl, "Bearer st-1"),
        ]);
        _http.Requests[1].Form["subject_token"].ShouldBe("gh-jwt-1");
    }

    [Fact]
    public async Task AnUnauthorizedCall_IsRenewedOnce_AndSentOnceMore()
    {
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-2"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"))
            .Respond(HttpStatusCode.OK, AnnotationsAnswer);

        await CallAsync();

        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization)).ShouldBe(
        [
            (HttpMethod.Get, $"{GitHubRequestUrl}&audience={Uri.EscapeDataString(TokenEndpoint)}", $"Bearer {GitHubRequestToken}"),
            (HttpMethod.Post, TokenEndpoint, null),
            (HttpMethod.Get, AnnotationsUrl, "Bearer st-1"),
            (HttpMethod.Get, $"{GitHubRequestUrl}&audience={Uri.EscapeDataString(TokenEndpoint)}", $"Bearer {GitHubRequestToken}"),
            (HttpMethod.Post, TokenEndpoint, null),
            (HttpMethod.Get, AnnotationsUrl, "Bearer st-2"),
        ]);
        _http.Requests[4].Form["subject_token"].ShouldBe("gh-jwt-2");
    }

    // The tests above replace every primary handler. The real one of the exchange client must not follow a
    // redirect: the GitHub request carries the request token, and the exchange the platform token in its body.
    [Fact]
    public void TheExchangeClient_FollowsNoRedirect()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        Startup.ConfigureMachineSignIn(services);
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(MachineAuthenticationService));

        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        handler.ShouldBeOfType<HttpClientHandler>().AllowAutoRedirect.ShouldBeFalse();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static string GitHubAnswer(string idToken)
    {
        return $$"""{"count":1,"value":"{{idToken}}"}""";
    }

    private static string ExchangeAnswer(string accessToken)
    {
        return $$"""{"access_token":"{{accessToken}}","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":900,"scope":"story:read"}""";
    }

    // A command first checks and binds its target (BaseContextCommand), then calls the API.
    private async Task<AnnotationsResponse> CallAsync()
    {
        _provider.GetRequiredService<ICommandContext>().TrySetExplicitTarget("acme.billing", null);
        return await _provider.GetRequiredService<IAnnotationsApiClient>().GetAnnotationsAsync("acme", "billing", "default", null, null, null);
    }
}
