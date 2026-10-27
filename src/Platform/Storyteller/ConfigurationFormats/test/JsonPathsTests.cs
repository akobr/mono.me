using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

public class JsonPathsTests
{
    [Theory]
    [InlineData("db", "$.db")]
    [InlineData("_a-b", "$._a-b")]
    [InlineData("a.b", "$['a.b']")]
    [InlineData("0", "$['0']")]
    [InlineData("", "$['']")]
    [InlineData("it's", "$['it\\'s']")]
    [InlineData("C:\\x", "$['C:\\\\x']")]
    public void AName_IsPlainOrBracketed(string name, string path)
    {
        JsonPaths.Property(JsonPaths.Root, name).ShouldBe(path);
    }

    [Theory]
    [InlineData("a\nb", "$['a\\nb']")]
    [InlineData("a\rb", "$['a\\rb']")]
    [InlineData("a\tb", "$['a\\tb']")]
    [InlineData("a\u0000b", "$['a\\u0000b']")]
    [InlineData("a\u001Bb", "$['a\\u001Bb']")]
    [InlineData("a\u007Fb", "$['a\\u007Fb']")]
    public void AControlCharacter_IsEscaped_SoAPathNeverBreaksALine(string name, string path)
    {
        var escaped = JsonPaths.Property(JsonPaths.Root, name);

        escaped.ShouldBe(path);
        escaped.ShouldAllBe(character => !char.IsControl(character));
    }

    [Fact]
    public void AnItem_IsItsIndex()
    {
        JsonPaths.Item(JsonPaths.Property(JsonPaths.Root, "list"), 3).ShouldBe("$.list[3]");
    }

    [Fact]
    public void AnInvalidDocument_NamesTheEscapedPath()
    {
        var exception = Should.Throw<InvalidDocumentException>(
            () => DocumentFlattener.Flatten(new JObject { ["x\n::warning::y"] = "s3cret" }));

        exception.Path.ShouldBe("$['x\\n::warning::y']");
        exception.Message.ShouldNotContain("\n");
        exception.Message.ShouldNotContain("s3cret");
    }
}
