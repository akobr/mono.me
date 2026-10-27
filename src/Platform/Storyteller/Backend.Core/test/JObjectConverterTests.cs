using System.Text.Json;
using _42.Platform.Storyteller.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The JObject of a model as the API writes it with System.Text.Json: the bytes it wrote before, and the depth of
// configuration content that the depth limit of the API's options (32 under MVC) would refuse.
public class JObjectConverterTests
{
    // Escaped by the encoder of the options: HTML-sensitive and non-ASCII characters, the separators, a quote.
    private const string Content = """{"db":{"hosts":["a","b"],"port":5432,"rate":1.5,"on":true,"none":null},"text":"<b>Žluťoučký</b> & 'x' \u2028 \"q\"","empty":{},"list":[]}""";

    [Fact]
    public void Write_WritesTheBytesOfTheObjectWrittenStraightIntoTheWriter()
    {
        var options = Options(maxDepth: 32);
        var model = new Model { Name = "n", Content = JObject.Parse(Content) };

        var written = JsonSerializer.Serialize(model, options);

        using var document = JsonDocument.Parse(JObject.Parse(Content).ToString(Newtonsoft.Json.Formatting.None));
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            document.WriteTo(writer);
        }

        written.ShouldBe($"{{\"Name\":\"n\",\"Content\":{System.Text.Encoding.UTF8.GetString(buffer.ToArray())}}}");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(JObjectConverter.MaxDepth)]
    public void Write_ADeepObject_PassesTheDepthLimitOfTheOptions(int depth)
    {
        var content = Nested(depth);

        var written = JsonSerializer.Serialize(new Model { Name = "n", Content = content }, Options(maxDepth: 32));

        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(written)) { MaxDepth = null };
        JToken.DeepEquals(JObject.Load(reader)["Content"], content).ShouldBeTrue();
    }

    [Fact]
    public void Write_AnObjectDeeperThanItsLimit_Throws()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(new Model { Name = "n", Content = Nested(JObjectConverter.MaxDepth + 1) }, Options(maxDepth: 32)));
    }

    private static JsonSerializerOptions Options(int maxDepth)
    {
        var options = new JsonSerializerOptions { MaxDepth = maxDepth };
        options.Converters.Add(new JObjectConverter());
        return options;
    }

    // A value under depth levels of objects.
    private static JObject Nested(int depth)
    {
        var content = new JObject { ["leaf"] = "v" };

        for (var level = 1; level < depth; level++)
        {
            content = new JObject { ["n"] = content };
        }

        return content;
    }

    private sealed class Model
    {
        public string Name { get; set; } = string.Empty;

        public JObject? Content { get; set; }
    }
}
