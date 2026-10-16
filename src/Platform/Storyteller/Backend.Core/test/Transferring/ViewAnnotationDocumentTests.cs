using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewAnnotationDocumentTests
{
    private const int MaxValueLength = 256;

    // How the API writes an annotation in a GET response.
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly AuditActor Actor = AuditActor.Account("user_01", "Ada");

    [Fact]
    public void Write_HasTheEditableMembersWithoutNulls()
    {
        var annotation = Create("exe.northwind.payments.prod") with
        {
            Title = "Payments in production",
            Labels = ["eu"],
            Values = Values("""{"tier":"gold"}"""),
            TimeZone = "Europe/Prague",
        };

        Serialize(annotation).ShouldBe("""
            {
              "Title": "Payments in production",
              "Labels": [
                "eu"
              ],
              "Values": {
                "tier": "gold"
              },
              "TimeZone": "Europe/Prague"
            }

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Write_KeepsTheOrderOfTheMembers()
    {
        var annotation = Create("rst.payments") with
        {
            TimeZone = "UTC",
            ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ValidFrom = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            IsDisabled = false,
            Values = Values("""{"a":1}"""),
            Labels = [],
            DocumentationLink = "https://docs.example.com/payments",
            Description = "All payments.",
            Title = "Payments",
        };

        var document = ViewAnnotationDocument.Write(annotation);

        document.Properties().Select(property => property.Name).ShouldBe(
        [
            "Title", "Description", "DocumentationLink", "Labels", "Values", "IsDisabled", "ValidFrom", "ExpiresAt", "TimeZone",
        ]);
        Serialize(annotation).ShouldContain("\"ValidFrom\": \"2026-01-01T10:00:00+02:00\"");
        Serialize(annotation).ShouldContain("\"IsDisabled\": false");
    }

    [Fact]
    public void Write_LeavesOutWhatTheFileNameTheManifestAndThePlatformHold()
    {
        var annotation = Create("exe.northwind.payments.prod") with
        {
            Title = "Payments",
            Created = AuditStamp.Of(Actor, DateTimeOffset.UnixEpoch),
            Modified = AuditStamp.Of(Actor, DateTimeOffset.UnixEpoch),
            Deletion = new AnnotationDeletion { DeletionId = "d", RootKey = "exe.northwind.payments.prod", DeletedAt = DateTimeOffset.UnixEpoch, DeletedBy = Actor },
        };
        annotation = ((Execution)annotation) with { UnitNames = new HashSet<string> { "nightly" } };

        ViewAnnotationDocument.Write(annotation).Properties().Select(property => property.Name).ShouldBe(["Title"]);
    }

    [Fact]
    public void Write_HasTheUnitMembersOfUnitsOnly()
    {
        var unit = (Unit)Create("unt.payments.nightly") with { UnitType = "time", UnitDefinition = "0 2 * * *" };

        var document = ViewAnnotationDocument.Write(unit);

        document.Properties().Select(property => property.Name).ShouldBe(["UnitType", "UnitDefinition"]);
        document["UnitType"]!.Value<string>().ShouldBe("time");
        ViewAnnotationDocument.Write(Create("rst.payments")).Properties().ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_ComparesEqual()
    {
        var original = Create("usg.northwind.payments") with
        {
            Title = "Payments of Northwind",
            Description = "Line one.\nLine two ✓",
            DocumentationLink = "https://docs.example.com/usage",
            Labels = ["eu", "gold"],
            Values = Values("""
                {
                  "since": "2026-01-01T10:00:00.000Z",
                  "offset": "2026-01-01T10:00:00+02:00",
                  "ratio": 1.50,
                  "count": 12345678901234567890,
                  "nested": { "list": [1, "x", null, true], "at": "2026-10-14" },
                  "empty": {},
                  "none": null
                }
                """),
            IsDisabled = true,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            ExpiresAt = new DateTimeOffset(2027, 6, 30, 23, 59, 59, 123, TimeSpan.Zero),
            TimeZone = "Europe/Prague",
            Created = AuditStamp.Of(Actor, DateTimeOffset.UnixEpoch),
        };

        var read = ReadBack(original);

        AnnotationAuditDiff.Compute(original, read, MaxValueLength).IsEmpty.ShouldBeTrue();
        read.ShouldBeOfType<Usage>();
        read.ValidFrom!.Value.Offset.ShouldBe(TimeSpan.FromHours(2));
        read.ExpiresAt.ShouldBe(original.ExpiresAt);
        read.Labels.ShouldBe(["eu", "gold"]);

        // date-like strings stay the strings they were, as in the annotation reads
        var values = read.Values!;
        ((JsonElement)values["since"]).GetString().ShouldBe("2026-01-01T10:00:00.000Z");
        ((JsonElement)values["offset"]).GetString().ShouldBe("2026-01-01T10:00:00+02:00");
        ((JsonElement)values["nested"]).GetProperty("at").GetString().ShouldBe("2026-10-14");
    }

    [Fact]
    public void RoundTrip_OfAUnitComparesEqual()
    {
        var original = (Unit)Create("unt.payments.nightly") with { Title = "Nightly", UnitType = "time", UnitDefinition = "0 2 * * *" };

        var read = ReadBack(original);

        AnnotationAuditDiff.Compute(original, read, MaxValueLength).IsEmpty.ShouldBeTrue();
        read.ShouldBeOfType<Unit>().UnitDefinition.ShouldBe("0 2 * * *");
    }

    [Fact]
    public void RoundTrip_OfValuesBuiltInCodeComparesEqual()
    {
        var original = Create("rst.payments") with
        {
            Values = new Dictionary<string, object> { ["plan"] = "pro", ["seats"] = 5, ["settings"] = new { mode = "a" } },
        };

        AnnotationAuditDiff.Compute(original, ReadBack(original), MaxValueLength).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void RoundTrip_KeepsTheTextOfNumbersThatADoubleDoesNotHold()
    {
        // Cosmos DB answers 17 digits for doubles that 16 hold (Math.PI, 1/3); the API takes more digits and any
        // exponent; a double, as the reads of the other kinds parse numbers, would shorten them all
        const string json = """
            {
              "pi": 3.1415926535897931,
              "third": 0.33333333333333331,
              "precise": 0.30000000000000001,
              "long": 1.234567890123456789012345678901234567890,
              "huge": 1E+300,
              "beyond": 1e400,
              "tiny": 5E-324,
              "negative": -0.0,
              "list": [1.0000000000000001,{"deep":2.50}]
            }
            """;
        var original = Create("rst.payments") with { Values = Values(json) };

        var file = Serialize(original);
        var read = ReadBack(original);

        AnnotationAuditDiff.Compute(original, read, MaxValueLength).IsEmpty.ShouldBeTrue();
        var values = read.Values!;

        foreach (var name in new[] { "pi", "third", "precise", "long", "huge", "beyond", "tiny", "negative", "list" })
        {
            var text = ((JsonElement)original.Values![name]).GetRawText();
            ((JsonElement)values[name]).GetRawText().ShouldBe(text);

            if (name != "list")
            {
                file.ShouldContain($"\"{name}\": {text}");
            }
        }

        file.ShouldContain("""
                "list": [
                  1.0000000000000001,
                  {
                    "deep": 2.50
                  }
                ]
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Parse_KeepsTheTextOfNumbers()
    {
        var document = Parse("""{"Values":{"pi":3.1415926535897931,"count":12345678901234567890123,"list":[0.30000000000000001]}}""");

        var read = ViewAnnotationDocument.TryRead(document, Key("rst.payments"), out var annotation, out var errorCode, out var message);

        read.ShouldBeTrue($"{errorCode}: {message}");
        ((JsonElement)annotation!.Values!["pi"]).GetRawText().ShouldBe("3.1415926535897931");
        ((JsonElement)annotation.Values["count"]).GetRawText().ShouldBe("12345678901234567890123");
        ((JsonElement)annotation.Values["list"]).GetRawText().ShouldBe("[0.30000000000000001]");
    }

    [Theory]
    [InlineData("rst.payments")]
    [InlineData("sbt.northwind")]
    [InlineData("usg.northwind.payments")]
    [InlineData("cnt.northwind.prod")]
    [InlineData("exe.northwind.payments.prod")]
    [InlineData("unt.payments.nightly")]
    [InlineData("uxe.northwind.payments.prod.nightly")]
    public void TryRead_IgnoresTheServerOwnedMembersOfAResponse(string key)
    {
        var stored = Create(key) with
        {
            Title = "From the API",
            Labels = ["eu"],
            Values = Values("""{"tier":"gold"}"""),
            Created = AuditStamp.Of(Actor, DateTimeOffset.UnixEpoch),
            Modified = AuditStamp.Of(Actor, DateTimeOffset.UnixEpoch.AddDays(1)),
            Deletion = new AnnotationDeletion { DeletionId = "d", RootKey = key, DeletedAt = DateTimeOffset.UnixEpoch, DeletedBy = Actor, IsRoot = true },
        };

        // the body of GET …/annotations/{key}, saved as a file
        var response = JsonSerializer.Serialize((object)stored, ResponseOptions);
        response.ShouldContain("\"AnnotationKey\"");
        response.ShouldContain("\"Created\"");

        var read = Read(key, response);

        AnnotationAuditDiff.Compute(stored, read, MaxValueLength).IsEmpty.ShouldBeTrue();
        read.Created.ShouldBeNull();
        read.Deletion.ShouldBeNull();
        read.ProjectName.ShouldBe("main");
        read.ViewName.ShouldBe("default");
        read.AnnotationKey.ShouldBe(key);
    }

    [Fact]
    public void TryRead_DerivesTheKeyMembersFromTheFileName()
    {
        // the names in the file are server-owned and ignored
        var read = Read("usg.northwind.payments", """{"SubjectKey":"sbt.other","SubjectName":"other","ContextNames":["prod"],"Name":"x"}""");

        var usage = read.ShouldBeOfType<Usage>();
        usage.SubjectKey.ShouldBe("sbt.northwind");
        usage.ResponsibilityKey.ShouldBe("rst.payments");
        usage.Name.ShouldBe("payments");
        usage.ContextNames.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"AnnotationKey":"rst.payments"}""")]
    [InlineData("""{"AnnotationKey":"RST.payments"}""")]
    [InlineData("""{"annotationKey":"rst.payments","Title":"x"}""")]
    public void TryRead_AcceptsTheKeyOfTheFileName(string json)
    {
        ViewAnnotationDocument.TryRead(JObject.Parse(json), Key("rst.payments"), out _, out var errorCode, out _).ShouldBeTrue(errorCode);
    }

    [Theory]
    [InlineData("""{"AnnotationKey":"rst.invoices"}""")]
    [InlineData("""{"AnnotationKey":"rst.Payments"}""")]
    [InlineData("""{"AnnotationKey":"sbt.payments"}""")]
    [InlineData("""{"AnnotationKey":"not a key"}""")]
    [InlineData("""{"AnnotationKey":null}""")]
    [InlineData("""{"AnnotationKey":42}""")]
    public void TryRead_RejectsAnotherAnnotationKey(string json)
    {
        var (errorCode, message) = Fail("rst.payments", json);

        errorCode.ShouldBe(ErrorCodes.InvalidDocument);
        message.ShouldContain("AnnotationKey");
        message.ShouldContain("rst.payments");
    }

    [Theory]
    [InlineData("rst.payments", """{"Retries":3}""", "Retries")]
    [InlineData("rst.payments", """{"Title":"x","$patch":[]}""", "$patch")]
    [InlineData("rst.payments", """{"$remove":["Title"]}""", "$remove")]
    [InlineData("rst.payments", """{"UnitType":"time"}""", "UnitType")]
    [InlineData("sbt.northwind", """{"UnitDefinition":"x"}""", "UnitDefinition")]
    [InlineData("unt.payments.nightly", """{"UnitNames":[]}""", "UnitNames")]
    [InlineData("rst.payments", """{"SubjectKey":"sbt.x"}""", "SubjectKey")]
    public void TryRead_RejectsUnknownMembers(string key, string json, string member)
    {
        var (errorCode, message) = Fail(key, json);

        errorCode.ShouldBe(ErrorCodes.InvalidDocument);
        message.ShouldBe($"Unknown member '{member}'.");
    }

    [Fact]
    public void TryRead_ListsEveryUnknownMember()
    {
        // a configuration document read as an annotation, for example from an archive of PR #88
        var (errorCode, message) = Fail("rst.payments", """{"retries":3,"endpoint":"https://x","Title":"x"}""");

        errorCode.ShouldBe(ErrorCodes.InvalidDocument);
        message.ShouldBe("Unknown members 'retries', 'endpoint'.");
    }

    [Fact]
    public void TryRead_EmptyDocumentIsTheKeyWithoutContent()
    {
        var read = Read("rst.payments", "{}");

        read.ShouldBeOfType<Responsibility>();
        read.AnnotationKey.ShouldBe("rst.payments");
        read.Title.ShouldBeNull();
        read.Labels.ShouldBeNull();
        read.Values.ShouldBeNull();
        read.IsDisabled.ShouldBeNull();
        read.ValidFrom.ShouldBeNull();
        read.TimeZone.ShouldBeNull();
    }

    [Fact]
    public void TryRead_ClearsMissingMembers()
    {
        var stored = Create("rst.payments") with { Title = "Payments", Labels = ["eu"], TimeZone = "UTC", IsDisabled = true };

        var read = Read("rst.payments", """{"Title":"Payments","Labels":null}""");

        var changes = AnnotationAuditDiff.Compute(stored, read, MaxValueLength);
        changes.Fields.Select(field => field.Field).ShouldBe(["IsDisabled", "TimeZone"], ignoreOrder: true);
        changes.Labels!.Removed.ShouldBe(["eu"]);
    }

    [Fact]
    public void TryRead_GivesAUnitTheDefaultsOfACreate()
    {
        var unit = Read("unt.payments.nightly", "{}").ShouldBeOfType<Unit>();
        unit.UnitType.ShouldBe(AnnotationFactory.DefaultUnitType);
        unit.UnitDefinition.ShouldBe(AnnotationFactory.DefaultUnitDefinition);

        var partial = Read("unt.payments.nightly", """{"UnitType":"time","UnitDefinition":null}""").ShouldBeOfType<Unit>();
        partial.UnitType.ShouldBe("time");
        partial.UnitDefinition.ShouldBe("unknown");
    }

    [Fact]
    public void TryRead_MatchesMemberNamesInAnyCase()
    {
        var read = Read("rst.payments", """{"title":"Payments","LABELS":["eu"],"timeZone":"UTC"}""");

        read.Title.ShouldBe("Payments");
        read.Labels.ShouldBe(["eu"]);
        read.TimeZone.ShouldBe("UTC");
    }

    [Fact]
    public void TryRead_RejectsAMemberThatIsInTheFileTwice()
    {
        var (errorCode, message) = Fail("rst.payments", """{"Title":"a","title":"b"}""");

        errorCode.ShouldBe(ErrorCodes.InvalidDocument);
        message.ShouldContain("Title");
    }

    [Theory]
    [InlineData("""{"Title":5}""")]
    [InlineData("""{"Labels":"eu"}""")]
    [InlineData("""{"Labels":[1]}""")]
    [InlineData("""{"Values":[]}""")]
    [InlineData("""{"IsDisabled":"yes"}""")]
    [InlineData("""{"ValidFrom":"tomorrow"}""")]
    [InlineData("""{"ExpiresAt":17}""")]
    public void TryRead_RejectsValuesOfAWrongType(string json)
    {
        var (errorCode, message) = Fail("rst.payments", json);

        errorCode.ShouldBe(ErrorCodes.InvalidDocument);
        message.ShouldStartWith("The file is not a valid annotation: ");
    }

    [Fact]
    public void TryRead_AppliesTheAnnotationRules()
    {
        var (errorCode, message) = Fail("rst.payments", $$"""{"Title":"{{new string('x', AnnotationLimits.TitleMaxLength + 1)}}"}""");
        errorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        message.ShouldContain("rst.payments");

        Fail("rst.payments", """{"DocumentationLink":"ftp://docs"}""").ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
    }

    [Fact]
    public void Parse_AllowsCommentsAndKeepsDatesAsStrings()
    {
        var document = Parse("// exported\n{\"ValidFrom\":\"2026-01-01T10:00:00+02:00\" /* inline */, \"Values\":{\"at\":\"2026-10-14T01:30:12Z\"}}\n");

        document["ValidFrom"]!.Type.ShouldBe(JTokenType.String);
        document["Values"]!["at"]!.Type.ShouldBe(JTokenType.String);
        document["Values"]!["at"]!.Value<string>().ShouldBe("2026-10-14T01:30:12Z");
    }

    [Fact]
    public void Parse_AllowsTrailingCommasAsTheOtherKindsDo()
    {
        const string json = """{"Labels":["eu",],"Values":{"a":1,},}""";

        Parse(json)["Labels"]!.Values<string>().ShouldBe(["eu"]);
        ViewDocumentJson.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)))["Labels"]!.Values<string>().ShouldBe(["eu"]);
    }

    [Theory]
    [InlineData("""{"Title":"a","Title":"b"}""")]
    [InlineData("""{"Values":{"a":1,"a":2}}""")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("""{"Title":"a"} {}""")]
    [InlineData("{\"Title\":\"a\"}\n// end\n]")]
    [InlineData("""{"Values":{"n":NaN}}""")]
    [InlineData("""{"Title":"\uD800"}""")]
    public void Parse_RejectsWhatIsNotOneObjectWithUniqueMembers(string json)
    {
        Should.Throw<Newtonsoft.Json.JsonException>(() => Parse(json));
    }

    private static Annotation Create(string key)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "main", "default");
    }

    private static FullKey Key(string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), "acme", "main", "default");
    }

    // Values as the annotation reads hold them: JsonElement.
    private static IReadOnlyDictionary<string, object> Values(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
    }

    private static string Serialize(Annotation annotation)
    {
        return Encoding.UTF8.GetString(ViewDocumentJson.Serialize(ViewAnnotationDocument.Write(annotation)));
    }

    private static JObject Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ViewAnnotationDocument.Parse(stream);
    }

    // Exported as a file and read back from its bytes, as an import reads it.
    private static Annotation ReadBack(Annotation annotation)
    {
        using var stream = new MemoryStream(ViewDocumentJson.Serialize(ViewAnnotationDocument.Write(annotation)));
        var document = ViewAnnotationDocument.Parse(stream);

        ViewAnnotationDocument.TryRead(document, Key(annotation.AnnotationKey), out var read, out var errorCode, out var message).ShouldBeTrue($"{errorCode}: {message}");
        return read;
    }

    private static Annotation Read(string key, string json)
    {
        ViewAnnotationDocument.TryRead(Parse(json), Key(key), out var read, out var errorCode, out var message).ShouldBeTrue($"{errorCode}: {message}");
        return read;
    }

    private static (string ErrorCode, string Message) Fail(string key, string json)
    {
        ViewAnnotationDocument.TryRead(Parse(json), Key(key), out var read, out var errorCode, out var message).ShouldBeFalse();
        read.ShouldBeNull();
        return (errorCode, message);
    }
}
