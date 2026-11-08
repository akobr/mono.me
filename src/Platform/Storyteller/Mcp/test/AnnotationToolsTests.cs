using _42.Platform.Storyteller.Mcp.Tools;

using Moq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Mcp.UnitTests.Support.ToolFixtures;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class AnnotationToolsTests
{
    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders = new Dictionary<string, IEnumerable<string>>();

    private readonly Mock<Sdk.IAnnotationsApiClient> _api = new(MockBehavior.Strict);
    private readonly List<Sdk.AnnotationKeysRequest> _lookups = [];
    private readonly List<(string Key, JArray Patch)> _patches = [];
    private readonly List<(Sdk.Annotation Body, string? IfMatch, string? IfNoneMatch)> _creates = [];

    // The failure of a patch of a key (null: it is patched), and of a create (null: it is created).
    private Func<string, Exception?> _patchFailure = _ => null;
    private Func<Exception?> _createFailure = () => null;

    public AnnotationToolsTests()
    {
        // without If-Match: the tool patches whatever version is stored
        _api.Setup(api => api.PatchAnnotationVersionedAsync(Organization, Project, ViewName, It.IsAny<string>(), It.IsAny<JToken>(), null, It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, string key, JToken patch, string? _, CancellationToken _) =>
            {
                _patches.Add((key, (JArray)patch));
                return _patchFailure(key) is { } failure
                    ? Task.FromException<Sdk.VersionedResponse<Sdk.Annotation>>(failure)
                    : Task.FromResult(new Sdk.VersionedResponse<Sdk.Annotation>(200, AnnotationBodies.Create(AnnotationKey.Parse(key), Project, ViewName), NoHeaders));
            });
        _api.Setup(api => api.SetAnnotationAsync(Organization, Project, ViewName, It.IsAny<string>(), It.IsAny<Sdk.Annotation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, string _, Sdk.Annotation body, string? ifMatch, string? ifNoneMatch, CancellationToken _) =>
            {
                _creates.Add((body, ifMatch, ifNoneMatch));
                return _createFailure() is { } failure ? Task.FromException<Sdk.Annotation>(failure) : Task.FromResult(body);
            });
        _api.Setup(api => api.FindDeletedAnnotationsAsync(Organization, Project, ViewName, It.IsAny<Sdk.AnnotationKeysRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, Sdk.AnnotationKeysRequest, CancellationToken>((_, _, _, request, _) => _lookups.Add(request))
            .ReturnsAsync([]);
    }

    private AnnotationTools Tools => new(_api.Object);

    [Fact]
    public async Task SetAnnotation_ExistingKey_SendsOneJsonPatchOfTheGivenFields_WithoutARead()
    {
        var result = await Tools.SetAnnotation(
            Organization,
            Project,
            "sbt.northwind",
            title: "Northwind Ltd.",
            labels: ["eu", "b2b"],
            values: Element("""{"b":{"deep":[1,2]},"since":"2026-11-01T10:00:00.000+02:00"}"""),
            isDisabled: false,
            validFrom: "2026-11-01T00:00:00+01:00",
            timeZone: "Europe/Prague",
            clearFields: ["Description", "expiresAt"]);

        // labels and values replace their whole sets, a cleared field is null; date-like values stay as given
        var (key, patch) = _patches.ShouldHaveSingleItem();
        key.ShouldBe("sbt.northwind");
        patch.ToString(Formatting.None).ShouldBe(
            """[{"op":"add","path":"/Title","value":"Northwind Ltd."},{"op":"add","path":"/Labels","value":["eu","b2b"]},"""
            + """{"op":"add","path":"/Values","value":{"b":{"deep":[1,2]},"since":"2026-11-01T10:00:00.000+02:00"}},"""
            + """{"op":"add","path":"/IsDisabled","value":false},{"op":"add","path":"/ValidFrom","value":"2026-11-01T00:00:00+01:00"},"""
            + """{"op":"add","path":"/TimeZone","value":"Europe/Prague"},{"op":"add","path":"/Description","value":null},"""
            + """{"op":"add","path":"/ExpiresAt","value":null}]""");
        _creates.ShouldBeEmpty();
        _lookups.ShouldBeEmpty();
        result.Structured().Value<string>("AnnotationKey").ShouldBe("sbt.northwind");
    }

    [Fact]
    public async Task SetAnnotation_ClearedLabelsAndValues_AreSetToNull_Once()
    {
        await Tools.SetAnnotation(Organization, Project, "sbt.northwind", clearFields: ["labels", "VALUES", " timeZone ", "labels"]);

        _patches.ShouldHaveSingleItem().Patch.ToString(Formatting.None).ShouldBe(
            """[{"op":"add","path":"/Labels","value":null},{"op":"add","path":"/Values","value":null},{"op":"add","path":"/TimeZone","value":null}]""");
    }

    [Fact]
    public async Task SetAnnotation_ExistingUnit_PatchesItsTypeAlone()
    {
        // an existing unit has its definition, so only a create needs both
        await Tools.SetAnnotation(Organization, Project, "unt.billing.nightly", unitType: "time");

        _patches.ShouldHaveSingleItem().Patch.ToString(Formatting.None).ShouldBe("""[{"op":"add","path":"/UnitType","value":"time"}]""");
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_NoFields_SendsAnEmptyPatch()
    {
        await Tools.SetAnnotation(Organization, Project, "sbt.northwind");

        _patches.ShouldHaveSingleItem().Patch.ShouldBeEmpty();
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_MissingKey_IsCreatedWhole_OnlyWhileItDoesNotExist()
    {
        Missing("exe.northwind.billing.prod");

        var result = await Tools.SetAnnotation(
            Organization,
            Project,
            "exe.northwind.billing.prod",
            labels: ["eu"],
            values: Element("""{"retries":3}"""),
            validFrom: "2026-11-01T00:00:00Z",
            clearFields: ["title"]);

        _patches.ShouldHaveSingleItem();
        _lookups.ShouldHaveSingleItem().Keys.ShouldBe(
            new[] { "rst.billing", "sbt.northwind", "usg.northwind.billing", "cnt.northwind.prod", "exe.northwind.billing.prod" },
            ignoreOrder: true);
        var (body, ifMatch, ifNoneMatch) = _creates.ShouldHaveSingleItem();
        ifNoneMatch.ShouldBe("*");
        ifMatch.ShouldBeNull();
        var sent = body.ShouldBeOfType<Sdk.Execution>();
        sent.AnnotationKey.ShouldBe("exe.northwind.billing.prod");
        sent.UsageKey.ShouldBe("usg.northwind.billing");
        sent.ValidFrom.ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        sent.Labels.ShouldBe(new[] { "eu" });
        ((JToken)sent.Values!["retries"]).Value<int>().ShouldBe(3);
        sent.Title.ShouldBeNull();
        result.Structured().Value<string>("AnnotationKey").ShouldBe("exe.northwind.billing.prod");
    }

    [Fact]
    public async Task SetAnnotation_Create_RefusesADeletedAncestor()
    {
        Missing("usg.northwind.billing");
        DeletedAmongLookups(new Sdk.Subject { AnnotationKey = "sbt.northwind", Name = "northwind", Deletion = Deletion("sbt.northwind") });

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "usg.northwind.billing"));

        exception.Message.ShouldContain("sbt.northwind (deleted 2026-10-20 08:00:00Z by Ada)");
        exception.Message.ShouldContain("restore_annotations");
        exception.Message.ShouldContain("recreateDeleted");
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_DeletedKey_IsRefusedAsAMissingOne()
    {
        // a patch does not re-create a deleted key; the create path finds it among the deleted ones
        Missing("sbt.northwind", "AnnotationDeleted");
        DeletedAmongLookups(new Sdk.Subject { AnnotationKey = "sbt.northwind", Name = "northwind", Deletion = Deletion("sbt.northwind") });

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind"));

        exception.Message.ShouldContain("sbt.northwind (deleted");
        exception.Message.ShouldContain("restore_annotations");
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_DeletedKey_WithRecreateDeleted_IsCreatedWithoutTheLookup()
    {
        Missing("sbt.northwind", "AnnotationDeleted");

        await Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind", recreateDeleted: true);

        _lookups.ShouldBeEmpty();
        var (body, _, ifNoneMatch) = _creates.ShouldHaveSingleItem();
        body.ShouldBeOfType<Sdk.Subject>().Title.ShouldBe("Northwind");
        ifNoneMatch.ShouldBe("*");
    }

    [Fact]
    public async Task SetAnnotation_KeyCreatedMeanwhile_IsPatched()
    {
        var patches = 0;
        _patchFailure = _ => patches++ == 0 ? ApiError(404, "AnnotationNotFound") : null;
        _createFailure = () => ApiError(412, "AnnotationPreconditionFailed");

        var result = await Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind");

        _patches.Count.ShouldBe(2);
        _patches.ShouldAllBe(patch => patch.Patch.ToString(Formatting.None) == """[{"op":"add","path":"/Title","value":"Northwind"}]""");
        _creates.ShouldHaveSingleItem();
        result.IsError.ShouldNotBe(true);
    }

    [Fact]
    public async Task SetAnnotation_KeyCreatedAndDeletedEveryRound_FailsAfterThreeRounds()
    {
        _patchFailure = _ => ApiError(404, "AnnotationNotFound");
        _createFailure = () => ApiError(412, "AnnotationPreconditionFailed");

        var exception = await Should.ThrowAsync<Sdk.ApiException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind"));

        exception.StatusCode.ShouldBe(412);
        _patches.Count.ShouldBe(3);
        _creates.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ViewNotFound")]
    public async Task SetAnnotation_Other404_IsNotACreate(string? errorCode)
    {
        _patchFailure = _ => errorCode is null ? NotFound() : ApiError(404, errorCode);

        (await Should.ThrowAsync<Sdk.ApiException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind")))
            .StatusCode.ShouldBe(404);
        _creates.ShouldBeEmpty();
        _lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_PatchFailure_IsNotRetried()
    {
        _patchFailure = _ => ApiError(412, "PatchTestFailed");

        (await Should.ThrowAsync<Sdk.ApiException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind")))
            .StatusCode.ShouldBe(412);
        _patches.ShouldHaveSingleItem();
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_CreateUnit_NeedsTypeAndDefinition()
    {
        Missing("unt.billing.nightly");

        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "unt.billing.nightly", unitType: "time")))
            .Message.ShouldContain("needs unitType and unitDefinition");
        _creates.ShouldBeEmpty();

        await Tools.SetAnnotation(Organization, Project, "unt.billing.nightly", unitType: "time", unitDefinition: "0 2 * * *");

        var sent = _creates.ShouldHaveSingleItem().Body.ShouldBeOfType<Sdk.Unit>();
        sent.UnitType.ShouldBe("time");
        sent.UnitDefinition.ShouldBe("0 2 * * *");
        sent.ResponsibilityKey.ShouldBe("rst.billing");
    }

    [Fact]
    public async Task SetAnnotation_NewNameOutsideTheRule_IsRefused()
    {
        Missing("sbt.north wind");
        Absent("sbt.north wind");

        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.north wind")))
            .Message.ShouldContain("'north wind'");
        _creates.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_MissingAncestorWithANameOutsideTheRule_IsRefused()
    {
        Missing("exe.north wind.billing.prod");
        Absent("sbt.north wind");

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "exe.north wind.billing.prod"));

        exception.Message.ShouldContain("'north wind' of the new key 'sbt.north wind'");
        _creates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("usg.Žluťoučký kůň.billing", "sbt.Žluťoučký kůň")]
    [InlineData("usg.northwind.Fakturace účtů", "rst.Fakturace účtů")]
    [InlineData("uxe.northwind.billing.prod.Noční běh", "unt.billing.Noční běh")]
    public async Task SetAnnotation_NewKeyUnderAnExistingNameOutsideTheRule_IsCreated(string key, string existing)
    {
        // only the names that do not exist yet follow the rule; the valid ones are not even looked up
        Missing(key);
        Present(existing);

        await Tools.SetAnnotation(Organization, Project, key);

        _creates.ShouldHaveSingleItem().Body.AnnotationKey.ShouldBe(key);
    }

    [Fact]
    public async Task SetAnnotation_ExistingNameOutsideTheRule_IsAddressedAsItIs()
    {
        // an existing key is patched, so its names are not looked up
        await Tools.SetAnnotation(Organization, Project, "sbt.north wind", title: "Legacy");

        _patches.ShouldHaveSingleItem().Key.ShouldBe("sbt.north wind");
        _creates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("title")]
    [InlineData("TITLE")]
    public async Task SetAnnotation_FieldGivenAndCleared_IsRefused(string field)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "x", clearFields: [field])))
            .Message.ShouldContain("both given and in clearFields");
        _patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_UnknownClearField_IsRefused()
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", clearFields: ["name"])))
            .Message.ShouldContain("among title, description, documentationLink, labels, values, isDisabled, validFrom, expiresAt, timeZone");
        _patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_UnitTypeOnAnotherType_IsRefused()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", unitType: "time"));
        _patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_ValuesThatAreNotAnObject_AreRefused()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", values: Element("[1]")));
        _patches.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-11-01T00:00:00")]
    [InlineData("2026-11-01")]
    [InlineData("next monday")]
    public async Task SetAnnotation_InstantWithoutOffset_IsRefused(string validFrom)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", validFrom: validFrom)))
            .Message.ShouldContain("with an offset");
        _patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAnnotations_SendsTheKeysOfTheLines_AndReportsTheExistingOnes()
    {
        IEnumerable<string>? sent = null;
        _api.Setup(api => api.SetAnnotationsSimpleAsync(Organization, Project, ViewName, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IEnumerable<string>, CancellationToken>((_, _, _, keys, _) => sent = keys.ToList())
            .ReturnsAsync(new List<Sdk.Annotation>
            {
                new Sdk.Context { AnnotationKey = "cnt.northwind.prod", Name = "prod" },
                new Sdk.Execution { AnnotationKey = "exe.northwind.billing.prod", Name = "prod", Description = "long text" },
            });

        var result = await Tools.CreateAnnotations(Organization, Project, ["northwind.billing.prod", string.Empty, "rst.billing", "northwind.billing.prod"]);

        sent.ShouldBe(new[] { "exe.northwind.billing.prod", "rst.billing" });
        _lookups.ShouldHaveSingleItem().Keys.ShouldBe(
            new[] { "rst.billing", "sbt.northwind", "usg.northwind.billing", "cnt.northwind.prod", "exe.northwind.billing.prod" },
            ignoreOrder: true);
        var structured = result.Structured();
        structured["Created"]!.Select(item => item.Value<string>("AnnotationKey")).ShouldBe(new[] { "cnt.northwind.prod", "exe.northwind.billing.prod" });
        structured["Created"]![1]!.Value<string>("AnnotationType").ShouldBe("Execution");
        structured["Created"]![1]!["Description"].ShouldBeNull();
        structured["Skipped"]!.Values<string>().ShouldBe(new[] { "rst.billing" });
    }

    [Fact]
    public async Task CreateAnnotations_RefusesDeletedKeys()
    {
        DeletedAmongLookups(new Sdk.Responsibility { AnnotationKey = "rst.billing", Name = "billing", Deletion = Deletion("rst.billing") });

        (await Should.ThrowAsync<ToolInputException>(() => Tools.CreateAnnotations(Organization, Project, ["northwind.billing"])))
            .Message.ShouldContain("rst.billing");
    }

    [Fact]
    public async Task CreateAnnotations_LooksUpAtMost100KeysPerCall()
    {
        _api.Setup(api => api.SetAnnotationsSimpleAsync(Organization, Project, ViewName, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var lines = Enumerable.Range(0, 100).Select(index => $"s{index}.r.c.u").ToArray();

        await Tools.CreateAnnotations(Organization, Project, lines);

        // 100 subjects, usages, contexts, executions and units of execution, one responsibility and one unit
        _lookups.Sum(request => request.Keys.Count).ShouldBe(502);
        _lookups.ShouldAllBe(request => request.Keys.Count <= AnnotationGuards.MaxKeysPerLookup);
    }

    [Fact]
    public async Task CreateAnnotations_ExistingKeysOutsideTheNameRule_AreSkipped_NotRefused()
    {
        Present("sbt.Žluťoučký kůň");
        _api.Setup(api => api.SetAnnotationsSimpleAsync(Organization, Project, ViewName, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sdk.Annotation> { new Sdk.Usage { AnnotationKey = "usg.Žluťoučký kůň.billing", Name = "billing" } });

        var result = (await Tools.CreateAnnotations(Organization, Project, ["sbt.Žluťoučký kůň", "Žluťoučký kůň.billing"])).Structured();

        result["Created"]!.Select(item => item.Value<string>("AnnotationKey")).ShouldBe(new[] { "usg.Žluťoučký kůň.billing" });
        result["Skipped"]!.Values<string>().ShouldBe(new[] { "sbt.Žluťoučký kůň" });
    }

    [Fact]
    public async Task CreateAnnotations_NewNameOutsideTheRule_IsRefused_AndCreatesNothing()
    {
        Absent("sbt.north wind");

        (await Should.ThrowAsync<ToolInputException>(() => Tools.CreateAnnotations(Organization, Project, ["northwind", "north wind.billing"])))
            .Message.ShouldContain("'north wind'");
        _lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAnnotations_MoreThan100Lines_IsRefused()
    {
        var lines = Enumerable.Range(0, 101).Select(index => $"s{index}").ToArray();

        await Should.ThrowAsync<ToolInputException>(() => Tools.CreateAnnotations(Organization, Project, lines));
    }

    [Fact]
    public async Task ListAnnotations_PagesInTheHost_AcrossApiPages()
    {
        var tokens = new List<string?>();
        _api.Setup(api => api.GetAnnotationsAsync(Organization, Project, ViewName, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, bool?, bool?, CancellationToken>((_, _, _, token, _, _, _) => tokens.Add(token))
            .ReturnsAsync((string _, string _, string _, string token, bool? _, bool? _, CancellationToken _) => token is null
                ? Page("api-2", "sbt.a", "sbt.b", "sbt.c")
                : Page(null, "sbt.d"));

        var first = (await Tools.ListAnnotations(Organization, Project, limit: 2)).Structured();
        var second = (await Tools.ListAnnotations(Organization, Project, limit: 2, continuationToken: first.Value<string>("ContinuationToken"))).Structured();
        var third = (await Tools.ListAnnotations(Organization, Project, limit: 2, continuationToken: second.Value<string>("ContinuationToken"))).Structured();

        Keys(first).ShouldBe(new[] { "sbt.a", "sbt.b" });
        Keys(second).ShouldBe(new[] { "sbt.c" });
        Keys(third).ShouldBe(new[] { "sbt.d" });
        third["ContinuationToken"].ShouldBeNull();
        tokens.ShouldBe(new[] { null, null, "api-2" });
    }

    [Fact]
    public async Task ListAnnotations_SkipsEmptyApiPages_AndProjectsCompactItems()
    {
        _api.Setup(api => api.GetAnnotationsAsync(Organization, Project, ViewName, It.IsAny<string>(), true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string _, string token, bool? _, bool? _, CancellationToken _) => token is null
                ? Page("api-2")
                : new Sdk.AnnotationsResponse
                {
                    Annotations = [new Sdk.Subject { AnnotationKey = "sbt.a", Name = "a", Description = "long", IsDisabled = true, Labels = ["eu"] }],
                });

        var page = (await Tools.ListAnnotations(Organization, Project, includeInactive: true)).Structured();

        var item = page["Items"]!.ShouldHaveSingleItem();
        item.Value<string>("AnnotationType").ShouldBe("Subject");
        item.Value<string>("Status").ShouldBe("Disabled");
        item["Labels"]!.Values<string>().ShouldBe(new[] { "eu" });
        item["Description"].ShouldBeNull();
        page.Value<int>("Count").ShouldBe(1);
    }

    [Fact]
    public async Task FindAnnotations_MatchesTheNamesInTheHost_NotThroughTheApiQueries()
    {
        // the tool matches the names in the host, so no API name query is sent and every context comes back
        _api.Setup(api => api.GetContextsAsync(Organization, Project, ViewName, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.AnnotationsResponse_Context
            {
                Annotations =
                [
                    new Sdk.Context { AnnotationKey = "cnt.northwind.prod", Name = "prod" },
                    new Sdk.Context { AnnotationKey = "cnt.northwind.preprod", Name = "preprod" },
                    new Sdk.Context { AnnotationKey = "cnt.contoso.prod", Name = "prod" },
                    new Sdk.Context { AnnotationKey = "cnt.NorthWind.production", Name = "production" },
                    new Sdk.Context { AnnotationKey = "cnt.northwind.Prod", Name = "Prod" },
                ],
            });

        var page = (await Tools.FindAnnotations(Organization, Project, "CNT", subject: "northwind", context: " ^prod ")).Structured();

        // the exact name ignores case, the pattern does not
        Keys(page).ShouldBe(new[] { "cnt.northwind.prod", "cnt.NorthWind.production" });
    }

    [Fact]
    public async Task FindAnnotations_PagesTheMatches_AcrossApiPages()
    {
        var tokens = new List<string?>();
        _api.Setup(api => api.GetSubjectsAsync(Organization, Project, ViewName, null, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, bool?, bool?, CancellationToken>((_, _, _, _, token, _, _, _) => tokens.Add(token))
            .ReturnsAsync((string _, string _, string _, string _, string token, bool? _, bool? _, CancellationToken _) => token is null
                ? Subjects("api-2", "a1", "b1", "a2", "b2", "a3")
                : Subjects(null, "b3", "a4"));

        var first = (await Tools.FindAnnotations(Organization, Project, "sbt", subject: "^a", limit: 2)).Structured();
        var second = (await Tools.FindAnnotations(Organization, Project, "sbt", subject: "^a", limit: 2, continuationToken: first.Value<string>("ContinuationToken"))).Structured();
        var third = (await Tools.FindAnnotations(Organization, Project, "sbt", subject: "^a", limit: 2, continuationToken: second.Value<string>("ContinuationToken"))).Structured();

        Keys(first).ShouldBe(new[] { "sbt.a1", "sbt.a2" });
        Keys(second).ShouldBe(new[] { "sbt.a3" });
        Keys(third).ShouldBe(new[] { "sbt.a4" });
        third["ContinuationToken"].ShouldBeNull();
        tokens.ShouldBe(new[] { null, null, "api-2" });
    }

    [Fact]
    public async Task FindAnnotations_PatternThatTakesTooLong_IsAnInputError()
    {
        _api.Setup(api => api.GetSubjectsAsync(Organization, Project, ViewName, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Subjects(null, new string('a', 40) + "!"));

        (await Should.ThrowAsync<ToolInputException>(() => Tools.FindAnnotations(Organization, Project, "sbt", subject: "^(a+)+$")))
            .Message.ShouldContain("took too long");
    }

    [Theory]
    [InlineData("unt", null, "billing")]
    [InlineData("rst", "northwind", null)]
    public async Task FindAnnotations_FilterTheTypeDoesNotTake_IsRefused(string type, string? subject, string? unit)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.FindAnnotations(Organization, Project, type, subject: subject, unit: unit)))
            .Message.ShouldContain("takes the filters");
    }

    [Theory]
    [InlineData("^(prod")]
    [InlineData("%prod")]
    public async Task FindAnnotations_InvalidPattern_IsRefused(string pattern)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.FindAnnotations(Organization, Project, "sbt", subject: pattern));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, " del-1 ", false)]
    [InlineData(true, "del-1", null)]
    [InlineData(false, null, false)]
    public async Task ListDeletedAnnotations_WithADeletionId_ListsEverythingItRemoved_UnlessRootsIsTrue(bool? roots, string? deletionId, bool? sentRoots)
    {
        var sent = new List<(bool? Roots, string? DeletionId)>();
        _api.Setup(api => api.GetDeletedAnnotationsAsync(Organization, Project, ViewName, It.IsAny<bool?>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool?, string, string, CancellationToken>((_, _, _, rootsQuery, deletion, _, _) => sent.Add((rootsQuery, deletion)))
            .ReturnsAsync(Page(null));

        await Tools.ListDeletedAnnotations(Organization, Project, roots: roots, deletionId: deletionId);

        sent.ShouldHaveSingleItem().ShouldBe((sentRoots, deletionId?.Trim()));
    }

    [Fact]
    public async Task ListDescendants_UnknownKind_IsRefused()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.ListDescendants(Organization, Project, "sbt.northwind", descendants: "children"));
    }

    [Fact]
    public async Task GetAnnotation_IsWholeWithItsStatus()
    {
        _api.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, "sbt.northwind", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Subject { AnnotationKey = "sbt.northwind", Name = "northwind", Description = "long", ExpiresAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) });

        var annotation = (await Tools.GetAnnotation(Organization, Project, "sbt.northwind")).Structured();

        annotation.Value<string>("AnnotationType").ShouldBe("Subject");
        annotation.Value<string>("Description").ShouldBe("long");
        annotation.Value<string>("Status").ShouldBe("Expired");
    }

    [Fact]
    public async Task RestoreAnnotations_ReturnsCompactItemsAndFailures()
    {
        _api.Setup(api => api.RestoreAnnotationsAsync(Organization, Project, ViewName, It.Is<Sdk.AnnotationRestoreRequest>(request => request.Descendants == false && request.Keys.Single() == "sbt.northwind"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.AnnotationRestoreResult
            {
                Annotations = [new Sdk.Subject { AnnotationKey = "sbt.northwind", Name = "northwind", Description = "long" }],
                CreatedKeys = [],
                Failed = [new Sdk.AnnotationKeyFailure { Key = "sbt.other", ErrorCode = "NotFound" }],
            });

        var result = (await Tools.RestoreAnnotations(Organization, Project, ["sbt.northwind"], descendants: false)).Structured();

        result["Annotations"]![0]!["Description"].ShouldBeNull();
        result["Failed"]![0]!.Value<string>("ErrorCode").ShouldBe("NotFound");
    }

    private static Sdk.AnnotationsResponse Page(string? next, params string[] keys)
    {
        return new Sdk.AnnotationsResponse
        {
            Annotations = keys.Select(key => (Sdk.Annotation)new Sdk.Subject { AnnotationKey = key, Name = key[4..] }).ToList(),
            ContinuationToken = next,
            Count = keys.Length,
        };
    }

    private static Sdk.AnnotationsResponse_Subject Subjects(string? next, params string[] names)
    {
        return new Sdk.AnnotationsResponse_Subject
        {
            Annotations = names.Select(name => new Sdk.Subject { AnnotationKey = $"sbt.{name}", Name = name }).ToList(),
            ContinuationToken = next,
            Count = names.Length,
        };
    }

    private static IEnumerable<string?> Keys(JObject page)
    {
        return page["Items"]!.Select(item => item.Value<string>("AnnotationKey"));
    }

    private static Sdk.ApiException<Sdk.ErrorResponse> ApiError(int status, string errorCode)
    {
        return new Sdk.ApiException<Sdk.ErrorResponse>("Refused.", status, string.Empty, NoHeaders, new Sdk.ErrorResponse { Message = "Refused.", ErrorCode = errorCode }, null);
    }

    // A patch of the key answers 404 with the code (the key is missing or deleted); other keys are patched.
    private void Missing(string key, string errorCode = "AnnotationNotFound")
    {
        _patchFailure = patched => patched == key ? ApiError(404, errorCode) : null;
    }

    // The lookups of the name rule, which count deleted annotations as existing.
    private void Present(string key)
    {
        _api.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, key, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.Subject { AnnotationKey = key, Name = key.Split('.')[^1] });
    }

    private void Absent(string key)
    {
        _api.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, key, true, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
    }

    private void DeletedAmongLookups(params Sdk.Annotation[] deleted)
    {
        _api.Setup(api => api.FindDeletedAnnotationsAsync(Organization, Project, ViewName, It.IsAny<Sdk.AnnotationKeysRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, Sdk.AnnotationKeysRequest, CancellationToken>((_, _, _, request, _) => _lookups.Add(request))
            .ReturnsAsync((string _, string _, string _, Sdk.AnnotationKeysRequest request, CancellationToken _) =>
                deleted.Where(annotation => request.Keys.Contains(annotation.AnnotationKey)).ToList());
    }
}
