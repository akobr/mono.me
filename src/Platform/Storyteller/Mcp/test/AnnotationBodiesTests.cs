using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class AnnotationBodiesTests
{
    [Theory]
    [InlineData("rst.billing", "Responsibility", """{"AnnotationKey":"rst.billing","Name":"billing"}""")]
    [InlineData("sbt.northwind", "Subject", """{"AnnotationKey":"sbt.northwind","Name":"northwind"}""")]
    [InlineData("unt.billing.nightly", "Unit", """{"AnnotationKey":"unt.billing.nightly","Name":"nightly","ResponsibilityKey":"rst.billing","ResponsibilityName":"billing"}""")]
    [InlineData("usg.northwind.billing", "Usage", """{"AnnotationKey":"usg.northwind.billing","Name":"billing","SubjectKey":"sbt.northwind","SubjectName":"northwind","ResponsibilityKey":"rst.billing","ResponsibilityName":"billing"}""")]
    [InlineData("cnt.northwind.prod", "Context", """{"AnnotationKey":"cnt.northwind.prod","Name":"prod","SubjectKey":"sbt.northwind","SubjectName":"northwind"}""")]
    [InlineData(
        "exe.northwind.billing.prod",
        "Execution",
        """{"AnnotationKey":"exe.northwind.billing.prod","Name":"prod","SubjectKey":"sbt.northwind","SubjectName":"northwind","ResponsibilityKey":"rst.billing","ResponsibilityName":"billing","ContextKey":"cnt.northwind.prod","ContextName":"prod","UsageKey":"usg.northwind.billing"}""")]
    [InlineData(
        "uxe.northwind.billing.prod.nightly",
        "UnitOfExecution",
        """{"AnnotationKey":"uxe.northwind.billing.prod.nightly","Name":"nightly","SubjectKey":"sbt.northwind","SubjectName":"northwind","ResponsibilityKey":"rst.billing","ResponsibilityName":"billing","ContextKey":"cnt.northwind.prod","ContextName":"prod","UnitKey":"unt.billing.nightly","UnitName":"nightly","UsageKey":"usg.northwind.billing","ExecutionKey":"exe.northwind.billing.prod"}""")]
    public void Create_BuildsTheSubtypeWithTheMembersOfTheKey(string key, string type, string expected)
    {
        var body = AnnotationBodies.Create(AnnotationKey.Parse(key), "main", "default");

        // serialized as the SDK sends it: the converter writes the discriminator of the subtype
        var json = JObject.Parse(JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

        json.Value<string>("AnnotationType").ShouldBe(type);
        json.Value<string>("ProjectName").ShouldBe("main");
        json.Value<string>("ViewName").ShouldBe("default");

        foreach (var property in JObject.Parse(expected).Properties())
        {
            json.Value<string>(property.Name).ShouldBe(property.Value.Value<string>(), property.Name);
        }

        AnnotationBodies.TypeName(body).ShouldBe(type);
    }

    [Fact]
    public void CopyEditable_CopiesTheEditableMembersOnly()
    {
        var stored = new Sdk.Unit
        {
            AnnotationKey = "unt.billing.nightly",
            Name = "nightly",
            Title = "Nightly",
            Description = "Runs at night.",
            DocumentationLink = "https://docs.example.com/nightly",
            Labels = ["batch"],
            Values = new Dictionary<string, object> { ["owner"] = "ops" },
            IsDisabled = true,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TimeZone = "Europe/Prague",
            UnitType = "time",
            UnitDefinition = "0 2 * * *",
            Created = new Sdk.AuditStamp(),
            Deletion = new Sdk.AnnotationDeletion(),
        };
        var body = (Sdk.Unit)AnnotationBodies.Create(AnnotationKey.Parse("unt.billing.nightly"), "main", "default");

        AnnotationBodies.CopyEditable(stored, body);

        body.Title.ShouldBe("Nightly");
        body.Description.ShouldBe("Runs at night.");
        body.DocumentationLink.ShouldBe("https://docs.example.com/nightly");
        body.Labels.ShouldBe(new[] { "batch" });
        body.Values!["owner"].ShouldBe("ops");
        body.IsDisabled.ShouldBe(true);
        body.ValidFrom.ShouldBe(stored.ValidFrom);
        body.ExpiresAt.ShouldBe(stored.ExpiresAt);
        body.TimeZone.ShouldBe("Europe/Prague");
        body.UnitType.ShouldBe("time");
        body.UnitDefinition.ShouldBe("0 2 * * *");
        body.Created.ShouldBeNull();
        body.Deletion.ShouldBeNull();
        body.Labels.ShouldNotBeSameAs(stored.Labels);
    }

    [Theory]
    [InlineData("northwind", "sbt.northwind")]
    [InlineData("northwind.billing", "usg.northwind.billing")]
    [InlineData("northwind.billing.prod", "exe.northwind.billing.prod")]
    [InlineData("northwind.billing.prod.nightly", "uxe.northwind.billing.prod.nightly")]
    [InlineData("rst.billing", "rst.billing")]
    [InlineData(" unt.billing.nightly ", "unt.billing.nightly")]
    [InlineData("cnt.northwind.prod", "cnt.northwind.prod")]
    [InlineData("abc.def", "usg.abc.def")]
    public void CompactLines_GiveTheKeysOfTheApi(string line, string key)
    {
        CompactAnnotationLines.ToKey(line).ToString().ShouldBe(key);
    }

    [Fact]
    public void CompactLine_WithATypeCodeThatIsNoKey_IsRefused()
    {
        // the API would read "sbt.northwind.billing" as the execution exe.sbt.northwind.billing
        var exception = Should.Throw<ToolInputException>(() => CompactAnnotationLines.ToKey("sbt.northwind.billing"));

        exception.Message.ShouldContain("exe.sbt.northwind.billing");
    }

    [Fact]
    public void CompactLine_WithTooManyNames_IsRefused()
    {
        Should.Throw<ToolInputException>(() => CompactAnnotationLines.ToKey("a.b.c.d.e")).Message.ShouldContain("has 5 names");
    }

    [Fact]
    public void Summary_ComputesTheStatusLikeTheApi()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        Status(new Sdk.Subject { IsDisabled = true, ValidFrom = now.AddDays(1) }, now).ShouldBe(AnnotationStatus.Disabled);
        Status(new Sdk.Subject { ValidFrom = now.AddDays(1), ExpiresAt = now.AddDays(-1) }, now).ShouldBe(AnnotationStatus.Scheduled);
        Status(new Sdk.Subject { ExpiresAt = now }, now).ShouldBe(AnnotationStatus.Expired);
        Status(new Sdk.Subject { ValidFrom = now.AddDays(-1), ExpiresAt = now.AddDays(1) }, now).ShouldBe(AnnotationStatus.Active);
    }

    private static AnnotationStatus Status(Sdk.Annotation annotation, DateTimeOffset now)
    {
        annotation.AnnotationKey = "sbt.northwind";
        annotation.Name = "northwind";
        return AnnotationSummary.From(annotation, now).Status;
    }
}
