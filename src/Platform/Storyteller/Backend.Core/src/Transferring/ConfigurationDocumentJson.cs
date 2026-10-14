using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// How documents are written into and read from an archive. Reading uses the date handling of the configuration
// reads from Cosmos DB, so an exported document compares equal to the stored one when it is imported back.
public static class ConfigurationDocumentJson
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonLoadSettings LoadSettings = new()
    {
        CommentHandling = CommentHandling.Ignore,
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        LineInfoHandling = LineInfoHandling.Ignore,
    };

    private static readonly JsonSerializer ManifestSerializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
        Converters = { new StringEnumConverter() },
    });

    // Indented by two spaces, '\n' line ends and a final new line, without BOM.
    public static byte[] Serialize(JToken document)
    {
        using var writer = CreateWriter(out var text);
        document.WriteTo(writer);
        writer.Flush();
        text.Write('\n');
        return Utf8.GetBytes(text.ToString());
    }

    public static byte[] SerializeManifest(object manifest)
    {
        using var writer = CreateWriter(out var text);
        ManifestSerializer.Serialize(writer, manifest);
        writer.Flush();
        text.Write('\n');
        return Utf8.GetBytes(text.ToString());
    }

    // A JSON object with comments allowed and duplicate properties rejected; anything else throws JsonException.
    public static JObject Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        using var json = new JsonTextReader(reader)
        {
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            FloatParseHandling = FloatParseHandling.Double,
            MaxDepth = 64,
        };

        while (json.Read() && json.TokenType == JsonToken.Comment)
        {
            // comments before the document
        }

        if (json.TokenType != JsonToken.StartObject)
        {
            throw new JsonReaderException("The document is not a JSON object.");
        }

        var document = JObject.Load(json, LoadSettings);

        while (json.Read())
        {
            if (json.TokenType != JsonToken.Comment)
            {
                throw new JsonReaderException("The document continues after its JSON object.");
            }
        }

        return document;
    }

    private static JsonTextWriter CreateWriter(out StringWriter text)
    {
        text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" };
        return new JsonTextWriter(text)
        {
            Formatting = Formatting.Indented,
            Indentation = 2,
            IndentChar = ' ',
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            CloseOutput = true,
        };
    }
}
