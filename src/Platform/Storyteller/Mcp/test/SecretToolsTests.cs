using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Moq;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class SecretToolsTests
{
    private readonly Mock<Sdk.IKeyValuesApiClient> _keyValues = new(MockBehavior.Strict);
    private readonly Mock<Sdk.IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);

    [Fact]
    public async Task RevealKeyValue_AsksTheApiToReveal()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "s3cret" });
        _keyValues.Setup(api => api.GetKeyValueVersionAsync("house", "main", "default", "db.password", 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "old" });

        var current = await Tools().RevealKeyValue("house", "main", "db.password");
        var older = await Tools().RevealKeyValue("house", "main", "db.password", version: 1);

        current.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("s3cret");
        older.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("old");
    }

    [Fact]
    public async Task RevealKeyValue_Hyok_IsTheStoredDocumentMarkedClientEncrypted()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "api.token", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "api.token", Protection = Sdk.KeyValueProtection.Hyok, IsSecret = true, Value = "{\"protected\":\"eyJ\"}" });

        var result = await Tools().RevealKeyValue("house", "main", "api.token");

        result.StructuredContent!.Value.GetProperty(KeyValueProtections.ClientEncryptedProperty).GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetResolvedConfiguration_ReadsTheResolvedDocument()
    {
        _configurations.Setup(api => api.GetResolvedConfigurationAsync("house", "main", "default", "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = "sbt.northwind", Version = 2, Content = new JObject { ["host"] = "sql.internal" } });

        var result = await Tools().GetResolvedConfiguration("house", "main", "sbt.northwind");

        result.Text().ShouldContain("\"host\":\"sql.internal\"");
    }

    [Theory]
    [InlineData(nameof(SecretTools.RevealKeyValue))]
    [InlineData(nameof(SecretTools.GetResolvedConfiguration))]
    public void SecretTools_AreNotReadOnlyAndSayToAskFirst(string method)
    {
        var attribute = typeof(SecretTools).GetMethod(method)!
            .GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
            .Cast<ModelContextProtocol.Server.McpServerToolAttribute>()
            .Single();
        var description = typeof(SecretTools).GetMethod(method)!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;

        attribute.ReadOnly.ShouldBeFalse();
        attribute.Destructive.ShouldBeFalse();
        attribute.Idempotent.ShouldBeTrue();
        attribute.OpenWorld.ShouldBeFalse();
        description.ShouldContain(SecretTools.AskFirst);
        description.ShouldContain("audits secret reads");
    }

    private SecretTools Tools() => new(_keyValues.Object, _configurations.Object);
}
