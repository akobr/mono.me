using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using ModelContextProtocol.Protocol;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The tools that return secrets reach tools/list only when the deployment enables them and the API lets the MCP
// server's actor read secrets. Under NoSecretReveal, or when the exchange fails, they are hidden, and a call to one is
// refused before it reaches the API.
public class SecretToolsRegistrationTests
{
    private const string Marker = "LIST-MARKER-8c2f";

    private static readonly string[] SecretNames = ["reveal_key_value", "get_resolved_configuration", "check_configuration_bindings"];

    private static readonly string[] AlwaysListed = ["list_machines", "create_machine", "reset_machine", "list_key_values", "get_key_value", "diff_key_value"];

    public static TheoryData<string, Exception, string> ExchangeFailures() => new()
    {
        { "no account", new StorytellerSignInRefusedException(StorytellerSignInRefusedException.NoAccountMessage), "invalid_grant" },
        { "actor refused", new StorytellerActorException(StorytellerActorException.ActorNotAcceptedMessage, "invalid_client"), "invalid_client" },
        { "unavailable", new StorytellerSignInUnavailableException(), StorytellerSignInUnavailableException.ErrorCodeValue },
        { "other refusal", new StorytellerSignInFailedException("invalid_scope"), "invalid_scope" },
        { "connection", new HttpRequestException($"Connection reset ({Marker})."), nameof(HttpRequestException) },
        { "HttpClient timeout", new TaskCanceledException($"Timed out ({Marker}).", new TimeoutException()), nameof(TaskCanceledException) },
        { "unexpected", new InvalidOperationException($"Broken ({Marker})."), nameof(InvalidOperationException) },
    };

    [Fact]
    public void SecretToolNames_AreTheThreeToolsThatReturnSecrets()
    {
        SecretToolNames.All.ShouldBe(SecretNames, ignoreOrder: true);
    }

    [Fact]
    public async Task WithoutTheSetting_SecretToolsAreNotListed_AndNoExchangeRuns()
    {
        using var host = new McpTestHost();

        var names = await ListToolNamesAsync(host);

        names.ShouldNotContain(name => SecretNames.Contains(name));
        AlwaysListed.ShouldBeSubsetOf(names);

        // The list is the same for everyone: no list filter, so no exchange.
        host.Tokens.UserTokens.ShouldBeEmpty();
    }

    // Under 2025-11-25 an unset cache scope stays absent (Public): only the filter makes the list private. (From
    // 2026-07-28 on the SDK sends Private for an unset scope, so that revision cannot tell the two apart.)
    [Theory]
    [InlineData("without the setting", false)]
    [InlineData("with the setting", true)]
    public async Task TheListIsPrivate_OnlyWithTheSecretTools(string because, bool secretTools)
    {
        using var host = new McpTestHost(SecretToolsSetting(secretTools ? "true" : "false"));
        await using var client = await host.ConnectAsync(protocolVersion: "2025-11-25");

        var result = await client.ListToolsAsync(new ListToolsRequestParams());

        result.CacheScope.ShouldBe(secretTools ? CacheScope.Private : null, because);
    }

    [Fact]
    public async Task SettingFalse_SecretToolsAreNotListed()
    {
        using var host = new McpTestHost(SecretToolsSetting("false"));

        var names = await ListToolNamesAsync(host);

        names.ShouldNotContain(name => SecretNames.Contains(name));
    }

    [Fact]
    public async Task SettingFalse_UnderNoSecretReveal_TheOtherToolsStayListed()
    {
        using var host = new McpTestHost(SecretToolsSetting("false"));
        host.Tokens.Restrictions = NoSecretReveal();

        var names = await ListToolNamesAsync(host);

        AlwaysListed.ShouldBeSubsetOf(names);
        names.ShouldNotContain(name => SecretNames.Contains(name));
    }

    [Fact]
    public async Task WithTheSetting_AndNoRestriction_SecretToolsAreListed()
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        var inbound = TestTokens.Create("ada");

        var names = await ListToolNamesAsync(host, inbound);

