using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Moq;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class BindingCheckToolsTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "exe.northwind.invoicing.prod";
    private const string Secret = "S3CRET-VALUE-42";

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders = new Dictionary<string, IEnumerable<string>>();

    private readonly Mock<Sdk.IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);

    [Fact]
    public async Task Check_TellsWhichBindingsResolved_NeverTheirValues()
    {
        Effective("""
            {
              "password": "@secret(db.password)",
              "host": "@db.host",
              "plain": "x",
              "list": ["@public(region)"],
              "rule": { "$binding": "jlogic", "$definition": { "if": [{ "var": "tier" }, { "var": "tier" }, "@plan.fallback"] }, "$context": { "tier": "@plan.tier" } }
            }
            """);
        Resolved(new Sdk.Configuration
        {
            Content = JObject.Parse($$"""{"password": "{{Secret}}", "host": "@db.host", "plain": "x", "list": ["{{Secret}}-region"], "rule": 5}"""),
        });

        var result = await new BindingCheckTools(_configurations.Object).CheckConfigurationBindings(Org, Project, Key, View);

        result.Text().ShouldNotContain(Secret);
        result.StructuredContent!.Value.GetRawText().ShouldNotContain(Secret);
        var bindings = Bindings(result);

        // the strings in $context and $definition are replaced together with their object binding
        bindings.Keys.ShouldBe(["/password", "/host", "/list/0", "/rule"]);
        bindings["/password"].ShouldBe(true);
        bindings["/host"].ShouldBe(false);
        bindings["/list/0"].ShouldBe(true);
        bindings["/rule"].ShouldBe(true);
        JObject.Parse(result.StructuredContent.Value.GetRawText()).ContainsKey(SecretsWithheldNotice.ReasonProperty).ShouldBeFalse();
    }

    // 424-264: a stale sign-in leaves the secret bindings as stored; the check says so instead of a bare false.
    [Fact]
    public async Task Check_WithSecretsWithheldForAStaleSignIn_SaysWhy()
    {
        Effective("""{"password": "@secret(db.password)", "host": "@db.host"}""");
        Resolved(new Sdk.Configuration { Content = JObject.Parse("""{"password": "@secret(db.password)", "host": "db.local"}""") }, "step-up-required");

        var result = await new BindingCheckTools(_configurations.Object).CheckConfigurationBindings(Org, Project, Key, View);

        var content = JObject.Parse(result.StructuredContent!.Value.GetRawText());
        content[SecretsWithheldNotice.ReasonProperty]!.Value<string>().ShouldBe("step-up-required");
        content[SecretsWithheldNotice.PropertyName]!.Value<string>().ShouldBe(SecretsWithheldNotice.For("step-up-required"));
        var bindings = Bindings(result);
        bindings["/password"].ShouldBe(false);
        bindings["/host"].ShouldBe(true);
    }

    [Fact]
    public async Task Check_AFailedResolvedRead_IsUnknown_WithTheCodeOnly()
    {
        Effective("""{"password": "@secret(db.password)"}""");
        _configurations
            .Setup(client => client.GetResolvedConfigurationConditionalAsync(Org, Project, View, Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sdk.ApiException<Sdk.ErrorResponse>(
                $"Binding failed near {Secret}",
                422,
                $$"""{"Message":"Failed to evaluate {{Secret}}"}""",
                NoHeaders,
                new Sdk.ErrorResponse { Message = $"Failed to evaluate {Secret}", ErrorCode = "binding.evaluation" },
                null));

        var result = await new BindingCheckTools(_configurations.Object).CheckConfigurationBindings(Org, Project, Key, View);

        result.IsError.ShouldNotBe(true);
        result.Text().ShouldNotContain(Secret);
        var content = JObject.Parse(result.StructuredContent!.Value.GetRawText());
        content["ResolvedRead"]!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"Status":422,"ErrorCode":"binding.evaluation"}""");
        content["Bindings"]![0]!["Resolved"]!.Value<string>().ShouldBe(BindingCheckTools.Unknown);
    }

    [Fact]
    public async Task Check_WithoutBindings_DoesNotResolve()
    {
        Effective("""{"plain": "x"}""");

        var result = await new BindingCheckTools(_configurations.Object).CheckConfigurationBindings(Org, Project, Key, View);

        JObject.Parse(result.StructuredContent!.Value.GetRawText())["Bindings"]!.ShouldBeEmpty();
        _configurations.Verify(client => client.GetResolvedConfigurationConditionalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Dictionary<string, bool> Bindings(ModelContextProtocol.Protocol.CallToolResult result)
    {
        return JObject.Parse(result.StructuredContent!.Value.GetRawText())["Bindings"]!
            .ToDictionary(binding => binding["Pointer"]!.Value<string>()!, binding => binding["Resolved"]!.Value<bool>());
    }

    private void Resolved(Sdk.Configuration configuration, string? withheld = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();

        if (withheld is not null)
        {
            headers["X-2S-Secrets-Withheld"] = [withheld];
        }

        _configurations
            .Setup(client => client.GetResolvedConfigurationConditionalAsync(Org, Project, View, Key, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConditionalResponse<Sdk.Configuration>(200, configuration, headers));
    }

    private void Effective(string json)
    {
        _configurations
            .Setup(client => client.GetConfigurationAsync(Org, Project, View, Key, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Content = JObject.Parse(json) });
    }
}
