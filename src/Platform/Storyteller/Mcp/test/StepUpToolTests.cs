using System.Net;

using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// 424-264 through the host: the step-up 401 of the API (body and RFC 9470 challenge) reaches the model as the fixed text
// of a new sign-in, and a resolved read that the API answers with X-2S-Secrets-Withheld says why the secrets stayed out.
public sealed class StepUpToolTests : IDisposable
{
    private const string StepUpBody = """{"Message":"This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for 10 minutes.","ErrorCode":"StepUpRequired","Details":{"MaxAge":600}}""";

    private readonly McpTestHost _host = new(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" });

    [Theory]
    [InlineData("create_machine")]
    [InlineData("reset_machine")]
    [InlineData("set_key_value")]
    [InlineData("reveal_key_value")]
    public async Task AStepUpOfTheApi_IsTheTextOfANewSignIn(string tool)
    {
        _host.Api.Respond = request => request.Method == HttpMethod.Get && tool == "reset_machine"
            ? RecordingApiHandler.Json("""{"Id":"m-1","CredentialKind":"ApiKey"}""")
            : StepUp();
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync(tool, Arguments(tool));

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe($"Storyteller API answered 401 StepUpRequired: {ApiErrors.StepUpRequiredMessage(TimeSpan.FromMinutes(10))}");
        result.Text().ShouldContain("within the last 10 minutes");
        result.Text().ShouldContain("The assistant cannot do this itself.");
        result.StructuredContent!.Value.GetProperty("ErrorCode").GetString().ShouldBe(ApiErrors.StepUpRequiredCode);
        result.StructuredContent.Value.GetProperty("MaxAge").GetInt64().ShouldBe(600);
        _host.Logs.Entries.ShouldContain(entry => entry.Contains($"Tool {tool}: the Storyteller API answered 401 StepUpRequired.", StringComparison.Ordinal));
    }

    // A bare 401 keeps the text of the token exchange.
    [Fact]
    public async Task AnyOther401_KeepsTheTextOfTheTokenExchange()
    {
        _host.Api.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("set_key_value", Arguments("set_key_value"));

        result.Text().ShouldBe($"Storyteller API answered 401: {ApiErrors.UnauthorizedMessage}");
    }

    [Fact]
    public async Task AResolvedReadWithSecretsWithheld_SaysWhy()
    {
        _host.Api.Respond = _ =>
        {
            var response = RecordingApiHandler.Json("""{"AnnotationKey":"sbt.northwind","Version":2,"Content":{"password":"@secret(db.password)"}}""");
            response.Headers.Add("X-2S-Secrets-Withheld", "step-up-required");
            return response;
        };
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("get_resolved_configuration", new { organization = "house", project = "main", key = "sbt.northwind" });

        result.IsError.ShouldNotBe(true);
        result.Text().ShouldStartWith(SecretsWithheldNotice.For("step-up-required"));
        result.StructuredContent!.Value.GetProperty(SecretsWithheldNotice.ReasonProperty).GetString().ShouldBe("step-up-required");
        _host.Api.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/api/v1/house/main/default/configuration/sbt.northwind/resolved");
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static HttpResponseMessage StepUp()
    {
        var response = RecordingApiHandler.Json(StepUpBody, HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer error=\"insufficient_user_authentication\", error_description=\"A recent sign-in is required\", max_age=\"600\"");
        return response;
    }

    private static object Arguments(string tool)
    {
        return tool switch
        {
            "create_machine" => new { organization = "house", project = "main", scopes = new[] { "ConfigurationRead" } },
            "reset_machine" => new { organization = "house", project = "main", id = "m-1" },
            "set_key_value" => new { organization = "house", project = "main", key = "db.password", value = "s3cret", protection = "Standard" },
            _ => new { organization = "house", project = "main", key = "db.password" },
        };
    }
}
