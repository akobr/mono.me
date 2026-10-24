using _42.Platform.Storyteller.Mcp.Tools;

using Moq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Mcp.UnitTests.Support.ToolFixtures;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class TemplateToolsTests
{
    private readonly Mock<Sdk.ITemplatesApiClient> _api = new(MockBehavior.Strict);
    private object? _sentBody;

    public TemplateToolsTests()
    {
        _api.Setup(api => api.SetTemplateAsync(Organization, Project, ViewName, "exe", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, object, CancellationToken>((_, _, _, _, body, _) => _sentBody = body)
            .ReturnsAsync(new Sdk.ConfigurationTemplate { AnnotationType = "exe", Version = 1 });
        _api.Setup(api => api.PatchTemplateAsync(Organization, Project, ViewName, "exe", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, object, CancellationToken>((_, _, _, _, body, _) => _sentBody = body)
            .ReturnsAsync(new Sdk.ConfigurationTemplate { AnnotationType = "exe", Version = 3 });
    }

    private TemplateTools Tools => new(_api.Object);

    [Fact]
    public async Task ReplaceTemplate_PatchesTheTemplate_WithTests()
    {
        Current("""{"retries":3,"features":["audit"]}""");

        var result = (await Tools.ReplaceTemplate(Organization, Project, "EXE", Element("""{"retries":3,"features":["audit","trace"]}"""))).Structured();

        _sentBody.ShouldBeOfType<JArray>().ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/features","value":["audit"]},{"op":"replace","path":"/features","value":["audit","trace"]}]""");
        result.Value<bool>("Changed").ShouldBeTrue();
        result.Value<long>("Version").ShouldBe(3);
    }

    [Fact]
    public async Task ReplaceTemplate_DateLikeStrings_AreTestedAsStored()
    {
        // the SDK's template client keeps date-like strings as text, so a changed date is guarded like any value
        using var reader = new JsonTextReader(new StringReader("""{"since":"2026-10-24T10:00:00.000Z","n":1}""")) { DateParseHandling = DateParseHandling.None };
        _api.Setup(api => api.GetTemplateAsync(Organization, Project, ViewName, "exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConfigurationTemplate { AnnotationType = "exe", Version = 2, Content = JObject.Load(reader) });

        await Tools.ReplaceTemplate(Organization, Project, "exe", Element("""{"since":"2026-10-25T10:00:00.000Z","n":1}"""));

        _sentBody.ShouldBeOfType<JArray>().ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/since","value":"2026-10-24T10:00:00.000Z"},{"op":"replace","path":"/since","value":"2026-10-25T10:00:00.000Z"}]""");
    }

    [Fact]
    public async Task ReplaceTemplate_NoTemplateYet_SetsIt()
    {
        _api.Setup(api => api.GetTemplateAsync(Organization, Project, ViewName, "exe", It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        await Tools.ReplaceTemplate(Organization, Project, "exe", Element("""{"retries":3}"""));

        _sentBody.ShouldBeOfType<JObject>().ToString(Formatting.None).ShouldBe("""{"retries":3}""");
    }

    [Fact]
    public async Task ReplaceTemplate_EqualContent_WritesNothing()
    {
        Current("""{"retries":3}""");

        var result = (await Tools.ReplaceTemplate(Organization, Project, "exe", Element("""{"retries":3}"""))).Structured();

        result.Value<bool>("Changed").ShouldBeFalse();
        _sentBody.ShouldBeNull();
    }

    [Fact]
    public async Task UnknownTypeCode_IsRefused()
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.GetTemplate(Organization, Project, "execution")))
            .Message.ShouldContain("rst, unt, sbt, usg, cnt, exe or uxe");
    }

    [Fact]
    public async Task SetTemplate_SendsTheDocument()
    {
        await Tools.SetTemplate(Organization, Project, "exe", Element("""{"$remove":["$.old"],"retries":3}"""));

        _sentBody.ShouldBeOfType<JObject>().ToString(Formatting.None).ShouldBe("""{"$remove":["$.old"],"retries":3}""");
    }

    private void Current(string json)
    {
        _api.Setup(api => api.GetTemplateAsync(Organization, Project, ViewName, "exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConfigurationTemplate { AnnotationType = "exe", Version = 2, Content = JObject.Parse(json) });
    }
}
