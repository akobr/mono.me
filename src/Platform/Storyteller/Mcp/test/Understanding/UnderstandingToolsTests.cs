using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.Understanding;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using ModelContextProtocol.Protocol;

using Moq;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class UnderstandingToolsTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Marker = "VALUE-MARKER-42";

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders = new Dictionary<string, IEnumerable<string>>();

    private readonly Mock<Sdk.IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);
    private readonly Mock<Sdk.IAnnotationsApiClient> _annotations = new(MockBehavior.Strict);
    private readonly Mock<Sdk.IKeyValuesApiClient> _keyValues = new(MockBehavior.Strict);
    private readonly Mock<Sdk.IAccessApiClient> _access = new(MockBehavior.Strict);
    private readonly Mock<Sdk.ISchemasApiClient> _schemas = new(MockBehavior.Strict);
    private readonly Mock<Sdk.ITemplatesApiClient> _templates = new(MockBehavior.Strict);

    private UnderstandingTools Tools => new(_configurations.Object, _annotations.Object, _keyValues.Object, _access.Object, _schemas.Object, _templates.Object);

    [Fact]
    public async Task Search_ReadsTheStoredVersionOfEachSummary()
    {
        _configurations
            .Setup(client => client.GetConfigurationsAsync(Org, Project, View, "exe", "exe.northwind", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.ConfigurationsResponse
            {
                Configurations =
                [
                    new() { AnnotationKey = "exe.northwind.invoicing.prod", Version = 7, HasContent = true },
                    new() { AnnotationKey = "exe.northwind.billing.prod", Version = 2, HasContent = true },
                ],
            });
        _configurations
            .Setup(client => client.GetConfigurationVersionAsync(Org, Project, View, "exe.northwind.invoicing.prod", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { Content = JObject.Parse("""{"database": {"host": "db.prod"}}""") });

        var result = await Tools.SearchConfigurations(Org, Project, View, annotationType: "EXE", keyPrefix: "exe.northwind", responsibility: "invoic", property: "/database/host");

        var content = Structured(result);
        content["Count"]!.Value<int>().ShouldBe(1);
        content["Items"]![0]!["Pointer"]!.Value<string>().ShouldBe("/database/host");
        content["Items"]![0]!["Value"]!.Value<string>().ShouldBe("db.prod");
        content["Items"]![0]!["Version"]!.Value<long>().ShouldBe(7);
        content["IsTruncated"]!.Value<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Search_AnUnknownType_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SearchConfigurations(Org, Project, View, annotationType: "exec"));
    }

    [Fact]
    public async Task Explain_ReadsPairsWithoutValues_AndCopiesNoValue()
    {
        SetupExplanation(Bindings(("password", "@secret(db.password)"), ("host", "@db.host")), effectiveHash: "c0ffee12");
        _keyValues
            .Setup(client => client.GetKeyValueAsync(Org, Project, View, "db.password", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Version = 4, Value = Marker });
        _keyValues
            .Setup(client => client.GetKeyValueAsync(Org, Project, View, "db.host", false, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sdk.ApiException("Not found", 404, string.Empty, NoHeaders, null));
        _access
            .Setup(client => client.GetMachineAccessesAsync(Org, Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Sdk.MachineAccess { Id = "m1", Scopes = [Sdk.MachineScope.ConfigurationRead], AccessKey = Marker },
                new Sdk.MachineAccess { Id = "export", AnnotationKey = "unt.invoicing.export", Scopes = [Sdk.MachineScope.ConfigurationRead], AccessKey = Marker },
            ]);

        var result = await Tools.ExplainConfiguration(Org, Project, Compositions.ExecutionKey);

        result.Text().ShouldNotContain(Marker);
        var content = Structured(result);
        content["IsStale"]!.Value<bool>().ShouldBeFalse();
        var bindings = (JArray)content["Bindings"]!;
        bindings[0]!["References"]![0]!["KeyValue"]!["Version"]!.Value<long>().ShouldBe(4);
        bindings[1]!["References"]![0]!["KeyValue"]!["Exists"]!.Value<bool>().ShouldBeFalse();
        content["Consumers"]!["Machines"]!["ReadsThisDocument"]![0]!["Id"]!.Value<string>().ShouldBe("m1");
        content["Consumers"]!["Machines"]!["ReadsThroughSharedDescendants"]![0]!["Id"]!.Value<string>().ShouldBe("export");
        _keyValues.Verify(client => client.GetKeyValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.Is<bool?>(reveal => reveal != false), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Explain_LooksUpAtMost50Pairs()
    {
        SetupExplanation(Bindings(Enumerable.Range(0, 60).Select(index => ($"p{index}", $"@key{index}")).ToArray()), effectiveHash: "other");
        _keyValues
            .Setup(client => client.GetKeyValueAsync(Org, Project, View, It.IsAny<string>(), false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string key, bool? _, bool? _, CancellationToken _) => new Sdk.KeyValue { Key = key, Protection = Sdk.KeyValueProtection.Public });

        var result = await Tools.ExplainConfiguration(Org, Project, Compositions.ExecutionKey, includeConsumers: false);

        _keyValues.Verify(client => client.GetKeyValueAsync(Org, Project, View, It.IsAny<string>(), false, true, It.IsAny<CancellationToken>()), Times.Exactly(50));
        var content = Structured(result);
        content["KeyValueLookupsTruncated"]!.Value<bool>().ShouldBeTrue();
        content["IsStale"]!.Value<bool>().ShouldBeTrue();
        content["Consumers"]!["Machines"].ShouldBeNull();
        content["Consumers"]!["Reason"]!.Value<string>().ShouldNotBeNull().ShouldContain("includeConsumers");
    }

    [Theory]
    [InlineData(403)]
    [InlineData(401)]
    public async Task Explain_MachinesThatCannotBeListed_AreNullWithTheReason(int status)
    {
        SetupExplanation(Bindings(), effectiveHash: "c0ffee12");
        _access
            .Setup(client => client.GetMachineAccessesAsync(Org, Project, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sdk.ApiException<Sdk.ErrorResponse>("Forbidden", status, string.Empty, NoHeaders, new Sdk.ErrorResponse { Message = "No access.", ErrorCode = "AccessDenied" }, null));

        var result = await Tools.ExplainConfiguration(Org, Project, Compositions.ExecutionKey, pointer: "/currency");

        result.IsError.ShouldNotBe(true);
        var content = Structured(result);
        content["Consumers"]!["Machines"].ShouldBeNull();
        content["Consumers"]!["Reason"]!.Value<string>().ShouldNotBeNull().ShouldContain("Contributor");
        ((JArray)content["Values"]!).ShouldHaveSingleItem()["Pointer"]!.Value<string>().ShouldBe("/currency");
    }

    [Fact]
    public async Task Explain_ABadPointer_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.ExplainConfiguration(Org, Project, Compositions.ExecutionKey, pointer: "currency"));
    }

    [Fact]
    public async Task Suggest_ReportsEvidence_ForTheFirstThreeCandidates()
    {
        SetupComposition(Compositions.ExecutionKey, Compositions.Execution());
        _annotations
            .Setup(client => client.GetAnnotationAsync(Org, Project, View, Compositions.ExecutionKey, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Annotation { AnnotationKey = Compositions.ExecutionKey });
        _annotations
            .Setup(client => client.GetAnnotationAsync(Org, Project, View, It.IsIn("cnt.northwind.prod", "sbt.northwind"), true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sdk.ApiException("Not found", 404, string.Empty, NoHeaders, null));
        _annotations
            .Setup(client => client.FindDeletedAnnotationsAsync(Org, Project, View, It.Is<Sdk.AnnotationKeysRequest>(request => request.Keys.Single() == "cnt.northwind.prod"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Sdk.Annotation { AnnotationKey = "cnt.northwind.prod" }]);
        _annotations
            .Setup(client => client.FindDeletedAnnotationsAsync(Org, Project, View, It.Is<Sdk.AnnotationKeysRequest>(request => request.Keys.Single() == "sbt.northwind"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _annotations
            .Setup(client => client.GetDescendantsAsync(Org, Project, View, Compositions.ExecutionKey, "all", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.AnnotationsResponse { Annotations = [new Sdk.Annotation(), new Sdk.Annotation()], ContinuationToken = "more" });

        var result = await Tools.SuggestConfigurationPlacement(Org, Project, [Invoicing()], pointers: ["/currency"]);

        var suggestion = Structured(result);
        var candidates = (JArray)suggestion["Candidates"]!;
        candidates.Select(candidate => candidate["AnnotationKey"]!.Value<string>()).ShouldBe(
            ["exe.northwind.invoicing.prod", "cnt.northwind.prod", "sbt.northwind", "usg.northwind.invoicing", "rst.invoicing"]);
        candidates[0]!["Evidence"]!["State"]!.Value<string>().ShouldBe(PlacementEvidence.Exists);
        candidates[0]!["Evidence"]!["Descendants"]!.Value<int>().ShouldBe(2);
        candidates[0]!["Evidence"]!["Reach"]!.Value<string>().ShouldNotBeNull().ShouldContain("at least 2");
        candidates[1]!["Evidence"]!["State"]!.Value<string>().ShouldBe(PlacementEvidence.Deleted);
        candidates[1]!["Evidence"]!["Next"]!.Value<string>().ShouldNotBeNull().ShouldContain("restore_annotations");
        candidates[2]!["Evidence"]!["State"]!.Value<string>().ShouldBe(PlacementEvidence.Missing);
        candidates[3]!["Evidence"].ShouldBeNull();
        candidates[3]!["Effects"]![0]!["Effective"]!.Value<bool>().ShouldBeFalse();
        suggestion["Today"]![0]!["SourceId"]!.Value<string>().ShouldBe(Compositions.Subject);
        suggestion["SchemaPreview"].ShouldBeNull();
    }

    [Fact]
    public async Task Suggest_AFragment_PreviewsTheSchemaOnTheFirstExistingCandidate()
    {
        SetupComposition(Compositions.ExecutionKey, Compositions.Execution());
        SetupComposition("exe.northwind.invoicing.test", null);
        _annotations
            .Setup(client => client.GetAnnotationAsync(Org, Project, View, It.IsAny<string>(), true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string key, bool? _, bool? _, CancellationToken _) => new Sdk.Annotation { AnnotationKey = key });
        _annotations
            .Setup(client => client.GetDescendantsAsync(Org, Project, View, It.IsAny<string>(), "all", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.AnnotationsResponse { Annotations = [] });
        _configurations
            .Setup(client => client.GetConfigurationAsync(Org, Project, View, "usg.northwind.invoicing", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { Version = 2, Content = new JObject() });
        _configurations
            .Setup(client => client.GetConfigurationVersionAsync(Org, Project, View, "usg.northwind.invoicing", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { Version = 2, Content = JObject.Parse("""{"currency": "USD", "plan": "enterprise"}""") });
        _schemas
            .Setup(client => client.GetCombinedConfigurationSchemaAsync(Org, Project, View, "usg.northwind.invoicing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.CombinedConfigurationSchema
            {
                MergedContent = JObject.Parse("""{"type": "object", "properties": {"plan": {"type": "string"}, "limit": {"type": "integer"}}, "required": ["plan"]}"""),
            });
        var systems = new[] { Invoicing(), Invoicing(context: "test") };

        var result = await Tools.SuggestConfigurationPlacement(Org, Project, systems, fragment: Json("""{"limit": "many"}"""));

        var suggestion = Structured(result);
        suggestion["Systems"]![1]!["HasComposition"]!.Value<bool>().ShouldBeFalse();
        var candidates = (JArray)suggestion["Candidates"]!;
        candidates[0]!["AnnotationKey"]!.Value<string>().ShouldBe("usg.northwind.invoicing");
        candidates[0]!["Effects"]!.Select(effect => effect["Effective"]?.Value<bool?>()).ShouldBe([true, null]);
        var preview = suggestion["SchemaPreview"]!;
        preview["AnnotationKey"]!.Value<string>().ShouldBe("usg.northwind.invoicing");
        preview["IsValid"]!.Value<bool>().ShouldBeFalse();
        preview["Errors"]![0]!.Value<string>().ShouldBe("#/limit: IntegerExpected");
    }

    [Fact]
    public async Task Suggest_NeedsPointersOrAFragment()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SuggestConfigurationPlacement(Org, Project, [Invoicing()]));
        await Should.ThrowAsync<ToolInputException>(() => Tools.SuggestConfigurationPlacement(Org, Project, [Invoicing()], ["/a"], Json("""{"a": 1}""")));
        await Should.ThrowAsync<ToolInputException>(() => Tools.SuggestConfigurationPlacement(Org, Project, [], ["/a"]));
        await Should.ThrowAsync<ToolInputException>(() => Tools.SuggestConfigurationPlacement(Org, Project, [Invoicing(subject: "bad name")], ["/a"]));
    }

    [Fact]
    public async Task ReadAll_ReadsAtMostFourAtATime_InOrder()
    {
        var running = 0;
        var most = 0;

        var results = await UnderstandingTools.ReadAllAsync(
            Enumerable.Range(0, 10).ToList(),
            async (item, token) =>
            {
                most = Math.Max(most, Interlocked.Increment(ref running));
                await Task.Delay(5, token);
                Interlocked.Decrement(ref running);
                return item * 2;
            },
            CancellationToken.None);

        results.ShouldBe(Enumerable.Range(0, 10).Select(item => item * 2));
        most.ShouldBeLessThanOrEqualTo(UnderstandingTools.MaxConcurrentReads);
    }

    internal static JObject Structured(CallToolResult result)
    {
        return JObject.Parse(result.StructuredContent!.Value.GetRawText());
    }

    private static System.Text.Json.JsonElement Json(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static PlacementSystem Invoicing(string subject = "northwind", string context = "prod")
    {
        return new PlacementSystem { Subject = subject, Responsibility = "invoicing", Context = context };
    }

    private static Sdk.ConfigurationComposition Bindings(params (string Name, string Expression)[] bindings)
    {
        var composition = Compositions.Execution();

        foreach (var (name, expression) in bindings)
        {
            composition.SourceContent(Compositions.Context)[name] = expression;
            composition.Content()[name] = expression;
            composition.AddOrigin($"/{name}", 5);
        }

        return composition;
    }

    private void SetupExplanation(Sdk.ConfigurationComposition composition, string effectiveHash)
    {
        SetupComposition(Compositions.ExecutionKey, composition);
        _configurations
            .Setup(client => client.GetConfigurationAsync(Org, Project, View, Compositions.ExecutionKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Configuration { AnnotationKey = Compositions.ExecutionKey, Hash = effectiveHash, Content = composition.Content() });
    }

    private void SetupComposition(string key, Sdk.ConfigurationComposition? composition)
    {
        var setup = _configurations.Setup(client => client.GetConfigurationCompositionAsync(Org, Project, View, key, It.IsAny<string>(), It.IsAny<CancellationToken>()));

        if (composition is null)
        {
            setup.ThrowsAsync(new Sdk.ApiException("Not found", 404, string.Empty, NoHeaders, null));
        }
        else
        {
            setup.ReturnsAsync(composition);
        }
    }
}
