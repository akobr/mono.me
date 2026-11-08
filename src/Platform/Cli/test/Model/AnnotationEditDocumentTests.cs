using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.Model;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

using CoreUnit = _42.Platform.Storyteller.Unit;
using SdkModel = _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.UnitTests.Model;

public class AnnotationEditDocumentTests
{
    private const string ExecutionKey = "exe.northwind.billing.prod";
    private const string UnitKey = "unt.billing.export";

    private static readonly string[] Members =
    [
        "Title", "Description", "DocumentationLink", "Labels", "Values", "IsDisabled", "ValidFrom", "ExpiresAt", "TimeZone",
    ];

    private static readonly AnnotationKey Execution = AnnotationKey.Parse(ExecutionKey);
    private static readonly AnnotationKey UnitAnnotation = AnnotationKey.Parse(UnitKey);

    [Theory]
    [InlineData("rst.billing")]
    [InlineData("sbt.northwind")]
    [InlineData("usg.northwind.billing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData(ExecutionKey)]
    [InlineData("uxe.northwind.billing.prod.export")]
    public void Create_WithoutAnAnnotation_ListsEveryMemberEmpty(string key)
    {
        var document = AnnotationEditDocument.Create(AnnotationKey.Parse(key), null);

        document.Properties().Select(property => property.Name).ShouldBe(Members);
        document["Labels"].ShouldBeOfType<JArray>().ShouldBeEmpty();
        document["Values"].ShouldBeOfType<JObject>().Count.ShouldBe(0);

        foreach (var name in Members.Except(["Labels", "Values"]))
        {
            document[name]!.Type.ShouldBe(JTokenType.Null, name);
        }
    }

    [Fact]
    public void Create_OfANewUnit_HasTheDefaultsOfACreate()
    {
        var document = AnnotationEditDocument.Create(UnitAnnotation, null);

        document.Properties().Select(property => property.Name).ShouldBe([.. Members, "UnitType", "UnitDefinition"]);
        document["UnitType"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitType);
        document["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public void Create_FromAStoredAnnotation_HasItsMembers_AndItsDatesAsStrings()
    {
        var document = AnnotationEditDocument.Create(Execution, Stored());

        document["Title"]!.Value<string>().ShouldBe("Invoicing");
        document["Description"]!.Value<string>().ShouldBe("What it does");
        document["DocumentationLink"]!.Value<string>().ShouldBe("https://example.com/runbook");
        document["Labels"]!.Values<string>().ShouldBe(["eu", "prod"]);
        document["IsDisabled"]!.Value<bool>().ShouldBeTrue();
        document["ValidFrom"]!.Type.ShouldBe(JTokenType.String);
        document["ValidFrom"]!.Value<string>().ShouldBe("2026-12-01T00:00:00+01:00");
        document["ExpiresAt"]!.Value<string>().ShouldBe("2027-01-01T10:30:00.5+00:00");
        document["TimeZone"]!.Value<string>().ShouldBe("Europe/Prague");
        document["Values"]!["tier"]!.Value<string>().ShouldBe("gold");
        document["Values"]!["since"]!.Type.ShouldBe(JTokenType.String);
        document["Values"]!["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00.000+02:00");
        document["Values"]!["nested"]!["at"]!.Value<int>().ShouldBe(1);
        document["Values"]!["none"]!.Type.ShouldBe(JTokenType.Null);
        document["Values"]!["list"]!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""[1,"two",null]""");
        document["Values"]!["big"]!.Value<long>().ShouldBe(9_007_199_254_740_993L);
        document.ContainsKey("UnitType").ShouldBeFalse();
        document.ContainsKey("AnnotationKey").ShouldBeFalse();
        document.ContainsKey("Modified").ShouldBeFalse();
    }

    [Fact]
    public void Create_FromAStoredUnit_HasItsTypeAndDefinition()
    {
        var document = AnnotationEditDocument.Create(UnitAnnotation, new SdkModel.Unit
        {
            AnnotationKey = UnitKey,
            Name = "export",
            ProjectName = "proj1",
            ViewName = "default",
            UnitType = "time",
            UnitDefinition = "0 2 * * *",
        });

        document["UnitType"]!.Value<string>().ShouldBe("time");
        document["UnitDefinition"]!.Value<string>().ShouldBe("0 2 * * *");
    }

    [Fact]
    public void Create_OfStoredDuplicateLabels_IsTheNormalizedDocument()
    {
        var stored = Stored();
        stored.Labels = ["eu", "prod", "eu"];

        var document = AnnotationEditDocument.Create(Execution, stored);

        document["Labels"]!.Values<string>().ShouldBe(["eu", "prod"]);
        JToken.DeepEquals(AnnotationEditDocument.Normalize(Execution, document), document).ShouldBeTrue();
    }

    // A document parsed with dates, not the editor's file: a DateTime has no explicit offset, a DateTimeOffset has one.
    [Fact]
    public void Validate_OfDateTokens_TakesOnlyThoseWithAnOffset()
    {
        AnnotationEditDocument.Validate(Execution, new JObject { ["ValidFrom"] = new JValue(new DateTime(2026, 12, 1)) })
            .ShouldStartWith("'ValidFrom' must be a date and time with an offset");
        AnnotationEditDocument.Validate(Execution, new JObject { ["ValidFrom"] = new JValue(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(1))) })
            .ShouldBeNull();
    }

    [Fact]
    public void Create_ThenNormalize_IsTheSameDocument()
    {
        var document = AnnotationEditDocument.Create(Execution, Stored());

        JToken.DeepEquals(AnnotationEditDocument.Normalize(Execution, document), document).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{ "Title": 5 }""", "'Title' must be a string or null.")]
    [InlineData("""{ "Description": true }""", "'Description' must be a string or null.")]
    [InlineData("""{ "DocumentationLink": [] }""", "'DocumentationLink' must be a string or null.")]
    [InlineData("""{ "TimeZone": {} }""", "'TimeZone' must be a string or null.")]
    [InlineData("""{ "IsDisabled": "yes" }""", "'IsDisabled' must be true, false or null.")]
    [InlineData("""{ "ValidFrom": "soon" }""", "'ValidFrom' must be a date and time with an offset, for example \"2026-12-01T00:00:00+01:00\", or null.")]
    [InlineData("""{ "ValidFrom": "2026-12-01" }""", "'ValidFrom' must be a date and time with an offset")]
    [InlineData("""{ "ValidFrom": "2026-12-01T00:00:00" }""", "'ValidFrom' must be a date and time with an offset")]
    [InlineData("""{ "ValidFrom": "01.12.2026 00:00 +01:00" }""", "'ValidFrom' must be a date and time with an offset")]
    [InlineData("""{ "ExpiresAt": "12/01/2026 00:00:00 +01:00" }""", "'ExpiresAt' must be a date and time with an offset")]
    [InlineData("""{ "ExpiresAt": 20261201 }""", "'ExpiresAt' must be a date and time")]
    [InlineData("""{ "Labels": "eu" }""", "'Labels' must be an array of strings, or null.")]
    [InlineData("""{ "Labels": ["eu", 1] }""", "'Labels' must be an array of strings, or null.")]
    [InlineData("""{ "Labels": ["eu", null] }""", "'Labels' must be an array of strings, or null.")]
    [InlineData("""{ "Values": [] }""", "'Values' must be an object, or null.")]
    [InlineData("""{ "Title": "a", "title": "b" }""", "The member 'Title' is in the document twice.")]
    [InlineData("""{ "UnitType": "time" }""", "'UnitType' is a member of units only.")]
    [InlineData("""{ "unitDefinition": "x" }""", "'UnitDefinition' is a member of units only.")]
    [InlineData("""{ "Name": "other" }""", "'Name' cannot be edited: the key decides the type, the names and the relations, and the server keeps the stamps.")]
    [InlineData("""{ "AnnotationType": "Execution" }""", "'AnnotationType' cannot be edited")]
    [InlineData("""{ "projectName": "other" }""", "'projectName' cannot be edited")]
    [InlineData("""{ "UnitNames": [] }""", "'UnitNames' cannot be edited")]
    [InlineData("""{ "Deletion": null }""", "'Deletion' cannot be edited")]
    [InlineData("""{ "AdditionalProperties": {} }""", "Unknown member 'AdditionalProperties'.")]
    [InlineData("""{ "AnnotationKey": "exe.northwind.billing.prod" }""", "'AnnotationKey' cannot be edited")]
    [InlineData("""{ "UsageKey": "usg.northwind.billing" }""", "'UsageKey' cannot be edited")]
    [InlineData("""{ "Modified": null }""", "'Modified' cannot be edited")]
    [InlineData("""{ "Titel": "a" }""", "Unknown member 'Titel'. The members are Title, Description, DocumentationLink, Labels, Values, IsDisabled, ValidFrom, ExpiresAt, TimeZone.")]
    public void Validate_NamesTheProblem(string json, string problem)
    {
        AnnotationEditDocument.Validate(Execution, Document(json)).ShouldStartWith(problem);
    }

    [Fact]
    public void Validate_OfAUnit_TakesItsMembers_AndRejectsTheirWrongTypes()
    {
        AnnotationEditDocument.Validate(UnitAnnotation, Document("""{ "UnitType": "time", "UnitDefinition": null }""")).ShouldBeNull();
        AnnotationEditDocument.Validate(UnitAnnotation, Document("""{ "UnitType": 1 }""")).ShouldBe("'UnitType' must be a string or null.");
        AnnotationEditDocument.Validate(UnitAnnotation, Document("""{ "ResponsibilityKey": "rst.billing" }""")).ShouldStartWith("'ResponsibilityKey' cannot be edited");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "title": "Lower case", "LABELS": ["eu"], "values": { "a": [1, { "b": null }] } }""")]
    [InlineData("""{ "Title": null, "Labels": null, "Values": null, "IsDisabled": false }""")]
    [InlineData("""{ "ValidFrom": "2026-12-01T00:00:00-05:00", "ExpiresAt": "2026-12-01T00:00:00Z" }""")]
    [InlineData("""{ "DocumentationLink": "http://example.com/a?b=c#d", "Description": "" }""")]
    [InlineData("""{ "DocumentationLink": "" }""")]
    [InlineData("""{ "ValidFrom": "2026-12-01T00:00:00.1234567+05:30" }""")]
    [InlineData("""{ "ValidFrom": "2026-12-01T00:00:00+01" }""")]
    public void Validate_AcceptsAValidDocument(string json)
    {
        AnnotationEditDocument.Validate(Execution, Document(json)).ShouldBeNull();
    }

    // AnnotationRules.GetProblems, in its wording, at the limits of AnnotationLimits and beyond.
    [Theory]
    [InlineData("Title", 200, null)]
    [InlineData("Title", 201, "Title has 201 characters, at most 200 are allowed.")]
    [InlineData("Description", 10_000, null)]
    [InlineData("Description", 10_001, "Description has 10001 characters, at most 10000 are allowed.")]
    public void Validate_ChecksTheLengths(string member, int length, string? problem)
    {
        AnnotationEditDocument.Validate(Execution, new JObject { [member] = new string('a', length) }).ShouldBe(problem);
    }

    [Theory]
    [InlineData("https://example.com/runbook", null)]
    [InlineData("wiki.example.com/runbook", "DocumentationLink must be an absolute http or https URL.")]
    [InlineData("ftp://example.com/runbook", "DocumentationLink must be an absolute http or https URL.")]
    [InlineData("https://example.com/run book", "DocumentationLink must be an absolute http or https URL.")]
    [InlineData("https://example.com/runbook ", "DocumentationLink must be an absolute http or https URL.")]
    public void Validate_ChecksTheDocumentationLink(string link, string? problem)
    {
        AnnotationEditDocument.Validate(Execution, new JObject { ["DocumentationLink"] = link }).ShouldBe(problem);
    }

    [Fact]
    public void Validate_ChecksTheLengthOfTheDocumentationLink()
    {
        var link = "https://example.com/" + new string('a', 2048 - 20);

        AnnotationEditDocument.Validate(Execution, new JObject { ["DocumentationLink"] = link }).ShouldBeNull();
        AnnotationEditDocument.Validate(Execution, new JObject { ["DocumentationLink"] = link + "a" })
            .ShouldBe("DocumentationLink has 2049 characters, at most 2048 are allowed.");
    }

    [Fact]
    public void Check_GivesTheProblem_OrTheNormalizedDocument()
    {
        AnnotationEditDocument.Check(Execution, Document("""{ "Titel": "a" }""")).Problem!.ShouldStartWith("Unknown member 'Titel'.");
        AnnotationEditDocument.Check(Execution, Document("""{ "Titel": "a" }""")).Document.ShouldBeNull();

        var valid = AnnotationEditDocument.Check(Execution, Document("""{ "title": "Billing" }"""));

        valid.Problem.ShouldBeNull();
        JToken.DeepEquals(valid.Document, AnnotationEditDocument.Normalize(Execution, Document("""{ "Title": "Billing" }"""))).ShouldBeTrue();
    }

    [Fact]
    public void Normalize_FillsTheMissingMembers_InTheirOrderAndNames()
    {
        var normalized = AnnotationEditDocument.Normalize(Execution, Document("""{ "timeZone": "UTC", "title": "Billing" }"""));

        normalized.Properties().Select(property => property.Name).ShouldBe(Members);
        normalized["Title"]!.Value<string>().ShouldBe("Billing");
        normalized["TimeZone"]!.Value<string>().ShouldBe("UTC");
        normalized["Description"]!.Type.ShouldBe(JTokenType.Null);
        normalized["Labels"].ShouldBeOfType<JArray>().ShouldBeEmpty();
        normalized["Values"].ShouldBeOfType<JObject>().Count.ShouldBe(0);
    }

    [Fact]
    public void Normalize_EqualsTheEmptyDocument_ForNullLabelsAndValues()
    {
        var normalized = AnnotationEditDocument.Normalize(Execution, Document("""{ "Labels": null, "Values": null }"""));

        JToken.DeepEquals(normalized, AnnotationEditDocument.Create(Execution, null)).ShouldBeTrue();
    }

    [Fact]
    public void Normalize_KeepsTheFirstOfDuplicateLabels()
    {
        var normalized = AnnotationEditDocument.Normalize(Execution, Document("""{ "Labels": ["eu", "prod", "eu", "EU"] }"""));

        normalized["Labels"]!.Values<string>().ShouldBe(["eu", "prod", "EU"]);
    }

    [Theory]
    [InlineData("ValidFrom", "2026-12-01T00:00:00+01:00", "2026-12-01T00:00:00+01:00")]
    [InlineData("ValidFrom", "2026-12-01T00:00:00.000+01:00", "2026-12-01T00:00:00+01:00")]
    [InlineData("ValidFrom", "2026-12-01T00:00:00Z", "2026-12-01T00:00:00+00:00")]
    [InlineData("ValidFrom", "2026-12-01T00:00:00+01", "2026-12-01T00:00:00+01:00")]
    [InlineData("ValidFrom", "2026-12-01T10:30:00.1234567-03:00", "2026-12-01T10:30:00.1234567-03:00")]
    [InlineData("ExpiresAt", "2027-01-01T10:30:00.500Z", "2027-01-01T10:30:00.5+00:00")]
    [InlineData("ExpiresAt", "2027-01-01T10:30:00+05:30", "2027-01-01T10:30:00+05:30")]
    public void Normalize_WritesTheDatesAsCreateDoes(string member, string written, string normalized)
    {
        AnnotationEditDocument.Normalize(Execution, new JObject { [member] = written })[member]!.Value<string>().ShouldBe(normalized);
    }

    [Fact]
    public void Normalize_OfAUnit_GivesTheDefaultsForAMissingTypeOrDefinition()
    {
        var normalized = AnnotationEditDocument.Normalize(UnitAnnotation, Document("""{ "UnitType": "time", "UnitDefinition": null }"""));

        normalized["UnitType"]!.Value<string>().ShouldBe("time");
        normalized["UnitDefinition"]!.Value<string>().ShouldBe(CoreUnit.DefaultUnitDefinition);
    }

    [Fact]
    public void Normalize_OfAnInvalidDocument_Throws()
    {
        Should.Throw<ArgumentException>(() => AnnotationEditDocument.Normalize(Execution, Document("""{ "Titel": "a" }""")));
    }

    [Fact]
    public void ToAnnotation_IsOfTheKeysType_WithItsIdentity_AndTheContent()
    {
        var normalized = AnnotationEditDocument.Normalize(Execution, Document("""
            {
              "Title": "Billing", "Labels": ["eu"], "IsDisabled": true, "ValidFrom": "2026-12-01T00:00:00+01:00",
              "Values": { "since": "2026-10-24T10:00:00.000+02:00", "nested": { "at": 1 }, "none": null }
            }
            """));

        var annotation = AnnotationEditDocument.ToAnnotation(Execution, FullKey.Create(Execution, "org1", "proj1", "default"), normalized);

        annotation.ShouldBeOfType<_42.Platform.Storyteller.Execution>();
        annotation.AnnotationKey.ShouldBe(ExecutionKey);
        annotation.AnnotationType.ShouldBe(AnnotationType.Execution);
        annotation.Name.ShouldBe("prod");
        annotation.ProjectName.ShouldBe("proj1");
        annotation.ViewName.ShouldBe("default");
        annotation.Title.ShouldBe("Billing");
        annotation.Description.ShouldBeNull();
        annotation.Labels.ShouldBe(["eu"]);
        annotation.IsDisabled.ShouldBe(true);
        annotation.ValidFrom.ShouldBe(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(1)));
        annotation.Values!["since"].ShouldBe("2026-10-24T10:00:00.000+02:00");
        annotation.Values["none"].ShouldBeNull();
    }

    // As the API reads a body, "$ref", "$id", "$type" and "$values" are members of a value, not metadata of Newtonsoft.
    [Theory]
    [InlineData("""{ "schema": { "$ref": "#/defs/a" } }""")]
    [InlineData("""{ "schema": { "$ref": "#/defs/a", "description": "x" } }""")]
    [InlineData("""{ "schema": { "$id": "urn:x", "type": "object" } }""")]
    [InlineData("""{ "t": { "$type": "X", "a": 1 } }""")]
    [InlineData("""{ "s": { "$values": [1], "other": 3 } }""")]
    [InlineData("""{ "$ref": "x" }""")]
    [InlineData("""{ "$id": "1", "a": 2 }""")]
    public void ToAnnotation_KeepsValuesThatLookLikeMetadata(string values)
    {
        var normalized = AnnotationEditDocument.Normalize(Execution, Document($$"""{ "Values": {{values}} }"""));

        var annotation = AnnotationEditDocument.ToAnnotation(Execution, FullKey.Create(Execution, "org1", "proj1", "default"), normalized);

        JToken.DeepEquals(JObject.FromObject(annotation.Values!), Document(values)).ShouldBeTrue();
    }

    [Fact]
    public void ToAnnotation_OfAUnit_HasItsTypeAndDefinition()
    {
        var normalized = AnnotationEditDocument.Normalize(UnitAnnotation, Document("""{ "UnitType": "time", "UnitDefinition": "0 2 * * *" }"""));

        var unit = AnnotationEditDocument.ToAnnotation(UnitAnnotation, FullKey.Create(UnitAnnotation, "org1", "proj1", "default"), normalized)
            .ShouldBeOfType<CoreUnit>();

        unit.UnitType.ShouldBe("time");
        unit.UnitDefinition.ShouldBe("0 2 * * *");
    }

    // As the editor's file is read: date-like strings stay strings.
    private static JObject Document(string json)
    {
        return EditorService.ParseDocument(json);
    }

    private static SdkModel.Execution Stored()
    {
        return new SdkModel.Execution
        {
            AnnotationKey = ExecutionKey,
            Name = "prod",
            ProjectName = "proj1",
            ViewName = "default",
            Title = "Invoicing",
            Description = "What it does",
            DocumentationLink = "https://example.com/runbook",
            Labels = ["eu", "prod"],
            IsDisabled = true,
            ValidFrom = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(1)),
            ExpiresAt = new DateTimeOffset(2027, 1, 1, 10, 30, 0, 500, TimeSpan.Zero),
            TimeZone = "Europe/Prague",
            Values = new Dictionary<string, object>
            {
                ["tier"] = "gold",
                ["since"] = "2026-10-24T10:00:00.000+02:00",
                ["nested"] = new JObject { ["at"] = 1 },
                ["list"] = new JArray(1, "two", JValue.CreateNull()),
                ["big"] = 9_007_199_254_740_993L,
                ["none"] = null!,
            },
        };
    }
}