        SecretNames.ShouldBeSubsetOf(names);
        AlwaysListed.ShouldBeSubsetOf(names);
        host.Tokens.UserTokens.ShouldBe([inbound]);
    }

    [Fact]
    public async Task TheSetting_AddsExactlyTheSecretTools()
    {
        using var plain = new McpTestHost();
        using var withSecrets = new McpTestHost(SecretToolsSetting("true"));

        var plainNames = await ListToolNamesAsync(plain);
        var secretNames = await ListToolNamesAsync(withSecrets);

        secretNames.Except(plainNames).ShouldBe(SecretNames, ignoreOrder: true);
    }

    [Fact]
    public async Task UnderNoSecretReveal_SecretToolsAreHidden()
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        host.Tokens.Restrictions = NoSecretReveal();
        var inbound = TestTokens.Create("ada");

        var names = await ListToolNamesAsync(host, inbound);

        names.ShouldNotContain(name => SecretNames.Contains(name));
        AlwaysListed.ShouldBeSubsetOf(names);
        host.Tokens.UserTokens.ShouldBe([inbound]);
    }

    // Fail closed: a failed exchange hides the secret tools and still answers the list.
    [Theory]
    [MemberData(nameof(ExchangeFailures))]
    public async Task FailedExchange_HidesSecretTools_AndTheListStillAnswers(string because, Exception failure, string reason)
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        host.Tokens.Failure = failure;
        await using var client = await host.ConnectAsync();

        var result = await client.ListToolsAsync(new ListToolsRequestParams());
        var names = result.Tools.Select(tool => tool.Name).ToList();

        names.ShouldNotContain(name => SecretNames.Contains(name), because);
        AlwaysListed.ShouldBeSubsetOf(names, because);
        result.CacheScope.ShouldBe(CacheScope.Private, because);
        host.Logs.Entries.ShouldContain(entry => entry.Contains($"tools/list: no Storyteller token for the person ({reason}); the secret tools are hidden.", StringComparison.Ordinal), because);
        host.Logs.Entries.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal), because);
    }

    // The list differs per person whenever the secret tools are registered, so no shared cache may serve it.
    [Theory]
    [InlineData("no restriction", false)]
    [InlineData("NoSecretReveal", true)]
    public async Task WithTheSetting_TheListIsPrivate(string because, bool restricted)
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));

        if (restricted)
        {
            host.Tokens.Restrictions = NoSecretReveal();
        }

        await using var client = await host.ConnectAsync();

        var result = await client.ListToolsAsync(new ListToolsRequestParams());

        result.CacheScope.ShouldBe(CacheScope.Private, because);
    }

    // A hidden tool can still be called by name: the call filter refuses it after the exchange, before the API.
    [Theory]
    [InlineData("reveal_key_value", "db.password")]
    [InlineData("get_resolved_configuration", "exe.northwind.invoicing.prod")]
    [InlineData("check_configuration_bindings", "exe.northwind.invoicing.prod")]
    public async Task HiddenSecretTool_CalledUnderNoSecretReveal_IsRefusedWithoutReachingTheApi(string tool, string key)
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        host.Tokens.Restrictions = NoSecretReveal();
        var inbound = TestTokens.Create("ada");
        await using var client = await host.ConnectAsync(inbound);

        var result = await client.CallAsync(tool, new { organization = "house", project = "main", key });

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe(ApiErrors.ActorRestrictedMessage);
        result.StructuredContent!.Value.GetProperty("ErrorCode").GetString().ShouldBe(ApiErrors.ActorRestrictedCode);
        host.Api.Requests.ShouldBeEmpty();
        host.Tokens.UserTokens.ShouldBe([inbound]);
        host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool {tool}: refused, the API withholds secrets from the MCP server's actor (NoSecretReveal).", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecretTool_WithoutRestriction_ReachesTheApi()
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        host.Api.Respond = _ => RecordingApiHandler.Json("""{"Key":"db.password","Value":"s3cret","IsSecret":true,"Version":2}""");
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("reveal_key_value", new { organization = "house", project = "main", key = "db.password" });

        result.IsError.ShouldNotBe(true, result.Text());
        host.Api.Requests.ShouldHaveSingleItem().Uri.PathAndQuery.ShouldBe("/api/v1/house/main/default/key-values/db.password?reveal=true");
    }

    [Fact]
    public async Task UnderNoSecretReveal_OtherToolsStillReachTheApi()
    {
        using var host = new McpTestHost(SecretToolsSetting("true"));
        host.Tokens.Restrictions = NoSecretReveal();
        host.Api.Respond = _ => RecordingApiHandler.Json("""{"Key":"db.password","IsSecret":true,"Version":2}""");
        await using var client = await host.ConnectAsync();

        var result = await client.CallAsync("get_key_value", new { organization = "house", project = "main", key = "db.password" });

        result.IsError.ShouldNotBe(true, result.Text());
        host.Api.Requests.ShouldHaveSingleItem().Uri.PathAndQuery.ShouldBe("/api/v1/house/main/default/key-values/db.password?reveal=false");
    }

    private static Dictionary<string, string?> SecretToolsSetting(string value)
    {
        return new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = value };
    }

    private static HashSet<string> NoSecretReveal()
    {
        return new HashSet<string>(StringComparer.Ordinal) { StorytellerToken.NoSecretReveal };
    }

    private static async Task<List<string>> ListToolNamesAsync(McpTestHost host, string? token = null)
    {
        await using var client = await host.ConnectAsync(token);
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => tool.Name).ToList();
    }
}
