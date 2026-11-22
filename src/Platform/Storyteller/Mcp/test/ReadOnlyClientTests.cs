using System.Net;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// 424-302: while the API lets the person's client app only read (ReadOnly in the exchange's restrictions), tools/list
// offers only the read-only tool set (ReadOnlyHint, and the secret reads under NoSecretReveal's own rule), and a call to
// another tool is refused before it reaches the API. A failed or slow exchange lists the tools as usual: the API refuses
// the change anyway. A plain 401 of the API forgets the cached token, a step-up 401 does not.
public class ReadOnlyClientTests
{
    private static readonly string[] WriteTools = ["create_machine", "reset_machine", "set_annotation", "delete_configuration", "set_key_value"];

    private static readonly string[] ReadTools = ["list_projects", "list_machines", "get_key_value", "explain_configuration", "suggest_configuration_placement"];

    private static readonly string[] SecretNames = ["reveal_key_value", "get_resolved_configuration", "check_configuration_bindings"];

    [Fact]
    public void ReadOnlyToolSet_IsTheReadOnlyHintAndTheSecretReads()
    {
        ReadOnlyToolSet.Contains("list_machines", true).ShouldBeTrue();
        ReadOnlyToolSet.Contains("create_machine", false).ShouldBeFalse();
        ReadOnlyToolSet.Contains("create_machine", null).ShouldBeFalse();
        ReadOnlyToolSet.Contains(SecretToolNames.RevealKeyValue, false).ShouldBeTrue();
        ReadOnlyToolSet.Contains(null).ShouldBeFalse();
    }

    [Fact]
    public async Task UnderReadOnly_TheListHasOnlyTheReadOnlyToolSet()
    {
        using var host = new McpTestHost(SecretToolsSetting());
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly);

        var tools = await ListToolsAsync(host);
        var names = tools.Select(tool => tool.Name).ToList();

