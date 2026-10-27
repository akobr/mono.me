using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// Reads JSON as the SDK reads a body: date-like strings stay strings, numbers are doubles.
internal static class SdkJson
{
    public static JToken Parse(string json)
    {
        return Read(new StringReader(json));
    }

    public static JToken Read(TextReader text)
    {
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        return JToken.ReadFrom(reader);
    }
}
