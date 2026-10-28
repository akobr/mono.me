using System.Globalization;
using _42.Platform.Storyteller.ConfigurationFormats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Conformance;

// The suite itself, which the reference provider checks for every runtime: suite.json lists every file, every file
// matches the schema of its kind, the flattening files cover the rules of the study (section 3.3) for every target,
// and every runtime reads the files the same way.
public class SuiteTests
{
    // 2^53 - 1: the largest integer that a JavaScript number holds exactly.
    private const long MaxSafeInteger = 9007199254740991;

    // The study's rules of 3.3 that every target pins by name: numbers and booleans as JSON text, no null, empty
    // containers make no key.
    private static readonly string[] RuleVectors =
    [
        "numbers are their JSON text",
        "booleans are lower case",
        "null makes no key",
        "empty objects and arrays make no key",
    ];

    // The rule each target applies to a delimiter in a name; properties escapes the other delimiters in the bracket
    // form.
    private static readonly Dictionary<string, string> DelimiterRules = new(StringComparer.Ordinal)
    {
        ["flat"] = DocumentRules.ColonInName,
        ["properties"] = DocumentRules.BracketInName,
        ["env"] = DocumentRules.InvalidEnvName,
    };

    public static TheoryData<string> Files()
    {
        return new TheoryData<string>(ConformanceFiles.Listed().Select(file => file.Path));
    }

    public static TheoryData<string> FlatteningFiles()
    {
        return new TheoryData<string>(ConformanceFiles.OfKind("flattening"));
    }

