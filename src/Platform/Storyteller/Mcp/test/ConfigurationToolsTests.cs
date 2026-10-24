using _42.Platform.Storyteller.Mcp.Tools;

using Moq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Mcp.UnitTests.Support.ToolFixtures;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class ConfigurationToolsTests
{
    private const string Key = "exe.northwind.billing.prod";

    private readonly Mock<Sdk.IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);
    private readonly Mock<Sdk.IAnnotationsApiClient> _annotations = new(MockBehavior.Strict);
    private object? _sentBody;

    public ConfigurationToolsTests()
    {
        _annotations.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, Key, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Execution { AnnotationKey = Key, Name = "prod" });
        _configurations.Setup(api => api.SetConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<bool?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, bool?, object, CancellationToken>((_, _, _, _, _, body, _) => _sentBody = body)
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 8, Content = new JObject() });
        _configurations.Setup(api => api.PatchConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<bool?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, bool?, object, CancellationToken>((_, _, _, _, _, body, _) => _sentBody = body)
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 8, Content = new JObject() });
    }

    private ConfigurationTools Tools => new(_configurations.Object, _annotations.Object);

    [Fact]
    public async Task SetConfiguration_SendsTheDocumentAsJson()
    {
        await Tools.SetConfiguration(Organization, Project, Key, Element("""{"retries":3,"at":"2026-10-24T10:00:00.000Z"}"""), force: true);

        var body = _sentBody.ShouldBeOfType<JObject>();
        body.ToString(Formatting.None).ShouldBe("""{"retries":3,"at":"2026-10-24T10:00:00.000Z"}""");
        _configurations.Verify(api => api.SetConfigurationAsync(Organization, Project, ViewName, Key, true, It.IsAny<object>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Writes_RefuseAMissingAnnotation_NamingSetAnnotation()
    {
        _annotations.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, Key, true, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        var content = Element("""{"a":1}""");

        foreach (var write in new Func<Task>[]
                 {
                     () => Tools.SetConfiguration(Organization, Project, Key, content),
                     () => Tools.ReplaceConfiguration(Organization, Project, Key, content),
                     () => Tools.RevertConfiguration(Organization, Project, Key, 2),
                     () => Tools.PatchConfiguration(Organization, Project, Key, Element("""[{"op":"remove","path":"/a"}]""")),
                 })
        {
            (await Should.ThrowAsync<ToolInputException>(write)).Message.ShouldContain("set_annotation");
        }

        _sentBody.ShouldBeNull();
    }

    [Fact]
    public async Task Writes_RefuseADeletedAnnotation_NamingRestore()
    {
        _annotations.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, Key, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Execution { AnnotationKey = Key, Name = "prod", Deletion = Deletion("sbt.northwind") });

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools.SetConfiguration(Organization, Project, Key, Element("""{"a":1}""")));

        exception.Message.ShouldContain("restore_annotations");
        exception.Message.ShouldContain("with sbt.northwind");
    }

    [Fact]
    public async Task SetConfiguration_ContentThatIsNoObject_IsRefused()
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetConfiguration(Organization, Project, Key, Element("[1]"))))
            .Message.ShouldContain("'content' must be a JSON object");
    }

    [Fact]
    public async Task ReplaceConfiguration_PatchesTheStoredDocument_WithTests()
    {
        Own(7, """{"retries":3,"owner":"team-a","same":1}""");

        var result = await Tools.ReplaceConfiguration(Organization, Project, Key, Element("""{"retries":5,"same":1,"new":true}"""));

        _sentBody.ShouldBeOfType<JArray>().ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/owner","value":"team-a"},{"op":"remove","path":"/owner"},"""
            + """{"op":"test","path":"/retries","value":3},{"op":"replace","path":"/retries","value":5},"""
            + """{"op":"add","path":"/new","value":true}]""");
        result.Structured().Value<bool>("Changed").ShouldBeTrue();
        _configurations.Verify(api => api.PatchConfigurationAsync(Organization, Project, ViewName, Key, null, It.IsAny<object>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ReplaceConfiguration_NothingStored_WritesTheDocument()
    {
        _configurations.Setup(api => api.GetConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 0, Content = JObject.Parse("""{"inherited":1}""") });

        await Tools.ReplaceConfiguration(Organization, Project, Key, Element("""{"retries":5}"""));

        _sentBody.ShouldBeOfType<JObject>().ToString(Formatting.None).ShouldBe("""{"retries":5}""");
        _configurations.Verify(api => api.PatchConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<bool?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceConfiguration_EqualDocument_WritesNothing()
    {
        Own(7, """{"retries":3}""");

        var result = (await Tools.ReplaceConfiguration(Organization, Project, Key, Element("""{"retries":3}"""))).Structured();

        result.Value<bool>("Changed").ShouldBeFalse();
        result.Value<long>("Version").ShouldBe(7);
        _sentBody.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"$remove":["$.a"]}""")]
    [InlineData("""{"a":1,"$patch":[]}""")]
    public async Task ReplaceConfiguration_Directives_AreRefused(string content)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.ReplaceConfiguration(Organization, Project, Key, Element(content))))
            .Message.ShouldContain("set_configuration");
    }

    [Fact]
    public async Task RevertConfiguration_ReplacesTheStoredDocumentWithTheVersion()
    {
        Own(7, """{"retries":5}""");
        _configurations.Setup(api => api.GetConfigurationVersionAsync(Organization, Project, ViewName, Key, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 3, Content = JObject.Parse("""{"retries":2,"old":true}""") });

        await Tools.RevertConfiguration(Organization, Project, Key, 3);

        _sentBody.ShouldBeOfType<JArray>().ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/retries","value":5},{"op":"replace","path":"/retries","value":2},{"op":"add","path":"/old","value":true}]""");
    }

    [Fact]
    public async Task PatchConfiguration_SendsTheOperations()
    {
        Own(7, """{"a":1}""");

        await Tools.PatchConfiguration(Organization, Project, Key, Element("""[{"op":"test","path":"/a","value":1},{"op":"remove","path":"/a"}]"""));

        _sentBody.ShouldBeOfType<JArray>().Count.ShouldBe(2);
    }

    [Fact]
    public async Task PatchConfiguration_NothingStored_IsRefused_NamingTheFirstWrites()
    {
        // an effective read caches an item with Version 0 and the inherited values; the API would patch it as stored
        _configurations.Setup(api => api.GetConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 0, Content = JObject.Parse("""{"inherited":1}""") });

        var exception = await Should.ThrowAsync<ToolInputException>(
            () => Tools.PatchConfiguration(Organization, Project, Key, Element("""[{"op":"add","path":"/x","value":1}]""")));

        exception.Message.ShouldContain("no stored configuration");
        exception.Message.ShouldContain("set_configuration");
        exception.Message.ShouldContain("replace_configuration");
        _sentBody.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"op":"remove","path":"/a"}""", "must be a JSON array")]
    [InlineData("""[]""", "no operations")]
    [InlineData("""[{"op":"delete","path":"/a"}]""", "not an RFC 6902 operation")]
    [InlineData("""[{"op":"remove"}]""", "not an RFC 6902 operation")]
    [InlineData("""[{"op":"remove","path":"/a~1b"}]""", "literal characters")]
    [InlineData("""[{"op":"move","from":"/m~0n","path":"/x"}]""", "literal characters")]
    public async Task PatchConfiguration_InvalidOperations_AreRefused(string operations, string message)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.PatchConfiguration(Organization, Project, Key, Element(operations))))
            .Message.ShouldContain(message);
    }

    [Fact]
    public async Task GetConfiguration_Own_ReadsTheStoredVersion()
    {
        Own(7, """{"retries":3}""");

        var result = (await Tools.GetConfiguration(Organization, Project, Key, form: "own")).Structured();

        result.Value<long>("Version").ShouldBe(7);
        result.Value<bool>("IsStored").ShouldBeTrue();
        result["Content"]!.ToString(Formatting.None).ShouldBe("""{"retries":3}""");
    }

    [Fact]
    public async Task GetConfiguration_OwnWithNothingStored_SaysSo()
    {
        _configurations.Setup(api => api.GetConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = 0, Content = JObject.Parse("""{"inherited":1}""") });

        var result = (await Tools.GetConfiguration(Organization, Project, Key, form: "own")).Structured();

        result.Value<bool>("IsStored").ShouldBeFalse();
        result["Content"].ShouldBeNull();
    }

    [Theory]
    [InlineData("effective", 3)]
    [InlineData("version", null)]
    [InlineData("calculated", null)]
    public async Task GetConfiguration_FormAndVersionMismatch_IsRefused(string form, int? version)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.GetConfiguration(Organization, Project, Key, form: form, version: version));
    }

    [Fact]
    public async Task DiffConfiguration_PicksTheDiffOfItsArguments()
    {
        _configurations.Setup(api => api.GetConfigurationVersionDiffCustomAsync(Organization, Project, ViewName, Key, 5, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.DiffResult());
        _configurations.Setup(api => api.GetConfigurationViewDiffAsync(Organization, Project, ViewName, Key, "rollout", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.DiffResult());

        await Tools.DiffConfiguration(Organization, Project, Key, version: 5, versionFrom: 2);
        await Tools.DiffConfiguration(Organization, Project, Key, viewTo: "rollout");
        await Should.ThrowAsync<ToolInputException>(() => Tools.DiffConfiguration(Organization, Project, Key, version: 5, viewTo: "rollout"));
        await Should.ThrowAsync<ToolInputException>(() => Tools.DiffConfiguration(Organization, Project, Key));
    }

    private void Own(long version, string json)
    {
        _configurations.Setup(api => api.GetConfigurationAsync(Organization, Project, ViewName, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = version, Content = JObject.Parse("""{"inherited":true}""") });
        _configurations.Setup(api => api.GetConfigurationVersionAsync(Organization, Project, ViewName, Key, (int)version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Key, Version = version, Content = JObject.Parse(json) });
    }
}
