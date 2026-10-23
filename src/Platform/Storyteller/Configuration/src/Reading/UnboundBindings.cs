using _42.Platform.Storyteller.Sdk.Configuring.Flattening;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// Bindings that a resolved read left as written: a caller without Secrets.Read gets their @… text, without the
// withheld header. Only the paths are reported, never the values.
internal static class UnboundBindings
{
    public static IReadOnlyList<string> FindPaths(JToken? content)
    {
        var paths = new List<string>();

        if (content is not null)
        {
            Visit(content, JsonPaths.Root, paths);
        }

        return paths;
    }

    private static void Visit(JToken token, string path, List<string> paths)
    {
        switch (token)
        {
            case JObject container:
                foreach (var property in container.Properties())
                {
                    Visit(property.Value, JsonPaths.Property(path, property.Name), paths);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], JsonPaths.Item(path, index), paths);
                }

                break;

            case JValue { Type: JTokenType.String, Value: string text } when text.StartsWith('@'):
                paths.Add(path);
                break;
        }
    }
}
