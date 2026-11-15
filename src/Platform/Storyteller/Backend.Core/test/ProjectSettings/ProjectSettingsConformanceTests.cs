using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The .NET reference of the project-settings kind of the conformance suite: every vector of
// src/Platform/Storyteller/conformance/project-settings.json through ProjectSettingsJson.Read, and the catalog of the file
// against the one Abstractions.Annotations embeds. The conformance workflow runs this class on Linux and Windows.
public class ProjectSettingsConformanceTests
{
    public static TheoryData<string> Vectors()
    {
        return new TheoryData<string>(ProjectSettingsFiles.Vectors().Keys);
    }

    public static TheoryData<string> AcceptedVectors()
    {
        return new TheoryData<string>(ProjectSettingsFiles.Vectors().Where(vector => vector.Value.ContainsKey("expected")).Select(vector => vector.Key));
    }

    // The normalized settings, compared as JSON (the order of the members does not count), or every problem by its path
    // and rule, in any order.
    [Theory]
    [MemberData(nameof(Vectors))]
    public void TheVector_IsReadAsTheSuiteSays(string name)
    {
        var vector = ProjectSettingsFiles.Vectors()[name];

        var check = ProjectSettingsJson.Read(vector["settings"]);

        if (vector["expected"] is JsonObject expected)
        {
            check.Problems.Select(Describe).ShouldBeEmpty();
            check.IsValid.ShouldBeTrue();
            check.Settings.Version.ShouldBe(0);
            check.Settings.Modified.ShouldBeNull();

            var actual = Values(check.Settings);
            JsonNode.DeepEquals(actual, expected).ShouldBeTrue($"{actual.ToJsonString()} is not {expected.ToJsonString()}");
        }
        else
        {
            check.Settings.ShouldBeNull();
            check.Problems.Select(problem => $"{problem.Path} {problem.Rule}").Order(StringComparer.Ordinal).ShouldBe(
                vector["problems"]!.AsArray()
                    .Select(problem => $"{problem!["path"]!.GetValue<string>()} {problem["rule"]!.GetValue<string>()}")
                    .Order(StringComparer.Ordinal));
            check.Problems.ShouldAllBe(problem => !string.IsNullOrWhiteSpace(problem.Message));
        }
    }

    // What Read accepts it accepts again as it wrote it, in both shapes, with the same values.
    [Theory]
    [MemberData(nameof(AcceptedVectors))]
    public void TheNormalizedSettings_ReadTheSameAgain(string name)
    {
        var settings = ProjectSettingsJson.Read(ProjectSettingsFiles.Vectors()[name]["settings"]).Settings!;

        foreach (var shape in new[] { ProjectSettingsJsonShape.Api, ProjectSettingsJsonShape.File })
        {
            var again = ProjectSettingsJson.Read(ProjectSettingsJson.ToJson(settings, shape));

            again.Problems.Select(Describe).ShouldBeEmpty();
            ProjectSettingsRules.SameValues(again.Settings, settings).ShouldBeTrue();
            JsonNode.DeepEquals(Values(again.Settings!), Values(settings)).ShouldBeTrue();
        }
    }

    [Fact]
    public void TheVectors_RefuseByEveryRule()
    {
        var rules = ProjectSettingsFiles.Vectors().Values
            .SelectMany(vector => vector["problems"]?.AsArray() ?? [])
            .Select(problem => problem!["rule"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        rules.ShouldBe(ProjectSettingsRules.All, ignoreOrder: true);
    }

    [Fact]
    public void TheCatalogOfTheFile_IsTheEmbeddedCatalog()
    {
        var file = ProjectSettingsFiles.Read(ProjectSettingsFiles.Conformance)["catalog"]!.AsObject();
        var catalog = ProjectSettingsCatalog.Default;

        catalog.Format.ShouldBe(file["format"]!.GetValue<string>());
        catalog.TypeCodes.ShouldBe(Strings(file["typeCodes"]));
        catalog.Colors.ShouldBe(Strings(file["colors"]));
        catalog.Icons.ShouldBe(Strings(file["icons"]));
        catalog.DateFormats.ShouldBe(Strings(file["dateFormats"]));
        catalog.TimeZones.ShouldBe(Strings(file["timeZones"]));
        catalog.DefaultTimeZone.ShouldBe(file["defaults"]!["timeZone"]!.GetValue<string>());
        catalog.DefaultDateFormat.ShouldBe(file["defaults"]!["dateFormat"]!.GetValue<string>());
        catalog.LabelMaxLength.ShouldBe(file["limits"]!["label"]!.GetValue<int>());
        catalog.PluralMaxLength.ShouldBe(file["limits"]!["plural"]!.GetValue<int>());

        foreach (var (code, appearance) in file["builtIn"]!.AsObject())
        {
            catalog.BuiltIn[code].ShouldBe(new ProjectTypeAppearance
            {
                Label = appearance!["label"]!.GetValue<string>(),
                Plural = appearance["plural"]!.GetValue<string>(),
                Color = appearance["color"]!.GetValue<string>(),
                Icon = appearance["icon"]!.GetValue<string>(),
            });
        }

        catalog.BuiltIn.Keys.ShouldBe(catalog.TypeCodes, ignoreOrder: true);
    }

    // The settings as the vectors' expected values hold them: the Api shape without Version and Modified.
    private static JsonObject Values(ProjectSettings settings)
    {
        var json = ProjectSettingsJson.ToJson(settings, ProjectSettingsJsonShape.Api);
        json.Remove(nameof(ProjectSettings.Version));
        json.Remove(nameof(ProjectSettings.Modified));
        return json;
    }

    private static string[] Strings(JsonNode? list)
    {
        return list!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
    }

    private static string Describe(ProjectSettingsProblem problem)
    {
        return $"{problem.Path} {problem.Rule}: {problem.Message}";
    }
}
