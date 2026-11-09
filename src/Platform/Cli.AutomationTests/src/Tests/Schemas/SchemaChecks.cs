using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// The documents and the checks of the schema cases, after what sform prints for them
// (src/Platform/Cli/src/Commands/Schemas/SchemaOperations.cs):
//   - schema … get and set print the ConfigurationSchema model through WriteJson (View, AnnotationType or AnnotationKey,
//     Version, Content, Modified); a forced set adds "Schema saved as version N." and "Existing configurations were not
//     required to comply.";
//   - a schema the configurations of the view break ends with 2: the server's message, then "<key> (<view>)" and its
//     violations for each configuration (SchemaValidationConsole); a configuration write that breaks a schema the same;
//   - schema … versions prints "# Versions of the schema for <subject>" and a table of Version, Author, Created and
//     Expires, the newest first (SformResult.VersionRows), or "The schema has no versions.";
//   - schema … diff prints the API's JSON diff as "+A -D ~U" and hunks "@@ -a,b +c,d @@" with numbered lines
//     (DiffResultConsoleExtensions.WriteDiffResult); with --format unified the hunks and their +, - and space lines raw
//     (WriteUnifiedDiff), "No changes detected." on stderr when nothing changed.
// The documents are JSON Schemas of an object with string properties; a breaking one requires a property that the
// configurations of the case lack (they are set with -x a=1).
internal static class SchemaChecks
{
    // The header of schema … versions (SformResult.VersionRows).
    public const string VersionsHeader = "Versions of the schema for";

    private static readonly Regex HunkPattern = new(@"@@ -\d+,\d+ \+\d+,\d+ @@", RegexOptions.CultureInvariant);
    private static readonly Regex StatsPattern = new(@"^\s*\+\d+ -\d+ ~\d+\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // {"type":"object","properties":{"<name>":{"type":"string"},…}}: what any configuration of the cases complies with.
    public static string ObjectSchema(params string[] properties)
    {
        return Schema(properties, []);
    }

    // The same with "required": [the properties]: a schema that a configuration without them breaks.
    public static string RequiringSchema(params string[] properties)
    {
        return Schema(properties, properties);
    }

    // The model of schema … get or set: its Content equals the document, and its Version is the given one.
    public static SformResult ShouldPrintSchema(this SformResult result, string expectedContent, long version)
    {
        ArgumentNullException.ThrowIfNull(result);
        var model = result.JsonNode();
        ShouldEqualJson(result, model["Content"], expectedContent, "the schema's Content");
        return result.ShouldHaveVersion(version);
    }

    public static SformResult ShouldHaveVersion(this SformResult result, long version)
    {
        ArgumentNullException.ThrowIfNull(result);
        var printed = result.JsonNode()["Version"] is JsonValue value && value.TryGetValue<long>(out var number) ? number : (long?)null;
        return printed == version
            ? result
            : throw result.Fail($"printed the schema with the Version {printed?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}", expected: $"Version {version.ToString(CultureInfo.InvariantCulture)}");
    }

    // The node is the JSON document (compared as JSON: the order of the members does not matter).
    public static void ShouldEqualJson(SformResult result, JsonNode? actual, string expected, string what)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!JsonNode.DeepEquals(actual, JsonNode.Parse(expected)))
        {
            throw result.Fail($"printed {what} {actual?.ToJsonString() ?? "(none)"}, not {expected}");
        }
    }

    // A forced write of schema … set: the version line and the warning.
    public static SformResult ShouldBeForced(this SformResult result, long version)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result
            .ShouldContain($"Schema saved as version {version.ToString(CultureInfo.InvariantCulture)}.")
            .ShouldContain("Existing configurations were not required to comply.");
    }

    // A write refused because the configuration of the key in the view does not comply: exit 2 and "<key> (<view>)".
    public static SformResult ShouldRefuseForConfiguration(this SformResult result, string annotationKey, string view)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{annotationKey} ({view})");
    }

    // schema … versions succeeds and lists exactly these versions, the newest first.
    public static SformResult ShouldListVersions(this SformResult result, params long[] newestFirst)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(newestFirst);
        result.ShouldSucceed();
        if (newestFirst.Length == 0)
        {
            return result.ShouldContain("The schema has no versions.");
        }

        var listed = result.VersionRows(VersionsHeader);
        return listed.SequenceEqual(newestFirst)
            ? result
            : throw result.Fail($"listed the versions {Join(listed)}", expected: $"the versions {Join(newestFirst)}");
    }

    // schema … diff succeeds and prints a diff of hunks that shows the property.
    public static SformResult ShouldShowDiff(this SformResult result, string property)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result
            .ShouldSucceed()
            .ShouldMatch(HunkPattern)
            .ShouldContain($"\"{property}\"");
    }

    // The same for the API's JSON diff (no --format, or --format json), which sform prints with its statistics first.
    public static SformResult ShouldShowJsonDiff(this SformResult result, string property)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result
            .ShouldShowDiff(property)
            .ShouldMatch(StatsPattern);
    }

    private static string Schema(string[] properties, string[] required)
    {
        var members = new JsonObject();
        foreach (var property in properties)
        {
            members[property] = new JsonObject { ["type"] = "string" };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = members,
        };

        if (required.Length > 0)
        {
            schema["required"] = new JsonArray([.. required.Select(name => (JsonNode?)JsonValue.Create(name))]);
        }

        return schema.ToJsonString();
    }

    private static string Join(IEnumerable<long> versions)
    {
        var list = string.Join(", ", versions.Select(version => version.ToString(CultureInfo.InvariantCulture)));
        return list.Length == 0 ? "(none)" : list;
    }
}
