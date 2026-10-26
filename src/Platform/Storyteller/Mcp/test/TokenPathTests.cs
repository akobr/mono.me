using System.IO.Abstractions.TestingHelpers;
using System.Net;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

using Polly.Timeout;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public sealed class TokenPathTests : IDisposable
{
    private const string AccountJson = """{"Id":"user-1","UserName":"ada@example.com","Name":"Ada","AccessMap":{"house.main":"Contributor"}}""";
    private const string Marker = "EXCHANGE-MARKER-3b7e";

    private readonly McpTestHost _host = new();

    public static TheoryData<string, Exception, string> SignInFailures() => new()
    {
        { "no account", new StorytellerSignInRefusedException(StorytellerSignInRefusedException.NoAccountMessage), "Register once with sform or the admin UI." },
        { "token refused", new StorytellerSignInRefusedException("The subject token is not accepted for this actor."), "The subject token is not accepted for this actor." },
        { "actor refused", new StorytellerActorException(StorytellerActorException.ActorNotAcceptedMessage, "invalid_client"), StorytellerActorException.ActorNotAcceptedMessage },
        { "exchange off", new StorytellerActorException(StorytellerActorException.ExchangeDisabledMessage, "unsupported_grant_type"), StorytellerActorException.ExchangeDisabledMessage },
        { "unavailable", new StorytellerSignInUnavailableException(), StorytellerSignInUnavailableException.UnavailableMessage },
        { "other refusal", new StorytellerSignInFailedException("invalid_scope"), "could not get a token for the Storyteller API (invalid_scope)" },
    };

    // A provider that lets a transport failure escape still fails the sign-in, never the tool.
    public static TheoryData<string, Exception> EscapedFailures() => new()
    {
        { "connection", new HttpRequestException($"Connection reset ({Marker}).") },
        { "attempt timeout", new TimeoutRejectedException($"Timed out ({Marker}).") },
        { "HttpClient timeout", new TaskCanceledException($"Timed out ({Marker}).", new TimeoutException()) },
    };

    [Fact]
    public async Task ToolCall_SendsTheExchangedToken_NeverTheMcpToken()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(AccountJson);
        var inbound = TestTokens.Create("user-1");
        await using var client = await _host.ConnectAsync(inbound);

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldNotBe(true);
        result.Text().ShouldContain("ada@example.com");
        var request = _host.Api.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe("https://api.test/api/v1/access/account");
        request.Authorization.ShouldBe($"Bearer {StubTokenProvider.DownstreamFor("user-1")}");
        request.Authorization!.ShouldNotContain(inbound);
        _host.Tokens.UserTokens.ShouldBe([inbound]);
    }

    [Fact]
    public async Task TwoPeople_EachSendTheirOwnToken()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(AccountJson);
        var adaToken = TestTokens.Create("ada");
        var bobToken = TestTokens.Create("bob");
        await using var ada = await _host.ConnectAsync(adaToken);
        await using var bob = await _host.ConnectAsync(bobToken);

        await Task.WhenAll(
            ada.CallAsync("get_my_account"),
            bob.CallAsync("get_my_account"),
            ada.CallAsync("list_projects"),
            bob.CallAsync("list_projects"));

        var authorizations = _host.Api.Requests.Select(request => request.Authorization).ToList();
        authorizations.Count.ShouldBe(4);
        authorizations.Count(header => header == $"Bearer {StubTokenProvider.DownstreamFor("ada")}").ShouldBe(2);
        authorizations.Count(header => header == $"Bearer {StubTokenProvider.DownstreamFor("bob")}").ShouldBe(2);

        // Each exchange got the token of the person who called.
        _host.Tokens.UserTokens.Count(token => token == adaToken).ShouldBe(2);
        _host.Tokens.UserTokens.Count(token => token == bobToken).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(SignInFailures))]
    public async Task SignInFailure_IsAToolErrorWithItsText_AndNoApiCall(string because, Exception failure, string expected)
    {
        _host.Tokens.Failure = failure;
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true, because);
        result.Text().ShouldContain(expected, Case.Sensitive, because);
        _host.Api.Requests.ShouldBeEmpty(because);

        var errorCode = ((StorytellerSignInException)failure).ErrorCode;
        _host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool get_my_account: no Storyteller token for the person ({errorCode}).", StringComparison.Ordinal), because);
    }

    [Theory]
    [MemberData(nameof(EscapedFailures))]
    public async Task TransportFailureOfTheProvider_IsTheUnavailableSignIn(string because, Exception failure)
    {
        _host.Tokens.Failure = failure;
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true, because);
        result.Text().ShouldBe(StorytellerSignInUnavailableException.UnavailableMessage, because);
        _host.Api.Requests.ShouldBeEmpty(because);
        _host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool get_my_account: the token exchange did not finish ({failure.GetType().Name}).", StringComparison.Ordinal), because);
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal), because);
    }

    [Fact]
    public async Task MissingAccount_SaysRegisterOnce()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json("""{"Message":"Account not found."}""", HttpStatusCode.NotFound);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(DiscoveryTools.MissingAccountMessage);
        result.Text().ShouldContain("Register once with sform or the admin UI.");
    }

    [Fact]
    public async Task ApiError_IsAToolErrorWithStatusAndCode()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json("""{"Message":"No Reader access to 'house.main'.","ErrorCode":"AccessDenied"}""", HttpStatusCode.Forbidden);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("list_views", new { organization = "house", project = "main" });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("Storyteller API answered 403 AccessDenied: No Reader access to 'house.main'.");
        _host.Logs.Entries.ShouldContain(entry => entry.Contains("Tool list_views: the Storyteller API answered 403 AccessDenied.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApiUnauthorized_PointsAtTheTokenExchange()
    {
        _host.Api.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("list_projects");

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain(ApiErrors.UnauthorizedMessage);
        result.Text().ShouldNotContain("DelegatedClients");
    }

    // The API refuses a machine that reads secrets to the actor with ActorRestricted: the text names what was refused.
    [Fact]
    public async Task MachineThatReadsSecrets_RefusedToTheActor_SaysWhatWasRefused()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(
            """{"Message":"The token acts for the person through an actor that may not reveal secrets.","ErrorCode":"ActorRestricted"}""",
            HttpStatusCode.Forbidden);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("create_machine", new { organization = "house", project = "main", scopes = new[] { "ConfigurationRead", "SecretsRead" } });

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe($"Storyteller API answered 403 ActorRestricted: {ApiErrors.ActorRestrictedMessage}");
        _host.Api.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public void Host_UsesTheTokenExchangeProvider()
    {
        using var host = new McpTestHost(exchange: true);

        host.Services.GetRequiredService<IStorytellerTokenProvider>().ShouldBeOfType<StorytellerTokenExchangeProvider>();
    }

    // The whole path in the host: the MCP token goes to the API's token exchange as the subject token, with the actor's
    // proof, and the API calls of the tools carry the Storyteller token it answers, cached for the second call.
    [Fact]
    public async Task ToolCall_ExchangesTheMcpToken_AndSendsTheStorytellerToken()
    {
        using var host = new McpTestHost(exchange: true);
        host.Files.AddFile(McpTestHost.TokenFilePath, new MockFileData("actor-token"));
        host.Api.Respond = request => IsTokenExchange(request)
            ? RecordingApiHandler.Json("""{"access_token":"storyteller-user-1","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":600,"scope":"User.Impersonation","restrictions":[]}""")
            : RecordingApiHandler.Json(AccountJson);
        var inbound = TestTokens.Create("user-1");
        await using var client = await host.ConnectAsync(inbound);

        var first = await client.CallAsync("get_my_account");
        var second = await client.CallAsync("get_my_account");

        first.IsError.ShouldNotBe(true);
        second.IsError.ShouldNotBe(true);
        var requests = host.Api.Requests.ToList();
        requests.Count.ShouldBe(3);

        var exchange = requests[0];
        exchange.Method.ShouldBe(HttpMethod.Post);
        exchange.Uri.ToString().ShouldBe("https://api.test/api/v1/access/token");
        exchange.Authorization.ShouldBeNull();
        var form = QueryHelpers.ParseQuery(exchange.Body);
        form["subject_token"].ToString().ShouldBe(inbound);
        form["client_id"].ToString().ShouldBe(McpTestHost.ActorId);
        form["grant_type"].ToString().ShouldBe(StorytellerTokenExchangeProvider.GrantType);
        form["actor_token"].ToString().ShouldBe("actor-token");

        foreach (var account in requests.Skip(1))
        {
            account.Method.ShouldBe(HttpMethod.Get);
            account.Uri.ToString().ShouldBe("https://api.test/api/v1/access/account");
            account.Authorization.ShouldBe("Bearer storyteller-user-1");
            account.Authorization!.ShouldNotContain(inbound);
        }
    }

    // In the full host (the service defaults and the SDK clients next to it), an exchange the API cannot serve is sent
    // once: a retry would count again against the API's replay limit.
    [Fact]
    public async Task ExchangeThatIsUnavailable_IsSentOnce_InTheHost()
    {
        using var host = new McpTestHost(exchange: true);
        host.Files.AddFile(McpTestHost.TokenFilePath, new MockFileData("actor-token"));
        host.Api.Respond = request => IsTokenExchange(request)
            ? RecordingApiHandler.Json("""{"error":"temporarily_unavailable"}""", HttpStatusCode.ServiceUnavailable)
            : RecordingApiHandler.Json(AccountJson);
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(StorytellerSignInUnavailableException.UnavailableMessage);
        host.Api.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/v1/access/token", StringComparison.Ordinal)).ShouldBe(1);
        host.Api.Requests.ShouldNotContain(request => request.Uri.AbsolutePath.EndsWith("/v1/access/account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnexpectedApiAnswer_NeverWritesTheBodyToALog()
    {
        const string marker = "BODY-MARKER-42";
        _host.Api.Respond = _ => RecordingApiHandler.Json($$"""{"Message":"{{marker}}","ErrorCode":"{{marker}}"}""", HttpStatusCode.UnavailableForLegalReasons);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("list_projects");

        result.IsError.ShouldBe(true);
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(marker, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static bool IsTokenExchange(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/v1/access/token", StringComparison.Ordinal);
    }
}
