using _42.Platform.Storyteller.Mcp.Tools;

using Moq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Mcp.UnitTests.Support.ToolFixtures;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class SchemaToolsTests
{
    private readonly Mock<Sdk.ISchemasApiClient> _api = new(MockBehavior.Strict);

    private SchemaTools Tools => new(_api.Object);

    [Theory]
    [InlineData("type", null, "exe.a.b.c")]
    [InlineData("type", null, null)]
    [InlineData("annotation", "exe", "sbt.a")]
    [InlineData("annotation", null, null)]
    [InlineData("descendantType", "exe", null)]
    [InlineData("descendantType", null, "sbt.a")]
    [InlineData("layer", "exe", null)]
    [InlineData("type", "xyz", null)]
    [InlineData("annotation", null, "not-a-key")]
    public void Target_ChecksWhatTheLevelNeeds(string level, string? annotationType, string? key)
    {
        Should.Throw<ToolInputException>(() => SchemaTarget.Parse(level, annotationType, key));
    }

    [Fact]
    public void Target_NormalizesItsParts()
    {
        SchemaTarget.Parse("DescendantType", "EXE", "sbt.northwind").ShouldBe(new SchemaTarget(SchemaTarget.Level.DescendantType, "exe", "sbt.northwind"));
    }

    [Fact]
    public async Task SetSchema_SendsTheSchemaToItsLevel()
    {
        object? sent = null;
        _api.Setup(api => api.SetDescendantTypeSchemaAsync(Organization, Project, ViewName, "rst.billing", "exe", true, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, bool?, object, CancellationToken>((_, _, _, _, _, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.ConfigurationSchema { AnnotationKey = "rst.billing", AnnotationType = "exe", Version = 2 });

        var result = (await Tools.SetSchema(Organization, Project, "descendantType", Element("""{"type":"object","required":["currency"]}"""), annotationType: "exe", key: "rst.billing", force: true)).Structured();

        sent.ShouldBeOfType<JObject>().ToString(Formatting.None).ShouldBe("""{"type":"object","required":["currency"]}""");
        result.Value<long>("Version").ShouldBe(2);
    }

    [Fact]
    public async Task GetSchema_WithVersion_ReadsTheVersion()
    {
        _api.Setup(api => api.GetSchemaVersionAsync(Organization, Project, ViewName, "exe", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConfigurationSchema { AnnotationType = "exe", Version = 3 });
        _api.Setup(api => api.GetAnnotationSchemaAsync(Organization, Project, ViewName, "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConfigurationSchema { AnnotationKey = "sbt.northwind", Version = 1 });

        (await Tools.GetSchema(Organization, Project, "type", annotationType: "exe", version: 3)).Structured().Value<long>("Version").ShouldBe(3);
        (await Tools.GetSchema(Organization, Project, "annotation", key: "sbt.northwind")).Structured().Value<long>("Version").ShouldBe(1);
    }

    [Fact]
    public async Task DeleteAndDiff_UseTheMethodsOfTheLevel()
    {
        _api.Setup(api => api.DeleteAnnotationSchemaAsync(Organization, Project, ViewName, "sbt.northwind", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _api.Setup(api => api.GetDescendantTypeSchemaVersionDiffCustomAsync(Organization, Project, ViewName, "sbt.northwind", "cnt", 4, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.DiffResult());

        await Tools.DeleteSchema(Organization, Project, "annotation", key: "sbt.northwind");
        await Tools.DiffSchema(Organization, Project, "descendantType", 4, annotationType: "cnt", key: "sbt.northwind", versionFrom: 1);

        _api.VerifyAll();
    }
}
