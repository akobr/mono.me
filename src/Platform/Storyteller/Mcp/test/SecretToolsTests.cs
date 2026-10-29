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
        SetupCurrent(version: 3);
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "s3cret", Version = 3 });
        _keyValues.Setup(api => api.GetKeyValueVersionAsync("house", "main", "default", "db.password", 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "old", Version = 1 });

        var current = await Tools().RevealKeyValue("house", "main", "db.password");
        var older = await Tools().RevealKeyValue("house", "main", "db.password", version: 1);

        current.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("s3cret");
        older.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("old");
    }

    [Fact]
    public async Task RevealKeyValue_CurrentVersionByNumber_RevealsThroughThePairWhichTheApiAudits()
    {
        SetupCurrent(version: 3);
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "s3cret", Version = 3 });

        var result = await Tools().RevealKeyValue("house", "main", "db.password", version: 3);

        result.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("s3cret");
        _keyValues.Verify(api => api.GetKeyValueVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevealKeyValue_CurrentVersionOfADeletedPair_RevealsThroughThePairIncludingDeleted()
    {
        SetupCurrent(version: 3, deleted: true);
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "s3cret", Version = 3, IsDeleted = true });

        var result = await Tools().RevealKeyValue("house", "main", "db.password", version: 3);

        result.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("s3cret");
    }

    [Fact]
    public async Task RevealKeyValue_VersionWrittenOverBetweenTheReads_IsTheAskedVersion()
    {
        SetupCurrent(version: 3);
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "newer", Version = 4 });
        _keyValues.Setup(api => api.GetKeyValueVersionAsync("house", "main", "default", "db.password", 3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "asked", Version = 3 });

        var result = await Tools().RevealKeyValue("house", "main", "db.password", version: 3);

        result.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("asked");
    }

    [Fact]
    public async Task RevealKeyValue_VersionOfAKeyWithoutAPair_UsesTheVersionRoute()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", false, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(ToolFixtures.NotFound());
        _keyValues.Setup(api => api.GetKeyValueVersionAsync("house", "main", "default", "db.password", 2, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Value = "kept", Version = 2 });

        var result = await Tools().RevealKeyValue("house", "main", "db.password", version: 2);

        result.StructuredContent!.Value.GetProperty("Value").GetString().ShouldBe("kept");
    }

    [Fact]
    public void RevealKeyValue_DescriptionSaysWhichRevealsAreAudited()
    {
        var description = Description(nameof(SecretTools.RevealKeyValue));

        description.ShouldContain("a reveal of the current value, with or without its version number, is recorded as a Reveal event");
        description.ShouldContain("a reveal by an older version number is not audited by the API");
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
        _configurations.Setup(api => api.GetResolvedConfigurationAsync("house", "main", "default", "sbt.northwind", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = "sbt.northwind", Version = 2, Content = new JObject { ["host"] = "sql.internal" } });

        var result = await Tools().GetResolvedConfiguration("house", "main", "sbt.northwind");

        result.Text().ShouldContain("\"host\":\"sql.internal\"");
    }

    [Theory]
    [InlineData("northwind.invoicing")]
    [InlineData("Exe.Northwind")]
    [InlineData(" ")]
    public async Task GetResolvedConfiguration_NotAnAnnotationKey_IsAnInputErrorWithoutACall(string key)
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().GetResolvedConfiguration("house", "main", key));

        exception.Message.ShouldContain(AnnotationKeys.Format);
        _configurations.VerifyNoOtherCalls();
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
        var description = Description(method);

        attribute.ReadOnly.ShouldBeFalse();
        attribute.Destructive.ShouldBeFalse();
        attribute.Idempotent.ShouldBeTrue();
        attribute.OpenWorld.ShouldBeFalse();
        description.ShouldContain(SecretTools.AskFirst);
        description.ShouldContain("audits secret reads");
    }

    private static string Description(string method)
    {
        return typeof(SecretTools).GetMethod(method)!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;
    }

    // The pair as the reveal of a version reads it first: no value, deleted pairs included.
    private void SetupCurrent(long version, bool deleted = false)
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Version = version, IsDeleted = deleted });
    }

    private SecretTools Tools() => new(_keyValues.Object, _configurations.Object);
}