    [Fact]
    public async Task TheManifest_MatchesItsSchema()
    {
        await ShouldMatchAsync(ConformanceFiles.Manifest, "suite");
        ConformanceFiles.Read(ConformanceFiles.Manifest).Value<string>("version").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void TheManifest_ListsEveryFile()
    {
        var vectorFiles = ConformanceFiles.Embedded
            .Where(path => path != ConformanceFiles.Manifest && !path.StartsWith(ConformanceFiles.SchemaFolder, StringComparison.Ordinal));
        var listed = ConformanceFiles.Listed().Select(file => file.Path).ToList();

        listed.ShouldBeUnique();
        listed.ShouldBe(vectorFiles, ignoreOrder: true);
    }

    [Fact]
    public void EveryKind_HasASchema()
    {
        var kinds = ConformanceFiles.Read($"{ConformanceFiles.SchemaFolder}suite.schema.json")
            .SelectToken("properties.files.items.properties.kind.enum")!
            .Values<string>()
            .Select(kind => $"{ConformanceFiles.SchemaFolder}{kind}.schema.json")
            .Append($"{ConformanceFiles.SchemaFolder}suite.schema.json");

        ConformanceFiles.Embedded.Where(path => path.StartsWith(ConformanceFiles.SchemaFolder, StringComparison.Ordinal))
            .ShouldBe(kinds, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task TheFile_MatchesTheSchemaOfItsKind(string path)
    {
        var kind = ConformanceFiles.Listed().Single(file => file.Path == path).Kind;
        var file = ConformanceFiles.Read(path);

        await ShouldMatchAsync(path, kind);

        // The relative path an editor resolves.
        var depth = path.Count(character => character == '/');
        var prefix = depth == 0 ? "./" : string.Concat(Enumerable.Repeat("../", depth));
        file.Value<string>("$schema").ShouldBe($"{prefix}{ConformanceFiles.SchemaFolder}{kind}.schema.json");
    }

    // So that a schema that NJsonSchema reads too leniently cannot pass every file.
    [Theory]
    [InlineData("""{ "name": "both", "document": {}, "keys": {}, "error": { "rule": "empty-name", "path": "$" } }""", "flat")]
    [InlineData("""{ "name": "neither", "document": {} }""", "flat")]
    [InlineData("""{ "name": "unknown rule", "document": {}, "error": { "rule": "dot-in-name", "path": "$" } }""", "flat")]
    [InlineData("""{ "name": "relative path", "document": {}, "error": { "rule": "empty-name", "path": "a" } }""", "flat")]
    [InlineData("""{ "name": "number value", "document": {}, "keys": { "a": 1 } }""", "flat")]
    [InlineData("""{ "name": "extra field", "document": {}, "keys": {}, "note": "x" }""", "flat")]
    [InlineData("""{ "name": "unknown target", "document": {}, "keys": {} }""", "dotnet")]
    public async Task TheFlatteningSchema_RejectsAnInvalidFile(string vector, string target)
    {
        var schema = await JsonSchema.FromJsonAsync(ConformanceFiles.ReadText($"{ConformanceFiles.SchemaFolder}flattening.schema.json"));
        var file = JObject.Parse($$"""{ "target": "{{target}}", "description": "x", "vectors": [ {{vector}} ] }""");

        schema.Validate(file).ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TheFile_NamesEveryItemOnce(string path)
    {
        var file = ConformanceFiles.Read(path);
        var items = (file["vectors"] ?? file["scenarios"]).ShouldBeOfType<JArray>();

        items.Select(item => item.Value<string>("name")).ShouldBeUnique();
    }

    [Fact]
    public void TheFlatteningFiles_HaveOneFileForEveryTarget()
    {
        ConformanceFiles.OfKind("flattening")
            .Select(path => ConformanceFiles.Read(path).Value<string>("target"))
            .ShouldBe(DelimiterRules.Keys, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(FlatteningFiles))]
    public void EveryTarget_CoversTheRulesOfTheStudy(string path)
    {
        var file = ConformanceFiles.Read(path);
        var vectors = ConformanceFiles.Load(path);
        var rules = vectors.Values.Select(vector => vector.SelectToken("error.rule")?.Value<string>()).ToHashSet();

        foreach (var name in RuleVectors)
        {
            vectors.ShouldContainKey(name);
        }

        rules.ShouldContain(DocumentRules.CaseCollision);
        rules.ShouldContain(DelimiterRules[file.Value<string>("target")!]);
    }

    // JSON.parse keeps neither the literal of a number nor the order of names that are array indexes, so the suite holds
    // only what every runtime reads the same way.
    [Theory]
    [MemberData(nameof(Files))]
    public void TheFile_IsReadTheSameByEveryRuntime(string path)
    {
        var problems = new List<string>();

        FindUnportable(ConformanceFiles.Read(path), JsonPaths.Root, problems);

        problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{ "a": 1.0 }""", false)]
    [InlineData("""{ "a": 1E+2 }""", false)]
    [InlineData("""{ "a": -0.0 }""", false)]
    [InlineData("""{ "a": 9007199254740992 }""", false)]
    [InlineData("""{ "a": 100000000000000000000 }""", false)]
    [InlineData("""{ "b": 1, "0": 2 }""", false)]
    [InlineData("""{ "list": [ { "x": 1, "10": 2 } ] }""", false)]
    [InlineData("""{ "1": 1, "0": 2 }""", false)]
    [InlineData("""{ "0": 1, "1": 2, "b": 3, "01": 4, "-1": 5, "4294967295": 6 }""", true)]
    [InlineData("""{ "a": 1E+20, "b": 0.5, "c": -9007199254740991, "d": 5432 }""", true)]
    public void ThePortabilityRules_RejectWhatJavaScriptReadsDifferently(string json, bool isPortable)
    {
        var problems = new List<string>();

        FindUnportable(Parse(json), JsonPaths.Root, problems);

        (problems.Count == 0).ShouldBe(isPortable, string.Join("; ", problems));
    }

    private static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { FloatParseHandling = FloatParseHandling.Double };
        return JToken.ReadFrom(reader);
    }

    private static async Task ShouldMatchAsync(string path, string kind)
    {
        var schema = await JsonSchema.FromJsonAsync(ConformanceFiles.ReadText($"{ConformanceFiles.SchemaFolder}{kind}.schema.json"));
        var errors = schema.Validate(ConformanceFiles.Read(path));

        errors.Select(error => $"{error.Path}: {error.Kind}").ShouldBeEmpty();
    }

    private static void FindUnportable(JToken token, string path, List<string> problems)
    {
        switch (token)
        {
            case JObject container:
                var names = container.Properties().Select(property => property.Name).ToList();

                // JavaScript lists the names that are array indexes first, in ascending order, then the others in order.
                var javaScriptOrder = names.Where(IsArrayIndex)
                    .OrderBy(name => uint.Parse(name, CultureInfo.InvariantCulture))
                    .Concat(names.Where(name => !IsArrayIndex(name)));

                if (!names.SequenceEqual(javaScriptOrder))
                {
                    problems.Add($"{path}: JavaScript reorders the names, which start with an array index after another name.");
                }

                foreach (var property in container.Properties())
                {
                    FindUnportable(property.Value, JsonPaths.Property(path, property.Name), problems);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    FindUnportable(array[index], JsonPaths.Item(path, index), problems);
                }

                break;

            // An Int64 beyond 2^53 - 1, or a BigInteger: a JavaScript number does not hold it.
            case JValue { Type: JTokenType.Integer } integer when integer.Value is not long value || Math.Abs(value) > MaxSafeInteger:
                problems.Add($"{path}: the integer is beyond ±(2^53 - 1).");
                break;

            // 1.0 and 1E+2 are 1 and 100 for JavaScript, but 1.0 and 100.0 for Newtonsoft; 1E+20 is a double everywhere.
            case JValue { Type: JTokenType.Float, Value: double value } when Math.Abs(value) <= MaxSafeInteger && value == Math.Floor(value):
                problems.Add($"{path}: the number is written with a fraction or an exponent but is a safe integer.");
                break;
        }
    }

    // A canonical array index, as ECMAScript orders it: 0 to 2^32 - 2, without leading zeros or a sign.
    private static bool IsArrayIndex(string name)
    {
        return uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index != uint.MaxValue
            && index.ToString(CultureInfo.InvariantCulture) == name;
    }
}
