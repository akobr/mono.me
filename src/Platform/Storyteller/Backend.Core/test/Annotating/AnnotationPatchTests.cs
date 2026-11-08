using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Annotating;

public class AnnotationPatchTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 30, 0, TimeSpan.FromHours(2));

    // As the API writes a read body.
    private static readonly JsonSerializerOptions ReadBodyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Merge_OmittedMembers_KeepTheirValues()
    {
        var current = Stored();

        var result = Merge("""{"Title":"Invoices EU"}""").Apply(current);

        result.Title.ShouldBe("Invoices EU");
        result.Description.ShouldBe(current.Description);
        result.DocumentationLink.ShouldBe(current.DocumentationLink);
        result.TimeZone.ShouldBe(current.TimeZone);
        result.IsDisabled.ShouldBe(current.IsDisabled);
        result.ValidFrom.ShouldBe(current.ValidFrom);
        result.ExpiresAt.ShouldBe(current.ExpiresAt);
        result.Labels.ShouldBe(current.Labels);
        ValuesOf(result).ShouldBe(ValuesOf(current));
    }

    [Fact]
    public void Merge_Null_ClearsTheMember()
    {
        var patch = Merge("""
            {
              "IsDisabled": null, "ValidFrom": null, "ExpiresAt": null, "TimeZone": null, "Title": null,
              "Description": null, "DocumentationLink": null, "Labels": null, "Values": null
            }
            """);

        var result = patch.Apply(Stored());

        result.IsDisabled.ShouldBeNull();
        result.ValidFrom.ShouldBeNull();
        result.ExpiresAt.ShouldBeNull();
        result.TimeZone.ShouldBeNull();
        result.Title.ShouldBeNull();
        result.Description.ShouldBeNull();
        result.DocumentationLink.ShouldBeNull();
        result.Labels.ShouldBeNull();
        result.Values.ShouldBeNull();
    }

    [Fact]
    public void Merge_Values_MergeRecursively()
    {
        var current = Execution() with { Values = JsonValues("""{"plan":"pro","limits":{"cpu":2,"memory":4},"old":1}""") };

        var result = Merge("""{"Values":{"limits":{"memory":8,"cpu":null},"old":null,"region":"eu"}}""").Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"plan":"pro","limits":{"memory":8},"region":"eu"}"""));
    }

    [Fact]
    public void Merge_Values_OnAnAnnotationWithoutValues_SetsThem()
    {
        var result = Merge("""{"Values":{"plan":"pro","gone":null}}""").Apply(Execution());

        ValuesOf(result).ShouldBe(Normalized("""{"plan":"pro"}"""));
    }

    [Fact]
    public void Merge_ArrayValue_IsReplacedWhole()
    {
        var current = Execution() with { Values = JsonValues("""{"regions":["eu","us"],"limits":{"cpu":2}}""") };

        var result = Merge("""{"Values":{"regions":["ap"],"limits":5}}""").Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"regions":["ap"],"limits":5}"""));
    }

    [Fact]
    public void Merge_Labels_AreReplacedWhole()
    {
        var current = Execution() with { Labels = ["eu", "prod"] };

        var result = Merge("""{"Labels":["beta"]}""").Apply(current);

        result.Labels.ShouldBe(["beta"]);
    }

    [Fact]
    public void Merge_MemberNames_MatchCaseInsensitively()
    {
        var result = Merge("""{"title":"Invoices","ISDISABLED":true,"values":{"plan":"pro"},"labels":["eu"]}""").Apply(Execution());

        result.Title.ShouldBe("Invoices");
        result.IsDisabled.ShouldBe(true);
        result.Labels.ShouldBe(["eu"]);
        ValuesOf(result).ShouldBe(Normalized("""{"plan":"pro"}"""));
    }

    [Fact]
    public void Merge_ReadBodySentBack_IgnoresTheIdentityAndServerOwnedMembers()
    {
        var current = Stored();
        var body = JsonSerializer.SerializeToNode(current, current.GetType(), ReadBodyOptions)!.AsObject();
        body["title"] = "Changed";
        body["annotationKey"] = "exe.other.payments.dev";
        body["name"] = "renamed";
        body["annotationType"] = "Unit";
        body["projectName"] = "other";
        body["viewName"] = "other";
        body["subjectKey"] = "sbt.other";
        body["unitNames"] = new JsonArray("nightly", "weekly");
        body["deletion"] = new JsonObject { ["at"] = "2026-01-01T00:00:00Z" };

        var result = Merge(body.ToJsonString()).Apply(current);

        result.Title.ShouldBe("Changed");
        result.AnnotationKey.ShouldBe(current.AnnotationKey);
        result.Name.ShouldBe("prod");
        result.AnnotationType.ShouldBe(AnnotationType.Execution);
        result.ProjectName.ShouldBe("billing");
        result.ViewName.ShouldBe("default");
        var execution = result.ShouldBeOfType<Execution>();
        execution.SubjectKey.ShouldBe("sbt.northwind");
        execution.UnitNames.ShouldBeEmpty();
        result.Deletion.ShouldBeNull();
        result.Labels.ShouldBe(current.Labels);

        // RFC 7396: the body's "none": null removes that value, the rest stays
        ValuesOf(result).ShouldBe(Normalized("""{"plan":"pro","limits":{"cpu":2},"tags":["a","b"]}"""));
    }

    [Theory]
    [InlineData("""{"Titel":"Invoices"}""", "Titel")]
    [InlineData("""{"Title":"Invoices","Foo":1}""", "Foo")]
    [InlineData("""{"UnitNames":[],"Bar":null}""", "Bar")]
    public void Merge_UnknownMember_IsRejected(string patch, string member)
    {
        var exception = Should.Throw<InvalidInputException>(() => Merge(patch).Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain($"'{member}'");
    }

    [Fact]
    public void Merge_TheSameMemberTwiceWithAnotherCase_IsRejected()
    {
        var exception = Should.Throw<InvalidInputException>(() => Merge("""{"Title":"a","title":"b"}""").Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain("'title'");
    }

    [Theory]
    [InlineData("IsDisabled", "\"yes\"")]
    [InlineData("IsDisabled", "0")]
    [InlineData("ValidFrom", "5")]
    [InlineData("ExpiresAt", "\"tomorrow\"")]
    [InlineData("Title", "5")]
    [InlineData("TimeZone", "{}")]
    [InlineData("Description", "true")]
    [InlineData("DocumentationLink", "[]")]
    [InlineData("Labels", "[1]")]
    [InlineData("Labels", "[\"eu\",null]")]
    [InlineData("Labels", "\"eu\"")]
    [InlineData("Values", "[]")]
    [InlineData("Values", "\"plan\"")]
    public void Merge_WrongType_IsRejectedNamingTheMember(string member, string json)
    {
        var exception = Should.Throw<InvalidInputException>(() => Merge($$"""{"{{member}}":{{json}}}""").Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain($"'{member}'");
    }

    [Fact]
    public void Merge_Unit_ChangesItsTypeAndDefinition()
    {
        var result = Merge("""{"UnitType":"time","unitDefinition":"0 * * * *"}""").Apply(Unit());

        var unit = result.ShouldBeOfType<Unit>();
        unit.UnitType.ShouldBe("time");
        unit.UnitDefinition.ShouldBe("0 * * * *");
    }

    [Fact]
    public void Merge_Unit_KeepsItsTypeWhenOmitted()
    {
        var current = Unit() with { UnitType = "message", UnitDefinition = "invoice.*" };

        var unit = Merge("""{"Title":"Export"}""").Apply(current).ShouldBeOfType<Unit>();

        unit.UnitType.ShouldBe("message");
        unit.UnitDefinition.ShouldBe("invoice.*");
    }

    [Theory]
    [InlineData("""{"UnitType":null}""", "UnitType")]
    [InlineData("""{"UnitDefinition":null}""", "UnitDefinition")]
    [InlineData("""{"UnitType":5}""", "UnitType")]
    public void Merge_UnitMemberNullOrNotAString_IsRejected(string patch, string member)
    {
        var exception = Should.Throw<InvalidInputException>(() => Merge(patch).Apply(Unit()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain(member);
    }

    [Theory]
    [InlineData("UnitType")]
    [InlineData("UnitDefinition")]
    public void Merge_UnitMemberOnAnotherType_IsRejected(string member)
    {
        var exception = Should.Throw<InvalidInputException>(() => Merge($$"""{"{{member}}":"time"}""").Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain($"'{member}'");
    }

    [Fact]
    public void Merge_DuplicateLabels_KeepTheFirstOccurrence()
    {
        var result = Merge("""{"Labels":["prod","eu","prod","EU","eu"]}""").Apply(Execution());

        result.Labels.ShouldBe(["prod", "eu", "EU"]);
    }

    [Fact]
    public void Merge_DoesNotDependOnTheCallersDocument()
    {
        var document = JsonNode.Parse("""{"Values":{"plan":"pro"}}""")!.AsObject();
        var patch = AnnotationPatch.Merge(document);
        document["Title"] = "Later";

        var result = patch.Apply(Execution());

        result.Title.ShouldBeNull();
        document.ToJsonString().ShouldBe("""{"Values":{"plan":"pro"},"Title":"Later"}""");
    }

    [Fact]
    public void JsonPatch_AddExistingLabel_ChangesNothing()
    {
        var current = Execution() with { Labels = ["eu", "prod"] };

        var result = JsonPatch("""[{"op":"add","path":"/Labels/-","value":"eu"},{"op":"add","path":"/Labels/-","value":"beta"}]""").Apply(current);

        result.Labels.ShouldBe(["eu", "prod", "beta"]);
    }

    [Fact]
    public void JsonPatch_AddLabel_OnAnAnnotationWithoutLabels()
    {
        var result = JsonPatch("""[{"op":"add","path":"/Labels/-","value":"eu"}]""").Apply(Execution());

        result.Labels.ShouldBe(["eu"]);
    }

    [Fact]
    public void JsonPatch_AddValue_OnAnAnnotationWithoutValues()
    {
        var result = JsonPatch("""[{"op":"add","path":"/Values/plan","value":"pro"}]""").Apply(Execution());

        var value = result.Values.ShouldNotBeNull()["plan"].ShouldBeOfType<JsonElement>();
        value.GetString().ShouldBe("pro");
    }

    [Fact]
    public void JsonPatch_AddValue_ReplacesTheValueWhole()
    {
        var current = Execution() with { Values = JsonValues("""{"limits":{"cpu":2,"memory":4},"plan":"pro"}""") };

        var result = JsonPatch("""[{"op":"add","path":"/Values/limits","value":{"cpu":4}}]""").Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"limits":{"cpu":4},"plan":"pro"}"""));
    }

    [Fact]
    public void JsonPatch_ReplaceTitle_OnAnUnsetTitle()
    {
        var result = JsonPatch("""[{"op":"replace","path":"/Title","value":"Invoices"}]""").Apply(Execution());

        result.Title.ShouldBe("Invoices");
    }

    [Fact]
    public void JsonPatch_RemoveTitle_UnsetsIt()
    {
        var result = JsonPatch("""[{"op":"remove","path":"/Title"}]""").Apply(Execution() with { Title = "Invoices" });

        result.Title.ShouldBeNull();
    }

    [Fact]
    public void JsonPatch_PassingTest_Applies()
    {
        var patch = JsonPatch("""[{"op":"test","path":"/Title","value":"Invoices"},{"op":"replace","path":"/Title","value":"Bills"}]""");

        patch.Apply(Execution() with { Title = "Invoices" }).Title.ShouldBe("Bills");
    }

    [Fact]
    public void JsonPatch_FailedTest_IsTestFailed()
    {
        var patch = JsonPatch("""[{"op":"replace","path":"/Description","value":"x"},{"op":"test","path":"/Title","value":"Other"}]""");

        var exception = Should.Throw<JsonPatchException>(() => patch.Apply(Execution() with { Title = "Invoices" }));

        exception.Kind.ShouldBe(JsonPatchFailureKind.TestFailed);
        exception.OperationIndex.ShouldBe(1);
        exception.ErrorCode.ShouldBe(ErrorCodes.PatchTestFailed);
    }

    [Theory]
    [InlineData("""[{"op":"replace","path":"/Foo","value":"x"}]""")]
    [InlineData("""[{"op":"remove","path":"/Values/missing"}]""")]
    [InlineData("""[{"op":"add","path":"/Values/limits/cpu","value":2}]""")]
    [InlineData("""[{"op":"replace","path":"/Labels/0","value":"x"}]""")]
    [InlineData("""[{"op":"add","path":"/Labels/1","value":"x"}]""")]
    [InlineData("""[{"op":"add","path":"/Labels/01","value":"x"}]""")]
    [InlineData("""[{"op":"remove","path":"/Labels/-"}]""")]
    [InlineData("""[{"op":"add","path":"/Title/x","value":"x"}]""")]
    [InlineData("""[{"op":"copy","from":"/Values/missing","path":"/Values/x"}]""")]
    [InlineData("""[{"op":"move","from":"/Values","path":"/Values/x"}]""")]
    [InlineData("""[{"op":"remove","path":""}]""")]
    public void JsonPatch_OperationOnAnUnknownPath_IsOperationFailed(string operations)
    {
        var exception = Should.Throw<JsonPatchException>(() => JsonPatch(operations).Apply(Execution()));

        exception.Kind.ShouldBe(JsonPatchFailureKind.OperationFailed);
        exception.OperationIndex.ShouldBe(0);
        exception.ErrorCode.ShouldBe(ErrorCodes.PatchInvalid);
    }

    [Theory]
    [InlineData("""[{"op":"bogus","path":"/Title","value":"x"}]""")]
    [InlineData("""[{"op":"add","value":"x"}]""")]
    [InlineData("""[{"op":"add","path":"/Title"}]""")]
    [InlineData("""[{"op":"add","path":"Title","value":"x"}]""")]
    [InlineData("""[{"path":"/Title","value":"x"}]""")]
    [InlineData("""[{"op":"move","path":"/Title"}]""")]
    [InlineData("""[{"op":"add","path":"/a~2b","value":"x"}]""")]
    [InlineData("""["add"]""")]
    [InlineData("""[null]""")]
    public void JsonPatch_MalformedDocument_IsInvalidUpFront(string operations)
    {
        var exception = Should.Throw<JsonPatchException>(() => JsonPatch(operations));

        exception.Kind.ShouldBe(JsonPatchFailureKind.Invalid);
        exception.ErrorCode.ShouldBe(ErrorCodes.PatchInvalid);
    }

    [Fact]
    public void JsonPatch_ResultThatIsNotAnObject_IsInvalid()
    {
        var patch = JsonPatch("""[{"op":"replace","path":"","value":[]}]""");

        var exception = Should.Throw<JsonPatchException>(() => patch.Apply(Execution()));

        exception.Kind.ShouldBe(JsonPatchFailureKind.Invalid);
    }

    [Fact]
    public void JsonPatch_AddUnknownMember_IsRejected()
    {
        var exception = Should.Throw<InvalidInputException>(() => JsonPatch("""[{"op":"add","path":"/Foo","value":1}]""").Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain("'Foo'");
    }

    [Fact]
    public void JsonPatch_PathsAreCaseSensitive()
    {
        // /title is not the member Title: it is added as an unknown member
        var exception = Should.Throw<InvalidInputException>(() => JsonPatch("""[{"op":"add","path":"/title","value":"x"}]""").Apply(Execution()));

        exception.Message.ShouldContain("'title'");
    }

    [Theory]
    [InlineData("""[{"op":"remove","path":"/UnitType"}]""", "UnitType")]
    [InlineData("""[{"op":"replace","path":"/UnitDefinition","value":null}]""", "UnitDefinition")]
    public void JsonPatch_UnitMemberRemovedOrNull_IsRejected(string operations, string member)
    {
        var exception = Should.Throw<InvalidInputException>(() => JsonPatch(operations).Apply(Unit()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain(member);
    }

    [Fact]
    public void JsonPatch_LabelThatIsNotAString_IsRejected()
    {
        var exception = Should.Throw<InvalidInputException>(() => JsonPatch("""[{"op":"add","path":"/Labels/-","value":1}]""").Apply(Execution()));

        exception.Message.ShouldContain("'Labels'");
    }

    [Fact]
    public void JsonPatch_EscapedPointer_AddressesTheValueName()
    {
        var result = JsonPatch("""[{"op":"add","path":"/Values/a~1b","value":"slash"},{"op":"add","path":"/Values/c~0d","value":"tilde"}]""").Apply(Execution());

        ValuesOf(result).ShouldBe(Normalized("""{"a/b":"slash","c~d":"tilde"}"""));
    }

    [Fact]
    public void JsonPatch_EscapedPointer_ReplacesAndRemovesTheValue()
    {
        var current = Execution() with { Values = JsonValues("""{"a/b":1,"c~d":2,"e":3}""") };

        var result = JsonPatch("""[{"op":"replace","path":"/Values/a~1b","value":10},{"op":"remove","path":"/Values/c~0d"}]""").Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"a/b":10,"e":3}"""));
    }

    // RFC 6901 unescapes ~1 before ~0, so ~01 is the name ~1, never a slash.
    [Fact]
    public void JsonPatch_EscapedTilde_IsUnescapedOnce()
    {
        var result = JsonPatch("""[{"op":"add","path":"/Values/a~01b","value":1},{"op":"test","path":"/Values/a~01b","value":1}]""").Apply(Execution());

        ValuesOf(result).ShouldBe(Normalized("""{"a~1b":1}"""));
    }

    [Fact]
    public void JsonPatch_MoveAndCopy_RelocateValues()
    {
        var current = Execution() with { Values = JsonValues("""{"old":{"cpu":2},"plan":"pro"}"""), Labels = ["eu"] };
        var patch = JsonPatch("""
            [
              {"op":"move","from":"/Values/old","path":"/Values/limits"},
              {"op":"copy","from":"/Values/plan","path":"/Title"},
              {"op":"copy","from":"/Labels/0","path":"/Labels/0"},
              {"op":"move","from":"/Values/plan","path":"/Values/plan"}
            ]
            """);

        var result = patch.Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"limits":{"cpu":2},"plan":"pro"}"""));
        result.Title.ShouldBe("pro");
        result.Labels.ShouldBe(["eu"]);
    }

    [Fact]
    public void JsonPatch_ArrayIndexes_InsertReplaceAndRemove()
    {
        var current = Execution() with { Labels = ["a", "b", "c"] };
        var patch = JsonPatch("""
            [
              {"op":"add","path":"/Labels/0","value":"first"},
              {"op":"replace","path":"/Labels/2","value":"B"},
              {"op":"remove","path":"/Labels/3"},
              {"op":"add","path":"/Labels/3","value":"last"},
              {"op":"test","path":"/Labels","value":["first","a","B","last"]}
            ]
            """);

        patch.Apply(current).Labels.ShouldBe(["first", "a", "B", "last"]);
    }

    [Fact]
    public void JsonPatch_ReplaceTheWholeDocument_SetsEveryMember()
    {
        var current = Execution() with { Title = "Old", Labels = ["eu"] };

        var result = JsonPatch("""[{"op":"replace","path":"","value":{"Title":"New"}}]""").Apply(current);

        result.Title.ShouldBe("New");
        result.Labels.ShouldBeNull();
        result.Values.ShouldBeNull();
    }

    [Fact]
    public void JsonPatch_TestOnANullMember_Passes()
    {
        var patch = JsonPatch("""[{"op":"test","path":"/Title","value":null},{"op":"add","path":"/Title","value":"x"}]""");

        patch.Apply(Execution()).Title.ShouldBe("x");
    }

    [Fact]
    public void JsonPatch_NullValue_GivesANullEntry()
    {
        var result = JsonPatch("""[{"op":"add","path":"/Values/x","value":null}]""").Apply(Execution());

        var values = result.Values.ShouldNotBeNull();
        values.ContainsKey("x").ShouldBeTrue();
        values["x"].ShouldBeNull();
    }

    [Fact]
    public void Merge_NullValue_RemovesTheEntry()
    {
        var current = Execution() with { Values = new Dictionary<string, object> { ["x"] = null!, ["y"] = 1 } };

        var result = Merge("""{"Values":{"x":null}}""").Apply(current);

        ValuesOf(result).ShouldBe(Normalized("""{"y":1}"""));
    }

    [Theory]
    [InlineData("""{"Title":"__long__"}""")]
    [InlineData("""{"DocumentationLink":"ftp://docs.example.com"}""")]
    public void Merge_RulesStillApply(string patch)
    {
        var json = patch.Replace("__long__", new string('t', AnnotationLimits.TitleMaxLength + 1));

        var exception = Should.Throw<InvalidInputException>(() => Merge(json).Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
    }

    [Fact]
    public void JsonPatch_RulesStillApply()
    {
        var title = new string('t', AnnotationLimits.TitleMaxLength + 1);

        var exception = Should.Throw<InvalidInputException>(
            () => JsonPatch($$"""[{"op":"replace","path":"/Title","value":"{{title}}"}]""").Apply(Execution()));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain("Title");
    }

    [Fact]
    public void Apply_IsPure()
    {
        var current = Stored();
        var before = Serialize(current);
        var merge = Merge("""{"Title":"New","Values":{"limits":{"cpu":8},"plan":null},"Labels":["x"]}""");
        var jsonPatch = JsonPatch("""[{"op":"add","path":"/Values/limits","value":{"cpu":8}},{"op":"add","path":"/Labels/-","value":"x"}]""");

        foreach (var patch in new[] { merge, jsonPatch })
        {
            var first = patch.Apply(current);
            var second = patch.Apply(current);

            Serialize(second).ShouldBe(Serialize(first));
            Serialize(current).ShouldBe(before);
        }
    }

    [Fact]
    public void Apply_DatesKeepTheirOffset()
    {
        var current = Execution() with { ValidFrom = At };

        var kept = Merge("""{"Title":"x"}""").Apply(current);
        var set = Merge("""{"ExpiresAt":"2026-12-01T08:00:00-05:00"}""").Apply(current);

        kept.ValidFrom.ShouldBe(At);
        kept.ValidFrom!.Value.Offset.ShouldBe(TimeSpan.FromHours(2));
        set.ExpiresAt!.Value.Offset.ShouldBe(TimeSpan.FromHours(-5));
        set.ExpiresAt.Value.UtcDateTime.ShouldBe(new DateTime(2026, 12, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Apply_NormalizesTheResult()
    {
        var current = Execution() with
        {
            Name = "wrong",
            SubjectName = "wrong",
            ContextKey = "cnt.wrong.wrong",
            UnitNames = new HashSet<string> { "nightly" },
            Created = AuditStamp.Of(AuditActor.Account("ada"), At),
        };

        var result = Merge("""{"Title":"x"}""").Apply(current).ShouldBeOfType<Execution>();

        result.AnnotationKey.ShouldBe("exe.northwind.payments.prod");
        result.Name.ShouldBe("prod");
        result.SubjectName.ShouldBe("northwind");
        result.ContextKey.ShouldBe("cnt.northwind.prod");
        result.UnitNames.ShouldBeEmpty();
        result.Created.ShouldBeNull();
        result.ProjectName.ShouldBe("billing");
        result.ViewName.ShouldBe("default");
    }

    [Fact]
    public void Apply_EmptyPatch_KeepsTheEditableContent()
    {
        var current = Stored();

        foreach (var patch in new[] { Merge("{}"), JsonPatch("[]") })
        {
            patch.IsEmpty.ShouldBeTrue();

            var result = patch.Apply(current);

            AnnotationAuditDiff.Compute(current, result, 256).IsEmpty.ShouldBeTrue();
        }
    }

    [Fact]
    public void Format_AndIsEmpty_DescribeThePatch()
    {
        Merge("""{"Title":null}""").Format.ShouldBe(AnnotationPatchFormat.MergePatch);
        Merge("""{"Title":null}""").IsEmpty.ShouldBeFalse();
        JsonPatch("""[{"op":"remove","path":"/Title"}]""").Format.ShouldBe(AnnotationPatchFormat.JsonPatch);
        JsonPatch("""[{"op":"remove","path":"/Title"}]""").IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Document_HasEveryEditableMember()
    {
        var document = AnnotationPatchDocument.From(Execution());

        document.Select(member => member.Key).ShouldBe(
            ["IsDisabled", "ValidFrom", "ExpiresAt", "TimeZone", "Title", "Description", "DocumentationLink", "Labels", "Values"]);
        document.ToJsonString().ShouldBe(
            """{"IsDisabled":null,"ValidFrom":null,"ExpiresAt":null,"TimeZone":null,"Title":null,"Description":null,"DocumentationLink":null,"Labels":[],"Values":{}}""");
    }

    [Fact]
    public void Document_OfAUnit_HasItsTypeAndDefinition()
    {
        var document = AnnotationPatchDocument.From(Unit() with { ValidFrom = At, Values = new Dictionary<string, object> { ["n"] = 1 } });

        document["UnitType"]!.GetValue<string>().ShouldBe("event");
        document["UnitDefinition"]!.GetValue<string>().ShouldBe("unknown");
        document["ValidFrom"]!.GetValue<string>().ShouldBe("2026-11-01T09:30:00+02:00");
        document["Values"]!.ToJsonString().ShouldBe("""{"n":1}""");
    }

    private static AnnotationPatch Merge(string json)
    {
        return AnnotationPatch.Merge(JsonNode.Parse(json)!.AsObject());
    }

    private static AnnotationPatch JsonPatch(string json)
    {
        return AnnotationPatch.Json(JsonNode.Parse(json)!.AsArray());
    }

    // Values as the annotation reads hold them: a JsonElement for each value.
    private static Dictionary<string, object> JsonValues(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
    }

    private static string? ValuesOf(Annotation annotation)
    {
        return annotation.Values is null ? null : JsonSerializer.Serialize(annotation.Values);
    }

    private static string Normalized(string json)
    {
        return JsonSerializer.Serialize(JsonValues(json));
    }

    private static string Serialize(Annotation annotation)
    {
        return JsonSerializer.Serialize(annotation, annotation.GetType());
    }

    private static Execution Execution()
    {
        var key = AnnotationKey.CreateExecution("northwind", "payments", "prod");
        return (Execution)AnnotationFactory.CreateDefault(key, "billing", "default");
    }

    private static Unit Unit()
    {
        var key = AnnotationKey.CreateUnit("payments", "nightly");
        return (Unit)AnnotationFactory.CreateDefault(key, "billing", "default");
    }

    // A stored execution with every editable member set, its relation arrays and its stamps.
    private static Execution Stored()
    {
        return Execution() with
        {
            IsDisabled = false,
            ValidFrom = At,
            ExpiresAt = At.AddDays(30),
            TimeZone = "Europe/Prague",
            Title = "Invoices",
            Description = "Monthly invoices.",
            DocumentationLink = "https://docs.example.com/invoices",
            Labels = ["eu", "prod"],
            Values = JsonValues("""{"plan":"pro","limits":{"cpu":2},"tags":["a","b"],"none":null}"""),
            UnitNames = new HashSet<string> { "nightly" },
            Created = AuditStamp.Of(AuditActor.Account("ada"), At),
            Modified = AuditStamp.Of(AuditActor.Account("ada"), At.AddHours(1)),
        };
    }
}
