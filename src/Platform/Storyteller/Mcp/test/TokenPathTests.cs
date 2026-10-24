using System.Net;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public sealed class TokenPathTests : IDisposable
{
    private const string AccountJson = """{"Id":"user-1","UserName":"ada@example.com","Name":"Ada","AccessMap":{"house.main":"Contributor"}}""";

    private readonly McpTestHost _host = new();

    [Fact]
    public async Task ToolCall_SendsTheOnBehalfOfToken_NeverTheMcpToken()
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
        _host.Tokens.UserTokens.ShouldContain(inbound);
    }

    [Fact]
    public async Task TwoPeople_EachSendTheirOwnToken()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(AccountJson);
        await using var ada = await _host.ConnectAsync(TestTokens.Create("ada"));
        await using var bob = await _host.ConnectAsync(TestTokens.Create("bob"));

        await Task.WhenAll(
            ada.CallAsync("get_my_account"),
            bob.CallAsync("get_my_account"),
            ada.CallAsync("list_projects"),
            bob.CallAsync("list_projects"));

        var authorizations = _host.Api.Requests.Select(request => request.Authorization).ToList();
        authorizations.Count.ShouldBe(4);
        authorizations.Count(header => header == $"Bearer {StubTokenProvider.DownstreamFor("ada")}").ShouldBe(2);
        authorizations.Count(header => header == $"Bearer {StubTokenProvider.DownstreamFor("bob")}").ShouldBe(2);
    }

    [Fact]
    public async Task ConsentRequired_IsAToolErrorWithTheFixedText()
    {
        _host.Tokens.Failure = new StorytellerSignInRequiredException(StorytellerSignInRequiredException.ConsentMessage, "invalid_grant");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain(StorytellerSignInRequiredException.ConsentMessage);
        _host.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingAccount_HintsAtTheIdentityOfTheSignIn()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json("""{"Message":"Account not found."}""", HttpStatusCode.NotFound);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_my_account");

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("may differ");
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
    public async Task ApiUnauthorized_NamesTheDelegatedClientSetting()
    {
        _host.Api.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("list_projects");

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain(ApiErrors.UnauthorizedMessage);
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
}
