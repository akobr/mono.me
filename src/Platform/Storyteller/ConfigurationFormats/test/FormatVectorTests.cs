using _42.Platform.Storyteller.ConfigurationFormats.UnitTests.Readers;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// Vectors/flattening.properties.json, flattening.env.json and formats.json: the keys of properties and env read back
// from the formatted text, and the exact text of every format.
public class FormatVectorTests
{
    public static TheoryData<string> PropertiesNames()
    {
        return VectorFiles.Names(VectorFiles.Properties);
    }

    public static TheoryData<string> EnvNames()
    {
        return VectorFiles.Names(VectorFiles.Env);
    }

    public static TheoryData<string> FormatsNames()
    {
        return VectorFiles.Names(VectorFiles.Formats);
    }

    [Theory]
    [InlineData(VectorFiles.Properties, "keys")]
    [InlineData(VectorFiles.Env, "keys")]
    [InlineData(VectorFiles.Formats, "text")]
    public void TheFile_HasVectorsOfBothKinds(string file, string result)
    {
        var vectors = VectorFiles.Load(file).Values;

        vectors.ShouldContain(vector => vector[result] != null);
        vectors.ShouldContain(vector => vector["error"] != null);
        vectors.ShouldAllBe(vector => (vector[result] == null) != (vector["error"] == null));
    }

    // So that a document's flattening, properties and env outcomes can be read side by side.
    [Theory]
    [InlineData(VectorFiles.Properties)]
    [InlineData(VectorFiles.Env)]
    public void TheFile_HasEveryDocumentOfTheFlatteningVectors(string file)
    {
        var vectors = VectorFiles.Load(file);

        foreach (var (name, vector) in VectorFiles.Load(VectorFiles.Flattening))
        {
            vectors.ShouldContainKey(name);
            JToken.DeepEquals(vectors[name]["document"], vector["document"]).ShouldBeTrue(name);
        }
    }

    [Fact]
    public void TheFormatsFile_CoversEveryFormat()
    {
        VectorFiles.Load(VectorFiles.Formats).Values
            .Select(vector => vector.Value<string>("format"))
            .Distinct()
            .ShouldBe(ConfigurationFormatNames.Names, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(PropertiesNames))]
    public void PropertiesVector(string name)
    {
        var vector = VectorFiles.Load(VectorFiles.Properties)[name];

        if (ExpectError(vector, ConfigurationFormat.Properties))
        {
            return;
        }

        var text = ConfigurationFormatter.Format(vector["document"], ConfigurationFormat.Properties).Text;

        text.ShouldAllBe(character => character >= ' ' && character <= '~' || character == '\n');
        PropertiesReader.Read(text).ShouldBe(ExpectedKeys(vector));
    }

    [Theory]
    [MemberData(nameof(EnvNames))]
    public void EnvVector(string name)
    {
        var vector = VectorFiles.Load(VectorFiles.Env)[name];

        if (ExpectError(vector, ConfigurationFormat.Env))
        {
            Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.ToEnvironmentVariables(vector["document"]))
                .Path.ShouldBe(vector["error"]!.Value<string>("path"));
            return;
        }

        var expected = ExpectedKeys(vector);

        EnvReader.Read(ConfigurationFormatter.Format(vector["document"], ConfigurationFormat.Env).Text).ShouldBe(expected);
        ConfigurationFormatter.ToEnvironmentVariables(vector["document"])
            .Select(variable => (variable.Name, variable.Value))
            .ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(FormatsNames))]
    public void FormatsVector(string name)
    {
        var vector = VectorFiles.Load(VectorFiles.Formats)[name];
        ConfigurationFormatNames.TryParse(vector.Value<string>("format"), out var format).ShouldBeTrue();

        if (ExpectError(vector, format))
        {
            return;
        }

        ConfigurationFormatter.Format(vector["document"], format).Text.ShouldBe(vector.Value<string>("text"));
    }

    private static bool ExpectError(JObject vector, ConfigurationFormat format)
    {
        if (vector["error"] is not JObject error)
        {
            return false;
        }

        var exception = Should.Throw<InvalidDocumentException>(() => ConfigurationFormatter.Format(vector["document"], format));

        exception.Rule.ShouldBe(error.Value<string>("rule"));
        exception.Path.ShouldBe(error.Value<string>("path"));
        exception.Message.ShouldContain(exception.Path);
        return true;
    }

    // In document order, which every format keeps.
    private static List<(string Key, string Value)> ExpectedKeys(JObject vector)
    {
        return vector["keys"].ShouldBeOfType<JObject>().Properties()
            .Select(property => (property.Name, property.Value.Value<string>()!))
            .ToList();
    }
}
