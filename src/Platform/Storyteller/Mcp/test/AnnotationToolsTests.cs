using _42.Platform.Storyteller.Mcp.Tools;

using Moq;

using Newtonsoft.Json.Linq;

using static _42.Platform.Storyteller.Mcp.UnitTests.Support.ToolFixtures;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class AnnotationToolsTests
{
    private readonly Mock<Sdk.IAnnotationsApiClient> _api = new(MockBehavior.Strict);
    private readonly List<Sdk.AnnotationKeysRequest> _lookups = [];
    private Sdk.Annotation? _sent;

    public AnnotationToolsTests()
    {
        _api.Setup(api => api.SetAnnotationAsync(Organization, Project, ViewName, It.IsAny<string>(), It.IsAny<Sdk.Annotation>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, Sdk.Annotation, CancellationToken>((_, _, _, _, body, _) => _sent = body)
            .ReturnsAsync((string _, string _, string _, string _, Sdk.Annotation body, CancellationToken _) => body);
        _api.Setup(api => api.FindDeletedAnnotationsAsync(Organization, Project, ViewName, It.IsAny<Sdk.AnnotationKeysRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, Sdk.AnnotationKeysRequest, CancellationToken>((_, _, _, request, _) => _lookups.Add(request))
            .ReturnsAsync([]);
    }

    private AnnotationTools Tools => new(_api.Object);

    [Fact]
    public async Task SetAnnotation_Update_KeepsOmittedFields_AndClearsClearFields()
    {
        Stored(new Sdk.Subject
        {
            AnnotationKey = "sbt.northwind",
            Name = "northwind",
            Title = "Northwind",
            Description = "A customer.",
            Labels = ["eu"],
            Values = new Dictionary<string, object> { ["tier"] = "gold" },
            TimeZone = "Europe/Prague",
            ContextNames = ["prod"],
        });

        await Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "Northwind Ltd.", clearFields: ["Description"]);

        var sent = _sent.ShouldBeOfType<Sdk.Subject>();
        sent.AnnotationKey.ShouldBe("sbt.northwind");
        sent.Title.ShouldBe("Northwind Ltd.");
        sent.Description.ShouldBeNull();
        sent.Labels.ShouldBe(new[] { "eu" });
        sent.Values!["tier"].ShouldBe("gold");
        sent.TimeZone.ShouldBe("Europe/Prague");
        sent.ContextNames.ShouldBeNull();
        _lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetAnnotation_Values_ReplaceTheWholeSet()
    {
        Stored(new Sdk.Subject { AnnotationKey = "sbt.northwind", Name = "northwind", Values = new Dictionary<string, object> { ["a"] = 1 } });

        await Tools.SetAnnotation(Organization, Project, "sbt.northwind", values: Element("""{"b":{"deep":[1,2]}}"""));

        _sent!.Values!.Keys.ShouldBe(new[] { "b" });
        ((JToken)_sent.Values["b"]).ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"deep":[1,2]}""");
    }

    [Fact]
    public async Task SetAnnotation_Create_LooksUpTheKeyAndItsAncestors()
    {
        Missing("exe.northwind.billing.prod");

        await Tools.SetAnnotation(Organization, Project, "exe.northwind.billing.prod", validFrom: "2026-11-01T00:00:00Z");

        _lookups.ShouldHaveSingleItem().Keys.ShouldBe(
            new[] { "rst.billing", "sbt.northwind", "usg.northwind.billing", "cnt.northwind.prod", "exe.northwind.billing.prod" },
            ignoreOrder: true);
        var sent = _sent.ShouldBeOfType<Sdk.Execution>();
        sent.UsageKey.ShouldBe("usg.northwind.billing");
        sent.ValidFrom.ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
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
        _sent.ShouldBeNull();
    }

    [Fact]
    public async Task SetAnnotation_Create_WithRecreateDeleted_SkipsTheLookup()
    {
        Missing("sbt.northwind");

        await Tools.SetAnnotation(Organization, Project, "sbt.northwind", recreateDeleted: true);

        _lookups.ShouldBeEmpty();
        _sent.ShouldBeOfType<Sdk.Subject>();
    }

    [Fact]
    public async Task SetAnnotation_CreateUnit_NeedsTypeAndDefinition()
    {
        Missing("unt.billing.nightly");

        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "unt.billing.nightly", unitType: "time")))
            .Message.ShouldContain("needs unitType and unitDefinition");

        await Tools.SetAnnotation(Organization, Project, "unt.billing.nightly", unitType: "time", unitDefinition: "0 2 * * *");

        var sent = _sent.ShouldBeOfType<Sdk.Unit>();
        sent.UnitType.ShouldBe("time");
        sent.ResponsibilityKey.ShouldBe("rst.billing");
    }

    [Fact]
    public async Task SetAnnotation_NewNameOutsideTheRule_IsRefused()
    {
        Missing("sbt.north wind");

        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.north wind")))
            .Message.ShouldContain("'north wind'");
    }

    [Fact]
    public async Task SetAnnotation_ExistingNameOutsideTheRule_IsAddressedAsItIs()
    {
        Stored(new Sdk.Subject { AnnotationKey = "sbt.north wind", Name = "north wind" });

        await Tools.SetAnnotation(Organization, Project, "sbt.north wind", title: "Legacy");

        _sent!.Title.ShouldBe("Legacy");
    }

    [Theory]
    [InlineData("title")]
    [InlineData("TITLE")]
    public async Task SetAnnotation_FieldGivenAndCleared_IsRefused(string field)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", title: "x", clearFields: [field])))
            .Message.ShouldContain("both given and in clearFields");
    }

    [Fact]
    public async Task SetAnnotation_UnknownClearField_IsRefused()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", clearFields: ["name"]));
    }

    [Fact]
    public async Task SetAnnotation_UnitTypeOnAnotherType_IsRefused()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", unitType: "time"));
    }

    [Theory]
    [InlineData("2026-11-01T00:00:00")]
    [InlineData("2026-11-01")]
    [InlineData("next monday")]
    public async Task SetAnnotation_InstantWithoutOffset_IsRefused(string validFrom)
    {
        (await Should.ThrowAsync<ToolInputException>(() => Tools.SetAnnotation(Organization, Project, "sbt.northwind", validFrom: validFrom)))
            .Message.ShouldContain("with an offset");
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
    public async Task FindAnnotations_SendsTheFiltersOfTheType()
    {
        _api.Setup(api => api.GetContextsAsync(Organization, Project, ViewName, "northwind", "^prod", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.AnnotationsResponse_Context { Annotations = [new Sdk.Context { AnnotationKey = "cnt.northwind.prod", Name = "prod" }] });

        var page = (await Tools.FindAnnotations(Organization, Project, "CNT", subject: "northwind", context: " ^prod ")).Structured();

        Keys(page).ShouldBe(new[] { "cnt.northwind.prod" });
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
    public async Task FindAnnotations_PatternTheServerCannotUse_IsRefused(string pattern)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools.FindAnnotations(Organization, Project, "sbt", subject: pattern));
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

    private static IEnumerable<string?> Keys(JObject page)
    {
        return page["Items"]!.Select(item => item.Value<string>("AnnotationKey"));
    }

    private void Stored(Sdk.Annotation annotation)
    {
        _api.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, annotation.AnnotationKey, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(annotation);
    }

    private void Missing(string key)
    {
        _api.Setup(api => api.GetAnnotationAsync(Organization, Project, ViewName, key, true, null, It.IsAny<CancellationToken>()))
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