        names.ShouldNotContain(name => WriteTools.Contains(name));
        ReadTools.ShouldBeSubsetOf(names);
        SecretNames.ShouldBeSubsetOf(names);
        tools.ShouldAllBe(tool => tool.ProtocolTool.Annotations!.ReadOnlyHint == true || SecretToolNames.Contains(tool.Name));
    }

    // The production default for a client app the API does not trust.
    [Fact]
    public async Task UnderReadOnlyAndNoSecretReveal_OnlyTheReadOnlyToolsAreListed()
    {
        using var host = new McpTestHost(SecretToolsSetting());
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly, StorytellerToken.NoSecretReveal);

        var tools = await ListToolsAsync(host);

        tools.ShouldNotBeEmpty();
        tools.ShouldAllBe(tool => tool.ProtocolTool.Annotations!.ReadOnlyHint == true);
        ReadTools.ShouldBeSubsetOf(tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task WithoutReadOnly_TheWriteToolsAreListed()
    {
        using var host = new McpTestHost();
        host.Tokens.Restrictions = Restrictions(StorytellerToken.NoSecretReveal);

        var names = (await ListToolsAsync(host)).Select(tool => tool.Name).ToList();

        WriteTools.ShouldBeSubsetOf(names);
    }

    // Fail open: hiding is presentation only, and a session must not start with a read-only list by accident.
    [Fact]
    public async Task FailedExchange_ListsTheWriteTools()
    {
        using var host = new McpTestHost();
        host.Tokens.Failure = new StorytellerSignInUnavailableException();
        await using var client = await host.ConnectAsync();

        var result = await client.ListToolsAsync(new ListToolsRequestParams());

        WriteTools.ShouldBeSubsetOf(result.Tools.Select(tool => tool.Name));
        result.CacheScope.ShouldBe(CacheScope.Private);
    }

    [Fact]
    public async Task SlowExchange_ListsTheWriteTools_AfterTheWait()
    {
        using var host = new McpTestHost(SecretToolsSetting());
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly);
        host.Tokens.Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var names = (await ListToolsAsync(host)).Select(tool => tool.Name).ToList();

        WriteTools.ShouldBeSubsetOf(names);
        names.ShouldNotContain(name => SecretNames.Contains(name));
        host.Logs.Entries.ShouldContain(entry => entry.Contains($"tools/list: the Storyteller token for the person took longer than {ToolListFilter.TokenWait.TotalSeconds} seconds", StringComparison.Ordinal));
        host.Tokens.Pending.TrySetResult();
    }

    [Theory]
    [InlineData("create_machine")]
    [InlineData("reset_machine")]
    [InlineData("set_key_value")]
    [InlineData("delete_configuration")]
    public async Task WriteTool_UnderReadOnly_IsRefusedWithoutReachingTheApi(string tool)
    {
        using var host = new McpTestHost();
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly);
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync(tool, new { organization = "house", project = "main", key = "sbt.northwind", id = "m-1", value = "v", scopes = new[] { "ConfigurationRead" } });

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(ApiErrors.ActorReadOnlyMessage);
        result.StructuredContent!.Value.GetProperty("ErrorCode").GetString().ShouldBe(ApiErrors.ActorReadOnlyCode);
        host.Api.Requests.ShouldBeEmpty();
        host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool {tool}: refused, the API lets the person's client app only read (ReadOnly).", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadTool_UnderReadOnly_ReachesTheApi()
    {
        using var host = new McpTestHost();
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly, StorytellerToken.NoSecretReveal);
        host.Api.Respond = _ => RecordingApiHandler.Json("""{"Key":"db.host","Value":"db.example","IsSecret":false,"Version":1}""");
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("get_key_value", new { organization = "house", project = "main", key = "db.host" });

        result.IsError.ShouldNotBe(true, result.Text());
        host.Api.Requests.ShouldHaveSingleItem();
    }

    // The secret reads follow NoSecretReveal, not ReadOnly.
    [Fact]
    public async Task SecretRead_UnderReadOnlyAlone_ReachesTheApi()
    {
        using var host = new McpTestHost(SecretToolsSetting());
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly);
        host.Api.Respond = _ => RecordingApiHandler.Json("""{"Key":"db.password","Value":"s3cret","IsSecret":true,"Version":2}""");
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("reveal_key_value", new { organization = "house", project = "main", key = "db.password" });

        result.IsError.ShouldNotBe(true, result.Text());
        host.Api.Requests.ShouldHaveSingleItem();
    }

    // A read-only tool runs, here up to its own input check: the filter does not refuse it.
    [Fact]
    public async Task SuggestConfigurationPlacement_UnderReadOnly_IsNotRefused()
    {
        using var host = new McpTestHost();
        host.Tokens.Restrictions = Restrictions(StorytellerToken.ReadOnly);
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("suggest_configuration_placement", new { organization = "house", project = "main", systems = Array.Empty<object>() });

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("'systems' needs 1 to");
        result.Text().ShouldNotContain(ApiErrors.ActorReadOnlyMessage);
    }

    [Fact]
    public async Task APlain401_ForgetsTheCachedToken()
    {
        using var host = new McpTestHost();
        host.Api.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var inbound = TestTokens.Create("ada");
        await using var client = await host.ConnectAsync(inbound);

        var result = await client.CallAsync("list_machines", new { organization = "house", project = "main" });

        result.Text().ShouldBe($"Storyteller API answered 401: {ApiErrors.UnauthorizedMessage}");
        host.Tokens.Forgotten.ShouldBe([(inbound, StubTokenProvider.DownstreamFor("ada"))]);
    }

    // A step-up cannot be lifted by a new exchange, which copies the same sign-in time; each exchange counts against the
    // API's replay limit.
    [Fact]
    public async Task AStepUp401_KeepsTheCachedToken()
    {
        using var host = new McpTestHost();
        host.Api.Respond = _ =>
        {
            var response = RecordingApiHandler.Json("""{"Message":"A recent sign-in is needed.","ErrorCode":"StepUpRequired","Details":{"MaxAge":600}}""", HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer error=\"insufficient_user_authentication\", max_age=\"600\"");
            return response;
        };
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("set_key_value", new { organization = "house", project = "main", key = "db.password", value = "s3cret", protection = "Standard" });

        result.StructuredContent!.Value.GetProperty("ErrorCode").GetString().ShouldBe(ApiErrors.StepUpRequiredCode);
        host.Tokens.Forgotten.ShouldBeEmpty();
    }

    [Fact]
    public async Task A403_KeepsTheCachedToken()
    {
        using var host = new McpTestHost();
        host.Api.Respond = _ => RecordingApiHandler.Json("""{"Message":"No access.","ErrorCode":"AccessDenied"}""", HttpStatusCode.Forbidden);
        await using var client = await host.ConnectAsync();

        await client.CallAsync("list_machines", new { organization = "house", project = "main" });

        host.Tokens.Forgotten.ShouldBeEmpty();
    }

    private static Dictionary<string, string?> SecretToolsSetting()
    {
        return new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" };
    }

    private static HashSet<string> Restrictions(params string[] restrictions)
    {
        return new HashSet<string>(restrictions, StringComparer.Ordinal);
    }

    private static async Task<IList<McpClientTool>> ListToolsAsync(McpTestHost host)
    {
        await using var client = await host.ConnectAsync();
        return await client.ListToolsAsync();
    }
}
