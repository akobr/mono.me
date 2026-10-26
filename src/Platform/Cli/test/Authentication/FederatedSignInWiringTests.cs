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
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// The registrations of Startup with a machine: one call of a typed client gets a token from the SDK credential (for a
// federated machine in a GitHub Actions job: asks the runner for an ID token and exchanges it) and sends it to the API.
public class FederatedSignInWiringTests : IDisposable
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string GitHubRequestToken = "github-request-token-value";
    private const string TokenEndpoint = "https://storyteller.example.com/api/v1/acme/billing/access/token";
    private const string TokenUrl = "https://auth.example.com/oauth2/token";
    private const string AnnotationsUrl = "https://storyteller.example.com/api/v1/acme/billing/default/annotations";
    private const string AnnotationsAnswer = """{"Annotations":[],"Count":0}""";

    // The primary handler of every client and of the token requests: the runner, the token endpoint and the API
    // answer in turn.
    private readonly ScriptedHttpHandler _http = new();
    private ServiceProvider? _provider;

    [Fact]
    public async Task ACallOfATypedClient_AsksTheRunner_Exchanges_AndSendsTheStorytellerToken()
    {
        UseFederatedMachineInAGitHubActionsJob();
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TokenAnswer("st-1"))
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
        UseFederatedMachineInAGitHubActionsJob();
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TokenAnswer("st-1"))
            .Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-2"))
            .Respond(HttpStatusCode.OK, TokenAnswer("st-2"))
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

    // Change against today's sform: a client-credentials token the API refused is renewed too.
    [Fact]
    public async Task AnUnauthorizedCall_OfClientCredentials_IsRenewedOnce_AndSentOnceMore()
    {
        Build(new()
        {
            [StorytellerVariables.ENDPOINT] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = "client_01",
            [StorytellerVariables.CLIENT_SECRET] = "client-secret",
            [StorytellerVariables.TOKEN_URL] = TokenUrl,
        });
        _http.Respond(HttpStatusCode.OK, TokenAnswer("cc-1"))
            .Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, TokenAnswer("cc-2"))
            .Respond(HttpStatusCode.OK, AnnotationsAnswer);

        await CallAsync();

        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization)).ShouldBe(
        [
            (HttpMethod.Post, TokenUrl, null),
            (HttpMethod.Get, AnnotationsUrl, "Bearer cc-1"),
            (HttpMethod.Post, TokenUrl, null),
            (HttpMethod.Get, AnnotationsUrl, "Bearer cc-2"),
        ]);
    }

    // Startup registers the service with its public constructor: the settings and the file system, no HTTP client of
    // its own, as the SDK credential makes the token requests.
    [Fact]
    public void Startup_ResolvesTheMachineSignIn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        Startup.ConfigureMachineSignIn(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMachineSignIn>().ShouldBeSameAs(provider.GetRequiredService<MachineAuthenticationService>());
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }

    private static string GitHubAnswer(string idToken)
    {
        return $$"""{"count":1,"value":"{{idToken}}"}""";
    }

    private static string TokenAnswer(string accessToken)
    {
        return $$"""{"access_token":"{{accessToken}}","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":900,"scope":"story:read"}""";
    }

    private void UseFederatedMachineInAGitHubActionsJob()
    {
        Build(new()
        {
            [StorytellerVariables.ENDPOINT] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL] = GitHubRequestUrl,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = GitHubRequestToken,
        });
    }

    private void Build(Dictionary<string, string> environment)
    {
        var services = new ServiceCollection();

        // As Startup.ConfigureServices registers them; the token store and discovery are never used by a machine.
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.AddHttpClient();
        Startup.ConfigureMachineSignIn(services);

        // Startup reads the variables of the process, and the SDK credential sends its token requests to the network.
        services.Replace(ServiceDescriptor.Singleton(TestMachines.Settings(environment)));
        services.Replace(ServiceDescriptor.Singleton(provider => TestMachines.Create(
            provider.GetRequiredService<MachineSignInSettings>(),
            provider.GetRequiredService<IFileSystem>(),
            TestMachines.Hooks(_http))));
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddSingleton(Mock.Of<IAuthenticationConfigurationResolver>());
        services.AddSingleton<IAuthenticationService, AuthenticationServiceSelector>();
        services.AddSingleton<ICommandContext, CommandContext>();
        Startup.ConfigureStorytellerSdk(services);
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _http));

        _provider = services.BuildServiceProvider();
    }

    // A command first checks and binds its target (BaseContextCommand), then calls the API.
    private async Task<AnnotationsResponse> CallAsync()
    {
        _provider!.GetRequiredService<ICommandContext>().TrySetExplicitTarget("acme.billing", null);
        return await _provider!.GetRequiredService<IAnnotationsApiClient>().GetAnnotationsAsync("acme", "billing", "default", null, null, null);
    }
}
