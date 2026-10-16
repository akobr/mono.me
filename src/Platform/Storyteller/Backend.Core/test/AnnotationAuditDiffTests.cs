using System.Text.Json;
using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class AnnotationAuditDiffTests
{
    private const int MaxValueLength = 256;

    [Fact]
    public void Compute_NoChange_IsEmpty()
    {
        var annotation = Usage() with { Title = "Invoices", Labels = ["eu"], Values = Values(("plan", "pro")) };

        AnnotationAuditDiff.Compute(annotation, annotation with { }, MaxValueLength).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Compute_DefaultProperties_KeepPreviousAndNewValue()
    {
        var validFrom = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var before = Usage() with { Title = "Old", TimeZone = "Europe/Prague", IsDisabled = null };
        var after = Usage() with
        {
            Title = "New",
            DocumentationLink = "https://docs.example.com",
            TimeZone = "UTC",
            IsDisabled = true,
            ValidFrom = validFrom,
        };

        var changes = AnnotationAuditDiff.Compute(before, after, MaxValueLength);

        changes.Fields.ShouldContain(f => f.Field == "Title" && (string)f.Previous! == "Old" && (string)f.Value! == "New");
        changes.Fields.ShouldContain(f => f.Field == "DocumentationLink" && f.Previous == null && (string)f.Value! == "https://docs.example.com");
        changes.Fields.ShouldContain(f => f.Field == "TimeZone" && (string)f.Previous! == "Europe/Prague" && (string)f.Value! == "UTC");
        changes.Fields.ShouldContain(f => f.Field == "IsDisabled" && (bool)f.Previous! == false && (bool)f.Value! == true);
        changes.Fields.ShouldContain(f => f.Field == "ValidFrom" && f.Previous == null && (DateTimeOffset)f.Value! == validFrom);
        changes.Fields.Count.ShouldBe(5);
    }

    [Fact]
    public void Compute_NullAndFalseOrEmpty_AreTheSame()
    {
        var before = Usage() with { IsDisabled = null, Title = null };
        var after = Usage() with { IsDisabled = false, Title = string.Empty };

        AnnotationAuditDiff.Compute(before, after, MaxValueLength).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Compute_Description_IsOnlyFlagged()
    {
        var changes = AnnotationAuditDiff.Compute(Usage() with { Description = "secret old" }, Usage() with { Description = "secret new" }, MaxValueLength);

        var field = changes.Fields.ShouldHaveSingleItem();
        field.Field.ShouldBe("Description");
        field.Previous.ShouldBeNull();
        field.Value.ShouldBeNull();
    }

    [Fact]
    public void Compute_Unit_RecordsTheTypeAndFlagsTheDefinition()
    {
        var before = Unit() with { UnitType = "event", UnitDefinition = "a" };
        var after = Unit() with { UnitType = "timer", UnitDefinition = "0 * * * *" };

        var changes = AnnotationAuditDiff.Compute(before, after, MaxValueLength);

        changes.Fields.ShouldContain(f => f.Field == "UnitType" && (string)f.Previous! == "event" && (string)f.Value! == "timer");
        changes.Fields.ShouldContain(f => f.Field == "UnitDefinition" && f.Previous == null && f.Value == null);
    }

    [Fact]
    public void Compute_LongStrings_AreCutOnBothSides()
    {
        var before = Usage() with { DocumentationLink = "https://a.example.com/" + new string('a', 400) };
        var after = Usage() with { DocumentationLink = "https://b.example.com/" + new string('b', 400) };

        var field = AnnotationAuditDiff.Compute(before, after, MaxValueLength).Fields.ShouldHaveSingleItem();

        ((string)field.Previous!).Length.ShouldBe(MaxValueLength);
        ((string)field.Value!).Length.ShouldBe(MaxValueLength);
        field.IsPreviousTruncated.ShouldBeTrue();
        field.IsValueTruncated.ShouldBeTrue();
    }

    [Fact]
    public void Compute_Labels_ListAddedAndRemoved()
    {
        var changes = AnnotationAuditDiff.Compute(Usage() with { Labels = ["eu", "us"] }, Usage() with { Labels = ["eu", "apac", "EU"] }, MaxValueLength);

        changes.Labels.ShouldNotBeNull();
        changes.Labels.Added.ShouldBe(["apac", "EU"]);
        changes.Labels.Removed.ShouldBe(["us"]);
    }

    [Fact]
    public void Compute_Values_KeepPrimitivesAndOnlyTheKindOfObjects()
    {
        var before = Usage() with
        {
            Values = Values(("plan", "pro"), ("seats", 5), ("settings", new { mode = "a" }), ("legacy", true)),
        };
        var after = Usage() with
        {
            Values = Values(("plan", "enterprise"), ("seats", 5), ("settings", new { mode = "b" }), ("region", "eu")),
        };

        var values = AnnotationAuditDiff.Compute(before, after, MaxValueLength).Values;

        values.Select(v => v.Key).ShouldBe(["legacy", "plan", "region", "settings"]);

        var plan = values.Single(v => v.Key == "plan");
        plan.Change.ShouldBe(AnnotationValueChangeKind.Changed);
        plan.Previous.ShouldBe("pro");
        plan.Value.ShouldBe("enterprise");

        var settings = values.Single(v => v.Key == "settings");
        settings.PreviousKind.ShouldBe(AnnotationValueKind.Object);
        settings.ValueKind.ShouldBe(AnnotationValueKind.Object);
        settings.Previous.ShouldBeNull();
        settings.Value.ShouldBeNull();

        var region = values.Single(v => v.Key == "region");
        region.Change.ShouldBe(AnnotationValueChangeKind.Added);
        region.PreviousKind.ShouldBeNull();
        region.Value.ShouldBe("eu");

        var legacy = values.Single(v => v.Key == "legacy");
        legacy.Change.ShouldBe(AnnotationValueChangeKind.Removed);
        legacy.Previous.ShouldBe(true);
        legacy.ValueKind.ShouldBeNull();
    }

    [Fact]
    public void Compute_ValuesFromJsonAndFromObjects_CompareByContent()
    {
        var fromJson = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"seats":5,"settings":{"mode":"a"}}""")!;
        var before = Usage() with { Values = fromJson };
        var after = Usage() with { Values = Values(("seats", 5), ("settings", new { mode = "a" })) };

        AnnotationAuditDiff.Compute(before, after, MaxValueLength).IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("3.1415926535897931", "3.141592653589793")]
    [InlineData("0.33333333333333331", "0.3333333333333333")]
    [InlineData("1e+300", "1E+300")]
    [InlineData("1.50", "1.5")]
    [InlineData("0.30000000000000001", "0.3")]
    [InlineData("100", "1E2")]
    [InlineData("-0.0", "0")]
    [InlineData("""{"a":[1,{"b":3.1415926535897931}],"c":"x"}""", """{"c":"x","a":[1,{"b":3.141592653589793}]}""")]
    public void Compute_NumbersOfTheSameDouble_CompareEqual(string before, string after)
    {
        // Cosmos DB writes a double with 17 digits in a point read and with the fewest digits in a query
        var changes = AnnotationAuditDiff.Compute(
            Usage() with { Values = JsonValues(before) },
            Usage() with { Values = JsonValues(after) },
            MaxValueLength);

        changes.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0.1", "0.2")]
    [InlineData("3.1415926535897931", "3.14159265358979")]
    [InlineData("9007199254740993", "9007199254740992")]
    [InlineData("1", "\"1\"")]
    [InlineData("[1.5,2.5]", "[1.5]")]
    [InlineData("""{"a":1.5}""", """{"a":1.5,"b":1}""")]
    [InlineData("""{"a":1.5}""", """{"b":1.5}""")]
    public void Compute_OtherNumbersAndShapes_AreChanges(string before, string after)
    {
        var changes = AnnotationAuditDiff.Compute(
            Usage() with { Values = JsonValues(before) },
            Usage() with { Values = JsonValues(after) },
            MaxValueLength);

        changes.Values.ShouldHaveSingleItem().Change.ShouldBe(AnnotationValueChangeKind.Changed);
    }

    [Fact]
    public void Compute_KindChange_KeepsThePrimitiveSide()
    {
        var changes = AnnotationAuditDiff.Compute(
            Usage() with { Values = Values(("limit", 10)) },
            Usage() with { Values = Values(("limit", new[] { 1, 2 })) },
            MaxValueLength);

        var limit = changes.Values.ShouldHaveSingleItem();
        limit.PreviousKind.ShouldBe(AnnotationValueKind.Number);
        limit.Previous.ShouldBeOfType<JsonElement>().GetInt32().ShouldBe(10);
        limit.ValueKind.ShouldBe(AnnotationValueKind.Array);
        limit.Value.ShouldBeNull();
    }

    private static Dictionary<string, object> Values(params (string Key, object Value)[] values)
    {
        return values.ToDictionary(value => value.Key, value => value.Value);
    }

    // One value "v", as the annotation reads hold it.
    private static Dictionary<string, object> JsonValues(string json)
    {
        return new Dictionary<string, object> { ["v"] = JsonSerializer.Deserialize<JsonElement>(json) };
    }

    private static Usage Usage()
    {
        var key = AnnotationKey.CreateUsage("northwind", "invoicing");
        return (Usage)AnnotationFactory.CreateDefault(key, "billing", "default");
    }

    private static Unit Unit()
    {
        var key = AnnotationKey.CreateUnit("invoicing", "export");
        return (Unit)AnnotationFactory.CreateDefault(key, "billing", "default");
    }
}
