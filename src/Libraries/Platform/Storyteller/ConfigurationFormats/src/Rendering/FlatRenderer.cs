using System.Globalization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.ConfigurationFormats.Rendering;

// The IConfiguration keys and values as a JSON object of strings, indented by two spaces, with Newtonsoft's default
// escaping; an empty document is {}.
internal static class FlatRenderer
{
    public static string Render(IReadOnlyList<DocumentLeaf> leaves)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };

        // The indentation of JsonTextWriter writes the NewLine of its writer.
        using (var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented, Indentation = 2, CloseOutput = false })
        {
            json.WriteStartObject();

            foreach (var leaf in leaves)
            {
                json.WritePropertyName(DocumentFlattener.ToKey(leaf.Segments));
                json.WriteValue(leaf.Value);
            }

            json.WriteEndObject();
        }

        writer.Write('\n');
        return writer.ToString();
    }
}
